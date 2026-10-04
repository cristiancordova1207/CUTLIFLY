using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Cutlify.Core;
using Cutlify.Storage;

namespace Cutlify.Recording
{
    /// <summary>Barra flotante de grabación. Excluida de la propia grabación.</summary>
    public partial class RecordingBar : Window
    {
        private readonly Func<TimeSpan> _elapsed;
        private readonly Func<RecordingStats?> _stats;
        private readonly System.Drawing.Rectangle _region;
        private readonly DispatcherTimer _timer;
        private bool _paused;

        public event Action PauseToggled;
        public event Action StopRequested;

        public RecordingBar(System.Drawing.Rectangle region, Func<TimeSpan> elapsed, Func<RecordingStats?> stats = null)
        {
            _stats = stats;
            Resources["BarColor"] = Cutlify.UI.ThemeManager.IsDark ? Color.FromRgb(0x21, 0x1E, 0x2C) : Color.FromRgb(0xFF, 0xFF, 0xFF);
            InitializeComponent();
            _region = region;
            _elapsed = elapsed;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _timer.Tick += (_, __) =>
            {
                TimeText.Text = _elapsed().ToString(@"hh\:mm\:ss");
                var st = _stats?.Invoke();
                if (st.HasValue)
                {
                    StatsText.Visibility = Visibility.Visible;
                    StatsText.Text = $"{st.Value.RealFps:0}/{st.Value.TargetFps} FPS · {st.Value.Dropped} perdidos";
                }
            };
            PauseButton.Click += (_, __) => PauseToggled?.Invoke();
            StopButton.Click += (_, __) => StopRequested?.Invoke();
            Root.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) try { DragMove(); } catch { } };

            SourceInitialized += (_, __) =>
            {
                var h = new WindowInteropHelper(this).Handle;
                Native.AddExStyle(h, Native.WS_EX_TOOLWINDOW);
                Native.ExcludeFromCapture(h);
            };
            Loaded += (_, __) => { PlaceNearRegion(); _timer.Start(); Pulse(true); };
            Closed += (_, __) => _timer.Stop();
        }

        public void SetPaused(bool paused)
        {
            _paused = paused;
            StateText.Text = paused ? "PAUSA" : "REC";
            PauseIcon.Text = paused ? "" : "";
            PauseLabel.Text = paused ? "Reanudar" : "Pausar";
            Pulse(!paused);
        }

        private void Pulse(bool on)
        {
            if (!on) { Dot.BeginAnimation(OpacityProperty, null); Dot.Opacity = 0.4; return; }
            Dot.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.25, TimeSpan.FromSeconds(0.8)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        }

        /// <summary>Coloca la barra fuera de la región (arriba o abajo) en píxeles físicos.</summary>
        private void PlaceNearRegion()
        {
            var h = new WindowInteropHelper(this).Handle;
            Native.GetWindowRect(h, out var wr);
            int w = wr.Width, ht = wr.Height;
            var screen = System.Windows.Forms.Screen.FromRectangle(_region).WorkingArea;
            int x = _region.X + (_region.Width - w) / 2;
            int y = _region.Y - ht - 4;
            if (y < screen.Top) y = _region.Bottom + 4;
            if (y + ht > screen.Bottom) y = screen.Bottom - ht - 8; // pantalla completa: queda dentro, pero excluida de la captura
            x = Math.Max(screen.Left + 4, Math.Min(x, screen.Right - w - 4));
            Native.SetWindowPos(h, Native.HWND_TOPMOST, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
        }
    }
}
