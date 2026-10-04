using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Cutlifly.Core;

namespace Cutlifly.Recording
{
    /// <summary>Marco discontinuo alrededor de la región que se graba (fuera de ella, sin capturarse, sin recibir clics).</summary>
    public sealed class RegionFrame : IDisposable
    {
        private readonly List<Form> _edges = new List<Form>();

        public RegionFrame(Rectangle region)
        {
            const int t = 2, gap = 1;
            var r = Rectangle.Inflate(region, t + gap, t + gap);
            var edges = new[]
            {
                new Rectangle(r.Left, r.Top, r.Width, t),
                new Rectangle(r.Left, r.Bottom - t, r.Width, t),
                new Rectangle(r.Left, r.Top, t, r.Height),
                new Rectangle(r.Right - t, r.Top, t, r.Height)
            };
            foreach (var e in edges) _edges.Add(new EdgeForm(e));
        }

        public void Show()
        {
            foreach (var f in _edges) f.Show();
        }

        public void Dispose()
        {
            foreach (var f in _edges) { try { f.Close(); f.Dispose(); } catch { } }
            _edges.Clear();
        }

        private sealed class EdgeForm : Form
        {
            private readonly Rectangle _bounds;

            public EdgeForm(Rectangle bounds)
            {
                _bounds = bounds;
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                AutoScaleMode = AutoScaleMode.None;
                BackColor = Color.FromArgb(124, 92, 255);
                TopMost = true;
                Bounds = bounds;
                Opacity = 0.95;
            }

            protected override bool ShowWithoutActivation => true;

            protected override CreateParams CreateParams
            {
                get
                {
                    var cp = base.CreateParams;
                    cp.ExStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOPMOST;
                    return cp;
                }
            }

            protected override void OnHandleCreated(EventArgs e)
            {
                base.OnHandleCreated(e);
                Native.ExcludeFromCapture(Handle);
            }

            protected override void OnShown(EventArgs e)
            {
                base.OnShown(e);
                Native.SetWindowPos(Handle, Native.HWND_TOPMOST, _bounds.X, _bounds.Y, _bounds.Width, _bounds.Height, Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == 0x02E0) return; // WM_DPICHANGED
                base.WndProc(ref m);
            }
        }
    }
}
