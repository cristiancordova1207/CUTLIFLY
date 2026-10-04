using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Cutlify.Core;

namespace Cutlify.Recording
{
    public sealed class AudioDevice
    {
        public string Id;
        public string Name;
        public override string ToString() => Name;
    }

    /// <summary>
    /// Captura audio del sistema (WASAPI loopback) y/o micrófono, los convierte a 48 kHz estéreo y los
    /// mezcla en una sola pista. La lectura se hace al ritmo del reloj de la grabación: si una fuente no
    /// entrega datos (p. ej. el loopback en silencio) se rellena con silencio, así el audio no se desincroniza.
    /// </summary>
    public sealed class AudioCapture : IDisposable
    {
        public const int SampleRate = 48000;
        public const int Channels = 2;
        private static readonly TimeSpan MaxLatency = TimeSpan.FromMilliseconds(200);

        private readonly List<IWaveIn> _captures = new List<IWaveIn>();
        private readonly List<BufferedWaveProvider> _buffers = new List<BufferedWaveProvider>();
        private readonly MixingSampleProvider _mixer;
        private readonly object _sync = new object();
        private byte[] _scratch = new byte[0];

        public bool HasSystem { get; private set; }
        public bool HasMicrophone { get; private set; }
        public bool MicrophoneBlocked { get; private set; }
        public List<string> Warnings { get; } = new List<string>();
        public bool HasSources => _mixer.MixerInputs.Any();

        public string Description =>
            HasSystem && HasMicrophone ? "Sistema + Micrófono" : HasSystem ? "Audio del sistema" : HasMicrophone ? "Micrófono" : "Sin audio";

        private AudioCapture()
        {
            _mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels)) { ReadFully = true };
        }

        /// <summary>Crea la captura con los dispositivos indicados (null = predeterminado). Las fuentes que fallen se omiten con aviso.</summary>
        public static AudioCapture Create(bool system, string systemId, bool microphone, string micId)
        {
            var a = new AudioCapture();
            using var enumerator = new MMDeviceEnumerator();
            if (system)
            {
                try
                {
                    var dev = FindDevice(enumerator, systemId, DataFlow.Render);
                    a.AddCapture(new WasapiLoopbackCapture(dev));
                    a.HasSystem = true;
                }
                catch (Exception ex)
                {
                    a.Warnings.Add("No se pudo capturar el audio del sistema.");
                    Logger.Warn("Audio", "Loopback no disponible", ex);
                }
            }
            if (microphone)
            {
                try
                {
                    var dev = FindDevice(enumerator, micId, DataFlow.Capture);
                    a.AddCapture(new WasapiCapture(dev, true, 50));
                    a.HasMicrophone = true;
                }
                catch (Exception ex) when (IsAccessDenied(ex))
                {
                    a.MicrophoneBlocked = true;
                    a.Warnings.Add("Windows no permite que CUTLIFY utilice el micrófono.");
                    Logger.Warn("Audio", "Acceso al micrófono denegado por Windows", ex);
                }
                catch (Exception ex)
                {
                    a.Warnings.Add("No se pudo capturar el micrófono.");
                    Logger.Warn("Audio", "Micrófono no disponible", ex);
                }
            }
            return a;
        }

        /// <summary>Fuente sintética (pruebas automatizadas sin dispositivos de audio).</summary>
        internal static AudioCapture FromProvider(ISampleProvider provider)
        {
            var a = new AudioCapture();
            a._mixer.AddMixerInput(Normalize(provider));
            a.HasSystem = true;
            return a;
        }

        private static bool IsAccessDenied(Exception ex) =>
            ex is UnauthorizedAccessException || (ex is COMException c && c.HResult == unchecked((int)0x80070005));

        private static MMDevice FindDevice(MMDeviceEnumerator e, string id, DataFlow flow)
        {
            if (!string.IsNullOrEmpty(id))
            {
                try
                {
                    var d = e.GetDevice(id);
                    if (d.State == DeviceState.Active) return d;
                }
                catch { }
                Logger.Warn("Audio", "Dispositivo guardado no disponible; se usa el predeterminado");
            }
            return e.GetDefaultAudioEndpoint(flow, flow == DataFlow.Capture ? Role.Communications : Role.Multimedia);
        }

        private void AddCapture(IWaveIn capture)
        {
            var wf = capture.WaveFormat;
            // Normaliza WAVE_FORMAT_EXTENSIBLE a un formato equivalente que NAudio sabe convertir.
            var plain = wf.BitsPerSample == 32 ? WaveFormat.CreateIeeeFloatWaveFormat(wf.SampleRate, wf.Channels) : new WaveFormat(wf.SampleRate, wf.BitsPerSample, wf.Channels);
            var buffer = new BufferedWaveProvider(plain) { ReadFully = true, DiscardOnBufferOverflow = true, BufferDuration = TimeSpan.FromSeconds(2) };
            capture.DataAvailable += (_, e) => { lock (_sync) buffer.AddSamples(e.Buffer, 0, e.BytesRecorded); };
            capture.RecordingStopped += (_, e) => { if (e.Exception != null) Logger.Warn("Audio", "La captura de audio se detuvo", e.Exception); };
            _captures.Add(capture);
            _buffers.Add(buffer);
            _mixer.AddMixerInput(Normalize(buffer.ToSampleProvider()));
        }

        /// <summary>Convierte cualquier fuente a 48 kHz estéreo float.</summary>
        private static ISampleProvider Normalize(ISampleProvider src)
        {
            if (src.WaveFormat.Channels == 1) src = new MonoToStereoSampleProvider(src);
            else if (src.WaveFormat.Channels > 2)
            {
                var mux = new MultiplexingSampleProvider(new[] { src }, 2);
                mux.ConnectInputToOutput(0, 0);
                mux.ConnectInputToOutput(1, 1);
                src = mux;
            }
            if (src.WaveFormat.SampleRate != SampleRate) src = new WdlResamplingSampleProvider(src, SampleRate);
            return new PadWithSilence(src);
        }

        /// <summary>Completa con silencio: el mezclador descarta las entradas que devuelven menos muestras.</summary>
        private sealed class PadWithSilence : ISampleProvider
        {
            private readonly ISampleProvider _src;
            public PadWithSilence(ISampleProvider src) => _src = src;
            public WaveFormat WaveFormat => _src.WaveFormat;

            public int Read(float[] buffer, int offset, int count)
            {
                int n = _src.Read(buffer, offset, count);
                if (n < count) Array.Clear(buffer, offset + Math.Max(0, n), count - Math.Max(0, n));
                return count;
            }
        }

        public void Start()
        {
            foreach (var c in _captures.ToList())
            {
                try { c.StartRecording(); }
                catch (Exception ex)
                {
                    bool mic = c is WasapiCapture && !(c is WasapiLoopbackCapture);
                    if (mic && IsAccessDenied(ex)) { MicrophoneBlocked = true; Warnings.Add("Windows no permite que CUTLIFY utilice el micrófono."); }
                    else Warnings.Add(mic ? "No se pudo iniciar el micrófono." : "No se pudo iniciar el audio del sistema.");
                    if (mic) HasMicrophone = false; else HasSystem = false;
                    Logger.Warn("Audio", "No se pudo iniciar una fuente de audio", ex);
                    int i = _captures.IndexOf(c);
                    var input = _mixer.MixerInputs.ElementAt(i);
                    _mixer.RemoveMixerInput(input);
                    _captures.RemoveAt(i);
                    _buffers.RemoveAt(i);
                    c.Dispose();
                }
            }
        }

        /// <summary>Lee <paramref name="frames"/> fotogramas mezclados (float intercalado).</summary>
        public int Read(float[] buffer, int frames)
        {
            lock (_sync)
            {
                TrimLatency();
                return _mixer.Read(buffer, 0, frames * Channels) / Channels;
            }
        }

        /// <summary>Descarta lo capturado (durante la pausa).</summary>
        public void DiscardBuffered()
        {
            lock (_sync) foreach (var b in _buffers) b.ClearBuffer();
        }

        // Si un dispositivo entrega algo más rápido que el reloj, se descarta el exceso para no acumular retraso.
        private void TrimLatency()
        {
            foreach (var b in _buffers)
            {
                if (b.BufferedDuration <= MaxLatency) continue;
                int excess = b.BufferedBytes - (int)(b.WaveFormat.AverageBytesPerSecond * MaxLatency.TotalSeconds);
                excess -= excess % b.WaveFormat.BlockAlign;
                if (excess <= 0) continue;
                if (_scratch.Length < excess) _scratch = new byte[excess];
                b.ReadFully = false;
                b.Read(_scratch, 0, excess);
                b.ReadFully = true;
            }
        }

        public void Dispose()
        {
            foreach (var c in _captures)
            {
                try { c.StopRecording(); } catch { }
                try { c.Dispose(); } catch { }
            }
            _captures.Clear();
        }

        // ---- Dispositivos ----

        public static List<AudioDevice> ListDevices(bool capture)
        {
            var list = new List<AudioDevice>();
            try
            {
                using var e = new MMDeviceEnumerator();
                foreach (var d in e.EnumerateAudioEndPoints(capture ? DataFlow.Capture : DataFlow.Render, DeviceState.Active))
                    list.Add(new AudioDevice { Id = d.ID, Name = d.FriendlyName });
            }
            catch (Exception ex)
            {
                Logger.Warn("Audio", "No se pudieron enumerar dispositivos de audio", ex);
            }
            return list;
        }
    }

    /// <summary>Prueba del micrófono: abre el dispositivo solo mientras dura la prueba y publica el nivel (0–1).</summary>
    public sealed class MicrophoneTester : IDisposable
    {
        private WasapiCapture _capture;

        public event Action<float> Level;
        public event Action<string> Failed;

        public bool Start(string deviceId)
        {
            Stop();
            try
            {
                using var e = new MMDeviceEnumerator();
                MMDevice dev = null;
                if (!string.IsNullOrEmpty(deviceId)) { try { dev = e.GetDevice(deviceId); } catch { } }
                dev ??= e.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                _capture = new WasapiCapture(dev, true, 50);
                var wf = _capture.WaveFormat;
                _capture.DataAvailable += (_, a) => Level?.Invoke(Peak(a.Buffer, a.BytesRecorded, wf));
                _capture.StartRecording();
                return true;
            }
            catch (Exception ex)
            {
                bool denied = ex is UnauthorizedAccessException || (ex is COMException c && c.HResult == unchecked((int)0x80070005));
                Failed?.Invoke(denied ? "Windows no permite que CUTLIFY utilice el micrófono." : "No se pudo abrir el micrófono: " + ex.Message);
                Stop();
                return false;
            }
        }

        private static float Peak(byte[] buf, int count, WaveFormat wf)
        {
            float max = 0;
            if (wf.BitsPerSample == 32)
                for (int i = 0; i + 4 <= count; i += 4) max = Math.Max(max, Math.Abs(BitConverter.ToSingle(buf, i)));
            else if (wf.BitsPerSample == 16)
                for (int i = 0; i + 2 <= count; i += 2) max = Math.Max(max, Math.Abs(BitConverter.ToInt16(buf, i) / 32768f));
            return Math.Min(1, max);
        }

        public void Stop()
        {
            if (_capture == null) return;
            try { _capture.StopRecording(); } catch { }
            _capture.Dispose();
            _capture = null;
        }

        public void Dispose() => Stop();
    }
}
