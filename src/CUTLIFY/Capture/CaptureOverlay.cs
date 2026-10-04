using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using Cutlify.Core;
using Cutlify.Settings;

namespace Cutlify.Capture
{
    public sealed class OverlayResult
    {
        public CaptureMode Mode;
        public Rectangle ScreenRect;   // coordenadas físicas de pantalla
        public IntPtr Window;
        public Point[] FreeFormPath;   // coordenadas de pantalla (modo forma libre)
        public Color PickedColor;      // modo selector de color
    }

    /// <summary>
    /// Capa de selección sobre todas las pantallas: muestra la imagen congelada oscurecida y permite elegir
    /// región, forma libre, ventana, monitor, todas las pantallas o un color. Trabaja en píxeles físicos.
    /// Lupa, regla de dimensiones y barra no forman parte de la captura (se recorta de la imagen congelada).
    /// </summary>
    public sealed class CaptureOverlay : Form
    {
        private const int WM_DPICHANGED = 0x02E0;
        private const int VK_SPACE = 0x20;

        private sealed class ToolButton
        {
            public string Glyph, Label;
            public CaptureMode? Mode;
            public Action Click;
            public Rectangle Bounds;
        }

        private static readonly string[] Ratios = { "Libre", "1:1", "4:3", "16:9", "16:10", "21:9" };

        private readonly Bitmap _frozen;
        private readonly Bitmap _dim;
        private readonly Rectangle _vs;
        private readonly bool _recording;
        private readonly List<WindowInfo> _windows;
        private readonly Rectangle[] _screens;
        private CaptureMode _mode;

        private Point? _dragStart;
        private Point _lastMouse;
        private Rectangle _sel;                 // selección en coordenadas de cliente
        private readonly List<Point> _path = new List<Point>();
        private Rectangle _hover;
        private IntPtr _hoverHwnd;
        private bool _done;
        private bool _adjusting;
        private int _adjustHandle = -1;         // 0..7 asas, 8 mover
        private Rectangle _adjustStart;
        private Rectangle _magnifierRect;
        private string _ratio;

        private readonly List<ToolButton> _buttons = new List<ToolButton>();
        private Rectangle _toolbar;
        private Rectangle _confirmButton;
        private readonly float _scale;
        private readonly Font _iconFont, _labelFont, _smallFont;

        private static readonly Color Accent = Color.FromArgb(124, 92, 255);
        private static readonly Color AccentSoft = Color.FromArgb(237, 232, 255);

        public event Action<OverlayResult> Completed;

        public CaptureOverlay(Bitmap frozen, CaptureMode mode, bool recording)
        {
            _frozen = frozen;
            _recording = recording;
            _mode = recording && (mode == CaptureMode.FreeForm || mode == CaptureMode.ColorPicker) ? CaptureMode.Rectangle : mode;
            _ratio = Ratios.Contains(SettingsService.Current.AspectRatio) ? SettingsService.Current.AspectRatio : "Libre";
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

            _scale = (float)Native.DpiScaleAt(Screen.PrimaryScreen.Bounds.X + 1, Screen.PrimaryScreen.Bounds.Y + 1);
            _iconFont = CreateIconFont(S(16));
            _labelFont = new Font("Segoe UI Semibold", S(13), GraphicsUnit.Pixel);
            _smallFont = new Font("Segoe UI", S(11), GraphicsUnit.Pixel);
            BuildToolbar();

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            Bounds = _vs;
            Cursor = Cursors.Cross;
            KeyPreview = true;
            Text = "CUTLIFY";
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);
        }

        private int S(int v) => (int)Math.Round(v * _scale);

        private void BuildToolbar()
        {
            void Mode(string glyph, string label, CaptureMode m) => _buttons.Add(new ToolButton { Glyph = glyph, Label = label, Mode = m });
            Mode("", "Rectángulo", CaptureMode.Rectangle);
            if (!_recording) Mode("", "Libre", CaptureMode.FreeForm);
            Mode("", "Ventana", CaptureMode.Window);
            Mode("", "Pantalla", CaptureMode.FullScreen);
            if (Screen.AllScreens.Length > 1) Mode("", "Todas", CaptureMode.AllScreens);
            if (!_recording) Mode("", "Color", CaptureMode.ColorPicker);
            _buttons.Add(new ToolButton { Glyph = "", Label = _ratio, Click = CycleRatio });
            _buttons.Add(new ToolButton { Glyph = "", Label = "Cerrar", Click = () => Finish(null) });

            var primary = ToClient(Screen.PrimaryScreen.Bounds);
            int bw = S(58), bh = S(44), pad = S(6), sep = S(10);
            int width = pad * 2 + bw * _buttons.Count + sep * 2;
            _toolbar = new Rectangle(primary.X + (primary.Width - width) / 2, primary.Y + S(14), width, bh + pad * 2);
            int x = _toolbar.X + pad;
            for (int i = 0; i < _buttons.Count; i++)
            {
                if (i == _buttons.Count - 2 || i == _buttons.Count - 1) x += sep;
                _buttons[i].Bounds = new Rectangle(x, _toolbar.Y + pad, bw, bh);
                x += bw;
            }
        }

        private void CycleRatio()
        {
            _ratio = Ratios[(Array.IndexOf(Ratios, _ratio) + 1) % Ratios.Length];
            _buttons.First(b => b.Click == CycleRatio || b.Glyph == "").Label = _ratio;
            SettingsService.Current.AspectRatio = _ratio;
            SettingsService.Save();
            Invalidate(Rectangle.Inflate(_toolbar, S(20), S(20)));
        }

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
            if (keyData == Keys.Enter && _adjusting) { Finish(_sel, IntPtr.Zero); return true; }
            if (keyData == Keys.ControlKey || keyData == (Keys.ControlKey | Keys.Control)) { InvalidateMagnifier(PointToClient(Cursor.Position)); }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.ControlKey) InvalidateMagnifier(PointToClient(Cursor.Position));
            base.OnKeyUp(e);
        }

        private bool MagnifierVisible =>
            _mode == CaptureMode.ColorPicker || SettingsService.Current.ShowMagnifier || (ModifierKeys & Keys.Control) != 0;

        // ------------------------------------------------------------------ Ratón

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right) { Finish(null); return; }
            if (e.Button != MouseButtons.Left) return;
            _lastMouse = e.Location;

            var btn = _buttons.FirstOrDefault(b => b.Bounds.Contains(e.Location));
            if (btn != null)
            {
                if (btn.Click != null) { btn.Click(); return; }
                _mode = btn.Mode.Value;
                _sel = Rectangle.Empty;
                _path.Clear();
                _adjusting = false;
                UpdateHover(e.Location);
                Invalidate();
                return;
            }

            if (_adjusting)
            {
                if (_confirmButton.Contains(e.Location)) { Finish(_sel, IntPtr.Zero); return; }
                if (e.Clicks >= 2 && _sel.Contains(e.Location)) { Finish(_sel, IntPtr.Zero); return; }
                _adjustHandle = HitHandle(e.Location);
                if (_adjustHandle < 0 && _sel.Contains(e.Location)) _adjustHandle = 8;
                if (_adjustHandle >= 0) { _adjustStart = _sel; _dragStart = e.Location; Capture = true; return; }
                _adjusting = false; // clic fuera: nueva selección
            }

            switch (_mode)
            {
                case CaptureMode.Rectangle:
                    _dragStart = e.Location;
                    _sel = new Rectangle(e.Location, Size.Empty);
                    Capture = true;
                    break;
                case CaptureMode.FreeForm:
                    _dragStart = e.Location;
                    _path.Clear();
                    _path.Add(e.Location);
                    Capture = true;
                    break;
                case CaptureMode.Window:
                case CaptureMode.FullScreen:
                    if (!_hover.IsEmpty) Finish(_hover, _mode == CaptureMode.Window ? _hoverHwnd : IntPtr.Zero);
                    break;
                case CaptureMode.AllScreens:
                    Finish(new Rectangle(0, 0, _vs.Width, _vs.Height), IntPtr.Zero);
                    break;
                case CaptureMode.ColorPicker:
                    FinishColor(e.Location);
                    break;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            var delta = new Size(e.X - _lastMouse.X, e.Y - _lastMouse.Y);
            _lastMouse = e.Location;
            if (MagnifierVisible || !_magnifierRect.IsEmpty) InvalidateMagnifier(e.Location);

            if (_adjusting && _adjustHandle >= 0 && _dragStart.HasValue)
            {
                var old = _sel;
                _sel = AdjustRect(_adjustStart, _adjustHandle, e.X - _dragStart.Value.X, e.Y - _dragStart.Value.Y);
                InvalidateAround(Rectangle.Union(old, _sel));
            }
            else if (_mode == CaptureMode.Rectangle && _dragStart.HasValue)
            {
                var old = _sel;
                if ((Native.GetAsyncKeyState(VK_SPACE) & 0x8000) != 0)
                {
                    // Espacio: mover la selección en lugar de redimensionarla
                    _dragStart = _dragStart.Value + delta;
                    _sel.Offset(delta.Width, delta.Height);
                }
                else _sel = RectFrom(_dragStart.Value, e.Location);
                InvalidateAround(Rectangle.Union(old, _sel));
            }
            else if (_mode == CaptureMode.FreeForm && _dragStart.HasValue)
            {
                var prev = _path[_path.Count - 1];
                _path.Add(e.Location);
                InvalidateAround(Rectangle.FromLTRB(Math.Min(prev.X, e.X), Math.Min(prev.Y, e.Y), Math.Max(prev.X, e.X) + 1, Math.Max(prev.Y, e.Y) + 1));
            }
            else if (!_adjusting) UpdateHover(e.Location);

            Cursor = _buttons.Any(b => b.Bounds.Contains(e.Location)) || _confirmButton.Contains(e.Location) ? Cursors.Hand
                : _adjusting ? CursorForHandle(HitHandle(e.Location), e.Location)
                : Cursors.Cross;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || !_dragStart.HasValue) return;
            _dragStart = null;
            Capture = false;

            if (_adjusting) { _adjustHandle = -1; return; }

            if (_mode == CaptureMode.FreeForm)
            {
                if (_path.Count > 2)
                {
                    var b = PathBounds(_path);
                    if (b.Width >= 3 && b.Height >= 3) { Finish(b, IntPtr.Zero); return; }
                }
                _path.Clear();
                Invalidate();
                return;
            }

            if (_sel.Width >= 3 && _sel.Height >= 3)
            {
                if (SettingsService.Current.AdjustBeforeCapture) { _adjusting = true; InvalidateAround(_sel); }
                else Finish(_sel, IntPtr.Zero);
            }
            else { var old = _sel; _sel = Rectangle.Empty; InvalidateAround(old); }
        }

        private Rectangle RectFrom(Point a, Point b)
        {
            int w = Math.Abs(b.X - a.X) + 1, h = Math.Abs(b.Y - a.Y) + 1;
            double? ratio = _ratio switch { "1:1" => 1.0, "4:3" => 4 / 3.0, "16:9" => 16 / 9.0, "16:10" => 1.6, "21:9" => 21 / 9.0, _ => (double?)null };
            if (ratio.HasValue) h = Math.Max(1, (int)Math.Round(w / ratio.Value));
            int x = b.X >= a.X ? a.X : a.X - w + 1;
            int y = b.Y >= a.Y ? a.Y : a.Y - h + 1;
            return new Rectangle(x, y, w, h);
        }

        private static Rectangle PathBounds(List<Point> pts) =>
            Rectangle.FromLTRB(pts.Min(p => p.X), pts.Min(p => p.Y), pts.Max(p => p.X) + 1, pts.Max(p => p.Y) + 1);

        private Point[] HandlePoints(Rectangle r)
        {
            int cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
            return new[] { new Point(r.Left, r.Top), new Point(cx, r.Top), new Point(r.Right - 1, r.Top), new Point(r.Right - 1, cy),
                           new Point(r.Right - 1, r.Bottom - 1), new Point(cx, r.Bottom - 1), new Point(r.Left, r.Bottom - 1), new Point(r.Left, cy) };
        }

        private int HitHandle(Point p)
        {
            var pts = HandlePoints(_sel);
            for (int i = 0; i < pts.Length; i++)
                if (Math.Abs(pts[i].X - p.X) <= S(8) && Math.Abs(pts[i].Y - p.Y) <= S(8)) return i;
            return -1;
        }

        private Cursor CursorForHandle(int h, Point p) => h switch
        {
            0 or 4 => Cursors.SizeNWSE,
            2 or 6 => Cursors.SizeNESW,
            1 or 5 => Cursors.SizeNS,
            3 or 7 => Cursors.SizeWE,
            _ => _sel.Contains(p) ? Cursors.SizeAll : Cursors.Cross
        };

        private Rectangle AdjustRect(Rectangle r, int handle, int dx, int dy)
        {
            int l = r.Left, t = r.Top, rt = r.Right, b = r.Bottom;
            if (handle == 8) { l += dx; rt += dx; t += dy; b += dy; }
            else
            {
                if (handle is 0 or 6 or 7) l = Math.Min(l + dx, rt - 3);
                if (handle is 2 or 3 or 4) rt = Math.Max(rt + dx, l + 3);
                if (handle is 0 or 1 or 2) t = Math.Min(t + dy, b - 3);
                if (handle is 4 or 5 or 6) b = Math.Max(b + dy, t + 3);
            }
            return Rectangle.FromLTRB(l, t, rt, b);
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
            else if (_mode == CaptureMode.FullScreen) target = _screens.FirstOrDefault(s => s.Contains(p));
            else if (_mode == CaptureMode.AllScreens) target = new Rectangle(0, 0, _vs.Width, _vs.Height);
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
            var m = S(56);
            r.Inflate(m, m);
            Invalidate(r);
        }

        private Rectangle MagnifierBounds(Point p)
        {
            int size = S(124);
            int x = p.X + S(22), y = p.Y + S(22);
            if (x + size > Width) x = p.X - S(22) - size;
            if (y + size + S(26) > Height) y = p.Y - S(22) - size - S(26);
            return new Rectangle(x, y, size, size + S(24));
        }

        private void InvalidateMagnifier(Point p)
        {
            if (!_magnifierRect.IsEmpty) Invalidate(Rectangle.Inflate(_magnifierRect, 4, 4));
            _magnifierRect = MagnifierVisible ? MagnifierBounds(p) : Rectangle.Empty;
            if (!_magnifierRect.IsEmpty) Invalidate(Rectangle.Inflate(_magnifierRect, 4, 4));
        }

        // ------------------------------------------------------------------ Resultado

        private void FinishColor(Point p)
        {
            if (_done) return;
            var c = _frozen.GetPixel(Math.Clamp(p.X, 0, _frozen.Width - 1), Math.Clamp(p.Y, 0, _frozen.Height - 1));
            _done = true;
            Hide();
            try { Completed?.Invoke(new OverlayResult { Mode = CaptureMode.ColorPicker, PickedColor = Color.FromArgb(c.R, c.G, c.B) }); }
            finally { Close(); }
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
                {
                    result = new OverlayResult { Mode = _mode, Window = hwnd, ScreenRect = new Rectangle(r.X + _vs.X, r.Y + _vs.Y, r.Width, r.Height) };
                    if (_mode == CaptureMode.FreeForm) result.FreeFormPath = _path.Select(p => new Point(p.X + _vs.X, p.Y + _vs.Y)).ToArray();
                }
            }
            Hide();
            try { Completed?.Invoke(result); }
            finally { Close(); }
        }

        // ------------------------------------------------------------------ Pintado

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var clip = e.ClipRectangle;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.CompositingMode = CompositingMode.SourceCopy;
            g.DrawImage(_dim, clip, clip, GraphicsUnit.Pixel);

            var active = _mode == CaptureMode.Rectangle ? _sel : _mode == CaptureMode.FreeForm ? Rectangle.Empty : _hover;
            if (!active.IsEmpty)
            {
                var vis = Rectangle.Intersect(active, clip);
                if (!vis.IsEmpty) g.DrawImage(_frozen, vis, vis, GraphicsUnit.Pixel);
            }

            g.CompositingMode = CompositingMode.SourceOver;
            g.PixelOffsetMode = PixelOffsetMode.None;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            if (_mode == CaptureMode.FreeForm && _path.Count > 1) DrawFreeForm(g);
            if (!active.IsEmpty) DrawSelection(g, active);
            if (_adjusting) DrawConfirm(g);
            if (clip.IntersectsWith(Rectangle.Inflate(_toolbar, S(20), S(20)))) DrawToolbar(g);
            if (!_magnifierRect.IsEmpty && clip.IntersectsWith(_magnifierRect)) DrawMagnifier(g, PointToClient(Cursor.Position));
        }

        private void DrawFreeForm(Graphics g)
        {
            var pts = _path.ToArray();
            using (var path = new GraphicsPath())
            {
                path.AddLines(pts);
                path.CloseFigure();
                var state = g.Save();
                g.SetClip(path);
                g.CompositingMode = CompositingMode.SourceCopy;
                var b = PathBounds(_path);
                g.DrawImage(_frozen, b, b, GraphicsUnit.Pixel);
                g.Restore(state);
            }
            using var pen = new Pen(Color.White, Math.Max(1.5f, 2f * _scale));
            g.DrawLines(pen, pts);
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
                foreach (var p in HandlePoints(r))
                {
                    g.FillEllipse(fill, p.X - hr, p.Y - hr, hr * 2, hr * 2);
                    g.DrawEllipse(edge, p.X - hr, p.Y - hr, hr * 2, hr * 2);
                }
            }

            var text = $"{r.Width} × {r.Height}" + (_ratio != "Libre" && _mode == CaptureMode.Rectangle ? $"  ·  {_ratio}" : "");
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

        private void DrawConfirm(Graphics g)
        {
            var text = "✓ Capturar (Enter)";
            var size = g.MeasureString(text, _labelFont);
            int w = (int)size.Width + S(20), h = (int)size.Height + S(10);
            int x = Math.Max(S(4), Math.Min(_sel.Right - w, Width - w - S(4)));
            int y = _sel.Top - h - S(10);
            if (y < S(4)) y = _sel.Top + S(10);
            _confirmButton = new Rectangle(x, y, w, h);
            using (var path = RoundRect(_confirmButton, S(8)))
            using (var bg = new SolidBrush(Accent))
                g.FillPath(bg, path);
            using var fg = new SolidBrush(Color.White);
            g.DrawString(text, _labelFont, fg, x + S(10), y + S(5));
        }

        private void DrawMagnifier(Graphics g, Point p)
        {
            var r = _magnifierRect;
            int zoomBox = r.Width;
            const int pixels = 15;
            var src = new Rectangle(p.X - pixels / 2, p.Y - pixels / 2, pixels, pixels);
            var dest = new Rectangle(r.X, r.Y, zoomBox, zoomBox);
            using (var bg = new SolidBrush(Color.Black)) g.FillRectangle(bg, dest);
            var st = g.Save();
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(_frozen, dest, src, GraphicsUnit.Pixel);
            g.Restore(st);
            float cell = zoomBox / (float)pixels;
            using (var pen = new Pen(Color.FromArgb(220, 255, 255, 255), 1))
                g.DrawRectangle(pen, dest.X + cell * (pixels / 2), dest.Y + cell * (pixels / 2), cell, cell);
            using (var border = new Pen(Accent, Math.Max(1.5f, 2 * _scale))) g.DrawRectangle(border, dest);

            var c = _frozen.GetPixel(Math.Clamp(p.X, 0, _frozen.Width - 1), Math.Clamp(p.Y, 0, _frozen.Height - 1));
            var label = $"#{c.R:X2}{c.G:X2}{c.B:X2}   {p.X + _vs.X}, {p.Y + _vs.Y}";
            var lr = new Rectangle(r.X, r.Y + zoomBox, zoomBox, r.Height - zoomBox);
            using (var lb = new SolidBrush(Color.FromArgb(240, 30, 26, 46))) g.FillRectangle(lb, lr);
            using (var sw = new SolidBrush(Color.FromArgb(c.R, c.G, c.B))) g.FillRectangle(sw, lr.X + S(5), lr.Y + S(6), S(12), S(12));
            using var fg = new SolidBrush(Color.White);
            g.DrawString(label, _smallFont, fg, lr.X + S(21), lr.Y + S(5));
        }

        private void DrawToolbar(Graphics g)
        {
            using (var shadow = RoundRect(new RectangleF(_toolbar.X, _toolbar.Y + S(2), _toolbar.Width, _toolbar.Height), S(12)))
            using (var sb = new SolidBrush(Color.FromArgb(50, 0, 0, 0)))
                g.FillPath(sb, shadow);
            using (var path = RoundRect(_toolbar, S(12)))
            using (var bg = new SolidBrush(Color.FromArgb(248, 252, 251, 255)))
                g.FillPath(bg, path);

            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            foreach (var b in _buttons)
            {
                bool selected = b.Mode.HasValue && b.Mode.Value == _mode;
                if (selected)
                {
                    using var p = RoundRect(Rectangle.Inflate(b.Bounds, -S(2), -S(2)), S(8));
                    using var sbg = new SolidBrush(AccentSoft);
                    g.FillPath(sbg, p);
                }
                using var fg = new SolidBrush(selected ? Accent : Color.FromArgb(60, 56, 80));
                g.DrawString(b.Glyph, _iconFont, fg, new RectangleF(b.Bounds.X, b.Bounds.Y + S(2), b.Bounds.Width, b.Bounds.Height - S(18)), sf);
                g.DrawString(b.Label, _smallFont, fg, new RectangleF(b.Bounds.X - S(4), b.Bounds.Bottom - S(17), b.Bounds.Width + S(8), S(15)), sf);
            }
            using (var sepPen = new Pen(Color.FromArgb(225, 220, 240), 1))
            {
                foreach (var i in new[] { _buttons.Count - 2, _buttons.Count - 1 })
                    g.DrawLine(sepPen, _buttons[i].Bounds.X - S(5), _toolbar.Y + S(12), _buttons[i].Bounds.X - S(5), _toolbar.Bottom - S(12));
            }

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
