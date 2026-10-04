using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using Cutlify.Clipboard;
using Cutlify.Core;
using Cutlify.Hotkeys;
using Cutlify.Recording;
using Cutlify.Settings;
using Cutlify.Storage;
using Cutlify.Update;

namespace Cutlify.UI
{
    public partial class SettingsWindow : Window
    {
        private readonly AppController _app;
        private readonly MicrophoneTester _micTester = new MicrophoneTester();
        private System.Windows.Threading.DispatcherTimer _micTimeout;
        private bool _loading;
        private string _capturingHotkey;
        private static AppSettings S => SettingsService.Current;

        public SettingsWindow(AppController app, string page)
        {
            _app = app;
            InitializeComponent();
            DataContext = S;
            SourceInitialized += (_, __) => Native.RoundCorners(new WindowInteropHelper(this).Handle);
            CloseButton.Click += (_, __) => Close();
            Closed += (_, __) => { _app.Hotkeys.CancelCapture(); _micTester.Dispose(); SettingsService.Save(); };

            Nav.SelectionChanged += (_, __) => ShowPage((Nav.SelectedItem as ListBoxItem)?.Tag as string);

            // Guardar al cambiar cualquier interruptor enlazado.
            AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler((s, e) =>
            {
                if (e.OriginalSource is CheckBox cb && cb != StartWithWindows) { SettingsService.Save(); UpdateAudioOptions(); }
            }));

            StartWithWindows.Click += (_, __) =>
            {
                StartupManager.SetEnabled(StartWithWindows.IsChecked == true);
                S.StartWithWindows = StartupManager.IsEnabled();
                SettingsService.Save();
            };

            HookCombo(FormatCombo, v => S.ImageFormat = v);
            HookCombo(DelayCombo, v => S.DefaultDelaySeconds = int.Parse(v));
            HookCombo(RetentionCombo, v => { S.RetentionMinutes = int.Parse(v); _app.Cleaner.CleanExpired(); UpdateStorage(); });
            HookCombo(UpdateCombo, v => S.UpdateCheck = v);
            InitRecording();

            ThemeLight.Checked += (_, __) => SetTheme("Light");
            ThemeDark.Checked += (_, __) => SetTheme("Dark");
            ThemeSystem.Checked += (_, __) => SetTheme("System");

            // Almacenamiento
            CleanButton.Click += (_, __) => { _app.ConfirmCleanTemp(this); UpdateStorage(); };
            OpenTempButton.Click += (_, __) => AppController.OpenFolder(AppPaths.Temp);
            ClearHistoryButton.Click += (_, __) => _app.ClearHistory(this);
            CapturesFolderButton.Click += (_, __) => ChangeFolder(ItemKind.Image, pick: true);
            RecordingsFolderButton.Click += (_, __) => ChangeFolder(ItemKind.Video, pick: true);
            CapturesDefaultButton.Click += (_, __) => ChangeFolder(ItemKind.Image, pick: false);
            RecordingsDefaultButton.Click += (_, __) => ChangeFolder(ItemKind.Video, pick: false);

            // Avanzado
            LogsButton.Click += (_, __) => AppController.OpenFolder(AppPaths.Logs);
            ResetButton.Click += (_, __) =>
            {
                if (DialogWindow.Ask(this, "¿Restaurar los valores predeterminados?", "Se restaura toda la configuración. El historial y tus archivos no se borran.", 1, "Cancelar", "Restaurar") != 1) return;
                SettingsService.Reset();
                ReloadAll();
            };

            // Atajos
            HkCaptureButton.Click += (_, __) => BeginHotkeyCapture("capture");
            HkRecordButton.Click += (_, __) => BeginHotkeyCapture("record");
            HkOpenButton.Click += (_, __) => BeginHotkeyCapture("open");
            HkRepeatButton.Click += (_, __) => BeginHotkeyCapture("repeat");
            HkResetButton.Click += (_, __) =>
            {
                var d = new AppSettings();
                S.HotkeyCapture = d.HotkeyCapture;
                S.HotkeyRecord = d.HotkeyRecord;
                S.HotkeyOpen = d.HotkeyOpen;
                S.HotkeyRepeat = d.HotkeyRepeat;
                SettingsService.Save();
                HkStatus.Text = _app.ApplyHotkeys();
                LoadValues();
            };

            // Privacidad
            ConsentRevokeButton.Click += (_, __) => { S.MicrophoneConsent = MicConsent.Denied; SettingsService.Save(); _micTester.Stop(); UpdatePrivacy(); };
            ConsentAskButton.Click += (_, __) => { S.MicrophoneConsent = MicConsent.Unknown; _app.EnsureMicrophoneConsent(this); UpdatePrivacy(); };
            WindowsMicButton.Click += (_, __) => AppController.OpenUrl("ms-settings:privacy-microphone");

            // Diagnóstico
            RunDiagnosticsButton.Click += (_, __) =>
            {
                DiagnosticsText.Text = "Ejecutando…";
                Dispatcher.BeginInvoke(new Action(() => DiagnosticsText.Text = DiagnosticsService.Run()), System.Windows.Threading.DispatcherPriority.Background);
            };
            CopyDiagnosticsButton.Click += (_, __) => { if (DiagnosticsText.Text.Length > 0) ClipboardService.CopyText(DiagnosticsText.Text); };

            CheckUpdatesButton.Click += (_, __) => _app.CheckForUpdates(manual: true);
            ReleasesButton.Click += (_, __) => AppController.OpenUrl(UpdateService.ReleasesPage);

            LoadValues();
            var target = Nav.Items.Cast<ListBoxItem>().FirstOrDefault(i => (string)i.Tag == page) ?? (ListBoxItem)Nav.Items[0];
            Nav.SelectedItem = target;
        }

        private void ReloadAll()
        {
            DataContext = null;
            DataContext = S;
            _app.ApplySettings();
            LoadValues();
            LoadRecording();
        }

        public void ShowPage(string tag)
        {
            if (tag == null) return;
            foreach (FrameworkElement p in Pages.Children)
                p.Visibility = p.Name == "Page_" + tag ? Visibility.Visible : Visibility.Collapsed;
            if (tag == "storage") UpdateStorage();
            if (tag == "privacy") UpdatePrivacy();
            if (tag != "record") StopMicTest();
            if (Nav.SelectedItem is ListBoxItem li && (string)li.Tag != tag)
                Nav.SelectedItem = Nav.Items.Cast<ListBoxItem>().FirstOrDefault(i => (string)i.Tag == tag);
        }

        private void LoadValues()
        {
            _loading = true;
            StartWithWindows.IsChecked = StartupManager.IsEnabled();
            MainWindow.SelectByTag(FormatCombo, S.ImageFormat);
            MainWindow.SelectByTag(DelayCombo, S.DefaultDelaySeconds.ToString());
            MainWindow.SelectByTag(RetentionCombo, S.RetentionMinutes.ToString());
            MainWindow.SelectByTag(UpdateCombo, S.UpdateCheck);
            ThemeLight.IsChecked = S.Theme == "Light";
            ThemeDark.IsChecked = S.Theme == "Dark";
            ThemeSystem.IsChecked = S.Theme != "Light" && S.Theme != "Dark";
            HkCaptureText.Text = S.HotkeyCapture;
            HkRecordText.Text = S.HotkeyRecord;
            HkOpenText.Text = S.HotkeyOpen;
            HkRepeatText.Text = S.HotkeyRepeat;
            CapturesFolderText.Text = AppPaths.CapturesFolder;
            RecordingsFolderText.Text = AppPaths.RecordingsFolder;
            VersionText.Text = "Versión " + UpdateService.CurrentVersionText;
            LastCheckText.Text = S.LastUpdateCheckUtc == default ? "Aún no se ha comprobado." : "Última comprobación: " + S.LastUpdateCheckUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
            _loading = false;
        }

        private void HookCombo(ComboBox combo, Action<string> apply)
        {
            combo.SelectionChanged += (_, __) =>
            {
                if (_loading || !(combo.SelectedItem is ComboBoxItem it)) return;
                apply((string)it.Tag);
                SettingsService.Save();
            };
        }

        private static ComboBoxItem Item(string text, string tag, bool enabled = true) =>
            new ComboBoxItem { Content = text, Tag = tag, IsEnabled = enabled };

        private void SetTheme(string theme)
        {
            if (_loading) return;
            S.Theme = theme;
            SettingsService.Save();
            ThemeManager.Apply(theme);
        }

        // ------------------------------------------------------------------ Grabación

        private int _maxRefresh, _maxHeight;

        private void InitRecording()
        {
            _maxRefresh = VideoSettings.MaxRefreshRate();
            _maxHeight = VideoSettings.MaxScreenHeight();

            foreach (var p in VideoSettings.Profiles)
            {
                bool ok = VideoSettings.IsProfileCompatible(p, _maxRefresh, _maxHeight);
                ProfileCombo.Items.Add(Item(ok ? p.Name : $"{p.Name} (no compatible)", p.Name, ok));
            }
            ProfileCombo.Items.Add(Item("Personalizado", "Personalizado"));
            foreach (var r in VideoSettings.Resolutions)
            {
                bool ok = VideoSettings.CompatibleResolutions(_maxHeight).Contains(r);
                ResolutionCombo.Items.Add(Item(r == "Automática" ? "Automática (tamaño original)" : ok ? r : $"{r} (supera tu pantalla)", r, ok));
            }
            foreach (var f in VideoSettings.FpsOptions)
            {
                bool ok = VideoSettings.CompatibleFps(_maxRefresh).Contains(f);
                FpsCombo.Items.Add(Item(ok ? $"{f} FPS" : $"{f} FPS (monitor de {_maxRefresh} Hz)", f.ToString(), ok));
            }
            FpsHint.Text = $"Tu monitor más rápido funciona a {_maxRefresh} Hz: no se pueden capturar más fotogramas reales que eso.";
            foreach (var c in VideoSettings.DetectCodecs())
                CodecCombo.Items.Add(Item(c.ToString(), c.Name, c.Available));
            foreach (var q in VideoSettings.Qualities) QualityCombo.Items.Add(Item(q, q));
            foreach (var b in VideoSettings.BitrateOptionsMbps) BitrateCombo.Items.Add(Item(b == 0 ? "Automático" : $"{b} Mbps", b.ToString()));
            BitrateCombo.Items.Add(Item("Personalizado", "custom"));

            ProfileCombo.SelectionChanged += (_, __) =>
            {
                if (_loading || !(ProfileCombo.SelectedItem is ComboBoxItem it)) return;
                var p = VideoSettings.Profiles.FirstOrDefault(x => x.Name == (string)it.Tag);
                S.RecordProfile = (string)it.Tag;
                if (p != null) { S.RecordResolution = p.Resolution; S.RecordFps = p.Fps; }
                SettingsService.Save();
                LoadRecording();
            };
            void Manual(Action a) { if (_loading) return; a(); S.RecordProfile = MatchProfile(); SettingsService.Save(); LoadRecording(); }
            ResolutionCombo.SelectionChanged += (_, __) => { if (ResolutionCombo.SelectedItem is ComboBoxItem it) Manual(() => S.RecordResolution = (string)it.Tag); };
            FpsCombo.SelectionChanged += (_, __) => { if (FpsCombo.SelectedItem is ComboBoxItem it) Manual(() => S.RecordFps = int.Parse((string)it.Tag)); };
            CodecCombo.SelectionChanged += (_, __) => { if (CodecCombo.SelectedItem is ComboBoxItem it) Manual(() => S.RecordCodec = (string)it.Tag); };
            QualityCombo.SelectionChanged += (_, __) => { if (QualityCombo.SelectedItem is ComboBoxItem it) Manual(() => S.RecordQuality = (string)it.Tag); };
            BitrateCombo.SelectionChanged += (_, __) =>
            {
                if (_loading || !(BitrateCombo.SelectedItem is ComboBoxItem it)) return;
                if ((string)it.Tag == "custom")
                {
                    CustomBitrateBox.Visibility = Visibility.Visible;
                    if (S.RecordBitrateMbps <= 0) S.RecordBitrateMbps = 15;
                    CustomBitrateBox.Text = S.RecordBitrateMbps.ToString();
                    CustomBitrateBox.Focus();
                }
                else S.RecordBitrateMbps = int.Parse((string)it.Tag);
                SettingsService.Save();
                LoadRecording();
            };
            CustomBitrateBox.LostFocus += (_, __) =>
            {
                if (int.TryParse(CustomBitrateBox.Text, out int v) && v >= 1 && v <= 500) { S.RecordBitrateMbps = v; SettingsService.Save(); }
                LoadRecording();
            };

            SourceSystem.Checked += (_, __) => SetSource(true, false);
            SourceMic.Checked += (_, __) => SetSource(false, true);
            SourceBoth.Checked += (_, __) => SetSource(true, true);
            SystemDeviceCombo.SelectionChanged += (_, __) => { if (!_loading && SystemDeviceCombo.SelectedItem is ComboBoxItem it) { S.SystemAudioDeviceId = it.Tag as string; SettingsService.Save(); } };
            MicDeviceCombo.SelectionChanged += (_, __) =>
            {
                if (_loading || !(MicDeviceCombo.SelectedItem is ComboBoxItem it)) return;
                S.MicrophoneDeviceId = it.Tag as string;
                SettingsService.Save();
                if (_micTimeout?.IsEnabled == true) StartMicTest();
            };
            MicTestButton.Click += (_, __) => { if (_micTimeout?.IsEnabled == true) StopMicTest(); else StartMicTest(); };
            _micTester.Level += v => Dispatcher.BeginInvoke(new Action(() => MicLevel.Value = v));
            _micTester.Failed += msg => Dispatcher.BeginInvoke(new Action(() =>
            {
                MicStatus.Text = msg;
                StopMicTest();
                if (msg.StartsWith("Windows")) AppController.ShowWindowsMicrophoneBlocked(this);
            }));
            RecordDefaultsButton.Click += (_, __) =>
            {
                var d = AppSettings.Defaults();
                S.RecordProfile = d.RecordProfile; S.RecordResolution = d.RecordResolution; S.RecordFps = d.RecordFps;
                S.RecordCodec = d.RecordCodec; S.RecordQuality = d.RecordQuality; S.RecordBitrateMbps = d.RecordBitrateMbps;
                S.RecordAudio = d.RecordAudio; S.RecordSystemAudio = d.RecordSystemAudio; S.RecordMicrophone = d.RecordMicrophone;
                S.HardwareEncoding = d.HardwareEncoding; S.RecordCursor = d.RecordCursor; S.HighlightClicks = d.HighlightClicks;
                SettingsService.Save();
                ReloadAll();
            };
            LoadRecording();
        }

        private string MatchProfile() =>
            VideoSettings.Profiles.Skip(1).FirstOrDefault(p => p.Resolution == S.RecordResolution && p.Fps == S.RecordFps)?.Name ?? "Personalizado";

        private void LoadRecording()
        {
            _loading = true;
            MainWindow.SelectByTag(ProfileCombo, S.RecordProfile);
            MainWindow.SelectByTag(ResolutionCombo, S.RecordResolution);
            MainWindow.SelectByTag(FpsCombo, S.RecordFps.ToString());
            MainWindow.SelectByTag(CodecCombo, S.RecordCodec);
            MainWindow.SelectByTag(QualityCombo, S.RecordQuality);
            bool preset = VideoSettings.BitrateOptionsMbps.Contains(S.RecordBitrateMbps);
            MainWindow.SelectByTag(BitrateCombo, preset ? S.RecordBitrateMbps.ToString() : "custom");
            CustomBitrateBox.Visibility = preset ? Visibility.Collapsed : Visibility.Visible;
            if (!preset) CustomBitrateBox.Text = S.RecordBitrateMbps.ToString();

            var example = VideoSettings.OutputSize(new System.Drawing.Size(3840, 2160), S.RecordResolution);
            if (S.RecordResolution == "Automática") example = new System.Drawing.Size(1920, 1080);
            var (bps, auto) = VideoSettings.ResolveBitrate(S, example);
            BitrateHint.Text = auto
                ? $"Automático: ≈ {bps / 1_000_000.0:0.#} Mbps para {example.Width}×{example.Height} a {S.RecordFps} FPS con {S.RecordCodec} (calidad {S.RecordQuality.ToLowerInvariant()})."
                : $"Fijo: {bps / 1_000_000} Mbps.";

            SourceSystem.IsChecked = S.RecordSystemAudio && !S.RecordMicrophone;
            SourceMic.IsChecked = !S.RecordSystemAudio && S.RecordMicrophone;
            SourceBoth.IsChecked = S.RecordSystemAudio && S.RecordMicrophone;
            FillDevices(SystemDeviceCombo, AudioCapture.ListDevices(false), S.SystemAudioDeviceId);
            FillDevices(MicDeviceCombo, AudioCapture.ListDevices(true), S.MicrophoneDeviceId);
            _loading = false;
            UpdateAudioOptions();
        }

        private static void FillDevices(ComboBox combo, System.Collections.Generic.List<AudioDevice> devices, string selected)
        {
            combo.Items.Clear();
            combo.Items.Add(Item("Predeterminado de Windows", null));
            foreach (var d in devices) combo.Items.Add(Item(d.Name, d.Id));
            combo.SelectedItem = combo.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == selected) ?? combo.Items[0];
        }

        private void SetSource(bool system, bool mic)
        {
            if (_loading) return;
            S.RecordSystemAudio = system;
            S.RecordMicrophone = mic;
            SettingsService.Save();
            UpdateAudioOptions();
        }

        private void UpdateAudioOptions()
        {
            AudioOptions.IsEnabled = S.RecordAudio;
            AudioOptions.Opacity = S.RecordAudio ? 1 : 0.5;
            SystemDeviceCombo.IsEnabled = S.RecordSystemAudio;
            MicDeviceCombo.IsEnabled = S.RecordMicrophone;
        }

        private void StartMicTest()
        {
            if (!_app.EnsureMicrophoneConsent(this)) { MicStatus.Text = "Has elegido no permitir el micrófono. Puedes cambiarlo en Privacidad."; return; }
            if (!_micTester.Start(S.MicrophoneDeviceId)) return;
            MicStatus.Text = "Habla para comprobar el nivel. La prueba se detiene sola en 15 segundos.";
            MicTestButton.Content = "Detener prueba";
            _micTimeout ??= new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            _micTimeout.Tick -= OnMicTimeout;
            _micTimeout.Tick += OnMicTimeout;
            _micTimeout.Start();
        }

        private void OnMicTimeout(object sender, EventArgs e) => StopMicTest();

        private void StopMicTest()
        {
            _micTimeout?.Stop();
            _micTester.Stop();
            MicLevel.Value = 0;
            MicTestButton.Content = "Probar micrófono";
        }

        // ------------------------------------------------------------------ Almacenamiento

        private void UpdateStorage()
        {
            var (count, bytes) = TempCleaner.GetUsage();
            TempCountText.Text = count.ToString();
            TempSizeText.Text = HistoryItem.FormatBytes(bytes);
            TempPathText.Text = AppPaths.Temp;
            CapturesFolderText.Text = AppPaths.CapturesFolder;
            RecordingsFolderText.Text = AppPaths.RecordingsFolder;
        }

        private void ChangeFolder(ItemKind kind, bool pick)
        {
            bool images = kind == ItemKind.Image;
            var current = images ? AppPaths.CapturesFolder : AppPaths.RecordingsFolder;
            string next = images ? AppPaths.DefaultCapturesFolder : AppPaths.DefaultRecordingsFolder;
            if (pick)
            {
                using var dlg = new System.Windows.Forms.FolderBrowserDialog
                {
                    Description = images ? "Ubicación de capturas" : "Ubicación de grabaciones",
                    UseDescriptionForTitle = true,
                    SelectedPath = current
                };
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                next = dlg.SelectedPath;
            }
            if (string.Equals(System.IO.Path.GetFullPath(next), System.IO.Path.GetFullPath(current), StringComparison.OrdinalIgnoreCase)) return;

            if (images) S.CapturesFolder = pick ? next : null; else S.RecordingsFolder = pick ? next : null;
            SettingsService.Save();

            int existing = _app.History.Items.Count(i => i.Kind == kind && i.IsPermanent && i.HasFile && AppPaths.IsInside(i.FilePath, current));
            if (existing > 0 && DialogWindow.Ask(this, "¿Mover los archivos existentes?",
                    $"Hay {existing} {(images ? "capturas" : "grabaciones")} guardadas en la ubicación anterior.\nLos archivos nuevos se guardarán en:\n{next}",
                    0, "Mover archivos existentes", "Dejarlos donde están") == 0)
            {
                int moved = _app.MoveSavedFiles(current, next, kind);
                DialogWindow.Ask(this, "Archivos movidos", $"Se movieron {moved} de {existing} archivos.", 0, "Aceptar");
            }
            UpdateStorage();
        }

        // ------------------------------------------------------------------ Privacidad

        private void UpdatePrivacy()
        {
            ConsentText.Text = "Consentimiento en CUTLIFY: " + DiagnosticsService.MicConsentLabel(S.MicrophoneConsent);
            WindowsMicText.Text = "Privacidad de Windows: " + DiagnosticsService.WindowsMicrophoneAccess();
            ConsentRevokeButton.IsEnabled = S.MicrophoneConsent == MicConsent.Granted;
        }

        // ------------------------------------------------------------------ Atajos

        private void BeginHotkeyCapture(string which)
        {
            _capturingHotkey = which;
            HkStatus.Text = "Pulsa la nueva combinación (con Ctrl, Alt, Shift o Win)…";
            var label = which switch { "capture" => HkCaptureText, "record" => HkRecordText, "repeat" => HkRepeatText, _ => HkOpenText };
            label.Text = "…";
            _app.Hotkeys.CaptureNext(hk => OnHotkeyCaptured(hk));
        }

        private void OnHotkeyCaptured(Hotkey hk)
        {
            var which = _capturingHotkey;
            _capturingHotkey = null;
            string text = hk.ToString();
            var others = new[] { ("capture", S.HotkeyCapture), ("record", S.HotkeyRecord), ("open", S.HotkeyOpen), ("repeat", S.HotkeyRepeat) }
                .Where(o => o.Item1 != which).Select(o => o.Item2);
            if (others.Any(o => Hotkey.TryParse(o, out var x) && x.Equals(hk)))
                HkStatus.Text = $"Conflicto: {text} ya está asignado a otra acción de CUTLIFY.";
            else if (HotkeyManager.IsTakenByAnotherApp(hk))
                HkStatus.Text = $"Conflicto: {text} está en uso por otra aplicación.";
            else
            {
                switch (which)
                {
                    case "capture": S.HotkeyCapture = text; break;
                    case "record": S.HotkeyRecord = text; break;
                    case "repeat": S.HotkeyRepeat = text; break;
                    default: S.HotkeyOpen = text; break;
                }
                SettingsService.Save();
                var problems = _app.ApplyHotkeys();
                HkStatus.Text = string.IsNullOrEmpty(problems) ? $"Atajo actualizado: {text}" : problems;
            }
            LoadValues();
        }
    }
}
