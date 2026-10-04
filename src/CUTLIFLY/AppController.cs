using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Cutlifly.Capture;
using Cutlifly.Clipboard;
using Cutlifly.Core;
using Cutlifly.Hotkeys;
using Cutlifly.Notifications;
using Cutlifly.Recording;
using Cutlifly.Settings;
using Cutlifly.Storage;
using Cutlifly.Tray;
using Cutlifly.UI;
using Cutlifly.Update;
using Drawing = System.Drawing;

namespace Cutlifly
{
    /// <summary>Orquesta capturas, grabaciones, historial, atajos, bandeja y ventanas.</summary>
    public sealed class AppController : IDisposable
    {
        public static AppController Instance { get; private set; }

        private sealed class LastCapture
        {
            public CaptureMode Mode;
            public Drawing.Rectangle Rect;
            public IntPtr Window;
        }

        private readonly Application _wpf;
        private readonly DispatcherTimer _cleanupTimer;
        private readonly DispatcherTimer _updateTimer;
        private TrayIcon _tray;
        private MainWindow _main;
        private SettingsWindow _settings;
        private HistoryWindow _historyWindow;
        private bool _busy;
        private LastCapture _last;

        // grabación
        private ScreenRecorder _recorder;
        private RecordingOptions _recOptions;
        private RecordingBar _bar;
        private RegionFrame _frame;
        private bool _recordFromMain;

        public HistoryService History { get; } = new HistoryService();
        public TempCleaner Cleaner { get; }
        public HotkeyManager Hotkeys { get; }
        public bool IsExiting { get; private set; }
        public bool IsRecording => _recorder != null;

        private AppController(Application app)
        {
            _wpf = app;
            Cleaner = new TempCleaner(History);
            Hotkeys = new HotkeyManager(app.Dispatcher);
            _cleanupTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
            _cleanupTimer.Tick += (_, __) => SafeClean();
            _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            _updateTimer.Tick += (_, __) => { _updateTimer.Interval = TimeSpan.FromHours(6); if (IsUpdateDue()) CheckForUpdates(false); };
        }

        public static void Initialize(Application app, bool startInTray, bool justUpdated)
        {
            Instance = new AppController(app);
            Instance.Start(startInTray);
            if (justUpdated)
                ToastWindow.ShowToast("CUTLIFLY se actualizó", $"Ahora usas la versión {UpdateService.CurrentVersionText}.");
        }

        private void Start(bool startInTray)
        {
            Logger.PurgeOld();
            SettingsService.Load();
            ThemeManager.Apply(SettingsService.Current.Theme);
            ThemeManager.WatchSystem();
            StartupManager.Refresh();

            History.Load();
            SafeClean(); // limpia lo que caducó mientras CUTLIFLY estaba cerrado
            _cleanupTimer.Start();

            Hotkeys.Start();
            var problems = ApplyHotkeys();
            if (!string.IsNullOrEmpty(problems)) Logger.Warn("Hotkeys", problems);

            _tray = new TrayIcon(
                open: () => ShowMainWindow(),
                capture: () => StartCapture(SettingsService.Current.LastMode, 0, false),
                record: () => StartRecording(CaptureMode.Rectangle, 0, false),
                settings: () => ShowSettings(),
                exit: Exit);

            if (!startInTray) ShowMainWindow();
            _updateTimer.Start();
            Logger.Info("App", $"CUTLIFLY {UpdateService.CurrentVersionText} iniciado");
        }

        private void SafeClean()
        {
            try { Cleaner.CleanExpired(); }
            catch (Exception ex) { Logger.Error("Storage", "Error en la limpieza automática", ex); }
        }

        public void ApplySettings()
        {
            ThemeManager.Apply(SettingsService.Current.Theme);
            ApplyHotkeys();
            _main?.LoadSettings();
        }

        /// <summary>Registra los atajos; devuelve un texto con problemas (o vacío).</summary>
        public string ApplyHotkeys()
        {
            var s = SettingsService.Current;
            Hotkeys.Clear();
            var errors = new System.Collections.Generic.List<string>();
            void Bind(string text, string name, Action action)
            {
                if (Hotkey.TryParse(text, out var hk)) Hotkeys.Bind(hk, action);
                else errors.Add($"Atajo no válido para {name}: {text}");
            }
            Bind(s.HotkeyCapture, "captura rápida", () => StartCapture(SettingsService.Current.LastMode, 0, false));
            Bind(s.HotkeyRecord, "grabación rápida", ToggleQuickRecord);
            Bind(s.HotkeyOpen, "abrir CUTLIFLY", () => ShowMainWindow());
            return string.Join("\n", errors);
        }

        // ------------------------------------------------------------------ Ventanas

        public void ShowMainWindow(HistoryItem item = null)
        {
            if (_main == null) _main = new MainWindow(this);
            if (!_main.IsVisible) _main.Show();
            if (_main.WindowState == WindowState.Minimized) _main.WindowState = WindowState.Normal;
            _main.Activate();
            _main.Topmost = true;
            _main.Topmost = false;
            _main.Focus();
            if (item != null) _main.ShowItem(item);
            else if (_main.Current == null && History.Items.Count > 0) _main.ShowItem(History.Items[0]);
        }

        public void ShowSettings(string page = null)
        {
            if (_settings == null)
            {
                _settings = new SettingsWindow(this, page);
                _settings.Closed += (_, __) => { _settings = null; ApplySettings(); };
                _settings.Show();
            }
            else if (page != null) _settings.ShowPage(page);
            _settings.Activate();
        }

        public void ShowHistory()
        {
            if (_historyWindow == null)
            {
                _historyWindow = new HistoryWindow(this);
                if (_main != null && _main.IsVisible) _historyWindow.Owner = _main;
                _historyWindow.Closed += (_, __) => _historyWindow = null;
                _historyWindow.Show();
            }
            _historyWindow.Activate();
        }

        private bool HideMainForCapture()
        {
            bool visible = _main != null && _main.IsVisible && _main.WindowState != WindowState.Minimized;
            if (visible) _main.Hide();
            _historyWindow?.Hide();
            return visible;
        }

        private void RestoreAfterCapture(bool mainWasVisible)
        {
            if (_historyWindow != null) _historyWindow.Show();
            if (mainWasVisible) ShowMainWindow();
        }

        // ------------------------------------------------------------------ Captura

        public async void StartCapture(CaptureMode mode, int delaySeconds, bool fromMain)
        {
            if (_busy) return;
            _busy = true;
            bool mainWasVisible = false;
            bool retry = false;
            Drawing.Bitmap frozen = null;
            try
            {
                mainWasVisible = HideMainForCapture();
                if (mainWasVisible) await Task.Delay(220); // dejar que la ventana desaparezca
                await Countdown(delaySeconds, "Captura");

                frozen = ScreenCapture.CaptureVirtualScreen();
                var result = await RunOverlay(frozen, mode, false);
                if (result == null) { RestoreAfterCapture(mainWasVisible); return; }

                var vs = ScreenCapture.VirtualScreen;
                var local = new Drawing.Rectangle(result.ScreenRect.X - vs.X, result.ScreenRect.Y - vs.Y, result.ScreenRect.Width, result.ScreenRect.Height);
                BitmapSource image;
                using (var cropped = ScreenCapture.Crop(frozen, local)) image = ImageUtil.ToBitmapSource(cropped);

                _last = new LastCapture { Mode = result.Mode, Rect = result.ScreenRect, Window = result.Window };
                if (SettingsService.Current.LastMode != result.Mode) { SettingsService.Current.LastMode = result.Mode; SettingsService.Save(); }
                if (_historyWindow != null) _historyWindow.Show();
                ProcessCapture(image, fromMain || mainWasVisible);
            }
            catch (Exception ex)
            {
                Logger.Error("Capture", "Fallo en la captura", ex);
                retry = DialogWindow.Ask(null, "No se pudo realizar la captura.", ex.Message, 0, "Reintentar", "Cerrar") == 0;
                if (!retry) RestoreAfterCapture(mainWasVisible);
            }
            finally
            {
                frozen?.Dispose();
                _busy = false;
            }
            if (retry) StartCapture(mode, 0, fromMain || mainWasVisible);
        }

        private static Task<OverlayResult> RunOverlay(Drawing.Bitmap frozen, CaptureMode mode, bool recording)
        {
            var tcs = new TaskCompletionSource<OverlayResult>();
            var overlay = new CaptureOverlay(frozen, mode, recording);
            overlay.Completed += r => tcs.TrySetResult(r);
            overlay.FormClosed += (_, __) => { tcs.TrySetResult(null); overlay.Dispose(); };
            overlay.Show();
            return tcs.Task;
        }

        private static async Task Countdown(int seconds, string what)
        {
            if (seconds <= 0) return;
            if (SettingsService.Current.ShowNotifications)
                ToastWindow.ShowToast($"{what} en {seconds} segundos", "Prepara la pantalla…");
            await Task.Delay(TimeSpan.FromSeconds(seconds));
        }

        /// <summary>Repite la última captura con la misma configuración (misma región / ventana / monitor).</summary>
        public async void RepeatLastCapture()
        {
            if (_last == null) { StartCapture(_main?.SelectedMode ?? SettingsService.Current.LastMode, 0, true); return; }
            if (_busy) return;
            _busy = true;
            bool mainWasVisible = false;
            try
            {
                var rect = _last.Rect;
                if (_last.Mode == CaptureMode.Window)
                {
                    if (!Native.IsWindow(_last.Window) || Native.IsIconic(_last.Window))
                    {
                        _busy = false;
                        StartCapture(CaptureMode.Window, 0, true);
                        return;
                    }
                    rect = WindowFinder.GetBounds(_last.Window);
                    Native.SetForegroundWindow(_last.Window);
                }
                mainWasVisible = HideMainForCapture();
                await Task.Delay(mainWasVisible ? 260 : 60);
                rect.Intersect(ScreenCapture.VirtualScreen);
                BitmapSource image;
                using (var bmp = ScreenCapture.CaptureRegion(rect)) image = ImageUtil.ToBitmapSource(bmp);
                if (_historyWindow != null) _historyWindow.Show();
                ProcessCapture(image, true);
            }
            catch (Exception ex)
            {
                Logger.Error("Capture", "Fallo al repetir la captura", ex);
                DialogWindow.Ask(null, "No se pudo realizar la captura.", ex.Message, 0, "Cerrar");
                RestoreAfterCapture(mainWasVisible);
            }
            finally
            {
                _busy = false;
            }
        }

        private void ProcessCapture(BitmapSource image, bool showMain)
        {
            var s = SettingsService.Current;
            bool copied = s.AutoCopyCaptures && ClipboardService.CopyImage(image);

            var item = new HistoryItem { Kind = ItemKind.Image, Width = image.PixelWidth, Height = image.PixelHeight };
            if (s.SaveTemporarily)
            {
                try
                {
                    var ext = s.ImageFormat == "JPG" ? ".jpg" : ".png";
                    var path = Path.Combine(AppPaths.Temp, $"CUTLIFLY_{DateTime.Now:yyyyMMdd_HHmmss_fff}{ext}");
                    ImageUtil.Save(image, path);
                    item.FilePath = path;
                    item.SizeBytes = new FileInfo(path).Length;
                }
                catch (Exception ex)
                {
                    Logger.Error("Storage", "No se pudo guardar la captura temporal", ex);
                    item.MemoryImage = image;
                }
            }
            else item.MemoryImage = image;
            History.Add(item);

            if (s.ShowNotifications)
            {
                if (copied)
                    ToastWindow.ShowToast("Captura copiada al portapapeles", "Puedes pegarla con Ctrl + V", image, null, () => ShowMainWindow(item));
                else
                    ToastWindow.ShowToast("Captura realizada", s.AutoCopyCaptures ? "No se pudo copiar al portapapeles." : "Ábrela en CUTLIFLY para editarla.", image, null, () => ShowMainWindow(item));
            }
            if (showMain || s.OpenEditorAfterCapture) ShowMainWindow(item);
        }

        // ------------------------------------------------------------------ Grabación

        public void ToggleQuickRecord()
        {
            if (IsRecording) StopRecording();
            else StartRecording(CaptureMode.Rectangle, 0, false);
        }

        public async void StartRecording(CaptureMode mode, int delaySeconds, bool fromMain)
        {
            if (IsRecording) { StopRecording(); return; }
            if (_busy) return;
            _busy = true;
            bool mainWasVisible = false;
            Drawing.Bitmap frozen = null;
            try
            {
                mainWasVisible = HideMainForCapture();
                if (mainWasVisible) await Task.Delay(220);
                frozen = ScreenCapture.CaptureVirtualScreen();
                var result = await RunOverlay(frozen, mode, true);
                frozen.Dispose();
                frozen = null;
                if (result == null) { RestoreAfterCapture(mainWasVisible); return; }
                await Countdown(delaySeconds, "Grabación");

                var s = SettingsService.Current;
                var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                _recOptions = new RecordingOptions
                {
                    Region = result.ScreenRect,
                    Fps = s.RecordFps == 60 ? 60 : 30,
                    Quality = s.RecordQuality,
                    IncludeCursor = s.RecordCursor,
                    HardwareEncoding = s.HardwareEncoding,
                    OutputPath = Path.Combine(AppPaths.Temp, $"CUTLIFLY_{stamp}.mp4"),
                    ThumbPath = Path.Combine(AppPaths.Thumbs, $"CUTLIFLY_{stamp}.jpg")
                };
                var recorder = new ScreenRecorder(_recOptions);
                recorder.Failed += ex => _wpf.Dispatcher.BeginInvoke(new Action(() => OnRecordingFailed(ex)));
                recorder.Start();
                _recorder = recorder;
                _recordFromMain = fromMain || mainWasVisible;

                bool full = IsWholeMonitor(result.ScreenRect);
                if (!full) { _frame = new RegionFrame(result.ScreenRect); _frame.Show(); }
                _bar = new RecordingBar(result.ScreenRect, () => _recorder?.Elapsed ?? TimeSpan.Zero);
                _bar.PauseToggled += TogglePause;
                _bar.StopRequested += StopRecording;
                _bar.Show();
                _tray?.SetTooltip("CUTLIFLY — Grabando…");
                Logger.Info("Recording", $"Grabación iniciada {recorder.VideoSize.Width}x{recorder.VideoSize.Height} @ {_recOptions.Fps} fps");
            }
            catch (Exception ex)
            {
                Logger.Error("Recording", "No se pudo iniciar la grabación", ex);
                CleanupRecordingUi();
                _recorder = null;
                DialogWindow.Ask(null, "No se pudo iniciar la grabación.",
                    "Windows no pudo crear el codificador de vídeo para esta región.\n\nDetalle: " + ex.Message, 0, "Cerrar");
                RestoreAfterCapture(mainWasVisible);
            }
            finally
            {
                frozen?.Dispose();
                _busy = false;
            }
        }

        private static bool IsWholeMonitor(Drawing.Rectangle r) =>
            System.Windows.Forms.Screen.AllScreens.Any(s => s.Bounds == r);

        private void TogglePause()
        {
            if (_recorder == null) return;
            if (_recorder.IsPaused) _recorder.Resume(); else _recorder.Pause();
            _bar?.SetPaused(_recorder.IsPaused);
        }

        public async void StopRecording()
        {
            var rec = _recorder;
            var opt = _recOptions;
            if (rec == null) return;
            _recorder = null;
            CleanupRecordingUi();
            var duration = rec.Elapsed;
            try
            {
                await Task.Run(() => rec.Stop());
                int frames = rec.FramesWritten;
                var size = rec.VideoSize;
                rec.Dispose();
                if (frames == 0 || !File.Exists(opt.OutputPath))
                    throw new InvalidOperationException("La grabación no contiene fotogramas.");

                var item = new HistoryItem
                {
                    Kind = ItemKind.Video,
                    FilePath = opt.OutputPath,
                    ThumbPath = File.Exists(opt.ThumbPath) ? opt.ThumbPath : null,
                    DurationMs = (long)duration.TotalMilliseconds,
                    Width = size.Width,
                    Height = size.Height,
                    SizeBytes = new FileInfo(opt.OutputPath).Length
                };
                History.Add(item);
                Logger.Info("Recording", $"Grabación guardada ({item.DurationLabel}, {item.SizeLabel})");

                var s = SettingsService.Current;
                bool copied = s.CopyRecordingsAsFile && ClipboardService.CopyFile(item.FilePath);
                if (s.ShowNotifications)
                    ToastWindow.ShowToast(copied ? "Grabación guardada y copiada como archivo" : "Grabación guardada",
                        copied ? $"{item.DurationLabel}  ·  Pégala con Ctrl + V en apps compatibles" : item.DurationLabel,
                        item.Thumbnail, item.DurationLabel, () => ShowMainWindow(item),
                        new (string, Action)[] { ("Abrir", () => ShowMainWindow(item)) });
                if (_recordFromMain) ShowMainWindow(item);
            }
            catch (Exception ex)
            {
                Logger.Error("Recording", "Error al finalizar la grabación", ex);
                try { if (File.Exists(opt.OutputPath) && rec.FramesWritten == 0) File.Delete(opt.OutputPath); } catch { }
                DialogWindow.Ask(null, "No se pudo guardar la grabación.", ex.Message, 0, "Cerrar");
            }
            finally
            {
                _tray?.SetTooltip("CUTLIFLY — Screen Capture & Recording");
            }
        }

        private void OnRecordingFailed(Exception ex)
        {
            var opt = _recOptions;
            _recorder?.Dispose();
            _recorder = null;
            CleanupRecordingUi();
            _tray?.SetTooltip("CUTLIFLY — Screen Capture & Recording");
            try { if (opt != null && File.Exists(opt.OutputPath)) File.Delete(opt.OutputPath); } catch { }
            DialogWindow.Ask(null, "La grabación se detuvo por un error.", ex.Message, 0, "Cerrar");
        }

        private void CleanupRecordingUi()
        {
            try { _bar?.Close(); } catch { }
            _bar = null;
            _frame?.Dispose();
            _frame = null;
        }

        // ------------------------------------------------------------------ Acciones sobre elementos

        public static BitmapSource GetImage(HistoryItem item)
        {
            if (item.MemoryImage != null) return item.MemoryImage;
            try { return item.HasFile ? HistoryItem.LoadImage(item.FilePath) : null; }
            catch (Exception ex) { Logger.Warn("History", "No se pudo abrir la imagen", ex); return null; }
        }

        public void CopyImage(BitmapSource image)
        {
            if (image == null) return;
            if (ClipboardService.CopyImage(image))
            {
                if (SettingsService.Current.ShowNotifications) ToastWindow.ShowToast("Copiada al portapapeles", "Puedes pegarla con Ctrl + V", image);
            }
            else DialogWindow.Ask(_main, "No se pudo copiar", "Otra aplicación está usando el portapapeles. Inténtalo de nuevo.", 0, "Cerrar");
        }

        public void Copy(HistoryItem item)
        {
            if (item.IsVideo)
            {
                if (ClipboardService.CopyFile(item.FilePath))
                {
                    if (SettingsService.Current.ShowNotifications)
                        ToastWindow.ShowToast("Archivo copiado", "Pégalo con Ctrl + V en apps que acepten archivos.", item.Thumbnail, item.DurationLabel);
                }
                else DialogWindow.Ask(_main, "No se pudo copiar el archivo", "", 0, "Cerrar");
            }
            else CopyImage(GetImage(item));
        }

        public void Save(HistoryItem item)
        {
            if (item.IsVideo) SaveVideo(item);
            else SaveImage(item, GetImage(item));
        }

        public void SaveImage(HistoryItem item, BitmapSource image)
        {
            if (image == null) return;
            var s = SettingsService.Current;
            var folder = AppPaths.Ensure(s.PicturesFolder ?? AppPaths.DefaultPicturesFolder);
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Guardar captura — CUTLIFLY",
                InitialDirectory = folder,
                FileName = $"CUTLIFLY_{DateTime.Now:yyyyMMdd_HHmmss}",
                Filter = "Imagen PNG (*.png)|*.png|Imagen JPG (*.jpg)|*.jpg",
                FilterIndex = s.ImageFormat == "JPG" ? 2 : 1,
                AddExtension = true
            };
            if (dlg.ShowDialog(_main?.IsVisible == true ? _main : null) != true) return;
            try
            {
                ImageUtil.Save(image, dlg.FileName);
                if (item.IsExternal || item.IsPermanent)
                {
                    var copy = new HistoryItem { Kind = ItemKind.Image, Width = image.PixelWidth, Height = image.PixelHeight };
                    History.MarkPermanent(copy, dlg.FileName);
                    _main?.ShowItem(copy);
                }
                else
                {
                    item.Width = image.PixelWidth;
                    item.Height = image.PixelHeight;
                    History.MarkPermanent(item, dlg.FileName);
                }
                if (s.ShowNotifications) ToastWindow.ShowToast("Captura guardada", dlg.FileName, image, null, () => OpenLocationPath(dlg.FileName));
            }
            catch (Exception ex)
            {
                Logger.Error("Storage", "No se pudo guardar la imagen", ex);
                DialogWindow.Ask(_main, "No se pudo guardar la captura.", ex.Message, 0, "Cerrar");
            }
        }

        public void SaveVideo(HistoryItem item)
        {
            if (!item.HasFile) return;
            var s = SettingsService.Current;
            var folder = AppPaths.Ensure(s.VideosFolder ?? AppPaths.DefaultVideosFolder);
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Guardar grabación — CUTLIFLY",
                InitialDirectory = folder,
                FileName = Path.GetFileNameWithoutExtension(item.FilePath),
                Filter = "Vídeo MP4 (*.mp4)|*.mp4",
                AddExtension = true
            };
            if (dlg.ShowDialog(_main?.IsVisible == true ? _main : null) != true) return;
            try
            {
                if (_main?.Current == item) _main.Video.Release();
                File.Copy(item.FilePath, dlg.FileName, true);
                if (item.IsExternal) { var copy = new HistoryItem { Kind = ItemKind.Video, DurationMs = item.DurationMs }; History.MarkPermanent(copy, dlg.FileName); item = copy; }
                else History.MarkPermanent(item, dlg.FileName);
                if (_main?.Current == item || _main?.Current?.FilePath == dlg.FileName) _main.ShowItem(item);
                if (s.ShowNotifications) ToastWindow.ShowToast("Grabación guardada", dlg.FileName, item.Thumbnail, item.DurationLabel, () => OpenLocationPath(dlg.FileName));
            }
            catch (Exception ex)
            {
                Logger.Error("Storage", "No se pudo guardar el vídeo", ex);
                DialogWindow.Ask(_main, "No se pudo guardar la grabación.", ex.Message, 0, "Cerrar");
            }
        }

        public void Open(HistoryItem item)
        {
            if (!item.HasFile)
            {
                if (!item.IsVideo) ShowMainWindow(item);
                return;
            }
            try { Process.Start(new ProcessStartInfo(item.FilePath) { UseShellExecute = true }); }
            catch (Exception ex) { Logger.Warn("UI", "No se pudo abrir el archivo", ex); }
        }

        public void OpenLocation(HistoryItem item)
        {
            if (item.HasFile) OpenLocationPath(item.FilePath);
            else DialogWindow.Ask(_main, "Solo en el portapapeles", "Esta captura no tiene archivo. Pulsa Guardar para crearlo.", 0, "Aceptar");
        }

        private static void OpenLocationPath(string path)
        {
            try { Process.Start("explorer.exe", $"/select,\"{path}\""); }
            catch (Exception ex) { Logger.Warn("UI", "No se pudo abrir la ubicación", ex); }
        }

        public static void OpenFolder(string folder)
        {
            try { Process.Start(new ProcessStartInfo(AppPaths.Ensure(folder)) { UseShellExecute = true }); }
            catch (Exception ex) { Logger.Warn("UI", "No se pudo abrir la carpeta", ex); }
        }

        public static void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { Logger.Warn("UI", "No se pudo abrir el enlace", ex); }
        }

        public void Pin(BitmapSource image)
        {
            if (image != null) new PinWindow(image).Show();
        }

        public void Delete(HistoryItem item, Window owner)
        {
            if (item.IsExternal) { _main?.ShowItem(History.Items.FirstOrDefault()); return; }
            if (item.IsPermanent &&
                DialogWindow.Ask(owner, "¿Eliminar este archivo?", $"{item.Name} se enviará a la Papelera.", 1, "Cancelar", "Eliminar") != 1)
                return;
            if (_main?.Current == item) _main.Video.Release();
            History.Remove(item, deleteFile: true);
        }

        public void ConfirmCleanTemp(Window owner)
        {
            var (count, bytes) = TempCleaner.GetUsage();
            if (count == 0)
            {
                DialogWindow.Ask(owner, "No hay archivos temporales", "La carpeta temporal ya está vacía.", 0, "Aceptar");
                return;
            }
            if (DialogWindow.Ask(owner, $"¿Eliminar {count} archivos temporales?",
                    $"Se recuperarán {HistoryItem.FormatBytes(bytes)}. Las capturas guardadas no se tocan.", 1, "Cancelar", "Limpiar") != 1)
                return;
            if (_main?.Current != null && !_main.Current.IsPermanent) _main.Video.Release();
            Cleaner.CleanAll();
        }

        // ------------------------------------------------------------------ Actualizaciones

        private bool IsUpdateDue()
        {
            var s = SettingsService.Current;
            var since = DateTime.UtcNow - s.LastUpdateCheckUtc;
            return s.UpdateCheck switch
            {
                "Startup" => !_checkedThisSession,
                "Daily" => since > TimeSpan.FromDays(1),
                "Weekly" => since > TimeSpan.FromDays(7),
                _ => false
            };
        }

        private bool _checkedThisSession;
        private bool _updating;

        public async void CheckForUpdates(bool manual)
        {
            if (_updating) return;
            _checkedThisSession = true;
            try
            {
                var release = await UpdateService.CheckAsync();
                SettingsService.Current.LastUpdateCheckUtc = DateTime.UtcNow;
                SettingsService.Save();
                if (release == null)
                {
                    if (manual) DialogWindow.Ask(_settings ?? (Window)_main, "CUTLIFLY está actualizado", $"Tienes la versión más reciente ({UpdateService.CurrentVersionText}).", 0, "Aceptar");
                    return;
                }
                if (manual) ShowUpdateDialog(release);
                else ToastWindow.ShowToast("Nueva versión disponible", $"CUTLIFLY {release.Version} está disponible.", null, null,
                    () => ShowUpdateDialog(release), new (string, Action)[] { ("Ver", () => ShowUpdateDialog(release)) });
            }
            catch (Exception ex)
            {
                Logger.Warn("Update", "No se pudo comprobar actualizaciones", ex);
                if (manual) DialogWindow.Ask(_settings ?? (Window)_main, "No se pudo buscar actualizaciones", "Comprueba tu conexión a Internet.\n\n" + ex.Message, 0, "Cerrar");
            }
        }

        private async void ShowUpdateDialog(ReleaseInfo release)
        {
            if (_updating) return;
            var dlg = new DialogWindow("Nueva versión disponible",
                $"CUTLIFLY {release.Version} está disponible.\nVersión instalada: {UpdateService.CurrentVersionText}", 1, "Más tarde", "Actualizar");
            dlg.SetDetails(string.IsNullOrWhiteSpace(release.Notes) ? "Sin notas de versión." : release.Notes);
            dlg.ShowDialog();
            if (dlg.Result != 1) return;

            if (IsRecording)
            {
                DialogWindow.Ask(null, "Grabación en curso", "Detén la grabación antes de actualizar.", 0, "Aceptar");
                return;
            }

            _updating = true;
            var progress = new DialogWindow("Descargando actualización…", $"CUTLIFLY {release.Version}", -1);
            progress.SetProgress(0);
            progress.Show();
            try
            {
                var file = await UpdateService.DownloadAsync(release, new Progress<double>(p => progress.SetProgress(p)));
                progress.SetMessage("Instalando y reiniciando CUTLIFLY…");
                await Task.Delay(400);
                UpdateService.InstallAndRestart(file);
                progress.Close();
                Exit();
            }
            catch (Exception ex)
            {
                Logger.Error("Update", "Falló la actualización", ex);
                progress.Close();
                DialogWindow.Ask(null, "No se pudo actualizar", ex.Message, 0, "Cerrar");
            }
            finally
            {
                _updating = false;
            }
        }

        // ------------------------------------------------------------------ Salida

        public void Exit()
        {
            if (IsExiting) return;
            IsExiting = true;
            try
            {
                if (_recorder != null)
                {
                    var rec = _recorder;
                    _recorder = null;
                    CleanupRecordingUi();
                    rec.Stop();
                    rec.Dispose();
                }
            }
            catch (Exception ex) { Logger.Error("Recording", "Error al detener la grabación al salir", ex); }
            History.Save();
            SettingsService.Save();
            foreach (Window w in _wpf.Windows.Cast<Window>().ToList()) { try { w.Close(); } catch { } }
            _wpf.Shutdown();
        }

        public void Dispose()
        {
            _cleanupTimer.Stop();
            _updateTimer.Stop();
            Hotkeys.Dispose();
            _tray?.Dispose();
        }
    }
}
