using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Cutlify.Capture;
using Cutlify.Clipboard;
using Cutlify.Core;
using Cutlify.Notifications;
using Cutlify.Recording;
using Cutlify.Settings;
using Cutlify.Storage;
using Cutlify.UI;
using Drawing = System.Drawing;

namespace Cutlify
{
    public sealed partial class AppController
    {
        private ScreenRecorder _recorder;
        private RecordingOptions _recOptions;
        private HistoryItem _recItem;
        private string _recMarker;
        private RecordingBar _bar;
        private RegionFrame _frame;
        private bool _recordFromMain;

        public bool IsRecording => _recorder != null;

        public void ToggleQuickRecord()
        {
            if (IsRecording) StopRecording();
            else StartRecording(CaptureMode.Rectangle, 0, false);
        }

        /// <summary>
        /// Pide consentimiento propio antes del primer uso del micrófono. Devuelve true si se puede usar.
        /// Si el usuario elige «Ahora no», se recuerda y no se accede al micrófono.
        /// </summary>
        public bool EnsureMicrophoneConsent(System.Windows.Window owner)
        {
            var s = SettingsService.Current;
            if (s.MicrophoneConsent == MicConsent.Granted) return true;
            if (s.MicrophoneConsent == MicConsent.Denied) return false;
            int r = DialogWindow.Ask(owner, "Acceso al micrófono", "Para grabar tu voz, CUTLIFY necesita acceso al micrófono.\n\nSolo se usa durante la grabación o la prueba de micrófono.", 1, "Ahora no", "Permitir");
            s.MicrophoneConsent = r == 1 ? MicConsent.Granted : MicConsent.Denied;
            SettingsService.Save();
            return r == 1;
        }

        public static void ShowWindowsMicrophoneBlocked(System.Windows.Window owner)
        {
            if (DialogWindow.Ask(owner, "Windows no permite que CUTLIFY utilice el micrófono.",
                    "Activa «Permitir que las aplicaciones de escritorio accedan al micrófono» en la configuración de privacidad de Windows.",
                    1, "Cerrar", "Abrir configuración de Windows") == 1)
                OpenUrl("ms-settings:privacy-microphone");
        }

        private AudioCapture CreateAudio()
        {
            var s = SettingsService.Current;
            if (!s.RecordAudio || (!s.RecordSystemAudio && !s.RecordMicrophone)) return null;
            bool mic = s.RecordMicrophone && EnsureMicrophoneConsent(null);
            var audio = AudioCapture.Create(s.RecordSystemAudio, s.SystemAudioDeviceId, mic, s.MicrophoneDeviceId);
            audio.Start();
            if (audio.MicrophoneBlocked) ShowWindowsMicrophoneBlocked(null);
            if (!audio.HasSources)
            {
                audio.Dispose();
                if (s.ShowNotifications) ToastWindow.ShowToast("Grabando sin audio", string.Join(" ", audio.Warnings));
                return null;
            }
            return audio;
        }

        public async void StartRecording(CaptureMode mode, int delaySeconds, bool fromMain)
        {
            if (IsRecording) { StopRecording(); return; }
            if (_busy) return;
            _busy = true;
            _recOptions = null;
            bool mainWasVisible = false;
            Drawing.Bitmap frozen = null;
            AudioCapture audio = null;
            try
            {
                mainWasVisible = HideMainForCapture();
                if (mainWasVisible) await Task.Delay(220);
                frozen = ScreenCapture.CaptureVirtualScreen();
                var result = await RunOverlay(frozen, mode, true);
                frozen.Dispose();
                frozen = null;
                if (result == null) { RestoreAfterCapture(mainWasVisible); return; }

                var s = SettingsService.Current;
                if (!VideoSettings.IsCodecAvailable(s.RecordCodec))
                    throw new InvalidOperationException($"El codec {s.RecordCodec} no está disponible en este equipo. Elige otro en Configuración → Grabación.");

                audio = CreateAudio();
                await CountdownWindow.Run(delaySeconds, "Grabación");

                var output = VideoSettings.OutputSize(result.ScreenRect.Size, s.RecordResolution);
                var (bitrate, auto) = VideoSettings.ResolveBitrate(s, output);
                var path = AppPaths.UniqueFile(AppPaths.Temp, "Grabacion", ".mp4");
                var item = new HistoryItem
                {
                    Kind = ItemKind.Video,
                    FilePath = path,
                    FileName = Path.GetFileName(path),
                    Width = output.Width & ~1,
                    Height = output.Height & ~1,
                    Fps = s.RecordFps,
                    Codec = s.RecordCodec,
                    BitrateKbps = bitrate / 1000,
                    BitrateAuto = auto,
                    Audio = audio?.Description ?? "Sin audio"
                };
                item.ThumbPath = ThumbnailService.PathFor(item.Id);
                _recOptions = new RecordingOptions
                {
                    Region = result.ScreenRect,
                    OutputSize = output,
                    Fps = s.RecordFps,
                    Codec = s.RecordCodec,
                    BitrateBps = bitrate,
                    IncludeCursor = s.RecordCursor,
                    HighlightClicks = s.HighlightClicks,
                    HardwareEncoding = s.HardwareEncoding,
                    Audio = audio,
                    OutputPath = path,
                    ThumbPath = item.ThumbPath
                };

                // Marcador para detectar grabaciones interrumpidas por un cierre inesperado.
                _recMarker = path + TempCleaner.MarkerExtension;
                File.WriteAllText(_recMarker, JsonSerializer.Serialize(item));

                var recorder = new ScreenRecorder(_recOptions);
                recorder.Failed += ex => _wpf.Dispatcher.BeginInvoke(new Action(() => OnRecordingFailed(ex)));
                audio = null; // ahora pertenece al grabador
                recorder.Start();
                _recorder = recorder;
                _recItem = item;
                _recordFromMain = fromMain || mainWasVisible;

                if (!IsWholeMonitor(result.ScreenRect)) { _frame = new RegionFrame(result.ScreenRect); _frame.Show(); }
                _bar = new RecordingBar(result.ScreenRect, () => _recorder?.Elapsed ?? TimeSpan.Zero,
                    s.ShowRecordingStats ? () => _recorder?.Stats : (Func<RecordingStats?>)null);
                _bar.PauseToggled += ToggleRecordingPause;
                _bar.StopRequested += StopRecording;
                _bar.Show();
                _tray?.SetTooltip("CUTLIFY — Grabando…");
                Logger.Info("Recording", $"Grabación iniciada {recorder.VideoSize.Width}x{recorder.VideoSize.Height} @ {s.RecordFps} fps, {s.RecordCodec}, {bitrate / 1000} kbps, audio: {item.Audio}");
            }
            catch (Exception ex)
            {
                Logger.Error("Recording", "No se pudo iniciar la grabación", ex);
                audio?.Dispose();
                _recOptions?.Audio?.Dispose();
                CleanupRecordingUi();
                DeleteMarker();
                if (_recorder == null && _recOptions != null) { try { File.Delete(_recOptions.OutputPath); } catch { } }
                _recorder = null;
                DialogWindow.Ask(null, "No se pudo iniciar la grabación.",
                    ex is InvalidOperationException ? ex.Message : "Windows no pudo crear el codificador de vídeo con esta configuración. Prueba con menos FPS, otra resolución o H.264.\n\nDetalle: " + ex.Message, 0, "Cerrar");
                RestoreAfterCapture(mainWasVisible);
            }
            finally
            {
                frozen?.Dispose();
                _busy = false;
            }
        }

        private static bool IsWholeMonitor(Drawing.Rectangle r) =>
            System.Windows.Forms.Screen.AllScreens.Any(s => s.Bounds == r) || r == ScreenCapture.VirtualScreen;

        private void ToggleRecordingPause()
        {
            if (_recorder == null) return;
            if (_recorder.IsPaused) _recorder.Resume(); else _recorder.Pause();
            _bar?.SetPaused(_recorder.IsPaused);
        }

        private void DeleteMarker()
        {
            try { if (_recMarker != null && File.Exists(_recMarker)) File.Delete(_recMarker); } catch { }
            _recMarker = null;
        }

        public async void StopRecording()
        {
            var rec = _recorder;
            var opt = _recOptions;
            var item = _recItem;
            if (rec == null) return;
            _recorder = null;
            CleanupRecordingUi();
            try
            {
                await Task.Run(() => rec.Stop());
                var stats = rec.Stats;
                rec.Dispose();
                DeleteMarker();
                if (stats.Frames == 0 || !File.Exists(opt.OutputPath))
                    throw new InvalidOperationException("La grabación no contiene fotogramas.");

                item.DurationMs = (long)stats.Elapsed.TotalMilliseconds;
                item.RealFps = Math.Round(stats.RealFps, 1);
                item.DroppedFrames = stats.Dropped;
                item.SizeBytes = new FileInfo(opt.OutputPath).Length;
                if (!File.Exists(item.ThumbPath)) item.ThumbPath = null;
                History.Add(item);
                Logger.Info("Recording", $"Grabación guardada ({item.DurationLabel}, {item.SizeLabel}, {item.RealFps} FPS reales, {item.DroppedFrames} perdidos)");

                var s = SettingsService.Current;
                bool copied = s.CopyRecordingsAsFile && ClipboardService.CopyFile(item.FilePath);
                if (s.ShowNotifications)
                    ToastWindow.ShowToast(copied ? "Grabación guardada y copiada como archivo" : "Grabación guardada",
                        $"{item.DurationLabel}  ·  {item.Width} × {item.Height}  ·  {item.FpsLabel}",
                        item.Thumbnail, item.DurationLabel, () => ShowMainWindow(item),
                        new (string, Action)[] { ("Abrir", () => ShowMainWindow(item)) });
                if (_recordFromMain) ShowMainWindow(item);
            }
            catch (Exception ex)
            {
                Logger.Error("Recording", "Error al finalizar la grabación", ex);
                DeleteMarker();
                try { if (File.Exists(opt.OutputPath) && rec.FramesWritten == 0) File.Delete(opt.OutputPath); } catch { }
                DialogWindow.Ask(null, "No se pudo guardar la grabación.", ex.Message, 0, "Cerrar");
            }
            finally
            {
                _tray?.SetTooltip("CUTLIFY — Screen Capture & Recording");
            }
        }

        /// <summary>El grabador intenta finalizar el MP4 antes de avisar; si el archivo es válido se conserva.</summary>
        private void OnRecordingFailed(Exception ex)
        {
            var rec = _recorder;
            var opt = _recOptions;
            var item = _recItem;
            _recorder = null;
            CleanupRecordingUi();
            _tray?.SetTooltip("CUTLIFY — Screen Capture & Recording");
            bool kept = false;
            try
            {
                var stats = rec?.Stats;
                rec?.Dispose();
                DeleteMarker();
                if (rec != null && rec.FramesWritten > 0 && opt != null && File.Exists(opt.OutputPath) && new FileInfo(opt.OutputPath).Length > 0)
                {
                    item.DurationMs = (long)stats.Value.Elapsed.TotalMilliseconds;
                    item.RealFps = Math.Round(stats.Value.RealFps, 1);
                    item.SizeBytes = new FileInfo(opt.OutputPath).Length;
                    History.Add(item);
                    kept = true;
                }
                else if (opt != null && File.Exists(opt.OutputPath)) File.Delete(opt.OutputPath);
            }
            catch (Exception e2) { Logger.Error("Recording", "Error al cerrar la grabación fallida", e2); }
            DialogWindow.Ask(null, "La grabación se detuvo por un error.",
                (kept ? "Se conservó lo grabado hasta el fallo.\n\n" : "") + ex.Message, 0, "Cerrar");
        }

        private void CleanupRecordingUi()
        {
            try { _bar?.Close(); } catch { }
            _bar = null;
            _frame?.Dispose();
            _frame = null;
        }

        private void StopRecordingForExit()
        {
            try
            {
                if (_recorder == null) return;
                var rec = _recorder;
                var item = _recItem;
                _recorder = null;
                CleanupRecordingUi();
                rec.Stop();
                var stats = rec.Stats;
                rec.Dispose();
                DeleteMarker();
                if (stats.Frames > 0 && File.Exists(item.FilePath))
                {
                    item.DurationMs = (long)stats.Elapsed.TotalMilliseconds;
                    item.SizeBytes = new FileInfo(item.FilePath).Length;
                    History.Add(item);
                }
            }
            catch (Exception ex) { Logger.Error("Recording", "Error al detener la grabación al salir", ex); }
        }
    }
}
