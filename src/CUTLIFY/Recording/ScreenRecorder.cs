using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using Cutlify.Core;

namespace Cutlify.Recording
{
    public sealed class RecordingOptions
    {
        public Rectangle Region;          // píxeles físicos de pantalla
        public Size OutputSize;           // vacío = tamaño de la región
        public int Fps = 30;
        public string Codec = "H.264";
        public int BitrateBps;            // 0 = automático (calidad Automática)
        public bool IncludeCursor = true;
        public bool HighlightClicks;
        public bool HardwareEncoding = true;
        public AudioCapture Audio;        // null = sin audio
        public string OutputPath;
        public string ThumbPath;
    }

    /// <summary>Estadísticas reales: nunca se duplican fotogramas para aparentar más FPS.</summary>
    public readonly struct RecordingStats
    {
        public int TargetFps { get; init; }
        public long Frames { get; init; }
        public TimeSpan Elapsed { get; init; }
        public double RealFps => Elapsed.TotalSeconds > 0.5 ? Frames / Elapsed.TotalSeconds : 0;
        public long Dropped => Math.Max(0, (long)(Elapsed.TotalSeconds * TargetFps) - Frames);
    }

    /// <summary>
    /// Graba una región de la pantalla a MP4 con Media Foundation Sink Writer (H.264/HEVC/AV1 + AAC).
    /// Con <see cref="RecordingOptions.HardwareEncoding"/> MF usa el codificador por hardware si existe.
    /// </summary>
    public sealed class ScreenRecorder : IDisposable
    {
        private const int VK_LBUTTON = 0x01, VK_RBUTTON = 0x02;

        private readonly RecordingOptions _opt;
        private readonly int _width, _height;
        private Thread _thread, _audioThread;
        private volatile bool _stop;
        private volatile bool _paused;
        private readonly Stopwatch _clock = new Stopwatch();
        private long _pausedTicks;
        private long _pauseStartTicks;
        private readonly ManualResetEventSlim _started = new ManualResetEventSlim();
        private Exception _startError;
        private readonly Dictionary<IntPtr, Point> _hotspots = new Dictionary<IntPtr, Point>();
        private readonly object _writeLock = new object();
        private IMFSinkWriter _writer;
        private int _audioStream = -1;
        private long _lastLeftClick = long.MinValue, _lastRightClick = long.MinValue;

        public event Action<Exception> Failed;

        public bool IsPaused => _paused;
        public long FramesWritten { get; private set; }
        public long AudioFramesWritten { get; private set; }
        public bool HardwareUsed { get; private set; }

        public TimeSpan Elapsed
        {
            get
            {
                long paused = _pausedTicks + (_paused ? _clock.ElapsedTicks - _pauseStartTicks : 0);
                return TimeSpan.FromSeconds((_clock.ElapsedTicks - paused) / (double)Stopwatch.Frequency);
            }
        }

        public RecordingStats Stats => new RecordingStats { TargetFps = _opt.Fps, Frames = FramesWritten, Elapsed = Elapsed };

        public ScreenRecorder(RecordingOptions options)
        {
            _opt = options;
            var size = options.OutputSize.IsEmpty ? options.Region.Size : options.OutputSize;
            // Los codificadores exigen dimensiones pares.
            _width = Math.Max(2, size.Width & ~1);
            _height = Math.Max(2, size.Height & ~1);
            if (_opt.BitrateBps <= 0) _opt.BitrateBps = VideoSettings.AutoBitrate(_width, _height, _opt.Fps, _opt.Codec, "Automática");
        }

        public Size VideoSize => new Size(_width, _height);
        public int BitrateBps => _opt.BitrateBps;

        /// <summary>Compatibilidad con pruebas anteriores: bitrate automático con calidad.</summary>
        public static int ComputeBitrate(int w, int h, int fps, string quality) => VideoSettings.AutoBitrate(w, h, fps, "H.264", quality);

        /// <summary>Inicia la grabación. Lanza excepción si el codificador no se pudo crear.</summary>
        public void Start()
        {
            _thread = new Thread(Run) { IsBackground = true, Name = "CUTLIFY Recorder", Priority = ThreadPriority.AboveNormal };
            _thread.SetApartmentState(ApartmentState.MTA);
            _thread.Start();
            _started.Wait();
            if (_startError != null) throw _startError;
        }

        public void Pause()
        {
            if (_paused) return;
            _pauseStartTicks = _clock.ElapsedTicks;
            _paused = true;
        }

        public void Resume()
        {
            if (!_paused) return;
            _pausedTicks += _clock.ElapsedTicks - _pauseStartTicks;
            _paused = false;
        }

        /// <summary>Detiene y finaliza el MP4. Bloquea hasta que el archivo está cerrado.</summary>
        public void Stop()
        {
            if (_paused) Resume();
            _stop = true;
            _thread?.Join();
        }

        private IMFSinkWriter CreateWriter(bool hardware, out int videoStream)
        {
            MF.MFCreateAttributes(out var attrs, 3);
            attrs.SetUINT32(MF.READWRITE_ENABLE_HARDWARE_TRANSFORMS, hardware ? 1 : 0);
            attrs.SetUINT32(MF.SINK_WRITER_DISABLE_THROTTLING, 1);
            attrs.SetGUID(MF.TRANSCODE_CONTAINERTYPE, MF.TranscodeContainerType_MPEG4);

            IMFSinkWriter writer = null;
            var types = new List<IMFMediaType>();
            IMFMediaType NewType() { MF.MFCreateMediaType(out var t); types.Add(t); return t; }
            try
            {
                MF.MFCreateSinkWriterFromURL(_opt.OutputPath, IntPtr.Zero, attrs, out writer);

                var outType = NewType();
                outType.SetGUID(MF.MT_MAJOR_TYPE, MF.MediaType_Video);
                outType.SetGUID(MF.MT_SUBTYPE, VideoSettings.Subtype(_opt.Codec));
                outType.SetUINT32(MF.MT_AVG_BITRATE, _opt.BitrateBps);
                outType.SetUINT32(MF.MT_INTERLACE_MODE, 2); // progresivo
                outType.SetUINT64(MF.MT_FRAME_SIZE, MF.Pack(_width, _height));
                outType.SetUINT64(MF.MT_FRAME_RATE, MF.Pack(_opt.Fps, 1));
                outType.SetUINT64(MF.MT_PIXEL_ASPECT_RATIO, MF.Pack(1, 1));
                writer.AddStream(outType, out videoStream);

                var inType = NewType();
                inType.SetGUID(MF.MT_MAJOR_TYPE, MF.MediaType_Video);
                inType.SetGUID(MF.MT_SUBTYPE, MF.VideoFormat_RGB32);
                inType.SetUINT32(MF.MT_INTERLACE_MODE, 2);
                inType.SetUINT32(MF.MT_DEFAULT_STRIDE, _width * 4); // positivo = de arriba a abajo
                inType.SetUINT64(MF.MT_FRAME_SIZE, MF.Pack(_width, _height));
                inType.SetUINT64(MF.MT_FRAME_RATE, MF.Pack(_opt.Fps, 1));
                inType.SetUINT64(MF.MT_PIXEL_ASPECT_RATIO, MF.Pack(1, 1));
                writer.SetInputMediaType(videoStream, inType, null);

                if (_opt.Audio != null && _opt.Audio.HasSources)
                {
                    var aOut = NewType();
                    aOut.SetGUID(MF.MT_MAJOR_TYPE, MF.MediaType_Audio);
                    aOut.SetGUID(MF.MT_SUBTYPE, MF.AudioFormat_AAC);
                    aOut.SetUINT32(MF.MT_AUDIO_BITS_PER_SAMPLE, 16);
                    aOut.SetUINT32(MF.MT_AUDIO_SAMPLES_PER_SECOND, AudioCapture.SampleRate);
                    aOut.SetUINT32(MF.MT_AUDIO_NUM_CHANNELS, AudioCapture.Channels);
                    aOut.SetUINT32(MF.MT_AUDIO_AVG_BYTES_PER_SECOND, 24000); // 192 kbps
                    writer.AddStream(aOut, out _audioStream);

                    var aIn = NewType();
                    aIn.SetGUID(MF.MT_MAJOR_TYPE, MF.MediaType_Audio);
                    aIn.SetGUID(MF.MT_SUBTYPE, MF.AudioFormat_PCM);
                    aIn.SetUINT32(MF.MT_AUDIO_BITS_PER_SAMPLE, 16);
                    aIn.SetUINT32(MF.MT_AUDIO_SAMPLES_PER_SECOND, AudioCapture.SampleRate);
                    aIn.SetUINT32(MF.MT_AUDIO_NUM_CHANNELS, AudioCapture.Channels);
                    aIn.SetUINT32(MF.MT_AUDIO_BLOCK_ALIGNMENT, AudioCapture.Channels * 2);
                    aIn.SetUINT32(MF.MT_AUDIO_AVG_BYTES_PER_SECOND, AudioCapture.SampleRate * AudioCapture.Channels * 2);
                    writer.SetInputMediaType(_audioStream, aIn, null);
                }

                writer.BeginWriting();
                return writer;
            }
            catch
            {
                _audioStream = -1;
                if (writer != null) Marshal.ReleaseComObject(writer);
                throw;
            }
            finally
            {
                Marshal.ReleaseComObject(attrs);
                foreach (var t in types) Marshal.ReleaseComObject(t);
            }
        }

        private void Run()
        {
            IntPtr screenDc = IntPtr.Zero, memDc = IntPtr.Zero, dib = IntPtr.Zero, old = IntPtr.Zero;
            bool mfStarted = false, timer = false;
            try
            {
                MF.MFStartup(MF.Version, 0);
                mfStarted = true;
                int stream;
                try
                {
                    _writer = CreateWriter(_opt.HardwareEncoding, out stream);
                    HardwareUsed = _opt.HardwareEncoding;
                }
                catch (Exception ex) when (_opt.HardwareEncoding)
                {
                    Logger.Warn("Recording", "Codificador por hardware no disponible; usando software", ex);
                    try { System.IO.File.Delete(_opt.OutputPath); } catch { }
                    _writer = CreateWriter(false, out stream);
                }

                screenDc = Native.GetDC(IntPtr.Zero);
                memDc = Native.CreateCompatibleDC(screenDc);
                var bmi = new Native.BITMAPINFOHEADER
                {
                    biSize = Marshal.SizeOf<Native.BITMAPINFOHEADER>(),
                    biWidth = _width,
                    biHeight = -_height, // top-down
                    biPlanes = 1,
                    biBitCount = 32
                };
                dib = Native.CreateDIBSection(screenDc, ref bmi, 0, out IntPtr bits, IntPtr.Zero, 0);
                if (dib == IntPtr.Zero) throw new InvalidOperationException("No se pudo crear el búfer de captura");
                old = Native.SelectObject(memDc, dib);
                bool scaled = _width != _opt.Region.Width || _height != _opt.Region.Height;
                if (scaled) Native.SetStretchBltMode(memDc, Native.HALFTONE);

                Native.timeBeginPeriod(1);
                timer = true;
                _clock.Start();
                _started.Set();

                if (_audioStream >= 0)
                {
                    _audioThread = new Thread(AudioLoop) { IsBackground = true, Name = "CUTLIFY Audio", Priority = ThreadPriority.AboveNormal };
                    _audioThread.SetApartmentState(ApartmentState.MTA);
                    _audioThread.Start();
                }

                int frameBytes = _width * _height * 4;
                long frameDuration = 10_000_000L / _opt.Fps;
                long lastTime = -1;
                double interval = 1.0 / _opt.Fps;
                double next = 0;

                while (!_stop)
                {
                    if (_paused) { Thread.Sleep(10); continue; }

                    double now = Elapsed.TotalSeconds;
                    if (now < next)
                    {
                        int ms = (int)((next - now) * 1000);
                        if (ms > 1) Thread.Sleep(ms - 1); else Thread.SpinWait(200);
                        continue;
                    }
                    next = Math.Max(next + interval, now); // no acumular retraso; los fotogramas no capturados cuentan como perdidos

                    var r = _opt.Region;
                    if (scaled) Native.StretchBlt(memDc, 0, 0, _width, _height, screenDc, r.X, r.Y, r.Width, r.Height, Native.SRCCOPY);
                    else Native.BitBlt(memDc, 0, 0, _width, _height, screenDc, r.X, r.Y, Native.SRCCOPY);
                    if (_opt.HighlightClicks) DrawClicks(memDc);
                    if (_opt.IncludeCursor) DrawCursor(memDc);

                    long ts = (long)(Elapsed.TotalSeconds * 10_000_000);
                    if (ts <= lastTime) ts = lastTime + 1;
                    lastTime = ts;
                    WriteVideoFrame(stream, bits, frameBytes, ts, frameDuration);

                    if (FramesWritten == 0 && _opt.ThumbPath != null) SaveThumb(bits);
                    FramesWritten++;
                }

                _audioThread?.Join();
                if (FramesWritten > 0) lock (_writeLock) _writer.Finalize_();
            }
            catch (Exception ex)
            {
                Logger.Error("Recording", "Error durante la grabación", ex);
                _stop = true;
                if (!_started.IsSet) { _startError = ex; _started.Set(); }
                else
                {
                    try { _audioThread?.Join(); if (FramesWritten > 0) lock (_writeLock) _writer?.Finalize_(); } catch { }
                    Failed?.Invoke(ex);
                }
            }
            finally
            {
                if (timer) Native.timeEndPeriod(1);
                if (old != IntPtr.Zero) Native.SelectObject(memDc, old);
                if (dib != IntPtr.Zero) Native.DeleteObject(dib);
                if (memDc != IntPtr.Zero) Native.DeleteDC(memDc);
                if (screenDc != IntPtr.Zero) Native.ReleaseDC(IntPtr.Zero, screenDc);
                if (_writer != null) { Marshal.ReleaseComObject(_writer); _writer = null; }
                if (mfStarted) { try { MF.MFShutdown(); } catch { } }
                _clock.Stop();
                _started.Set();
            }
        }

        /// <summary>Escribe el audio mezclado al ritmo del reloj de la grabación (pausas excluidas).</summary>
        private void AudioLoop()
        {
            const int rate = AudioCapture.SampleRate, ch = AudioCapture.Channels;
            var floats = new float[rate * ch];
            var pcm = new byte[rate * ch * 2];
            long written = 0;
            try
            {
                while (true)
                {
                    bool stopping = _stop;
                    if (_paused) { _opt.Audio.DiscardBuffered(); Thread.Sleep(10); continue; }
                    long target = (long)(Elapsed.TotalSeconds * rate);
                    long need = target - written;
                    if (need >= rate / 100 || (stopping && need > 0))
                    {
                        int frames = (int)Math.Min(need, rate);
                        _opt.Audio.Read(floats, frames);
                        int n = frames * ch;
                        for (int i = 0; i < n; i++)
                        {
                            int v = (int)(Math.Clamp(floats[i], -1f, 1f) * 32767f);
                            pcm[2 * i] = (byte)v;
                            pcm[2 * i + 1] = (byte)(v >> 8);
                        }
                        WriteAudio(pcm, n * 2, written * 10_000_000L / rate, frames * 10_000_000L / rate);
                        written += frames;
                        AudioFramesWritten = written;
                        continue;
                    }
                    if (stopping) break;
                    Thread.Sleep(10);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Audio", "Error al escribir el audio; la grabación continúa sin audio", ex);
            }
        }

        private void WriteAudio(byte[] data, int length, long time, long duration)
        {
            MF.MFCreateMemoryBuffer(length, out var buffer);
            IMFSample sample = null;
            try
            {
                buffer.Lock(out IntPtr dest, out _, out _);
                Marshal.Copy(data, 0, dest, length);
                buffer.Unlock();
                buffer.SetCurrentLength(length);
                MF.MFCreateSample(out sample);
                sample.AddBuffer(buffer);
                sample.SetSampleTime(time);
                sample.SetSampleDuration(duration);
                lock (_writeLock) _writer.WriteSample(_audioStream, sample);
            }
            finally
            {
                if (sample != null) Marshal.ReleaseComObject(sample);
                Marshal.ReleaseComObject(buffer);
            }
        }

        private unsafe void WriteVideoFrame(int stream, IntPtr bits, int frameBytes, long time, long duration)
        {
            MF.MFCreateMemoryBuffer(frameBytes, out var buffer);
            IMFSample sample = null;
            try
            {
                buffer.Lock(out IntPtr dest, out _, out _);
                Buffer.MemoryCopy((void*)bits, (void*)dest, frameBytes, frameBytes);
                buffer.Unlock();
                buffer.SetCurrentLength(frameBytes);
                MF.MFCreateSample(out sample);
                sample.AddBuffer(buffer);
                sample.SetSampleTime(time);
                sample.SetSampleDuration(duration);
                lock (_writeLock) _writer.WriteSample(stream, sample);
            }
            finally
            {
                if (sample != null) Marshal.ReleaseComObject(sample);
                Marshal.ReleaseComObject(buffer);
            }
        }

        private double ScaleX => _width / (double)_opt.Region.Width;
        private double ScaleY => _height / (double)_opt.Region.Height;

        private void DrawCursor(IntPtr hdc)
        {
            var ci = new Native.CURSORINFO { cbSize = Marshal.SizeOf<Native.CURSORINFO>() };
            if (!Native.GetCursorInfo(ref ci) || (ci.flags & Native.CURSOR_SHOWING) == 0) return;
            if (!_hotspots.TryGetValue(ci.hCursor, out var hot))
            {
                if (Native.GetIconInfo(ci.hCursor, out var info))
                {
                    hot = new Point(info.xHotspot, info.yHotspot);
                    if (info.hbmMask != IntPtr.Zero) Native.DeleteObject(info.hbmMask);
                    if (info.hbmColor != IntPtr.Zero) Native.DeleteObject(info.hbmColor);
                }
                _hotspots[ci.hCursor] = hot;
            }
            int x = (int)((ci.ptScreenPos.X - _opt.Region.X) * ScaleX) - hot.X;
            int y = (int)((ci.ptScreenPos.Y - _opt.Region.Y) * ScaleY) - hot.Y;
            if (x < -64 || y < -64 || x > _width || y > _height) return;
            Native.DrawIconEx(hdc, x, y, ci.hCursor, 0, 0, 0, IntPtr.Zero, Native.DI_NORMAL);
        }

        /// <summary>Dibuja un círculo en el vídeo al hacer clic (no modifica el escritorio real).</summary>
        private void DrawClicks(IntPtr hdc)
        {
            long now = _clock.ElapsedMilliseconds;
            if ((Native.GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0) _lastLeftClick = now;
            if ((Native.GetAsyncKeyState(VK_RBUTTON) & 0x8000) != 0) _lastRightClick = now;
            bool left = now - _lastLeftClick < 300, right = now - _lastRightClick < 300;
            if (!left && !right) return;
            if (!Native.GetCursorPos(out var p)) return;
            int x = (int)((p.X - _opt.Region.X) * ScaleX), y = (int)((p.Y - _opt.Region.Y) * ScaleY);
            int radius = Math.Max(10, (int)(20 * ScaleX));
            int color = right ? 0xB67EFF : 0xFF5C7C; // COLORREF (0x00BBGGRR): rosa / lavanda
            var pen = Native.CreatePen(0, Math.Max(2, radius / 6), color);
            var oldPen = Native.SelectObject(hdc, pen);
            var oldBrush = Native.SelectObject(hdc, Native.GetStockObject(Native.NULL_BRUSH));
            Native.Ellipse(hdc, x - radius, y - radius, x + radius, y + radius);
            Native.SelectObject(hdc, oldBrush);
            Native.SelectObject(hdc, oldPen);
            Native.DeleteObject(pen);
        }

        private void SaveThumb(IntPtr bits)
        {
            try
            {
                using var frame = new Bitmap(_width, _height, _width * 4, PixelFormat.Format32bppRgb, bits);
                Capture.ImageUtil.SaveThumbnail(frame, _opt.ThumbPath);
            }
            catch (Exception ex)
            {
                Logger.Warn("Recording", "No se pudo crear la miniatura", ex);
            }
        }

        public void Dispose()
        {
            if (_thread != null && _thread.IsAlive) Stop();
            _opt.Audio?.Dispose();
            _started.Dispose();
        }
    }
}
