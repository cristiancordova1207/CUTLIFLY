using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using Cutlifly.Core;
using Cutlifly.Hotkeys;
using Cutlifly.Settings;
using Cutlifly.Storage;
using Cutlifly.Update;

namespace Cutlifly.UI
{
    public partial class SettingsWindow : Window
    {
        private readonly AppController _app;
        private bool _loading;
        private string _capturingHotkey;

        public SettingsWindow(AppController app, string page)
        {
            _app = app;
            InitializeComponent();
            DataContext = SettingsService.Current;
            SourceInitialized += (_, __) => Native.RoundCorners(new WindowInteropHelper(this).Handle);
            CloseButton.Click += (_, __) => Close();
            Closed += (_, __) => { _app.Hotkeys.CancelCapture(); SettingsService.Save(); };

            Nav.SelectionChanged += (_, __) => ShowPage((Nav.SelectedItem as ListBoxItem)?.Tag as string);

            // Guardar al cambiar cualquier interruptor enlazado.
            AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler((s, e) =>
            {
                if (e.OriginalSource is CheckBox cb && cb != StartWithWindows) SettingsService.Save();
            }));

            StartWithWindows.Click += (_, __) =>
            {
                StartupManager.SetEnabled(StartWithWindows.IsChecked == true);
                SettingsService.Current.StartWithWindows = StartupManager.IsEnabled();
                SettingsService.Save();
            };

            HookCombo(FormatCombo, v => SettingsService.Current.ImageFormat = v);
            HookCombo(FpsCombo, v => SettingsService.Current.RecordFps = int.Parse(v));
            HookCombo(QualityCombo, v => SettingsService.Current.RecordQuality = v);
            HookCombo(RetentionCombo, v => { SettingsService.Current.RetentionMinutes = int.Parse(v); _app.Cleaner.CleanExpired(); UpdateStorage(); });
            HookCombo(UpdateCombo, v => SettingsService.Current.UpdateCheck = v);

            ThemeLight.Checked += (_, __) => SetTheme("Light");
            ThemeDark.Checked += (_, __) => SetTheme("Dark");
            ThemeSystem.Checked += (_, __) => SetTheme("System");

            CleanButton.Click += (_, __) => { _app.ConfirmCleanTemp(this); UpdateStorage(); };
            OpenTempButton.Click += (_, __) => AppController.OpenFolder(AppPaths.Temp);
            LogsButton.Click += (_, __) => AppController.OpenFolder(AppPaths.Logs);
            ResetButton.Click += (_, __) =>
            {
                if (DialogWindow.Ask(this, "¿Restablecer la configuración?", "Se restaurarán los valores predeterminados. El historial no se borra.", 1, "Cancelar", "Restablecer") != 1) return;
                SettingsService.Reset();
                DataContext = null;
                DataContext = SettingsService.Current;
                _app.ApplySettings();
                LoadValues();
            };
            PicturesFolderButton.Click += (_, __) => PickFolder(true);
            VideosFolderButton.Click += (_, __) => PickFolder(false);

            HkCaptureButton.Click += (_, __) => BeginHotkeyCapture("capture");
            HkRecordButton.Click += (_, __) => BeginHotkeyCapture("record");
            HkOpenButton.Click += (_, __) => BeginHotkeyCapture("open");
            HkResetButton.Click += (_, __) =>
            {
                var d = new AppSettings();
                SettingsService.Current.HotkeyCapture = d.HotkeyCapture;
                SettingsService.Current.HotkeyRecord = d.HotkeyRecord;
                SettingsService.Current.HotkeyOpen = d.HotkeyOpen;
                SettingsService.Save();
                HkStatus.Text = _app.ApplyHotkeys();
                LoadValues();
            };

            CheckUpdatesButton.Click += (_, __) => _app.CheckForUpdates(manual: true);
            ReleasesButton.Click += (_, __) => AppController.OpenUrl(UpdateService.ReleasesPage);

            LoadValues();
            var target = Nav.Items.Cast<ListBoxItem>().FirstOrDefault(i => (string)i.Tag == page) ?? (ListBoxItem)Nav.Items[0];
            Nav.SelectedItem = target;
        }

        public void ShowPage(string tag)
        {
            if (tag == null) return;
            foreach (FrameworkElement p in Pages.Children)
                p.Visibility = p.Name == "Page_" + tag ? Visibility.Visible : Visibility.Collapsed;
            if (tag == "storage") UpdateStorage();
            if (Nav.SelectedItem is ListBoxItem li && (string)li.Tag != tag)
                Nav.SelectedItem = Nav.Items.Cast<ListBoxItem>().FirstOrDefault(i => (string)i.Tag == tag);
        }

        private void LoadValues()
        {
            _loading = true;
            var s = SettingsService.Current;
            StartWithWindows.IsChecked = StartupManager.IsEnabled();
            MainWindow.SelectByTag(FormatCombo, s.ImageFormat);
            MainWindow.SelectByTag(FpsCombo, s.RecordFps.ToString());
            MainWindow.SelectByTag(QualityCombo, s.RecordQuality);
            MainWindow.SelectByTag(RetentionCombo, s.RetentionMinutes.ToString());
            MainWindow.SelectByTag(UpdateCombo, s.UpdateCheck);
            ThemeLight.IsChecked = s.Theme == "Light";
            ThemeDark.IsChecked = s.Theme == "Dark";
            ThemeSystem.IsChecked = s.Theme != "Light" && s.Theme != "Dark";
            HkCaptureText.Text = s.HotkeyCapture;
            HkRecordText.Text = s.HotkeyRecord;
            HkOpenText.Text = s.HotkeyOpen;
            PicturesFolderText.Text = s.PicturesFolder ?? AppPaths.DefaultPicturesFolder;
            VideosFolderText.Text = s.VideosFolder ?? AppPaths.DefaultVideosFolder;
            VersionText.Text = "Versión " + UpdateService.CurrentVersionText;
            LastCheckText.Text = s.LastUpdateCheckUtc == default ? "Aún no se ha comprobado." : "Última comprobación: " + s.LastUpdateCheckUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
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

        private void SetTheme(string theme)
        {
            if (_loading) return;
            SettingsService.Current.Theme = theme;
            SettingsService.Save();
            ThemeManager.Apply(theme);
        }

        private void UpdateStorage()
        {
            var (count, bytes) = TempCleaner.GetUsage();
            TempCountText.Text = count.ToString();
            TempSizeText.Text = HistoryItem.FormatBytes(bytes);
            TempPathText.Text = AppPaths.Temp;
        }

        private void PickFolder(bool pictures)
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = pictures ? "Carpeta para guardar capturas" : "Carpeta para guardar grabaciones",
                UseDescriptionForTitle = true,
                SelectedPath = pictures ? (SettingsService.Current.PicturesFolder ?? AppPaths.DefaultPicturesFolder)
                                        : (SettingsService.Current.VideosFolder ?? AppPaths.DefaultVideosFolder)
            };
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            if (pictures) SettingsService.Current.PicturesFolder = dlg.SelectedPath;
            else SettingsService.Current.VideosFolder = dlg.SelectedPath;
            SettingsService.Save();
            LoadValues();
        }

        private void BeginHotkeyCapture(string which)
        {
            _capturingHotkey = which;
            HkStatus.Text = "Pulsa la nueva combinación (con Ctrl, Alt, Shift o Win)…";
            var label = which switch { "capture" => HkCaptureText, "record" => HkRecordText, _ => HkOpenText };
            label.Text = "…";
            _app.Hotkeys.CaptureNext(hk => OnHotkeyCaptured(hk));
        }

        private void OnHotkeyCaptured(Hotkey hk)
        {
            var s = SettingsService.Current;
            var which = _capturingHotkey;
            _capturingHotkey = null;
            string text = hk.ToString();
            var others = new[] { ("capture", s.HotkeyCapture), ("record", s.HotkeyRecord), ("open", s.HotkeyOpen) }
                .Where(o => o.Item1 != which).Select(o => o.Item2);
            if (others.Any(o => Hotkey.TryParse(o, out var x) && x.Equals(hk)))
                HkStatus.Text = $"Conflicto: {text} ya está asignado a otra acción de CUTLIFLY.";
            else if (HotkeyManager.IsTakenByAnotherApp(hk))
                HkStatus.Text = $"Conflicto: {text} está en uso por otra aplicación.";
            else
            {
                if (which == "capture") s.HotkeyCapture = text;
                else if (which == "record") s.HotkeyRecord = text;
                else s.HotkeyOpen = text;
                SettingsService.Save();
                var problems = _app.ApplyHotkeys();
                HkStatus.Text = string.IsNullOrEmpty(problems) ? $"Atajo actualizado: {text}" : problems;
            }
            LoadValues();
        }
    }
}
