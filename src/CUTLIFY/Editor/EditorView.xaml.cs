using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Cutlify.Editor
{
    public enum EditorTool { Select, Pen, Highlighter, Eraser, Shape, Text, Crop }

    /// <summary>Editor sencillo de capturas: anotaciones, texto, formas, recorte y deshacer/rehacer.</summary>
    public partial class EditorView : UserControl
    {
        private sealed class EditAction
        {
            public Action Undo;
            public Action Redo;
        }

        private static readonly Color[] Palette =
        {
            Color.FromRgb(0x1F, 0x1B, 0x2E), Colors.White, Color.FromRgb(0xF0, 0x50, 0x6E), Color.FromRgb(0xFF, 0x9F, 0x43),
            Color.FromRgb(0xFF, 0xD4, 0x3B), Color.FromRgb(0x2F, 0xBF, 0x71), Color.FromRgb(0x3D, 0x8B, 0xFF),
            Color.FromRgb(0x7C, 0x5C, 0xFF), Color.FromRgb(0xFF, 0x7E, 0xB6)
        };

        private readonly Stack<EditAction> _undo = new Stack<EditAction>();
        private readonly Stack<EditAction> _redo = new Stack<EditAction>();

        private BitmapSource _base;
        private EditorTool _tool = EditorTool.Pen;
        private Color _color = Color.FromRgb(0xF0, 0x50, 0x6E);
        private Color _highlightColor = Color.FromRgb(0xFF, 0xD4, 0x3B);

        // estado de arrastre
        private Point _start;
        private bool _dragging;
        private UIElement _active;
        private TranslateTransform _moveTransform;
        private Point _moveOrigin;
        private TextBox _textBox;

        // recorte
        private Rect _crop;
        private int _cropHandle = -1; // 0..7 asas, 8 mover
        private Rect _cropStartRect;
        private readonly Rectangle[] _cropDim = new Rectangle[4];
        private Rectangle _cropBorder;
        private readonly Rectangle[] _cropHandles = new Rectangle[8];

        public event Action CopyRequested, SaveRequested, SaveAsRequested, OcrRequested, PinRequested, OpenLocationRequested, DeleteRequested;

        public bool HasEdits => _undo.Count > 0;

        public EditorView()
        {
            InitializeComponent();
            BuildPalette();
            BuildCropLayer();

            foreach (var rb in new[] { ToolSelect, ToolPen, ToolHighlighter, ToolEraser, ToolShape, ToolText, ToolCrop })
                rb.Checked += (s, _) => SetTool((EditorTool)Enum.Parse(typeof(EditorTool), (string)((FrameworkElement)s).Tag));

            UndoButton.Click += (_, __) => Undo();
            RedoButton.Click += (_, __) => Redo();
            CopyButton.Click += (_, __) => CopyRequested?.Invoke();
            SaveButton.Click += (_, __) => SaveRequested?.Invoke();
            SaveAsButton.Click += (_, __) => SaveAsRequested?.Invoke();
            OcrButton.Click += (_, __) => OcrRequested?.Invoke();
            MoreColorsButton.Click += (_, __) => PickCustomColor();
            PinButton.Click += (_, __) => PinRequested?.Invoke();
            FolderButton.Click += (_, __) => OpenLocationRequested?.Invoke();
            DeleteButton.Click += (_, __) => DeleteRequested?.Invoke();
            CropApply.Click += (_, __) => ApplyCrop();
            CropCancel.Click += (_, __) => ToolSelect.IsChecked = true;

            Ink.MouseLeftButtonDown += Ink_Down;
            Ink.MouseMove += Ink_Move;
            Ink.MouseLeftButtonUp += Ink_Up;
            CropLayer.MouseLeftButtonDown += Crop_Down;
            CropLayer.MouseMove += Crop_Move;
            CropLayer.MouseLeftButtonUp += Crop_Up;

            FontSizeCombo.SelectionChanged += (_, __) => UpdateTextBoxStyle();
            BoldToggle.Click += (_, __) => UpdateTextBoxStyle();
            AlignLeft.Checked += (_, __) => UpdateTextBoxStyle();
            AlignCenter.Checked += (_, __) => UpdateTextBoxStyle();
            AlignRight.Checked += (_, __) => UpdateTextBoxStyle();

            ToolPen.IsChecked = true;
            Loaded += (_, __) => ApplyPixelScale();
        }

        // ------------------------------------------------------------------ API

        public void Load(BitmapSource image)
        {
            CommitText();
            _undo.Clear();
            _redo.Clear();
            Ink.Children.Clear();
            SetBase(image);
            if (_tool == EditorTool.Crop) ToolSelect.IsChecked = true;
            UpdateUndoButtons();
        }

        public void SetActionsVisible(bool canDelete, bool canOpenLocation)
        {
            DeleteButton.IsEnabled = canDelete;
            FolderButton.IsEnabled = canOpenLocation;
        }

        /// <summary>Imagen final (captura + anotaciones) en píxeles originales.</summary>
        public BitmapSource Render()
        {
            CommitText();
            if (_base == null) return null;
            if (Ink.Children.Count == 0) return _base;
            int w = _base.PixelWidth, h = _base.PixelHeight;
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawImage(_base, new Rect(0, 0, w, h));
                var vb = new VisualBrush(Ink)
                {
                    Stretch = Stretch.None,
                    ViewboxUnits = BrushMappingMode.Absolute,
                    Viewbox = new Rect(0, 0, w, h),
                    ViewportUnits = BrushMappingMode.Absolute,
                    Viewport = new Rect(0, 0, w, h),
                    AlignmentX = AlignmentX.Left,
                    AlignmentY = AlignmentY.Top
                };
                dc.DrawRectangle(vb, null, new Rect(0, 0, w, h));
            }
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }

        public bool HandleKey(KeyEventArgs e)
        {
            if (_textBox != null) return false;
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            if (ctrl && e.Key == Key.Z) { Undo(); return true; }
            if (ctrl && e.Key == Key.Y) { Redo(); return true; }
            if (ctrl && e.Key == Key.C) { CopyRequested?.Invoke(); return true; }
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            if (ctrl && shift && e.Key == Key.S) { SaveAsRequested?.Invoke(); return true; }
            if (ctrl && e.Key == Key.S) { SaveRequested?.Invoke(); return true; }
            if (e.Key == Key.Escape && _tool == EditorTool.Crop) { ToolSelect.IsChecked = true; return true; }
            if (e.Key == Key.Enter && _tool == EditorTool.Crop) { ApplyCrop(); return true; }
            return false;
        }

        // ------------------------------------------------------------------ Configuración

        private void SetBase(BitmapSource image)
        {
            _base = image;
            BaseImage.Source = image;
            int w = image?.PixelWidth ?? 1, h = image?.PixelHeight ?? 1;
            Surface.Width = w;
            Surface.Height = h;
            BaseImage.Width = w;
            BaseImage.Height = h;
            Ink.Width = w;
            Ink.Height = h;
            CropLayer.Width = w;
            CropLayer.Height = h;
            ApplyPixelScale();
        }

        /// <summary>Muestra la captura a 1:1 en píxeles físicos (si cabe), compensando el escalado DPI.</summary>
        private void ApplyPixelScale()
        {
            if (!IsLoaded) return;
            var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
            Surface.LayoutTransform = dpi > 1.01 ? new ScaleTransform(1 / dpi, 1 / dpi) : Transform.Identity;
        }

        private double ViewScale
        {
            get
            {
                try
                {
                    var t = Surface.TransformToAncestor(Stage);
                    var a = t.Transform(new Point(0, 0));
                    var b = t.Transform(new Point(100, 0));
                    var s = (b.X - a.X) / 100.0;
                    return s > 0 ? s : 1;
                }
                catch { return 1; }
            }
        }

        private void BuildPalette()
        {
            foreach (var c in Palette) ColorPanel.Children.Add(CreateSwatch(c));
            BuildRecentColors();
            SyncPalette();
        }

        private RadioButton CreateSwatch(Color c)
        {
            var rb = new RadioButton
            {
                GroupName = "colors",
                Width = 22,
                Height = 22,
                Margin = new Thickness(3, 0, 3, 0),
                Cursor = Cursors.Hand,
                Tag = c,
                ToolTip = $"#{c.R:X2}{c.G:X2}{c.B:X2}",
                Template = SwatchTemplate(c)
            };
            System.Windows.Automation.AutomationProperties.SetName(rb, $"Color #{c.R:X2}{c.G:X2}{c.B:X2}");
            rb.Checked += (s, _) =>
            {
                var col = (Color)((FrameworkElement)s).Tag;
                if (_tool == EditorTool.Highlighter) _highlightColor = col; else _color = col;
                UpdateTextBoxStyle();
            };
            return rb;
        }

        private void BuildRecentColors()
        {
            RecentPanel.Children.Clear();
            foreach (var hex in Settings.SettingsService.Current.RecentColors.Take(6))
            {
                try { RecentPanel.Children.Add(CreateSwatch((Color)ColorConverter.ConvertFromString(hex))); } catch { }
            }
        }

        private void PickCustomColor()
        {
            var current = _tool == EditorTool.Highlighter ? _highlightColor : _color;
            using var dlg = new System.Windows.Forms.ColorDialog { FullOpen = true, Color = System.Drawing.Color.FromArgb(current.R, current.G, current.B) };
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            var c = Color.FromRgb(dlg.Color.R, dlg.Color.G, dlg.Color.B);
            var hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            var recent = Settings.SettingsService.Current.RecentColors;
            recent.RemoveAll(h => string.Equals(h, hex, StringComparison.OrdinalIgnoreCase));
            recent.Insert(0, hex);
            if (recent.Count > 6) recent.RemoveRange(6, recent.Count - 6);
            Settings.SettingsService.Save();
            if (_tool == EditorTool.Highlighter) _highlightColor = c; else _color = c;
            BuildRecentColors();
            SyncPalette();
            UpdateTextBoxStyle();
        }

        private static ControlTemplate SwatchTemplate(Color c)
        {
            var t = new ControlTemplate(typeof(RadioButton));
            var outer = new FrameworkElementFactory(typeof(Border));
            outer.Name = "ring";
            outer.SetValue(Border.CornerRadiusProperty, new CornerRadius(11));
            outer.SetValue(Border.BorderThicknessProperty, new Thickness(2));
            outer.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
            outer.SetValue(Border.PaddingProperty, new Thickness(2));
            var inner = new FrameworkElementFactory(typeof(Border));
            inner.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            inner.SetValue(Border.BackgroundProperty, new SolidColorBrush(c));
            inner.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)));
            inner.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            outer.AppendChild(inner);
            t.VisualTree = outer;
            var trig = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
            trig.Setters.Add(new Setter(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(0x7C, 0x5C, 0xFF)), "ring"));
            t.Triggers.Add(trig);
            return t;
        }

        private void SyncPalette()
        {
            var target = _tool == EditorTool.Highlighter ? _highlightColor : _color;
            bool found = false;
            foreach (RadioButton rb in ColorPanel.Children.Cast<RadioButton>().Concat(RecentPanel.Children.Cast<RadioButton>()))
            {
                bool match = !found && (Color)rb.Tag == target;
                rb.IsChecked = match;
                found |= match;
            }
        }

        private void SetTool(EditorTool tool)
        {
            CommitText();
            if (_tool == EditorTool.Crop && tool != EditorTool.Crop) CropLayer.Visibility = Visibility.Collapsed;
            _tool = tool;

            bool drawing = tool is EditorTool.Pen or EditorTool.Highlighter or EditorTool.Shape or EditorTool.Text;
            ColorArea.Visibility = drawing ? Visibility.Visible : Visibility.Collapsed;
            OpacityPanel.Visibility = tool is EditorTool.Pen or EditorTool.Shape or EditorTool.Text ? Visibility.Visible : Visibility.Collapsed;
            ThicknessPanel.Visibility = tool is EditorTool.Pen or EditorTool.Highlighter or EditorTool.Shape ? Visibility.Visible : Visibility.Collapsed;
            ShapePanel.Visibility = tool == EditorTool.Shape ? Visibility.Visible : Visibility.Collapsed;
            TextPanel.Visibility = tool == EditorTool.Text ? Visibility.Visible : Visibility.Collapsed;
            CropPanel.Visibility = tool == EditorTool.Crop ? Visibility.Visible : Visibility.Collapsed;
            HintText.Text = tool switch
            {
                EditorTool.Select => "Arrastra una anotación para moverla.",
                EditorTool.Eraser => "Pasa el borrador sobre una anotación para eliminarla.",
                EditorTool.Text => "Haz clic en la imagen para escribir.",
                _ => ""
            };
            Ink.Cursor = tool switch
            {
                EditorTool.Select => Cursors.Arrow,
                EditorTool.Text => Cursors.IBeam,
                EditorTool.Eraser => Cursors.Cross,
                _ => Cursors.Pen
            };
            SyncPalette();
            if (tool == EditorTool.Crop) BeginCrop();
        }

        // ------------------------------------------------------------------ Dibujo

        private Brush StrokeBrush(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

        /// <summary>Color con la opacidad elegida (lápiz, formas y texto).</summary>
        private Color WithOpacity(Color c) => Color.FromArgb((byte)(255 * OpacitySlider.Value / 100.0), c.R, c.G, c.B);

        private void Ink_Down(object sender, MouseButtonEventArgs e)
        {
            if (_base == null) return;
            var p = e.GetPosition(Ink);

            if (_tool == EditorTool.Text)
            {
                if (_textBox != null) { CommitText(); Focus(); return; }
                StartText(p);
                e.Handled = true;
                return;
            }
            Focus();

            _start = p;
            _dragging = true;
            Ink.CaptureMouse();
            double th = ThicknessSlider.Value;

            switch (_tool)
            {
                case EditorTool.Select:
                    _active = HitChild(p);
                    if (_active != null)
                    {
                        _moveTransform = EnsureTranslate(_active);
                        _moveOrigin = new Point(_moveTransform.X, _moveTransform.Y);
                    }
                    break;
                case EditorTool.Eraser:
                    EraseAt(p);
                    break;
                case EditorTool.Pen:
                    _active = new Polyline
                    {
                        Stroke = StrokeBrush(WithOpacity(_color)), StrokeThickness = th,
                        StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                        Points = new PointCollection { p, p }
                    };
                    break;
                case EditorTool.Highlighter:
                    _active = new Polyline
                    {
                        Stroke = StrokeBrush(Color.FromArgb(0x70, _highlightColor.R, _highlightColor.G, _highlightColor.B)),
                        StrokeThickness = Math.Max(12, th * 4),
                        StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Square, StrokeEndLineCap = PenLineCap.Square,
                        Points = new PointCollection { p, p }
                    };
                    break;
                case EditorTool.Shape:
                    _active = CreateShape(p, th);
                    break;
            }

            if (_active != null && _tool is EditorTool.Pen or EditorTool.Highlighter or EditorTool.Shape)
            {
                _active.RenderTransform = new TranslateTransform();
                Ink.Children.Add(_active);
            }
        }

        private Shape CreateShape(Point p, double th)
        {
            var kind = (string)((ComboBoxItem)ShapeCombo.SelectedItem)?.Tag ?? "Rectangle";
            var stroke = StrokeBrush(WithOpacity(_color));
            Shape s = kind switch
            {
                "Ellipse" => new Ellipse(),
                "Line" => new Line { X1 = p.X, Y1 = p.Y, X2 = p.X, Y2 = p.Y, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round },
                "Arrow" => new Path { StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Tag = "arrow" },
                _ => new Rectangle()
            };
            s.Stroke = stroke;
            s.StrokeThickness = th;
            if (s is Rectangle || s is Ellipse)
            {
                if (FillCheck.IsChecked == true) s.Fill = stroke;
                Canvas.SetLeft(s, p.X);
                Canvas.SetTop(s, p.Y);
            }
            if (s is Path arrow) arrow.Fill = stroke;
            return s;
        }

        private void Ink_Move(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            var p = e.GetPosition(Ink);
            p = new Point(Math.Max(0, Math.Min(Ink.Width, p.X)), Math.Max(0, Math.Min(Ink.Height, p.Y)));

            switch (_tool)
            {
                case EditorTool.Select when _active != null:
                    _moveTransform.X = _moveOrigin.X + (p.X - _start.X);
                    _moveTransform.Y = _moveOrigin.Y + (p.Y - _start.Y);
                    break;
                case EditorTool.Eraser:
                    EraseAt(p);
                    break;
                case EditorTool.Pen:
                case EditorTool.Highlighter:
                    if (_active is Polyline pl) pl.Points.Add(p);
                    break;
                case EditorTool.Shape:
                    UpdateShape(_active as Shape, _start, p, (Keyboard.Modifiers & ModifierKeys.Shift) != 0);
                    break;
            }
        }

        private void UpdateShape(Shape s, Point a, Point b, bool constrain)
        {
            switch (s)
            {
                case Line l:
                    l.X2 = b.X; l.Y2 = b.Y;
                    break;
                case Path path:
                    path.Data = ArrowGeometry(a, b, path.StrokeThickness);
                    break;
                case null:
                    break;
                default:
                    double w = Math.Abs(b.X - a.X), h = Math.Abs(b.Y - a.Y);
                    if (constrain) w = h = Math.Max(w, h);
                    Canvas.SetLeft(s, b.X < a.X ? a.X - w : a.X);
                    Canvas.SetTop(s, b.Y < a.Y ? a.Y - h : a.Y);
                    s.Width = w;
                    s.Height = h;
                    break;
            }
        }

        private static Geometry ArrowGeometry(Point a, Point b, double th)
        {
            var v = b - a;
            double len = v.Length;
            var g = new StreamGeometry();
            if (len < 1) return g;
            v /= len;
            double head = Math.Max(12, th * 4);
            var n = new Vector(-v.Y, v.X);
            var baseP = b - v * head;
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(a, false, false);
                ctx.LineTo(baseP, true, true);
                ctx.BeginFigure(b, true, true);
                ctx.LineTo(baseP + n * head * 0.55, true, true);
                ctx.LineTo(baseP - n * head * 0.55, true, true);
            }
            g.Freeze();
            return g;
        }

        private void Ink_Up(object sender, MouseButtonEventArgs e)
        {
            if (!_dragging) return;
            _dragging = false;
            Ink.ReleaseMouseCapture();
            var el = _active;
            _active = null;

            switch (_tool)
            {
                case EditorTool.Select when el != null:
                    var tr = _moveTransform;
                    var from = _moveOrigin;
                    var to = new Point(tr.X, tr.Y);
                    if (from != to)
                        Push(new EditAction { Undo = () => { tr.X = from.X; tr.Y = from.Y; }, Redo = () => { tr.X = to.X; tr.Y = to.Y; } }, applied: true);
                    break;
                case EditorTool.Pen:
                case EditorTool.Highlighter:
                case EditorTool.Shape:
                    if (el == null) break;
                    if (el is Shape s && IsDegenerate(s)) { Ink.Children.Remove(el); break; }
                    Push(new EditAction { Undo = () => Ink.Children.Remove(el), Redo = () => Ink.Children.Add(el) }, applied: true);
                    break;
            }
        }

        private static bool IsDegenerate(Shape s) => s switch
        {
            Line l => Math.Abs(l.X2 - l.X1) < 2 && Math.Abs(l.Y2 - l.Y1) < 2,
            Path p => p.Data == null || p.Data.Bounds.IsEmpty,
            Polyline _ => false,
            _ => double.IsNaN(s.Width) || s.Width < 2 || s.Height < 2
        };

        private static TranslateTransform EnsureTranslate(UIElement el)
        {
            if (el.RenderTransform is TranslateTransform t && !t.IsFrozen) return t;
            t = new TranslateTransform();
            el.RenderTransform = t;
            return t;
        }

        private UIElement HitChild(Point p)
        {
            UIElement found = null;
            double r = 6 / ViewScale;
            VisualTreeHelper.HitTest(Ink, null, res =>
            {
                var d = res.VisualHit;
                while (d != null && VisualTreeHelper.GetParent(d) != Ink) d = VisualTreeHelper.GetParent(d);
                if (d is UIElement u && Ink.Children.Contains(u)) { found = u; return HitTestResultBehavior.Stop; }
                return HitTestResultBehavior.Continue;
            }, new GeometryHitTestParameters(new EllipseGeometry(p, r, r)));
            return found;
        }

        private void EraseAt(Point p)
        {
            var el = HitChild(p);
            if (el == null) return;
            int index = Ink.Children.IndexOf(el);
            Ink.Children.Remove(el);
            Push(new EditAction
            {
                Undo = () => Ink.Children.Insert(Math.Min(index, Ink.Children.Count), el),
                Redo = () => Ink.Children.Remove(el)
            }, applied: true);
        }

        // ------------------------------------------------------------------ Texto

        private void StartText(Point p)
        {
            _textBox = new TextBox
            {
                MinWidth = 60,
                AcceptsReturn = true,
                Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x7C, 0x5C, 0xFF)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(2),
                Style = null
            };
            Canvas.SetLeft(_textBox, p.X);
            Canvas.SetTop(_textBox, p.Y);
            UpdateTextBoxStyle();
            _textBox.LostKeyboardFocus += (_, __) => CommitText();
            _textBox.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape) { CommitText(); e.Handled = true; }
            };
            Ink.Children.Add(_textBox);
            Dispatcher.BeginInvoke(new Action(() => { _textBox?.Focus(); Keyboard.Focus(_textBox); }), System.Windows.Threading.DispatcherPriority.Input);
        }

        private double SelectedFontSize =>
            double.TryParse((FontSizeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString(), out var v) ? v : 32;

        private TextAlignment SelectedAlignment =>
            AlignCenter.IsChecked == true ? TextAlignment.Center : AlignRight.IsChecked == true ? TextAlignment.Right : TextAlignment.Left;

        private void UpdateTextBoxStyle()
        {
            if (_textBox == null) return;
            _textBox.FontSize = SelectedFontSize;
            _textBox.FontWeight = BoldToggle.IsChecked == true ? FontWeights.Bold : FontWeights.Normal;
            _textBox.Foreground = StrokeBrush(WithOpacity(_color));
            _textBox.CaretBrush = StrokeBrush(_color);
            _textBox.TextAlignment = SelectedAlignment;
            _textBox.FontFamily = new FontFamily("Segoe UI");
        }

        private void CommitText()
        {
            var tb = _textBox;
            if (tb == null) return;
            _textBox = null;
            Ink.Children.Remove(tb);
            if (string.IsNullOrWhiteSpace(tb.Text)) return;
            var block = new TextBlock
            {
                Text = tb.Text,
                FontSize = tb.FontSize,
                FontWeight = tb.FontWeight,
                FontFamily = tb.FontFamily,
                Foreground = tb.Foreground,
                TextAlignment = tb.TextAlignment,
                Padding = new Thickness(3),
                RenderTransform = new TranslateTransform()
            };
            Canvas.SetLeft(block, Canvas.GetLeft(tb));
            Canvas.SetTop(block, Canvas.GetTop(tb));
            Ink.Children.Add(block);
            Push(new EditAction { Undo = () => Ink.Children.Remove(block), Redo = () => Ink.Children.Add(block) }, applied: true);
        }

        // ------------------------------------------------------------------ Recorte

        private void BuildCropLayer()
        {
            var dim = new SolidColorBrush(Color.FromArgb(140, 12, 10, 24));
            for (int i = 0; i < 4; i++) { _cropDim[i] = new Rectangle { Fill = dim, IsHitTestVisible = false }; CropLayer.Children.Add(_cropDim[i]); }
            _cropBorder = new Rectangle { Stroke = Brushes.White, StrokeDashArray = new DoubleCollection { 4, 3 }, IsHitTestVisible = false };
            CropLayer.Children.Add(_cropBorder);
            for (int i = 0; i < 8; i++)
            {
                _cropHandles[i] = new Rectangle { Fill = Brushes.White, Stroke = new SolidColorBrush(Color.FromRgb(0x7C, 0x5C, 0xFF)), IsHitTestVisible = false, RadiusX = 2, RadiusY = 2 };
                CropLayer.Children.Add(_cropHandles[i]);
            }
        }

        private void BeginCrop()
        {
            if (_base == null) return;
            _crop = new Rect(0, 0, _base.PixelWidth, _base.PixelHeight);
            CropLayer.Visibility = Visibility.Visible;
            Dispatcher.BeginInvoke(new Action(LayoutCrop), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private Point[] HandlePoints()
        {
            var r = _crop;
            double cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
            return new[]
            {
                r.TopLeft, new Point(cx, r.Top), r.TopRight, new Point(r.Right, cy),
                r.BottomRight, new Point(cx, r.Bottom), r.BottomLeft, new Point(r.Left, cy)
            };
        }

        private void LayoutCrop()
        {
            double w = CropLayer.Width, h = CropLayer.Height, s = ViewScale;
            var r = _crop;
            SetRect(_cropDim[0], 0, 0, w, r.Top);
            SetRect(_cropDim[1], 0, r.Bottom, w, h - r.Bottom);
            SetRect(_cropDim[2], 0, r.Top, r.Left, r.Height);
            SetRect(_cropDim[3], r.Right, r.Top, w - r.Right, r.Height);
            _cropBorder.StrokeThickness = 1.5 / s;
            SetRect(_cropBorder, r.X, r.Y, r.Width, r.Height);
            double hs = 12 / s;
            var pts = HandlePoints();
            for (int i = 0; i < 8; i++)
            {
                _cropHandles[i].StrokeThickness = 1.5 / s;
                SetRect(_cropHandles[i], pts[i].X - hs / 2, pts[i].Y - hs / 2, hs, hs);
            }
            CropSizeText.Text = $"{(int)r.Width} × {(int)r.Height}";
        }

        private static void SetRect(FrameworkElement el, double x, double y, double w, double h)
        {
            Canvas.SetLeft(el, x);
            Canvas.SetTop(el, y);
            el.Width = Math.Max(0, w);
            el.Height = Math.Max(0, h);
        }

        private void Crop_Down(object sender, MouseButtonEventArgs e)
        {
            var p = e.GetPosition(CropLayer);
            double tol = 14 / ViewScale;
            var pts = HandlePoints();
            _cropHandle = -1;
            for (int i = 0; i < 8; i++)
                if ((pts[i] - p).Length <= tol) { _cropHandle = i; break; }
            if (_cropHandle < 0 && _crop.Contains(p)) _cropHandle = 8;
            if (_cropHandle < 0) return;
            _start = p;
            _cropStartRect = _crop;
            CropLayer.CaptureMouse();
        }

        private void Crop_Move(object sender, MouseEventArgs e)
        {
            var p = e.GetPosition(CropLayer);
            if (_cropHandle < 0)
            {
                double tol = 14 / ViewScale;
                var pts = HandlePoints();
                int hover = Array.FindIndex(pts, q => (q - p).Length <= tol);
                CropLayer.Cursor = hover switch
                {
                    0 or 4 => Cursors.SizeNWSE,
                    2 or 6 => Cursors.SizeNESW,
                    1 or 5 => Cursors.SizeNS,
                    3 or 7 => Cursors.SizeWE,
                    _ => _crop.Contains(p) ? Cursors.SizeAll : Cursors.Arrow
                };
                return;
            }
            double W = CropLayer.Width, H = CropLayer.Height, min = 8;
            var r = _cropStartRect;
            double dx = p.X - _start.X, dy = p.Y - _start.Y;
            double l = r.Left, t = r.Top, rt = r.Right, b = r.Bottom;
            if (_cropHandle == 8)
            {
                dx = Math.Max(-l, Math.Min(dx, W - rt));
                dy = Math.Max(-t, Math.Min(dy, H - b));
                _crop = new Rect(l + dx, t + dy, r.Width, r.Height);
            }
            else
            {
                if (_cropHandle is 0 or 6 or 7) l = Math.Max(0, Math.Min(l + dx, rt - min));
                if (_cropHandle is 2 or 3 or 4) rt = Math.Min(W, Math.Max(rt + dx, l + min));
                if (_cropHandle is 0 or 1 or 2) t = Math.Max(0, Math.Min(t + dy, b - min));
                if (_cropHandle is 4 or 5 or 6) b = Math.Min(H, Math.Max(b + dy, t + min));
                _crop = new Rect(new Point(l, t), new Point(rt, b));
            }
            LayoutCrop();
        }

        private void Crop_Up(object sender, MouseButtonEventArgs e)
        {
            _cropHandle = -1;
            CropLayer.ReleaseMouseCapture();
        }

        private void ApplyCrop()
        {
            if (_tool != EditorTool.Crop || _base == null) return;
            var rect = new Int32Rect((int)Math.Round(_crop.X), (int)Math.Round(_crop.Y), (int)Math.Round(_crop.Width), (int)Math.Round(_crop.Height));
            rect.Width = Math.Min(rect.Width, _base.PixelWidth - rect.X);
            rect.Height = Math.Min(rect.Height, _base.PixelHeight - rect.Y);
            if (rect.Width < 1 || rect.Height < 1) return;
            if (rect.X == 0 && rect.Y == 0 && rect.Width == _base.PixelWidth && rect.Height == _base.PixelHeight) { ToolSelect.IsChecked = true; return; }

            var oldBase = _base;
            var oldChildren = Ink.Children.Cast<UIElement>().ToList();
            var flat = Render();
            var cropped = new CroppedBitmap(flat, rect);
            cropped.Freeze();
            BitmapSource newBase = cropped;

            void ToNew() { Ink.Children.Clear(); SetBase(newBase); }
            void ToOld() { SetBase(oldBase); Ink.Children.Clear(); foreach (var c in oldChildren) Ink.Children.Add(c); }

            ToNew();
            Push(new EditAction { Undo = ToOld, Redo = ToNew }, applied: true);
            ToolSelect.IsChecked = true;
        }

        // ------------------------------------------------------------------ Deshacer / rehacer

        private void Push(EditAction a, bool applied)
        {
            if (!applied) a.Redo();
            _undo.Push(a);
            _redo.Clear();
            UpdateUndoButtons();
        }

        public void Undo()
        {
            CommitText();
            if (_undo.Count == 0) return;
            var a = _undo.Pop();
            a.Undo();
            _redo.Push(a);
            UpdateUndoButtons();
        }

        public void Redo()
        {
            if (_redo.Count == 0) return;
            var a = _redo.Pop();
            a.Redo();
            _undo.Push(a);
            UpdateUndoButtons();
        }

        private void UpdateUndoButtons()
        {
            UndoButton.IsEnabled = _undo.Count > 0;
            RedoButton.IsEnabled = _redo.Count > 0;
        }
    }
}
