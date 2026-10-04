using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Cutlify.Core;
using Cutlify.UI;

namespace Cutlify
{
    public partial class App : Application
    {
        private const string MutexName = "CUTLIFY_SingleInstance_v1";
        private const string ShowEventName = "CUTLIFY_ShowMainWindow_v1";

        private Mutex _mutex;
        private EventWaitHandle _showEvent;

        protected override void OnStartup(StartupEventArgs e)
        {
            _mutex = new Mutex(true, MutexName, out bool first);
            if (!first)
            {
                // Ya hay una instancia: pedirle que se muestre y salir.
                try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { }
                Shutdown();
                return;
            }

            DispatcherUnhandledException += OnDispatcherException;
            AppDomain.CurrentDomain.UnhandledException += (_, a) => Logger.Error("App", "Excepción no controlada", a.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (_, a) => { Logger.Error("App", "Tarea no observada", a.Exception); a.SetObserved(); };

            base.OnStartup(e);
            System.Windows.Forms.Application.EnableVisualStyles();

            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            var t = new Thread(() =>
            {
                while (_showEvent.WaitOne())
                    Dispatcher.BeginInvoke(new Action(() => AppController.Instance?.ShowMainWindow()));
            }) { IsBackground = true, Name = "CUTLIFY SingleInstance" };
            t.Start();

            bool tray = e.Args.Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase));
            bool updated = e.Args.Any(a => a.Equals("--updated", StringComparison.OrdinalIgnoreCase));
            AppController.Initialize(this, startInTray: tray, justUpdated: updated);
        }

        private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Logger.Error("App", "Error en la interfaz", e.Exception);
            e.Handled = true;
            try
            {
                DialogWindow.Ask(null, "Algo salió mal", "CUTLIFY encontró un error y lo registró en los logs.\n\n" + e.Exception.Message, 0, "Aceptar");
            }
            catch { }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            AppController.Instance?.Dispose();
            try { _mutex?.ReleaseMutex(); } catch { }
            base.OnExit(e);
        }
    }
}
