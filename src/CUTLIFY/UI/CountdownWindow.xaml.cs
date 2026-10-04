using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using Cutlify.Core;

namespace Cutlify.UI
{
    /// <summary>Cuenta regresiva visual antes de capturar. Excluida de la captura y no roba el foco.</summary>
    public partial class CountdownWindow : Window
    {
        public CountdownWindow(string caption)
        {
            InitializeComponent();
            Caption.Text = caption;
            SourceInitialized += (_, __) =>
            {
                var h = new WindowInteropHelper(this).Handle;
                Native.AddExStyle(h, Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW | Native.WS_EX_TRANSPARENT | Native.WS_EX_LAYERED);
                Native.ExcludeFromCapture(h);
            };
        }

        public static async Task Run(int seconds, string caption)
        {
            if (seconds <= 0) return;
            var w = new CountdownWindow(caption);
            w.Show();
            for (int i = seconds; i > 0; i--)
            {
                w.Number.Text = i.ToString();
                w.Number.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.35, TimeSpan.FromMilliseconds(950)));
                await Task.Delay(1000);
            }
            w.Close();
            await Task.Delay(120); // que desaparezca antes de congelar la pantalla
        }
    }
}
