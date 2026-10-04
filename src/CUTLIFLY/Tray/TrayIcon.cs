using System;
using System.Drawing;
using System.Windows.Forms;

namespace Cutlifly.Tray
{
    /// <summary>Icono de CUTLIFLY en la bandeja del sistema.</summary>
    public sealed class TrayIcon : IDisposable
    {
        private readonly NotifyIcon _icon;

        public TrayIcon(Action open, Action capture, Action record, Action settings, Action exit)
        {
            var menu = new ContextMenuStrip { ShowImageMargin = false, Font = new Font("Segoe UI", 9.5f) };
            menu.Items.Add("Abrir CUTLIFLY", null, (_, __) => open());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Captura rápida", null, (_, __) => capture());
            menu.Items.Add("Grabar pantalla", null, (_, __) => record());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Configuración", null, (_, __) => settings());
            menu.Items.Add("Salir", null, (_, __) => exit());
            menu.Items[0].Font = new Font(menu.Font, FontStyle.Bold);

            _icon = new NotifyIcon
            {
                Icon = LoadIcon(),
                Text = "CUTLIFLY — Screen Capture & Recording",
                ContextMenuStrip = menu,
                Visible = true
            };
            _icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) open(); };
        }

        private static Icon LoadIcon()
        {
            try
            {
                var info = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/cutlifly.ico"));
                if (info != null) return new Icon(info.Stream, SystemInformation.SmallIconSize);
            }
            catch { }
            return SystemIcons.Application;
        }

        public void SetTooltip(string text) => _icon.Text = text.Length > 63 ? text.Substring(0, 63) : text;

        public void Dispose()
        {
            _icon.Visible = false;
            _icon.Dispose();
        }
    }
}
