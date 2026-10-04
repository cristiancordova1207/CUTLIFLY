using System;
using System.Drawing;
using System.Windows.Forms;

namespace Cutlify.Tray
{
    /// <summary>Icono de CUTLIFY en la bandeja del sistema.</summary>
    public sealed class TrayIcon : IDisposable
    {
        private readonly NotifyIcon _icon;
        private readonly ToolStripMenuItem _pauseItem;

        public TrayIcon(Action capture, Action record, Action open, Action settings, Action togglePause, Action exit)
        {
            var menu = new ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = true, Font = new Font("Segoe UI", 9.5f) };
            var title = new ToolStripMenuItem("CUTLIFY", null, (_, __) => open()) { Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
            menu.Items.Add(title);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Nueva captura", null, (_, __) => capture());
            menu.Items.Add("Nueva grabación", null, (_, __) => record());
            menu.Items.Add("Abrir CUTLIFY", null, (_, __) => open());
            menu.Items.Add("Configuración", null, (_, __) => settings());
            menu.Items.Add(new ToolStripSeparator());
            _pauseItem = new ToolStripMenuItem("Pausar temporalmente atajos", null, (_, __) => togglePause()) { CheckOnClick = false };
            menu.Items.Add(_pauseItem);
            menu.Items.Add("Salir", null, (_, __) => exit());

            _icon = new NotifyIcon
            {
                Icon = LoadIcon(),
                Text = "CUTLIFY — Screen Capture & Recording",
                ContextMenuStrip = menu,
                Visible = true
            };
            _icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) open(); };
        }

        private static Icon LoadIcon()
        {
            try
            {
                var info = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/CUTLIFY;component/Assets/cutlify.ico"));
                if (info != null) return new Icon(info.Stream, SystemInformation.SmallIconSize);
            }
            catch { }
            return SystemIcons.Application;
        }

        public void SetPaused(bool paused)
        {
            _pauseItem.Checked = paused;
            SetTooltip(paused ? "CUTLIFY — En pausa (atajos desactivados)" : "CUTLIFY — Screen Capture & Recording");
        }

        public void SetTooltip(string text) => _icon.Text = text.Length > 63 ? text.Substring(0, 63) : text;

        public void Dispose()
        {
            _icon.Visible = false;
            _icon.Dispose();
        }
    }
}
