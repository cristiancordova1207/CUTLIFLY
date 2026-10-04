using System;
using System.Drawing;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Cutlifly.Capture;
using Cutlifly.Editor;
using Cutlifly.Notifications;
using Cutlifly.Recording;
using Cutlifly.Settings;
using Cutlifly.Storage;
using Cutlifly.UI;
using Xunit;

namespace Cutlifly.Tests
{
    /// <summary>
    /// Crea y muestra todas las ventanas reales con los recursos de la app para detectar
    /// errores de XAML/recursos que solo aparecen en tiempo de ejecución.
    /// </summary>
    public class UiSmokeTests
    {
        private static void RunSta(Action action)
        {
            ExceptionDispatchInfo error = null;
            var t = new Thread(() =>
            {
                try { action(); }
                catch (Exception ex) { error = ExceptionDispatchInfo.Capture(ex); }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            error?.Throw();
        }

        private static BitmapSource SampleImage()
        {
            var bmp = BitmapSource.Create(64, 48, 96, 96, PixelFormats.Bgr32, null, new byte[64 * 48 * 4], 64 * 4);
            bmp.Freeze();
            return bmp;
        }

        private static void Pump(Window w)
        {
            w.Show();
            w.UpdateLayout();
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            w.Close();
        }

        [Fact]
        public void AllWindowsLoad()
        {
            RunSta(() =>
            {
                var app = new App();
                app.InitializeComponent();
                var controller = AppController.CreateDetached(app);
                var img = SampleImage();
                var item = new HistoryItem { Kind = ItemKind.Image, MemoryImage = img, Width = 64, Height = 48 };
                controller.History.Items.Add(item);

                foreach (var theme in new[] { "Light", "Dark" })
                {
                    ThemeManager.Apply(theme);

                    var main = new MainWindow(controller);
                    main.Show();
                    main.ShowItem(item);
                    main.UpdateLayout();
                    main.ShowItem(new HistoryItem { Kind = ItemKind.Video, FilePath = null, IsExternal = true, DurationMs = 38000 });
                    main.ShowItem(null);
                    main.Close();

                    foreach (var page in new[] { "general", "capture", "record", "clipboard", "storage", "hotkeys", "appearance", "advanced", "about" })
                    {
                        var sw = new SettingsWindow(controller, page);
                        Pump(sw);
                    }
                    Pump(new HistoryWindow(controller));
                    Pump(new PinWindow(img));
                    var dlg = new DialogWindow("Título", "Mensaje", 1, "Cancelar", "Aceptar");
                    dlg.SetDetails("Notas");
                    dlg.SetProgress(50);
                    Pump(dlg);
                    var bar = new RecordingBar(new Rectangle(100, 100, 400, 300), () => TimeSpan.FromSeconds(18));
                    bar.SetPaused(true);
                    Pump(bar);
                    ToastWindow.ShowToast("Captura copiada al portapapeles", "Puedes pegarla con Ctrl + V", img, "00:38", null,
                        new (string, Action)[] { ("Abrir", () => { }) });
                }

                // Editor: carga, render a tamaño original, deshacer/rehacer.
                var host = new Window { Width = 800, Height = 600 };
                var editor = new EditorView();
                host.Content = editor;
                host.Show();
                editor.Load(img);
                host.UpdateLayout();
                Assert.False(editor.HasEdits);
                var rendered = editor.Render();
                Assert.Equal(64, rendered.PixelWidth);
                Assert.Equal(48, rendered.PixelHeight);
                editor.Undo();
                editor.Redo();
                host.Close();
            });
        }

        [Fact]
        public void OverlayConstructsOverVirtualScreen()
        {
            using var frozen = new Bitmap(ScreenCapture.VirtualScreen.Width, ScreenCapture.VirtualScreen.Height);
            using var overlay = new CaptureOverlay(frozen, CaptureMode.Rectangle, recording: false);
            Assert.Equal(ScreenCapture.VirtualScreen, overlay.Bounds);
        }
    }
}
