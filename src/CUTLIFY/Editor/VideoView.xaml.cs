using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Cutlify.Storage;

namespace Cutlify.Editor
{
    /// <summary>Vista previa de grabaciones: reproducir, duración, tamaño y acciones.</summary>
    public partial class VideoView : UserControl
    {
        private readonly DispatcherTimer _timer;
        private bool _playing;
        private bool _seeking;

        public HistoryItem Item { get; private set; }

        public event Action CopyRequested, SaveRequested, SaveAsRequested, OpenRequested, OpenLocationRequested, DeleteRequested;

        public VideoView()
        {
            InitializeComponent();
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _timer.Tick += (_, __) =>
            {
                if (_seeking) return;
                Seek.Value = Player.Position.TotalSeconds;
                PositionText.Text = HistoryItem.FormatDuration(Player.Position);
            };
            PlayButton.Click += (_, __) => TogglePlay();
            BigPlay.Click += (_, __) => TogglePlay();
            Player.MediaOpened += (_, __) =>
            {
                if (Player.NaturalDuration.HasTimeSpan)
                {
                    var d = Player.NaturalDuration.TimeSpan;
                    Seek.Maximum = d.TotalSeconds;
                    DurationText.Text = HistoryItem.FormatDuration(d);
                }
            };
            Player.MediaEnded += (_, __) => { Pause(); Player.Position = TimeSpan.Zero; };
            Player.MediaFailed += (_, e) => InfoText.Text = "No se pudo reproducir: " + e.ErrorException?.Message;
            Seek.PreviewMouseDown += (_, __) => _seeking = true;
            Seek.PreviewMouseUp += (_, __) => { Player.Position = TimeSpan.FromSeconds(Seek.Value); _seeking = false; };
            Seek.ValueChanged += (_, __) => { if (_seeking) PositionText.Text = HistoryItem.FormatDuration(TimeSpan.FromSeconds(Seek.Value)); };

            CopyButton.Click += (_, __) => CopyRequested?.Invoke();
            SaveButton.Click += (_, __) => SaveRequested?.Invoke();
            SaveAsButton.Click += (_, __) => SaveAsRequested?.Invoke();
            OpenButton.Click += (_, __) => OpenRequested?.Invoke();
            FolderButton.Click += (_, __) => OpenLocationRequested?.Invoke();
            DeleteButton.Click += (_, __) => DeleteRequested?.Invoke();
            Unloaded += (_, __) => Release();
        }

        public void Load(HistoryItem item)
        {
            Release();
            Item = item;
            NameText.Text = item.Name;
            long size = item.SizeBytes;
            try { if (item.HasFile) size = new FileInfo(item.FilePath).Length; } catch { }
            var parts = new[] { HistoryItem.FormatDuration(TimeSpan.FromMilliseconds(item.DurationMs)), HistoryItem.FormatBytes(size), item.DimensionsLabel,
                                item.FpsLabel, item.Codec, item.BitrateLabel, item.Audio, item.StatusLabel };
            InfoText.Text = string.Join("  ·  ", Array.FindAll(parts, p => !string.IsNullOrWhiteSpace(p)));
            InfoText.ToolTip = AppController.Describe(item);
            DurationText.Text = HistoryItem.FormatDuration(TimeSpan.FromMilliseconds(item.DurationMs));
            Seek.Maximum = Math.Max(0.1, item.DurationMs / 1000.0);
            Seek.Value = 0;
            DeleteButton.IsEnabled = !item.IsExternal;
            if (item.HasFile)
            {
                Player.Source = new Uri(item.FilePath);
                Player.Play();
                Player.Pause(); // muestra el primer fotograma
                Player.Position = TimeSpan.Zero;
            }
        }

        /// <summary>Libera el archivo (MediaElement lo bloquea mientras está abierto).</summary>
        public void Release()
        {
            Pause();
            Player.Close();
            Player.Source = null;
        }

        private void TogglePlay()
        {
            if (_playing) Pause();
            else
            {
                Player.Play();
                _playing = true;
                _timer.Start();
                PlayIcon.Text = "";
                BigPlay.Visibility = Visibility.Collapsed;
            }
        }

        private void Pause()
        {
            if (Player.Source != null) Player.Pause();
            _playing = false;
            _timer.Stop();
            PlayIcon.Text = "";
            BigPlay.Visibility = Visibility.Visible;
        }
    }
}
