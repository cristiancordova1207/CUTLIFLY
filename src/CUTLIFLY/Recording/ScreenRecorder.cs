using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using Cutlifly.Core;

namespace Cutlifly.Recording
{
    public sealed class RecordingOptions
    {
        public Rectangle Region;        // píxeles físicos de pantalla
        public int Fps = 30;
        public string Quality = "Media";
        public bool IncludeCursor = true;
        public bool HardwareEncoding = true;
        public string OutputPath;
        public string ThumbPath;
    }

    /// <summary>
    /// Graba una región de la pantalla a MP4 (H.264) usando Media Foundation Sink Writer.
    /// Con <see cref="RecordingOptions.HardwareEncoding"/> MF usa el codificador por hardware
    /// (NVENC / Quick Sync / AMF) si existe, y si no, el codificador de software de Windows.
    /// </summary>
    public sealed class ScreenRecorder : IDisposable
    {
        private readonly RecordingOptions _opt;
        private readonly int _width, _height;
        private Thread _thread;
        private volatile bool _stop;
        private volatile bool _paused;
        private readonly Stopwatch _clock = new Stopwatch();
        private long _pausedTicks;
        private long _pauseStartTicks;
        private readonly ManualResetEventSlim _started = new ManualResetEventSlim();
        private Exception _startError;
        private readonly Dictionary<IntPtr, Point> _hotspots = new Dictionary<IntPtr, Point>();

        public event Action<Exception> Failed;

        public bool IsPaused => _paused;
        public int FramesWritten { get; private set; }

        public TimeSpan Elapsed
        {
            get
            {
                long paused = _pausedTicks + (_paused ? _clock.ElapsedTicks - _pauseStartTicks : 0);
                return TimeSpan.FromSeconds((_clock.ElapsedTicks - paused) / (double)Stopwatch.Frequency);
            }
        }

        public ScreenRecorder(RecordingOptions options)
        {
            _opt = options;
            // H.264 exige dimensiones pares.
            _width = Math.Max(2, options.Region.Width & ~1);
            _height = Math.Max(2, options.Region.Height & ~1);
        }

        public Size VideoSize => new Size(_width, _height);

        public static int ComputeBitrate(int w, int h, int fps, string quality)
        {
            double bpp = quality switch { "Baja" => 0.05, "Alta" => 0.16, _ => 0.09 };
            long br = (long)(w * (double)h * fps * bpp);
            return (int)Math.Clamp(br, 1_000_000, 60_000_000);
        }

        /// <summary>Inicia la grabación. Lanza excepción si el codificador no se pudo crear.</summary>
        public void Start()
        {
            _thread = new Thread(Run) { IsBackground = true, Name = "CUTLIFLY Recorder", Priority = ThreadPriority.AboveNormal };
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

        private IMFSinkWriter CreateWriter(bool hardware, out int stream)
        {
            MF.MFCreateAttributes(out var attrs, 3);
            attrs.SetUINT32(MF.READWRITE_ENABLE_HARDWARE_TRANSFORMS, hardware ? 1 : 0);
            attrs.SetUINT32(MF.SINK_WRITER_DISABLE_THROTTLING, 1);
            attrs.SetGUID(MF.TRANSCODE_CONTAINERTYPE, MF.TranscodeContainerType_MPEG4);

            IMFSinkWriter writer = null;
            IMFMediaType outType = null, inType = null;
            try
            {
                MF.MFCreateSinkWriterFromURL(_opt.OutputPath, IntPtr.Zero, attrs, out writer);

                MF.MFCreateMediaType(out outType);
                outType.SetGUID(MF.MT_MAJOR_TYPE, MF.MediaType_Video);
                outType.SetGUID(MF.MT_SUBTYPE, MF.VideoFormat_H264);
                outType.SetUINT32(MF.MT_AVG_BITRATE, ComputeBitrate(_width, _height, _opt.Fps, _opt.Quality));
                outType.SetUINT32(MF.MT_INTERLACE_MODE, 2); // progresivo
                outType.SetUINT64(MF.MT_FRAME_SIZE, MF.Pack(_width, _height));
                outType.SetUINT64(MF.MT_FRAME_RATE, MF.Pack(_opt.Fps, 1));
                outType.SetUINT64(MF.MT_PIXEL_ASPECT_RATIO, MF.Pack(1, 1));
                writer.AddStream(outType, out stream);

                MF.MFCreateMediaType(out inType);
                inType.SetGUID(MF.MT_MAJOR_TYPE, MF.MediaType_Video);
                inType.SetGUID(MF.MT_SUBTYPE, MF.VideoFormat_RGB32);
                inType.SetUINT32(MF.MT_INTERLACE_MODE, 2);
                inType.SetUINT32(MF.MT_DEFAULT_STRIDE, _width * 4); // positivo = de arriba a abajo
                inType.SetUINT64(MF.MT_FRAME_SIZE, MF.Pack(_width, _height));
                inType.SetUINT64(MF.MT_FRAME_RATE, MF.Pack(_opt.Fps, 1));
                inType.SetUINT64(MF.MT_PIXEL_ASPECT_RATIO, MF.Pack(1, 1));
                writer.SetInputMediaType(stream, inType, null);
                writer.BeginWriting();
                return writer;
            }
            catch
            {
                if (writer != null) Marshal.ReleaseComObject(writer);
                throw;
            }
            finally
            {
                Marshal.ReleaseComObject(attrs);
                if (outType != null) Marshal.ReleaseComObject(outType);
                if (inType != null) Marshal.ReleaseComObject(inType);
            }
        }

        private void Run()
        {
            IMFSinkWriter writer = null;
            IntPtr screenDc = IntPtr.Zero, memDc = IntPtr.Zero, dib = IntPtr.Zero, old = IntPtr.Zero;
            bool mfStarted = false, timer = false;
            try
            {
                MF.MFStartup(MF.Version, 0);
                mfStarted = true;
                int stream;
                try
                {
                    writer = CreateWriter(_opt.HardwareEncoding, out stream);
                }
                catch (Exception ex) when (_opt.HardwareEncoding)
                {
                    Logger.Warn("Recording", "Codificador por hardware no disponible; usando software", ex);
                    try { System.IO.File.Delete(_opt.OutputPath); } catch { }
                    writer = CreateWriter(false, out stream);
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

                Native.timeBeginPeriod(1);
                timer = true;
                _clock.Start();
                _started.Set();

                int frameBytes = _width * _height * 4;
                long frameDuration = 10_000_000L / _opt.Fps;
                long lastTime = -1;
                double interval = 1.0 / _opt.Fps;
                double next = 0;

                while (!_stop)
                {
                    if (_paused) { Thread.Sleep(15); continue; }

                    double now = Elapsed.TotalSeconds;
                    if (now < next)
                    {
                        int ms = (int)((next - now) * 1000);
                        if (ms > 0) Thread.Sleep(ms);
                        continue;
                    }
                    next = Math.Max(next + interval, now); // no acumular retraso

                    Native.BitBlt(memDc, 0, 0, _width, _height, screenDc, _opt.Region.X, _opt.Region.Y, Native.SRCCOPY);
                    if (_opt.IncludeCursor) DrawCursor(memDc);

                    long ts = (long)(Elapsed.TotalSeconds * 10_000_000);
                    if (ts <= lastTime) ts = lastTime + 1;
                    lastTime = ts;
                    WriteFrame(writer, stream, bits, frameBytes, ts, frameDuration);

                    if (FramesWritten == 0 && _opt.ThumbPath != null) SaveThumb(bits);
                    FramesWritten++;
                }

                if (FramesWritten > 0) writer.Finalize_();
            }
            catch (Exception ex)
            {
                Logger.Error("Recording", "Error durante la grabación", ex);
                if (!_started.IsSet) { _startError = ex; _started.Set(); }
                else Failed?.Invoke(ex);
            }
            finally
            {
                if (timer) Native.timeEndPeriod(1);
                if (old != IntPtr.Zero) Native.SelectObject(memDc, old);
                if (dib != IntPtr.Zero) Native.DeleteObject(dib);
                if (memDc != IntPtr.Zero) Native.DeleteDC(memDc);
                if (screenDc != IntPtr.Zero) Native.ReleaseDC(IntPtr.Zero, screenDc);
                if (writer != null) Marshal.ReleaseComObject(writer);
                if (mfStarted) { try { MF.MFShutdown(); } catch { } }
                _clock.Stop();
                _started.Set();
            }
        }

        private static unsafe void WriteFrame(IMFSinkWriter writer, int stream, IntPtr bits, int frameBytes, long time, long duration)
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
                writer.WriteSample(stream, sample);
            }
            finally
            {
                if (sample != null) Marshal.ReleaseComObject(sample);
                Marshal.ReleaseComObject(buffer);
            }
        }

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
            int x = ci.ptScreenPos.X - _opt.Region.X - hot.X;
            int y = ci.ptScreenPos.Y - _opt.Region.Y - hot.Y;
            if (x < -64 || y < -64 || x > _width || y > _height) return;
            Native.DrawIconEx(hdc, x, y, ci.hCursor, 0, 0, 0, IntPtr.Zero, Native.DI_NORMAL);
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
            _started.Dispose();
        }
    }
}
