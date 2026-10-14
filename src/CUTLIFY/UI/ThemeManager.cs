using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Microsoft.Win32;
using Cutlify.Themes;

namespace Cutlify.UI
{
    /// <summary>Aplica estilo (Clásico, Cozy, Pixel art, Retro, Minimalista) y modo (claro / noche / sistema) en caliente.</summary>
    public static class ThemeManager
    {
        private static string _mode = "System";

        public static bool IsDark { get; private set; }
        public static ThemeStyle Style { get; private set; } = ThemeCatalog.Styles[0];
        public static Palette Palette => IsDark ? Style.Dark : Style.Light;
        public static event Action Changed;

        public static void Apply(string mode) => Apply(Style.Id, mode);

        public static void Apply(string styleId, string mode)
        {
            _mode = mode ?? "System";
            Style = ThemeCatalog.Get(styleId);
            IsDark = _mode == "Dark" || (_mode == "System" && !SystemUsesLightTheme());
            var dicts = Application.Current.Resources.MergedDictionaries;
            dicts[0] = Build(Style, Palette);
            Changed?.Invoke();
        }

        /// <summary>Diccionario con todos los recursos de tema (colores, bordes, fuente, sombras).</summary>
        public static ResourceDictionary Build(ThemeStyle style, Palette p)
        {
            SolidColorBrush B(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
            LinearGradientBrush G(params Color[] cs)
            {
                var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
                for (int i = 0; i < cs.Length; i++) g.GradientStops.Add(new GradientStop(cs[i], cs.Length == 1 ? 0 : i / (double)(cs.Length - 1)));
                g.Freeze();
                return g;
            }
            DropShadowEffect Shadow(double blur, double depth, double opacity, Color color)
            {
                var e = new DropShadowEffect { BlurRadius = blur, ShadowDepth = depth, Direction = style.Shadow == ShadowKind.Hard ? 315 : 270, Opacity = opacity, Color = color };
                e.Freeze();
                return e;
            }

            var d = new ResourceDictionary
            {
                ["AccentColor"] = p.Accent,
                ["ShadowColor"] = p.Shadow,
                ["Bg"] = B(p.Bg),
                ["Surface"] = B(p.Surface),
                ["SurfaceAlt"] = B(p.SurfaceAlt),
                ["Stage"] = B(p.Stage),
                ["BorderBrush"] = B(p.Border),
                ["Text"] = B(p.Text),
                ["TextSecondary"] = B(p.TextSecondary),
                ["Accent"] = B(p.Accent),
                ["AccentHover"] = B(p.AccentHover),
                ["AccentSoft"] = B(p.AccentSoft),
                ["AccentSoftHover"] = B(p.AccentSoftHover),
                ["OnAccent"] = B(p.OnAccent),
                ["Hover"] = B(p.Hover),
                ["Pressed"] = B(p.Pressed),
                ["Danger"] = B(p.Danger),
                ["PinkSoft"] = B(p.PinkSoft),
                ["BlueSoft"] = B(p.BlueSoft),
                ["AccentGradient"] = G(p.Gradient1, p.Gradient2),
                ["WindowGradient"] = G(p.Window1, p.Window2, p.Window3),
                ["RadiusS"] = new CornerRadius(style.RadiusS),
                ["RadiusM"] = new CornerRadius(style.RadiusM),
                ["RadiusL"] = new CornerRadius(style.RadiusL),
                ["BorderWidth"] = new Thickness(style.BorderWidth),
                ["UiFont"] = Font(style.Font),
                ["BaseFontSize"] = style.FontSize,
            };
            switch (style.Shadow)
            {
                case ShadowKind.Hard:
                    d["SoftShadow"] = Shadow(0, 4, 1, p.Border);
                    d["CardShadow"] = Shadow(0, 3, 1, p.Border);
                    d["AccentShadow"] = Shadow(0, 3, 1, p.Border);
                    break;
                case ShadowKind.None:
                    d["SoftShadow"] = null;
                    d["CardShadow"] = null;
                    d["AccentShadow"] = null;
                    break;
                default:
                    bool cozy = style.Animation == AnimationKind.Bouncy;
                    d["SoftShadow"] = Shadow(cozy ? 28 : 22, 3, cozy ? 0.18 : 0.14, p.Shadow);
                    d["CardShadow"] = Shadow(cozy ? 20 : 14, cozy ? 2 : 1, cozy ? 0.12 : 0.08, p.Shadow);
                    d["AccentShadow"] = Shadow(12, 2, 0.25, p.Accent);
                    break;
            }
            return d;
        }

        public static FontFamily Font(string spec) =>
            spec != null && spec.StartsWith("#")
                ? new FontFamily(new Uri("pack://application:,,,/CUTLIFY;component/Assets/Fonts/"), "./" + spec + ", Segoe UI")
                : new FontFamily(spec ?? "Segoe UI");

        public static System.Drawing.Color AccentDrawing =>
            System.Drawing.Color.FromArgb(Palette.Accent.R, Palette.Accent.G, Palette.Accent.B);

        public static void WatchSystem()
        {
            SystemEvents.UserPreferenceChanged += (_, e) =>
            {
                if (e.Category == UserPreferenceCategory.General && _mode == "System")
                    Application.Current.Dispatcher.BeginInvoke(new Action(() => Apply(Style.Id, _mode)));
            };
        }

        private static bool SystemUsesLightTheme()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return key?.GetValue("AppsUseLightTheme") is int v ? v != 0 : true;
            }
            catch { return true; }
        }
    }
}
