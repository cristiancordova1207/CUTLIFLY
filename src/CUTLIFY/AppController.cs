using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Cutlify.Capture;
using Cutlify.Clipboard;
using Cutlify.Core;
using Cutlify.Hotkeys;
using Cutlify.Notifications;
using Cutlify.Settings;
using Cutlify.Storage;
using Cutlify.Tray;
using Cutlify.UI;
using Cutlify.Update;
using Drawing = System.Drawing;

namespace Cutlify
{
    /// <summary>Orquesta capturas, grabaciones, historial, atajos, bandeja y ventanas.</summary>
    public sealed partial class AppController : IDisposable
    {
        public static AppController Instance { get; private set; }

        private sealed class LastCapture
        {
            public CaptureMode Mode;
            public Drawing.Rectangle Rect;
            public IntPtr Window;
            public int Delay;
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

        public HistoryService History { get; } = new HistoryService();
        public TempCleaner Cleaner { get; }
        public HotkeyManager Hotkeys { get; }
        public bool IsExiting { get; private set; }
        public bool IsPaused { get; private set; }

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

        /// <summary>Instancia sin hooks, bandeja ni temporizadores (pruebas de humo de la interfaz).</summary>
        internal static AppController CreateDetached(Application app) => new AppController(app);

        public static void Initialize(Application app, bool startInTray, bool justUpdated)
        {
            Instance = new AppController(app);
            Instance.Start(startInTray);
            if (justUpdated)
                ToastWindow.ShowToast("CUTLIFY se actualizó", $"Ahora usas la versión {UpdateService.CurrentVersionText}.");
        }

        private void Start(bool startInTray)
        {
            Logger.PurgeOld();
            SettingsService.Load();
            ThemeManager.Apply(SettingsService.Current.Theme);
            ThemeManager.WatchSystem();
            StartupManager.Refresh();
            AppPaths.EnsureUserFolders();

            History.Load();
            Cleaner.RecoverInterruptedRecordings();
            SafeClean(); // limpia lo que caducó mientras CUTLIFY estaba cerrado
            _cleanupTimer.Start();

            Hotkeys.Start();
            var problems = ApplyHotkeys();
            if (!string.IsNullOrEmpty(problems)) Logger.Warn("Hotkeys", problems);

            _tray = new TrayIcon(
                capture: () => StartCapture(SettingsService.Current.LastMode, SettingsService.Current.DefaultDelaySeconds, false),
                record: () => StartRecording(CaptureMode.Rectangle, 0, false),
                open: () => ShowMainWindow(),
                settings: () => ShowSettings(),
                togglePause: TogglePause,
                exit: Exit);

            if (!startInTray) ShowMainWindow();
            _updateTimer.Start();
            Logger.Info("App", $"CUTLIFY {UpdateService.CurrentVersionText} iniciado");
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
                if (string.IsNullOrWhiteSpace(text)) return;
                if (Hotkey.TryParse(text, out var hk)) Hotkeys.Bind(hk, () => { if (!IsPaused) action(); });
                else errors.Add($"Atajo no válido para {name}: {text}");
            }
            Bind(s.HotkeyCapture, "captura rápida", () => StartCapture(SettingsService.Current.LastMode, 0, false));
            Bind(s.HotkeyRecord, "grabación rápida", ToggleQuickRecord);
            Bind(s.HotkeyOpen, "abrir CUTLIFY", () => ShowMainWindow());
            Bind(s.HotkeyRepeat, "repetir captura", () => RepeatLastCapture());
            return string.Join("\n", errors);
        }

        /// <summary>Modo «Pausar CUTLIFY»: los atajos globales dejan de responder.</summary>
        public void TogglePause()
        {
            IsPaused = !IsPaused;
            _tray?.SetPaused(IsPaused);
            if (SettingsService.Current.ShowNotifications)
                ToastWindow.ShowToast(IsPaused ? "CUTLIFY en pausa" : "CUTLIFY activo",
                    IsPaused ? "Los atajos globales no responderán hasta que lo reanudes." : "Los atajos globales vuelven a funcionar.");
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
            else if (_main.Current == null)
            {
                var first = History.Items.FirstOrDefault(i => i.IsAvailable);
                if (first != null) _main.ShowItem(first);
            }
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
            _historyWindow?.Show();
            if (mainWasVisible) ShowMainWindow();
        }

        private Window DialogOwner => _settings ?? (_main != null && _main.IsVisible ? (Window)_main : null);

        // ------------------------------------------------------------------ Captura

        public async void StartCapture(CaptureMode mode, int delaySeconds, bool fromMain)
        {
            if (_busy) return;
            _busy = true;
            bool mainWasVisible = false, retry = false;
            Drawing.Bitmap frozen = null;
            try
            {
                mainWasVisible = HideMainForCapture();
                if (mainWasVisible) await Task.Delay(220); // dejar que la ventana desaparezca
                await CountdownWindow.Run(delaySeconds, "Captura");

                frozen = ScreenCapture.CaptureVirtualScreen();
                var result = await RunOverlay(frozen, mode, false);
                if (result == null) { RestoreAfterCapture(mainWasVisible); return; }

                if (result.Mode == CaptureMode.ColorPicker)
                {
                    RestoreAfterCapture(mainWasVisible);
                    ShowPickedColor(result.PickedColor);
                    return;
                }

                var vs = ScreenCapture.VirtualScreen;
                var local = new Drawing.Rectangle(result.ScreenRect.X - vs.X, result.ScreenRect.Y - vs.Y, result.ScreenRect.Width, result.ScreenRect.Height);
                BitmapSource image;
                if (result.Mode == CaptureMode.FreeForm && result.FreeFormPath != null)
                {
                    var poly = result.FreeFormPath.Select(p => new Drawing.Point(p.X - vs.X, p.Y - vs.Y)).ToArray();
                    image = ImageUtil.FreeFormCrop(frozen, local, poly);
                }
                else
                {
                    using var cropped = ScreenCapture.Crop(frozen, local);
                    image = ImageUtil.ToBitmapSource(cropped);
                }

                _last = new LastCapture { Mode = result.Mode, Rect = result.ScreenRect, Window = result.Window, Delay = delaySeconds };
                if (SettingsService.Current.LastMode != result.Mode) { SettingsService.Current.LastMode = result.Mode; SettingsService.Save(); }
                _historyWindow?.Show();
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

        private void ShowPickedColor(Drawing.Color c)
        {
            var hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            var rgb = $"RGB({c.R}, {c.G}, {c.B})";
            ClipboardService.CopyText(hex);
            ToastWindow.ShowToast($"{hex}  ·  {rgb}", "HEX copiado al portapapeles.", null, null, null,
                new (string, Action)[] { ("Copiar HEX", () => ClipboardService.CopyText(hex)), ("Copiar RGB", () => ClipboardService.CopyText(rgb)) });
        }

        /// <summary>Repite la última captura con la misma configuración (modo, región / ventana / monitor y retraso).</summary>
        public async void RepeatLastCapture()
        {
            if (_last == null || _last.Mode == CaptureMode.FreeForm || _last.Mode == CaptureMode.ColorPicker)
            {
                StartCapture(_last?.Mode ?? _main?.SelectedMode ?? SettingsService.Current.LastMode, _last?.Delay ?? 0, true);
                return;
            }
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
                        StartCapture(CaptureMode.Window, _last.Delay, true);
                        return;
                    }
                    rect = WindowFinder.GetBounds(_last.Window);
                    Native.SetForegroundWindow(_last.Window);
                }
                mainWasVisible = HideMainForCapture();
                await Task.Delay(mainWasVisible ? 260 : 60);
                await CountdownWindow.Run(_last.Delay, "Captura");
                rect.Intersect(ScreenCapture.VirtualScreen);
                BitmapSource image;
                using (var bmp = ScreenCapture.CaptureRegion(rect)) image = ImageUtil.ToBitmapSource(bmp);
                _historyWindow?.Show();
                ProcessCapture(image, mainWasVisible);
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
            bool alpha = image.Format == System.Windows.Media.PixelFormats.Bgra32;

            var item = new HistoryItem { Kind = ItemKind.Image, Width = image.PixelWidth, Height = image.PixelHeight };
            item.ThumbPath = ThumbnailService.Create(image, item.Id);
            if (s.SaveTemporarily)
            {
                try
                {
                    var ext = s.ImageFormat == "JPG" && !alpha ? ".jpg" : ".png";
                    var path = AppPaths.UniqueFile(AppPaths.Temp, "Captura", ext);
                    ImageUtil.Save(image, path);
                    item.FilePath = path;
                    item.SizeBytes = new System.IO.FileInfo(path).Length;
                }
                catch (Exception ex)
                {
                    Logger.Error("Storage", "No se pudo guardar la captura temporal", ex);
                    item.MemoryImage = image;
                }
            }
            else
            {
                item.MemoryImage = image;
                item.FileName = $"Captura_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png";
            }
            History.Add(item);

            if (s.ShowNotifications)
            {
                if (copied)
                    ToastWindow.ShowToast("Captura copiada al portapapeles", "Puedes pegarla con Ctrl + V", image, null, () => ShowMainWindow(item));
                else
                    ToastWindow.ShowToast("Captura realizada", s.AutoCopyCaptures ? "No se pudo copiar al portapapeles." : "Ábrela en CUTLIFY para editarla.", image, null, () => ShowMainWindow(item));
            }
            if (showMain || s.OpenEditorAfterCapture) ShowMainWindow(item);
        }

        // ------------------------------------------------------------------ Actualizaciones

        private bool _checkedThisSession;
        private bool _updating;

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
                    if (manual) DialogWindow.Ask(DialogOwner, "CUTLIFY está actualizado", $"Tienes la versión más reciente ({UpdateService.CurrentVersionText}).", 0, "Aceptar");
                    return;
                }
                if (manual) ShowUpdateDialog(release);
                else ToastWindow.ShowToast("Hay una nueva versión disponible", $"CUTLIFY {release.Version} está disponible.", null, null,
                    () => ShowUpdateDialog(release), new (string, Action)[] { ("Ver versión", () => ShowUpdateDialog(release)) });
            }
            catch (Exception ex)
            {
                Logger.Warn("Update", "No se pudo comprobar actualizaciones", ex);
                if (manual) DialogWindow.Ask(DialogOwner, "No se pudo buscar actualizaciones", "Comprueba tu conexión a Internet.\n\n" + ex.Message, 0, "Cerrar");
            }
        }

        private async void ShowUpdateDialog(ReleaseInfo release)
        {
            if (_updating) return;
            var dlg = new DialogWindow("Hay una nueva versión disponible",
                $"CUTLIFY {release.Version} está disponible.\nVersión instalada: {UpdateService.CurrentVersionText}", 1, "Más tarde", "Actualizar");
            dlg.SetDetails(string.IsNullOrWhiteSpace(release.Notes) ? "Sin notas de versión." : release.Notes);
            dlg.ShowDialog();
            if (dlg.Result != 1) return;

            if (IsRecording)
            {
                DialogWindow.Ask(null, "Grabación en curso", "Detén la grabación antes de actualizar.", 0, "Aceptar");
                return;
            }

            _updating = true;
            var progress = new DialogWindow("Descargando actualización…", $"CUTLIFY {release.Version}", -1);
            progress.SetProgress(0);
            progress.Show();
            try
            {
                var file = await UpdateService.DownloadAsync(release, new Progress<double>(p => progress.SetProgress(p)));
                progress.SetMessage("Instalando y reiniciando CUTLIFY…");
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
            StopRecordingForExit();
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
