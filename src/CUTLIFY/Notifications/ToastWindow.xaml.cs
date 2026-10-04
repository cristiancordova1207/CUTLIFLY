using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Cutlify.Core;

namespace Cutlify.Notifications
{
    /// <summary>Notificación propia de CUTLIFY (esquina inferior derecha, no roba el foco).</summary>
    public partial class ToastWindow : Window
    {
        private static ToastWindow _current;
        private readonly DispatcherTimer _timer;
        private readonly Action _onClick;
        private bool _closing;

        private ToastWindow(string title, string message, ImageSource thumb, string badge, Action onClick, IList<(string label, Action action)> actions)
        {
            InitializeComponent();
            TitleText.Text = title;
            MessageText.Text = message;
            MessageText.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
            Thumb.Source = thumb;
            ThumbBorder.Visibility = thumb == null ? Visibility.Collapsed : Visibility.Visible;
            if (!string.IsNullOrEmpty(badge)) { Badge.Text = badge; Badge.Visibility = Visibility.Visible; }
            _onClick = onClick;

            Actions.Visibility = actions == null || actions.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            if (actions != null)
                foreach (var (label, action) in actions)
                {
                    var b = new Button { Content = label, Style = (Style)FindResource("SoftButton"), Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 8, 0) };
                    b.Click += (_, e) => { e.Handled = true; FadeOut(); action?.Invoke(); };
                    Actions.Children.Add(b);
                }

            CloseButton.Click += (_, e) => { e.Handled = true; FadeOut(); };
            Root.MouseLeftButtonUp += (_, e) =>
            {
                if (e.OriginalSource is DependencyObject d && IsInsideButton(d)) return;
                FadeOut();
                _onClick?.Invoke();
            };

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(actions != null && actions.Count > 0 ? 7 : 4.5) };
            _timer.Tick += (_, __) => FadeOut();
            MouseEnter += (_, __) => _timer.Stop();
            MouseLeave += (_, __) => _timer.Start();

            SourceInitialized += (_, __) =>
            {
                var h = new WindowInteropHelper(this).Handle;
                Native.AddExStyle(h, Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW);
                Native.ExcludeFromCapture(h);
            };
            Loaded += (_, __) => { Position(); Animate(); _timer.Start(); };
            SizeChanged += (_, __) => Position();
        }

        private static bool IsInsideButton(DependencyObject d)
        {
            while (d != null)
            {
                if (d is Button) return true;
                d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            }
            return false;
        }

        public static void ShowToast(string title, string message, ImageSource thumb = null, string badge = null,
            Action onClick = null, IList<(string, Action)> actions = null)
        {
            try
            {
                _current?.CloseNow();
                _current = new ToastWindow(title, message, thumb, badge, onClick, actions);
                _current.Show();
            }
            catch (Exception ex)
            {
                Logger.Warn("Notifications", "No se pudo mostrar la notificación", ex);
            }
        }

        private void Position()
        {
            var wa = SystemParameters.WorkArea;
            Left = wa.Right - ActualWidth - 4;
            Top = wa.Bottom - ActualHeight - 4;
        }

        private void Animate()
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            Slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(40, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
        }

        private void FadeOut()
        {
            if (_closing) return;
            _closing = true;
            _timer.Stop();
            var a = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(180));
            a.Completed += (_, __) => CloseNow();
            BeginAnimation(OpacityProperty, a);
            Slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, 30, TimeSpan.FromMilliseconds(180)));
        }

        private void CloseNow()
        {
            _timer.Stop();
            if (_current == this) _current = null;
            try { Close(); } catch { }
        }
    }
}
