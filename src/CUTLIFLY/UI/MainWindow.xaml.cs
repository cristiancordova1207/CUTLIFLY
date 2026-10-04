using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Cutlifly.Core;
using Cutlifly.Settings;
using Cutlifly.Storage;
using CaptureMode = Cutlifly.Settings.CaptureMode;

namespace Cutlifly.UI
{
    public partial class MainWindow : Window
    {
        private static readonly string[] ImageExt = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp" };
        private static readonly string[] VideoExt = { ".mp4", ".mov", ".m4v", ".wmv", ".avi", ".mkv", ".webm" };

        private readonly AppController _app;
        private readonly DispatcherTimer _refresh;
        private bool _loadingSettings;

        public HistoryItem Current { get; private set; }

        public MainWindow(AppController app)
        {
            _app = app;
            InitializeComponent();

            RecentList.ItemsSource = app.History.Items;
            app.History.Items.CollectionChanged += OnHistoryChanged;
            UpdateCount();

            SourceInitialized += (_, __) => Native.RoundCorners(new WindowInteropHelper(this).Handle);
            StateChanged += OnStateChanged;
            Closing += OnClosing;

            MinButton.Click += (_, __) => WindowState = WindowState.Minimized;
            MaxButton.Click += (_, __) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            CloseButton.Click += (_, __) => Close();

            NewButton.Click += (_, __) => StartNew();
            NewMenuButton.Click += (_, __) => { NewMenu.PlacementTarget = NewMenuButton; NewMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom; NewMenu.IsOpen = true; };
            NewCapRect.Click += (_, __) => _app.StartCapture(CaptureMode.Rectangle, 0, true);
            NewCapWindow.Click += (_, __) => _app.StartCapture(CaptureMode.Window, 0, true);
            NewCapFull.Click += (_, __) => _app.StartCapture(CaptureMode.FullScreen, 0, true);
            NewCap3.Click += (_, __) => _app.StartCapture(SelectedMode, 3, true);
            NewCap5.Click += (_, __) => _app.StartCapture(SelectedMode, 5, true);
            NewCap10.Click += (_, __) => _app.StartCapture(SelectedMode, 10, true);
            NewRecRect.Click += (_, __) => _app.StartRecording(CaptureMode.Rectangle, 0, true);
            NewRecWindow.Click += (_, __) => _app.StartRecording(CaptureMode.Window, 0, true);
            NewRecFull.Click += (_, __) => _app.StartRecording(CaptureMode.FullScreen, 0, true);
            EmptyCapture.Click += (_, __) => _app.StartCapture(SelectedMode, SelectedDelay, true);
            EmptyRecord.Click += (_, __) => _app.StartRecording(SelectedMode, SelectedDelay, true);
            RepeatButton.Click += (_, __) => _app.RepeatLastCapture();
            HistoryButton.Click += (_, __) => _app.ShowHistory();
            SeeAllButton.Click += (_, __) => _app.ShowHistory();
            SettingsButton.Click += (_, __) => _app.ShowSettings();
            MoreButton.Click += (_, __) => { MoreMenu.PlacementTarget = MoreButton; MoreMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom; MoreMenu.IsOpen = true; };
            MenuUpdates.Click += (_, __) => _app.CheckForUpdates(manual: true);
            MenuTemp.Click += (_, __) => AppController.OpenFolder(AppPaths.Temp);
            MenuLogs.Click += (_, __) => AppController.OpenFolder(AppPaths.Logs);
            MenuAbout.Click += (_, __) => _app.ShowSettings("about");
            MenuExit.Click += (_, __) => _app.Exit();
            OpenTempButton.Click += (_, __) => AppController.OpenFolder(AppPaths.Temp);

            ModeCombo.SelectionChanged += (_, __) => { if (!_loadingSettings) { SettingsService.Current.LastMode = SelectedMode; SettingsService.Save(); } };
            DelayCombo.SelectionChanged += (_, __) => { if (!_loadingSettings) { SettingsService.Current.DefaultDelaySeconds = SelectedDelay; SettingsService.Save(); } };
            TempSwitch.Click += (_, __) => { SettingsService.Current.SaveTemporarily = TempSwitch.IsChecked == true; SettingsService.Save(); };
            RetentionCombo.SelectionChanged += (_, __) =>
            {
                if (_loadingSettings || !(RetentionCombo.SelectedItem is ComboBoxItem it)) return;
                SettingsService.Current.RetentionMinutes = int.Parse((string)it.Tag);
                SettingsService.Save();
                _app.Cleaner.CleanExpired();
            };

            RecentList.SelectionChanged += (_, __) =>
            {
                if (RecentList.SelectedItem is HistoryItem item && item != Current) ShowItem(item);
            };

            // Editor
            Editor.CopyRequested += () => _app.CopyImage(Editor.Render());
            Editor.SaveRequested += () => { if (Current != null) _app.SaveImage(Current, Editor.Render()); };
            Editor.PinRequested += () => _app.Pin(Editor.Render());
            Editor.OpenLocationRequested += () => { if (Current != null) _app.OpenLocation(Current); };
            Editor.DeleteRequested += () => { if (Current != null) _app.Delete(Current, this); };

            // Vídeo
            Video.CopyRequested += () => { if (Current != null) _app.Copy(Current); };
            Video.SaveRequested += () => { if (Current != null) _app.SaveVideo(Current); };
            Video.OpenRequested += () => { if (Current != null) _app.Open(Current); };
            Video.OpenLocationRequested += () => { if (Current != null) _app.OpenLocation(Current); };
            Video.DeleteRequested += () => { if (Current != null) _app.Delete(Current, this); };

            PreviewKeyDown += OnPreviewKeyDown;
            DragEnter += OnDragEnter;
            DragOver += OnDragEnter;
            DragLeave += (_, __) => DropHint.Visibility = Visibility.Collapsed;
            Drop += OnDrop;

            _refresh = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _refresh.Tick += (_, __) => { foreach (var i in _app.History.Items) i.Refresh(); };
            _refresh.Start();

            LoadSettings();
            SettingsService.Changed += () => Dispatcher.BeginInvoke(new Action(LoadSettings));
        }

        public CaptureMode SelectedMode =>
            ModeCombo.SelectedItem is ComboBoxItem it && Enum.TryParse<CaptureMode>((string)it.Tag, out var m) ? m : CaptureMode.Rectangle;

        public int SelectedDelay => DelayCombo.SelectedItem is ComboBoxItem it ? int.Parse((string)it.Tag) : 0;

        private void StartNew()
        {
            if (KindRecord.IsChecked == true) _app.StartRecording(SelectedMode, SelectedDelay, true);
            else _app.StartCapture(SelectedMode, SelectedDelay, true);
        }

        public void LoadSettings()
        {
            _loadingSettings = true;
            var s = SettingsService.Current;
            SelectByTag(ModeCombo, s.LastMode.ToString());
            SelectByTag(DelayCombo, s.DefaultDelaySeconds.ToString());
            SelectByTag(RetentionCombo, s.RetentionMinutes.ToString());
            TempSwitch.IsChecked = s.SaveTemporarily;
            CaptureKeyText.Text = s.HotkeyCapture.Replace("+", " + ");
            RecordKeyText.Text = s.HotkeyRecord.Replace("+", " + ");
            _loadingSettings = false;
        }

        public static void SelectByTag(ComboBox combo, string tag)
        {
            foreach (var o in combo.Items)
                if (o is ComboBoxItem it && (string)it.Tag == tag) { combo.SelectedItem = it; return; }
            if (combo.SelectedIndex < 0 && combo.Items.Count > 0) combo.SelectedIndex = 0;
        }

        // ------------------------------------------------------------------ Elementos

        public void ShowItem(HistoryItem item)
        {
            Current = item;
            if (item == null)
            {
                Video.Release();
                EmptyState.Visibility = Visibility.Visible;
                Editor.Visibility = Visibility.Collapsed;
                Video.Visibility = Visibility.Collapsed;
                RecentList.SelectedItem = null;
                return;
            }
            EmptyState.Visibility = Visibility.Collapsed;
            if (item.IsVideo)
            {
                Editor.Visibility = Visibility.Collapsed;
                Video.Visibility = Visibility.Visible;
                Video.Load(item);
            }
            else
            {
                Video.Release();
                Video.Visibility = Visibility.Collapsed;
                var img = AppController.GetImage(item);
                if (img == null) { ShowItem(null); return; }
                Editor.Visibility = Visibility.Visible;
                Editor.Load(img);
                Editor.SetActionsVisible(!item.IsExternal, item.HasFile);
            }
            if (_app.History.Items.Contains(item))
            {
                RecentList.SelectedItem = item;
                RecentList.ScrollIntoView(item);
            }
            else RecentList.SelectedItem = null;
        }

        private void OnHistoryChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            UpdateCount();
            if (e.OldItems != null && Current != null && e.OldItems.Contains(Current))
            {
                var next = _app.History.Items.FirstOrDefault();
                ShowItem(IsVisible ? next : null);
            }
        }

        private void UpdateCount()
        {
            CountText.Text = _app.History.Items.Count.ToString();
            RecentEmpty.Visibility = _app.History.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private HistoryItem ItemOf(object sender) => (sender as FrameworkElement)?.DataContext as HistoryItem;

        private void Ctx_Copy(object sender, RoutedEventArgs e) { var i = ItemOf(sender); if (i != null) _app.Copy(i); }
        private void Ctx_Save(object sender, RoutedEventArgs e) { var i = ItemOf(sender); if (i != null) _app.Save(i); }
        private void Ctx_Open(object sender, RoutedEventArgs e) { var i = ItemOf(sender); if (i != null) _app.Open(i); }
        private void Ctx_Pin(object sender, RoutedEventArgs e) { var i = ItemOf(sender); if (i != null && !i.IsVideo) _app.Pin(AppController.GetImage(i)); }
        private void Ctx_Location(object sender, RoutedEventArgs e) { var i = ItemOf(sender); if (i != null) _app.OpenLocation(i); }
        private void Ctx_Delete(object sender, RoutedEventArgs e) { var i = ItemOf(sender); if (i != null) _app.Delete(i, this); }

        // ------------------------------------------------------------------ Ventana

        private void OnStateChanged(object sender, EventArgs e)
        {
            RootGrid.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
            MaxButton.Content = WindowState == WindowState.Maximized ? "" : "";
            if (WindowState == WindowState.Minimized && SettingsService.Current.MinimizeToTray)
            {
                Hide();
                WindowState = WindowState.Normal;
            }
        }

        private void OnClosing(object sender, CancelEventArgs e)
        {
            if (_app.IsExiting) return;
            if (SettingsService.Current.RunInBackground)
            {
                e.Cancel = true;
                Video.Release();
                Hide();
            }
            else
            {
                e.Cancel = true;
                _app.Exit();
            }
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Editor.Visibility == Visibility.Visible && Editor.HandleKey(e)) { e.Handled = true; return; }
            if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control) { StartNew(); e.Handled = true; }
        }

        // ------------------------------------------------------------------ Arrastrar y soltar

        private static string DroppedFile(DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return null;
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            var f = files?.FirstOrDefault();
            if (f == null) return null;
            var ext = Path.GetExtension(f).ToLowerInvariant();
            return ImageExt.Contains(ext) || VideoExt.Contains(ext) ? f : null;
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            bool ok = DroppedFile(e) != null;
            e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
            DropHint.Visibility = ok ? Visibility.Visible : Visibility.Collapsed;
            e.Handled = true;
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            DropHint.Visibility = Visibility.Collapsed;
            var f = DroppedFile(e);
            if (f == null) return;
            bool video = VideoExt.Contains(Path.GetExtension(f).ToLowerInvariant());
            var item = new HistoryItem
            {
                Kind = video ? ItemKind.Video : ItemKind.Image,
                FilePath = f,
                IsExternal = true,
                IsPermanent = true,
                CreatedUtc = File.GetLastWriteTimeUtc(f),
                SizeBytes = new FileInfo(f).Length
            };
            if (!video)
            {
                try
                {
                    var img = HistoryItem.LoadImage(f);
                    item.Width = img.PixelWidth;
                    item.Height = img.PixelHeight;
                }
                catch (Exception ex)
                {
                    Logger.Warn("UI", "No se pudo abrir la imagen arrastrada", ex);
                    DialogWindow.Ask(this, "No se pudo abrir la imagen", ex.Message, 0, "Aceptar");
                    return;
                }
            }
            ShowItem(item);
        }
    }
}
