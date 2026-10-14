using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Cutlify.Core;
using Cutlify.Recording;
using Cutlify.Settings;

namespace Cutlify.UI
{
    /// <summary>Fila de pista enlazada a su configuración persistente.</summary>
    public sealed class TrackRow : INotifyPropertyChanged
    {
        public AudioTrackConfig Config { get; }
        public TrackRow(AudioTrackConfig c) => Config = c;

        public string Name { get => Config.Name; set { Config.Name = value; SettingsService.Save(); } }
        public double Gain { get => Config.Gain; set { Config.Gain = (float)value; Raise(nameof(Gain)); } }
        public bool Muted { get => Config.Muted; set { Config.Muted = value; Raise(nameof(Muted)); } }

        public string Glyph => Config.Kind switch { AudioTrackKind.Microphone => "", AudioTrackKind.System => "", _ => "" };

        public string Description => Config.Kind switch
        {
            AudioTrackKind.Microphone => "Micrófono" + (Config.DeviceId == null ? " · predeterminado" : ""),
            AudioTrackKind.System => "Escritorio (todo el sonido del PC)" + (Config.DeviceId == null ? " · salida predeterminada" : ""),
            _ => $"Solo el audio de {Config.ProcessName}"
        };

        private double _level;
        public double Level { get => _level; set { if (Math.Abs(_level - value) < 0.005) return; _level = value; Raise(nameof(Level)); } }

        private bool _editable = true;
        public bool Editable { get => _editable; set { _editable = value; Raise(nameof(Editable)); } }

        public event PropertyChangedEventHandler PropertyChanged;
        private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public partial class AudioRecorderWindow : Window
    {
        private readonly AppController _app;
        private readonly ObservableCollection<TrackRow> _rows = new ObservableCollection<TrackRow>();
        private readonly DispatcherTimer _timer;
        private bool _loading;
        private static AppSettings S => SettingsService.Current;

        public AudioRecorderWindow(AppController app)
        {
            _app = app;
            InitializeComponent();
            foreach (var c in S.AudioTracks) _rows.Add(new TrackRow(c));
            TrackList.ItemsSource = _rows;
            _rows.CollectionChanged += (_, __) => EmptyTracks.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            EmptyTracks.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            HotkeyHint.Text = string.IsNullOrEmpty(S.HotkeyAudio) ? "" : $"·  {S.HotkeyAudio.Replace("+", " + ")} para iniciar / detener";

            _loading = true;
            MainWindow.SelectByTag(FormatCombo, S.AudioMixFormat);
            SeparateSwitch.IsChecked = S.AudioSeparateTracks;
            _loading = false;
            FormatCombo.SelectionChanged += (_, __) => { if (!_loading && FormatCombo.SelectedItem is ComboBoxItem it) { S.AudioMixFormat = (string)it.Tag; SettingsService.Save(); } };
            SeparateSwitch.Click += (_, __) => { S.AudioSeparateTracks = SeparateSwitch.IsChecked == true; SettingsService.Save(); };

            SourceInitialized += (_, __) => Native.RoundCorners(new WindowInteropHelper(this).Handle);
            MinButton.Click += (_, __) => WindowState = WindowState.Minimized;
            CloseButton.Click += (_, __) => Close();
            Closed += (_, __) => { _timer.Stop(); _app.AudioStateChanged -= UpdateState; SettingsService.Save(); };

            AddMic.Click += (_, __) => ShowDeviceMenu(AddMic, AudioTrackKind.Microphone);
            AddSystem.Click += (_, __) => ShowDeviceMenu(AddSystem, AudioTrackKind.System);
            AddApp.Click += (_, __) => ShowAppMenu();
            RecordButton.Click += async (_, __) =>
            {
                if (_app.IsAudioRecording) _app.StopAudioRecording();
                else { RecordButton.IsEnabled = false; await _app.StartAudioRecording(); RecordButton.IsEnabled = true; }
            };
            PauseButton.Click += (_, __) => _app.ToggleAudioPause();

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
            _timer.Tick += (_, __) => Tick();
            _timer.Start();
            _app.AudioStateChanged += UpdateState;
            UpdateState();
        }

        private void UpdateState()
        {
            bool rec = _app.IsAudioRecording;
            var r = _app.AudioRecorder;
            RecordButton.Tag = rec ? "recording" : null;
            RecordButton.ToolTip = rec ? "Detener" : "Grabar";
            System.Windows.Automation.AutomationProperties.SetName(RecordButton, rec ? "Detener" : "Grabar");
            PauseButton.IsEnabled = rec;
            PauseIcon.Text = r?.IsPaused == true ? "" : "";
            PauseLabel.Text = r?.IsPaused == true ? "Reanudar" : "Pausar";
            StateText.Text = !rec ? "Listo para grabar" : r.IsPaused ? "En pausa" : $"Grabando {r.Tracks.Count} pista(s)";
            AddPanel.IsEnabled = !rec;
            FormatCombo.IsEnabled = SeparateSwitch.IsEnabled = !rec;
            foreach (var row in _rows) { row.Editable = !rec; if (!rec) row.Level = 0; }
            if (!rec) TimeText.Text = "00:00:00";
        }

        private void Tick()
        {
            var r = _app.AudioRecorder;
            if (r == null) return;
            TimeText.Text = r.Elapsed.ToString(@"hh\:mm\:ss");
            for (int i = 0; i < r.Tracks.Count; i++)
            {
                var row = _rows.FirstOrDefault(x => x.Config == r.Tracks[i].Config);
                if (row != null) row.Level = Math.Max(r.Levels[i], row.Level * 0.8); // caída suave del medidor
            }
        }

        private void AddTrack(AudioTrackConfig c)
        {
            S.AudioTracks.Add(c);
            _rows.Add(new TrackRow(c));
            SettingsService.Save();
        }

        private void RemoveTrack_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.DataContext is TrackRow row)) return;
            S.AudioTracks.Remove(row.Config);
            _rows.Remove(row);
            SettingsService.Save();
        }

        private void ShowDeviceMenu(Button anchor, AudioTrackKind kind)
        {
            var menu = new ContextMenu { PlacementTarget = anchor, Placement = System.Windows.Controls.Primitives.PlacementMode.Top };
            string label = kind == AudioTrackKind.Microphone ? "Micrófono" : "Escritorio";
            var def = new MenuItem { Header = kind == AudioTrackKind.Microphone ? "Micrófono predeterminado" : "Salida predeterminada (todo el sonido)" };
            def.Click += (_, __) => AddTrack(new AudioTrackConfig { Kind = kind, Name = UniqueName(label) });
            menu.Items.Add(def);
            var devices = AudioCapture.ListDevices(kind == AudioTrackKind.Microphone);
            if (devices.Count > 0) menu.Items.Add(new Separator());
            foreach (var d in devices)
            {
                var mi = new MenuItem { Header = d.Name };
                mi.Click += (_, __) => AddTrack(new AudioTrackConfig { Kind = kind, DeviceId = d.Id, Name = UniqueName(Short(d.Name)) });
                menu.Items.Add(mi);
            }
            menu.IsOpen = true;
        }

        private void ShowAppMenu()
        {
            if (!ProcessLoopbackCapture.IsSupported)
            {
                DialogWindow.Ask(this, "Requiere Windows 11", "Grabar el audio de una sola aplicación (por ejemplo, solo Spotify) necesita Windows 11. Puedes grabar el escritorio completo con «+ Escritorio».", 0, "Aceptar");
                return;
            }
            var menu = new ContextMenu { PlacementTarget = AddApp, Placement = System.Windows.Controls.Primitives.PlacementMode.Top };
            var apps = AudioApps.List();
            if (apps.Count == 0) menu.Items.Add(new MenuItem { Header = "No hay aplicaciones abiertas", IsEnabled = false });
            foreach (var a in apps.Take(30))
            {
                var mi = new MenuItem { Header = a.ToString(), Icon = a.PlayingAudio ? "" : null };
                mi.Click += (_, __) => AddTrack(new AudioTrackConfig { Kind = AudioTrackKind.Application, ProcessName = a.ProcessName, Name = UniqueName(a.Title) });
                menu.Items.Add(mi);
            }
            menu.IsOpen = true;
        }

        private static string Short(string device)
        {
            int i = device.IndexOf('(');
            return i > 2 ? device.Substring(0, i).Trim() : device;
        }

        private string UniqueName(string name)
        {
            string n = name;
            for (int i = 2; _rows.Any(r => string.Equals(r.Name, n, StringComparison.OrdinalIgnoreCase)); i++) n = $"{name} {i}";
            return n;
        }
    }
}
