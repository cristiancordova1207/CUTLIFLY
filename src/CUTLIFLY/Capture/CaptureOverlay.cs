using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using Cutlifly.Core;
using Cutlifly.Settings;

namespace Cutlifly.Capture
{
    public sealed class OverlayResult
    {
        public CaptureMode Mode;
        public Rectangle ScreenRect;   // coordenadas físicas de pantalla
        public IntPtr Window;
    }

    /// <summary>
    /// Capa de selección a pantalla completa (todas las pantallas). Muestra la imagen congelada,
    /// oscurecida, y deja seleccionar región / ventana / monitor. Todo en píxeles físicos.
    /// </summary>
    public sealed class CaptureOverlay : Form
    {
        private const int WM_DPICHANGED = 0x02E0;

        private readonly Bitmap _frozen;
        private readonly Bitmap _dim;
        private readonly Rectangle _vs;
        private readonly bool _recording;
        private readonly List<WindowInfo> _windows;
        private readonly Rectangle[] _screens;
        private CaptureMode _mode;

        private Point? _dragStart;
        private Rectangle _sel;        // selección en coordenadas de cliente
        private Rectangle _hover;
        private IntPtr _hoverHwnd;
        private bool _done;

        private readonly Rectangle _toolbar;
        private readonly Rectangle[] _buttons; // 0 rect, 1 ventana, 2 pantalla, 3 cerrar
        private readonly float _scale;
        private readonly Font _iconFont;
        private readonly Font _labelFont;
        private readonly Font _smallFont;

        private static readonly Color Accent = Color.FromArgb(124, 92, 255);
        private static readonly Color AccentSoft = Color.FromArgb(237, 232, 255);

        public event Action<OverlayResult> Completed;

        public CaptureOverlay(Bitmap frozen, CaptureMode mode, bool recording)
        {
            _frozen = frozen;
            _mode = mode;
            _recording = recording;
            _vs = ScreenCapture.VirtualScreen;
            _windows = WindowFinder.GetVisibleWindows();
            _screens = Screen.AllScreens.Select(s => ToClient(s.Bounds)).ToArray();

            _dim = new Bitmap(frozen.Width, frozen.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(_dim))
            {
                g.DrawImageUnscaled(frozen, 0, 0);
                using var b = new SolidBrush(Color.FromArgb(120, 10, 8, 24));
                g.FillRectangle(b, 0, 0, _dim.Width, _dim.Height);
            }

            var primary = ToClient(Screen.PrimaryScreen.Bounds);
            _scale = (float)Native.DpiScaleAt(Screen.PrimaryScreen.Bounds.X + 1, Screen.PrimaryScreen.Bounds.Y + 1);
            int bw = S(46), bh = S(40), pad = S(6), sep = S(10);
            int width = pad * 2 + bw * 4 + sep;
            _toolbar = new Rectangle(primary.X + (primary.Width - width) / 2, primary.Y + S(14), width, bh + pad * 2);
            _buttons = new Rectangle[4];
            for (int i = 0; i < 3; i++) _buttons[i] = new Rectangle(_toolbar.X + pad + i * bw, _toolbar.Y + pad, bw, bh);
            _buttons[3] = new Rectangle(_toolbar.X + pad + 3 * bw + sep, _toolbar.Y + pad, bw, bh);

            _iconFont = CreateIconFont(S(16));
            _labelFont = new Font("Segoe UI Semibold", S(13), GraphicsUnit.Pixel);
            _smallFont = new Font("Segoe UI", S(11), GraphicsUnit.Pixel);

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            Bounds = _vs;
            Cursor = Cursors.Cross;
            KeyPreview = true;
            Text = "CUTLIFLY";
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);
        }

        private int S(int v) => (int)Math.Round(v * _scale);

        private static Font CreateIconFont(float px)
        {
            foreach (var name in new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" })
            {
                var f = new Font(name, px, GraphicsUnit.Pixel);
                if (f.Name == name) return f;
                f.Dispose();
            }
            return new Font("Segoe UI Symbol", px, GraphicsUnit.Pixel);
        }

        private Rectangle ToClient(Rectangle screen) => new Rectangle(screen.X - _vs.X, screen.Y - _vs.Y, screen.Width, screen.Height);

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, _vs.X, _vs.Y, _vs.Width, _vs.Height, Native.SWP_SHOWWINDOW);
            Native.ForceForeground(Handle);
            Activate();
            Focus();
            UpdateHover(PointToClient(Cursor.Position));
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_DPICHANGED) return; // mantener el tamaño exacto sobre monitores con distinto DPI
            base.WndProc(ref m);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { Finish(null); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) Finish(null);
            base.OnKeyDown(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right) { Finish(null); return; }
            if (e.Button != MouseButtons.Left) return;

            for (int i = 0; i < _buttons.Length; i++)
            {
                if (!_buttons[i].Contains(e.Location)) continue;
                if (i == 3) { Finish(null); return; }
                _mode = (CaptureMode)i;
                _sel = Rectangle.Empty;
                UpdateHover(e.Location);
                Invalidate();
                return;
            }

            switch (_mode)
            {
                case CaptureMode.Rectangle:
                    _dragStart = e.Location;
                    _sel = new Rectangle(e.Location, Size.Empty);
                    Capture = true;
                    break;
                case CaptureMode.Window:
                    if (!_hover.IsEmpty) Finish(_hover, _hoverHwnd);
                    break;
                case CaptureMode.FullScreen:
                    if (!_hover.IsEmpty) Finish(_hover, IntPtr.Zero);
                    break;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_mode == CaptureMode.Rectangle && _dragStart.HasValue)
            {
                var old = _sel;
                var p = _dragStart.Value;
                _sel = Rectangle.FromLTRB(Math.Min(p.X, e.X), Math.Min(p.Y, e.Y), Math.Max(p.X, e.X) + 1, Math.Max(p.Y, e.Y) + 1);
                InvalidateAround(old);
                InvalidateAround(_sel);
            }
            else UpdateHover(e.Location);
            Cursor = _buttons.Any(b => b.Contains(e.Location)) ? Cursors.Hand : Cursors.Cross;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || !_dragStart.HasValue) return;
            _dragStart = null;
            Capture = false;
            if (_sel.Width >= 3 && _sel.Height >= 3) Finish(_sel, IntPtr.Zero);
            else { var old = _sel; _sel = Rectangle.Empty; InvalidateAround(old); }
        }

        private void UpdateHover(Point p)
        {
            Rectangle target = Rectangle.Empty;
            IntPtr hwnd = IntPtr.Zero;
            var screenPt = new Point(p.X + _vs.X, p.Y + _vs.Y);
            if (_mode == CaptureMode.Window)
            {
                foreach (var w in _windows)
                    if (w.Bounds.Contains(screenPt)) { target = ToClient(w.Bounds); hwnd = w.Handle; break; }
            }
            else if (_mode == CaptureMode.FullScreen)
            {
                target = _screens.FirstOrDefault(s => s.Contains(p));
            }
            if (target == _hover) return;
            var old = _hover;
            _hover = target;
            _hoverHwnd = hwnd;
            InvalidateAround(old);
            InvalidateAround(_hover);
        }

        private void InvalidateAround(Rectangle r)
        {
            if (r.IsEmpty) return;
            var m = S(48);
            r.Inflate(m, m);
            Invalidate(r);
        }

        private void Finish(Rectangle? clientRect, IntPtr hwnd = default)
        {
            if (_done) return;
            _done = true;
            OverlayResult result = null;
            if (clientRect.HasValue)
            {
                var r = clientRect.Value;
                r.Intersect(new Rectangle(0, 0, _vs.Width, _vs.Height));
                if (r.Width > 0 && r.Height > 0)
                    result = new OverlayResult { Mode = _mode, Window = hwnd, ScreenRect = new Rectangle(r.X + _vs.X, r.Y + _vs.Y, r.Width, r.Height) };
            }
            Hide();
            try { Completed?.Invoke(result); }
            finally { Close(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var clip = e.ClipRectangle;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.CompositingMode = CompositingMode.SourceCopy;
            g.DrawImage(_dim, clip, clip, GraphicsUnit.Pixel);

            var active = _mode == CaptureMode.Rectangle ? _sel : _hover;
            if (!active.IsEmpty)
            {
                var vis = Rectangle.Intersect(active, clip);
                if (!vis.IsEmpty) g.DrawImage(_frozen, vis, vis, GraphicsUnit.Pixel);
            }

            g.CompositingMode = CompositingMode.SourceOver;
            g.PixelOffsetMode = PixelOffsetMode.None;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            if (!active.IsEmpty) DrawSelection(g, active);
            if (clip.IntersectsWith(Rectangle.Inflate(_toolbar, S(20), S(20)))) DrawToolbar(g);
        }

        private void DrawSelection(Graphics g, Rectangle r)
        {
            using (var pen = new Pen(Color.White, Math.Max(1f, 1.5f * _scale)))
                g.DrawRectangle(pen, r.X, r.Y, r.Width - 1, r.Height - 1);

            if (_mode == CaptureMode.Rectangle)
            {
                float hr = 4.5f * _scale;
                using var fill = new SolidBrush(Color.White);
                using var edge = new Pen(Accent, Math.Max(1f, 1.5f * _scale));
                int cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
                foreach (var p in new[] { new Point(r.Left, r.Top), new Point(cx, r.Top), new Point(r.Right - 1, r.Top),
                                          new Point(r.Left, cy), new Point(r.Right - 1, cy),
                                          new Point(r.Left, r.Bottom - 1), new Point(cx, r.Bottom - 1), new Point(r.Right - 1, r.Bottom - 1) })
                {
                    g.FillEllipse(fill, p.X - hr, p.Y - hr, hr * 2, hr * 2);
                    g.DrawEllipse(edge, p.X - hr, p.Y - hr, hr * 2, hr * 2);
                }
            }

            var text = $"{r.Width} × {r.Height}";
            var size = g.MeasureString(text, _labelFont);
            float w = size.Width + S(16), h = size.Height + S(6);
            float x = r.X + (r.Width - w) / 2f;
            float y = r.Bottom + S(10);
            if (y + h > Height - S(4)) y = r.Bottom - h - S(10);
            x = Math.Max(S(4), Math.Min(x, Width - w - S(4)));
            using (var path = RoundRect(new RectangleF(x, y, w, h), h / 2))
            using (var bg = new SolidBrush(Color.FromArgb(235, 30, 26, 46)))
                g.FillPath(bg, path);
            using (var fg = new SolidBrush(Color.White))
                g.DrawString(text, _labelFont, fg, x + S(8), y + S(3));
        }

        private void DrawToolbar(Graphics g)
        {
            using (var shadow = RoundRect(new RectangleF(_toolbar.X, _toolbar.Y + S(2), _toolbar.Width, _toolbar.Height), S(12)))
            using (var sb = new SolidBrush(Color.FromArgb(50, 0, 0, 0)))
                g.FillPath(sb, shadow);
            using (var path = RoundRect(_toolbar, S(12)))
            using (var bg = new SolidBrush(Color.FromArgb(248, 252, 251, 255)))
                g.FillPath(bg, path);

            string[] glyphs = { "", "", "", "" };
            string[] tips = { "Rectangular", "Ventana", "Pantalla", "Cerrar" };
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            for (int i = 0; i < 4; i++)
            {
                var b = _buttons[i];
                bool selected = i < 3 && (int)_mode == i;
                if (selected)
                {
                    using var p = RoundRect(Rectangle.Inflate(b, -S(2), -S(2)), S(8));
                    using var sbg = new SolidBrush(AccentSoft);
                    g.FillPath(sbg, p);
                }
                using var fg = new SolidBrush(selected ? Accent : Color.FromArgb(60, 56, 80));
                g.DrawString(glyphs[i], _iconFont, fg, new RectangleF(b.X, b.Y, b.Width, b.Height - S(8)), sf);
                g.DrawString(tips[i], _smallFont, fg, new RectangleF(b.X - S(4), b.Bottom - S(16), b.Width + S(8), S(14)), sf);
            }
            using (var sepPen = new Pen(Color.FromArgb(225, 220, 240), 1))
                g.DrawLine(sepPen, _buttons[3].X - S(5), _toolbar.Y + S(12), _buttons[3].X - S(5), _toolbar.Bottom - S(12));

            if (_recording)
            {
                using var red = new SolidBrush(Color.FromArgb(240, 80, 110));
                g.FillEllipse(red, _toolbar.X + S(6), _toolbar.Y + S(6), S(7), S(7));
            }
        }

        private static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _dim.Dispose();
                _iconFont.Dispose();
                _labelFont.Dispose();
                _smallFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
