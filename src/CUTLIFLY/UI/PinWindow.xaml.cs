using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Cutlifly.Clipboard;

namespace Cutlifly.UI
{
    /// <summary>Captura fijada siempre encima de las demás ventanas (referencia mientras trabajas).</summary>
    public partial class PinWindow : Window
    {
        private readonly BitmapSource _image;
        private double _zoom = 1;
        private double _dpi = 1;

        public PinWindow(BitmapSource image)
        {
            InitializeComponent();
            _image = image;
            Img.Source = image;
            Loaded += (_, __) =>
            {
                _dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
                // tamaño inicial: 1:1 en píxeles físicos, limitado al 60 % del área de trabajo
                var wa = SystemParameters.WorkArea;
                double w = image.PixelWidth / _dpi, h = image.PixelHeight / _dpi;
                double fit = Math.Min(1, Math.Min(wa.Width * 0.6 / w, wa.Height * 0.6 / h));
                _zoom = fit;
                ApplySize();
                var mouse = System.Windows.Forms.Control.MousePosition;
                Left = Math.Max(wa.Left, Math.Min(wa.Right - Width, mouse.X / _dpi - Width / 2));
                Top = Math.Max(wa.Top, Math.Min(wa.Bottom - Height, mouse.Y / _dpi - Height / 2));
            };
            MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) Close(); else try { DragMove(); } catch { } };
            MouseWheel += (_, e) => { _zoom = Math.Clamp(_zoom * (e.Delta > 0 ? 1.1 : 1 / 1.1), 0.1, 4); ApplySize(); };
            KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
            MouseEnter += (_, __) => CloseButton.Visibility = Visibility.Visible;
            MouseLeave += (_, __) => CloseButton.Visibility = Visibility.Collapsed;
            CloseButton.Click += (_, __) => Close();
            CloseItem.Click += (_, __) => Close();
            CopyItem.Click += (_, __) => ClipboardService.CopyImage(_image);
            ZoomInItem.Click += (_, __) => { _zoom = Math.Min(4, _zoom * 1.25); ApplySize(); };
            ZoomOutItem.Click += (_, __) => { _zoom = Math.Max(0.1, _zoom / 1.25); ApplySize(); };
            OpacityItem.Click += (_, __) => Opacity = OpacityItem.IsChecked ? 0.6 : 1;
        }

        private void ApplySize()
        {
            Width = _image.PixelWidth / _dpi * _zoom + 24;
            Height = _image.PixelHeight / _dpi * _zoom + 24;
        }
    }
}
