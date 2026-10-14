using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.MediaFoundation;
using NAudio.Wave;
using Cutlify.Core;
using Cutlify.Settings;

namespace Cutlify.Recording
{
    /// <summary>Una fuente de audio de la grabadora: micrófono, escritorio (loopback) o una aplicación.</summary>
    public sealed class AudioTrackSource : IDisposable
    {
        private readonly IWaveIn _capture;
        private readonly BufferedWaveProvider _buffer;
        private readonly object _sync = new object();
        private byte[] _scratch = new byte[0];

        public AudioTrackConfig Config { get; }
        public string Name => Config.Name;
        public ISampleProvider Output { get; }
        public bool Failed { get; private set; }

        private AudioTrackSource(AudioTrackConfig config, IWaveIn capture, ISampleProvider synthetic)
        {
            Config = config;
            _capture = capture;
            if (capture != null)
            {
                _buffer = new BufferedWaveProvider(AudioUtil.PlainFormat(capture.WaveFormat))
                { ReadFully = true, DiscardOnBufferOverflow = true, BufferDuration = TimeSpan.FromSeconds(2) };
                capture.DataAvailable += (_, e) => { lock (_sync) _buffer.AddSamples(e.Buffer, 0, e.BytesRecorded); };
                capture.RecordingStopped += (_, e) => { if (e.Exception != null) { Failed = true; Logger.Warn("Audio", $"La pista «{Name}» se detuvo", e.Exception); } };
                Output = AudioUtil.Normalize(_buffer.ToSampleProvider(), MultiTrackAudioRecorder.SampleRate);
            }
            else Output = AudioUtil.Normalize(synthetic, MultiTrackAudioRecorder.SampleRate);
        }

        /// <summary>Crea la fuente real de la pista. Lanza una excepción comprensible si no está disponible.</summary>
        public static async Task<AudioTrackSource> CreateAsync(AudioTrackConfig c)
        {
            switch (c.Kind)
            {
                case AudioTrackKind.Microphone:
                {
                    using var e = new MMDeviceEnumerator();
                    var dev = Find(e, c.DeviceId, DataFlow.Capture);
                    return new AudioTrackSource(c, new WasapiCapture(dev, true, 50), null);
                }
                case AudioTrackKind.System:
                {
                    using var e = new MMDeviceEnumerator();
                    var dev = Find(e, c.DeviceId, DataFlow.Render);
                    return new AudioTrackSource(c, new WasapiLoopbackCapture(dev), null);
                }
                default:
                {
                    var pid = AudioApps.FindRootProcess(c.ProcessName)
                              ?? throw new InvalidOperationException($"{c.Name} no está abierta.");
                    return new AudioTrackSource(c, await ProcessLoopbackCapture.CreateAsync(pid, true), null);
                }
            }
        }

        /// <summary>Fuente sintética (pruebas automatizadas).</summary>
        internal static AudioTrackSource FromProvider(AudioTrackConfig c, ISampleProvider provider) => new AudioTrackSource(c, null, provider);

        private static MMDevice Find(MMDeviceEnumerator e, string id, DataFlow flow)
        {
            if (!string.IsNullOrEmpty(id))
            {
                try { var d = e.GetDevice(id); if (d.State == DeviceState.Active) return d; } catch { }
            }
            return e.GetDefaultAudioEndpoint(flow, flow == DataFlow.Capture ? Role.Communications : Role.Multimedia);
        }

        private bool _started;

        /// <summary>Abre el dispositivo (idempotente). Puede lanzar si Windows deniega el acceso.</summary>
        public void Start()
        {
            if (_started) return;
            _capture?.StartRecording();
            _started = true;
        }

        public int Read(float[] buffer, int samples)
        {
            lock (_sync)
            {
                if (_buffer != null) AudioUtil.TrimLatency(_buffer, TimeSpan.FromMilliseconds(200), ref _scratch);
                return Output.Read(buffer, 0, samples);
            }
        }

        public void Discard()
        {
            if (_buffer == null) return;
            lock (_sync) _buffer.ClearBuffer();
        }

        public void Dispose()
        {
            try { _capture?.StopRecording(); } catch { }
            try { _capture?.Dispose(); } catch { }
        }
    }

    public sealed class AudioRecordingResult
    {
        public string MixPath;
        public List<string> TrackPaths = new List<string>();
        public List<string> TrackNames = new List<string>();
        public TimeSpan Duration;
        public float[] Peaks = new float[0]; // envolvente de la mezcla para la miniatura (una muestra cada 50 ms)
    }

    /// <summary>
    /// Grabadora multipista: cada pista se guarda en su propia WAV y además se genera una mezcla (M4A/MP3/WAV).
    /// Todas las pistas avanzan con el mismo reloj, por lo que quedan sincronizadas (las pausas se excluyen).
    /// </summary>
    public sealed class MultiTrackAudioRecorder : IDisposable
    {
        public const int SampleRate = 48000;
        public const int Channels = 2;

        private readonly List<AudioTrackSource> _tracks;
        private readonly string _basePath;   // sin extensión
        private readonly string _mixFormat;
        private readonly bool _separate;
        private readonly Stopwatch _clock = new Stopwatch();
        private Thread _thread;
        private volatile bool _stop, _paused;
        private long _pausedTicks, _pauseStart;
        private readonly List<float> _peaks = new List<float>();
        private Exception _error;

        public IReadOnlyList<AudioTrackSource> Tracks => _tracks;
        public float[] Levels { get; }
        public bool IsPaused => _paused;
        public long FramesWritten { get; private set; }

        public TimeSpan Elapsed
        {
            get
            {
                long paused = _pausedTicks + (_paused ? _clock.ElapsedTicks - _pauseStart : 0);
                return TimeSpan.FromSeconds((_clock.ElapsedTicks - paused) / (double)Stopwatch.Frequency);
            }
        }

        public MultiTrackAudioRecorder(IEnumerable<AudioTrackSource> tracks, string basePath, string mixFormat, bool separateTracks)
        {
            _tracks = tracks.ToList();
            if (_tracks.Count == 0) throw new InvalidOperationException("Añade al menos una pista.");
            _basePath = basePath;
            _mixFormat = mixFormat is "MP3" or "WAV" ? mixFormat : "M4A";
            _separate = separateTracks && _tracks.Count > 1;
            Levels = new float[_tracks.Count];
        }

        public static string SafeName(string s) =>
            string.Concat((s ?? "Pista").Select(ch => Path.GetInvalidFileNameChars().Contains(ch) || ch == ' ' ? '_' : ch));

        private string MixWavPath => _mixFormat == "WAV" ? _basePath + ".wav" : _basePath + "_mezcla.tmp.wav";

        public void Start()
        {
            foreach (var t in _tracks) t.Start();
            _clock.Start();
            _thread = new Thread(Run) { IsBackground = true, Name = "CUTLIFY Audio recorder", Priority = ThreadPriority.AboveNormal };
            _thread.Start();
        }

        public void Pause() { if (_paused) return; _pauseStart = _clock.ElapsedTicks; _paused = true; }
        public void Resume() { if (!_paused) return; _pausedTicks += _clock.ElapsedTicks - _pauseStart; _paused = false; }

        private void Run()
        {
            var format = new WaveFormat(SampleRate, 16, Channels);
            var trackWriters = new List<WaveFileWriter>();
            WaveFileWriter mixWriter = null;
            var buffers = _tracks.Select(_ => new float[SampleRate * Channels]).ToArray();
            var mix = new float[SampleRate * Channels];
            var pcm = new byte[SampleRate * Channels * 2];
            long written = 0;
            int peakWindow = SampleRate / 20; // 50 ms
            float peakAcc = 0; int peakCount = 0;
            try
            {
                if (_separate)
                    foreach (var t in _tracks) trackWriters.Add(new WaveFileWriter($"{_basePath}_{SafeName(t.Name)}.wav", format));
                mixWriter = new WaveFileWriter(MixWavPath, format);

                while (true)
                {
                    bool stopping = _stop;
                    if (_paused) { foreach (var t in _tracks) t.Discard(); Thread.Sleep(10); continue; }
                    long target = (long)(Elapsed.TotalSeconds * SampleRate);
                    long need = target - written;
                    if (need < SampleRate / 100 && !(stopping && need > 0))
                    {
                        if (stopping) break;
                        Thread.Sleep(10);
                        continue;
                    }
                    int frames = (int)Math.Min(need, SampleRate);
                    int n = frames * Channels;
                    Array.Clear(mix, 0, n);
                    for (int i = 0; i < _tracks.Count; i++)
                    {
                        var t = _tracks[i];
                        var b = buffers[i];
                        t.Read(b, n);
                        float peak = 0;
                        for (int k = 0; k < n; k++) peak = Math.Max(peak, Math.Abs(b[k]));
                        Levels[i] = Math.Min(1f, peak);
                        if (_separate) Write(trackWriters[i], b, n, pcm);
                        if (!t.Config.Muted)
                        {
                            float g = Math.Clamp(t.Config.Gain, 0f, 2f);
                            for (int k = 0; k < n; k++) mix[k] += b[k] * g;
                        }
                    }
                    Write(mixWriter, mix, n, pcm);
                    for (int k = 0; k < n; k += Channels)
                    {
                        peakAcc = Math.Max(peakAcc, Math.Abs(mix[k]));
                        if (++peakCount >= peakWindow) { lock (_peaks) _peaks.Add(Math.Min(1f, peakAcc)); peakAcc = 0; peakCount = 0; }
                    }
                    written += frames;
                    FramesWritten = written;
                }
            }
            catch (Exception ex)
            {
                _error = ex;
                Logger.Error("Audio", "Error en la grabadora de audio", ex);
            }
            finally
            {
                foreach (var w in trackWriters) w.Dispose();
                mixWriter?.Dispose();
                _clock.Stop();
            }
        }

        private static void Write(WaveFileWriter w, float[] samples, int n, byte[] pcm)
        {
            for (int i = 0; i < n; i++)
            {
                int v = (int)(Math.Clamp(samples[i], -1f, 1f) * 32767f);
                pcm[2 * i] = (byte)v;
                pcm[2 * i + 1] = (byte)(v >> 8);
            }
            w.Write(pcm, 0, n * 2);
        }

        /// <summary>Detiene, cierra los archivos y codifica la mezcla (M4A/MP3). Bloquea hasta terminar.</summary>
        public AudioRecordingResult Stop()
        {
            if (_paused) Resume();
            var duration = Elapsed;
            _stop = true;
            _thread?.Join();
            foreach (var t in _tracks) t.Dispose();
            if (_error != null) throw new InvalidOperationException("La grabación de audio falló: " + _error.Message, _error);

            var result = new AudioRecordingResult { Duration = duration };
            lock (_peaks) result.Peaks = _peaks.ToArray();
            if (_separate)
                foreach (var t in _tracks)
                {
                    result.TrackPaths.Add($"{_basePath}_{SafeName(t.Name)}.wav");
                    result.TrackNames.Add(t.Name);
                }

            if (_mixFormat == "WAV") { result.MixPath = MixWavPath; return result; }
            var target = _basePath + (_mixFormat == "MP3" ? ".mp3" : ".m4a");
            try
            {
                MediaFoundationApi.Startup();
                using (var reader = new WaveFileReader(MixWavPath))
                {
                    if (_mixFormat == "MP3") MediaFoundationEncoder.EncodeToMp3(reader, target, 192000);
                    else MediaFoundationEncoder.EncodeToAac(reader, target, 192000);
                }
                File.Delete(MixWavPath);
                result.MixPath = target;
            }
            catch (Exception ex)
            {
                // Sin codificador disponible: se conserva la mezcla en WAV (sin pérdida) en lugar de fallar.
                Logger.Warn("Audio", $"No se pudo codificar la mezcla a {_mixFormat}; se conserva en WAV", ex);
                var wav = _basePath + ".wav";
                File.Move(MixWavPath, wav, true);
                result.MixPath = wav;
            }
            return result;
        }

        public void Dispose()
        {
            if (_thread != null && _thread.IsAlive) { _stop = true; _thread.Join(); }
            foreach (var t in _tracks) t.Dispose();
        }
    }
}
