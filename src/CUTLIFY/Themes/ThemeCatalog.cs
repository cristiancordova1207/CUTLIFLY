using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;

namespace Cutlify.Themes
{
    public enum ShadowKind { Soft, Hard, None }
    public enum AnimationKind { Smooth, Bouncy, Stepped, None }

    /// <summary>Colores de una variante (claro / noche) de un estilo.</summary>
    public sealed class Palette
    {
        public Color Bg, Surface, SurfaceAlt, Stage, Border, Text, TextSecondary, Accent, AccentHover, AccentSoft, AccentSoftHover,
                     OnAccent, Hover, Pressed, Danger, PinkSoft, BlueSoft, Gradient1, Gradient2, Window1, Window2, Window3, Shadow;
    }

    /// <summary>Estilo visual: forma (bordes, fuente, sombras, animación) + paletas claro y noche.</summary>
    public sealed class ThemeStyle
    {
        public string Id, Name, Description;
        public Palette Light, Dark;
        public double RadiusS, RadiusM, RadiusL, BorderWidth = 1, FontSize = 13;
        public string Font;                // familia del sistema o "#Familia" incluida en la app
        public ShadowKind Shadow = ShadowKind.Soft;
        public AnimationKind Animation = AnimationKind.Smooth;
        public bool SquareWindows;
    }

    public static class ThemeCatalog
    {
        private static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

        private static Palette P(string bg, string surface, string surfaceAlt, string stage, string border, string text, string text2,
            string accent, string accentHover, string soft, string softHover, string onAccent, string hover, string pressed, string danger,
            string pink, string blue, string g1, string g2, string w1, string w2, string w3, string shadow) => new Palette
        {
            Bg = C(bg), Surface = C(surface), SurfaceAlt = C(surfaceAlt), Stage = C(stage), Border = C(border), Text = C(text), TextSecondary = C(text2),
            Accent = C(accent), AccentHover = C(accentHover), AccentSoft = C(soft), AccentSoftHover = C(softHover), OnAccent = C(onAccent),
            Hover = C(hover), Pressed = C(pressed), Danger = C(danger), PinkSoft = C(pink), BlueSoft = C(blue),
            Gradient1 = C(g1), Gradient2 = C(g2), Window1 = C(w1), Window2 = C(w2), Window3 = C(w3), Shadow = C(shadow)
        };

        public static readonly IReadOnlyList<ThemeStyle> Styles = new List<ThemeStyle>
        {
            new ThemeStyle
            {
                Id = "Classic", Name = "Clásico", Description = "Lavanda pastel estilo Windows 11",
                RadiusS = 8, RadiusM = 10, RadiusL = 14, Font = "Segoe UI Variable Text, Segoe UI",
                Light = P("#F6F4FD", "#FFFFFF", "#FAF9FF", "#EFECFA", "#E7E2F6", "#1F1B2E", "#6E6A84", "#7C5CFF", "#6C4BF5", "#EEE9FF", "#E3DBFF",
                          "#FFFFFF", "#F1EEFB", "#E7E2F6", "#E5487A", "#FFE3EF", "#E1EBFF", "#8E72FF", "#6E4EF6", "#FBFAFF", "#F4F1FD", "#FBF2F8", "#5A4A9E"),
                Dark = P("#17151F", "#211E2C", "#1C1A26", "#14121B", "#322D44", "#F2F0FA", "#A7A2BD", "#A58BFF", "#B7A1FF", "#2E2745", "#3A3157",
                         "#16131F", "#2A2638", "#342F45", "#FF7BA3", "#3A2633", "#22293D", "#B49DFF", "#9578FF", "#1B1925", "#18161F", "#1E1824", "#000000")
            },
            new ThemeStyle
            {
                Id = "Cozy", Name = "Cozy", Description = "Cálido y esponjoso, con animaciones suaves y elásticas",
                RadiusS = 12, RadiusM = 16, RadiusL = 22, Font = "#Nunito", FontSize = 14, Animation = AnimationKind.Bouncy,
                Light = P("#FBF4EC", "#FFFDF9", "#FFF7EF", "#F3E7DA", "#EBDCCB", "#4A3B32", "#8C7766", "#E58F6B", "#D97E59", "#FCE6DA", "#F9D6C4",
                          "#FFFFFF", "#F7ECE1", "#EFDFCF", "#D9677A", "#FBE0E4", "#E2EEE6", "#F2A783", "#E58F6B", "#FFF9F2", "#FBF1E6", "#F8EDE6", "#8C6A50"),
                Dark = P("#2A2320", "#352C28", "#2F2724", "#241E1B", "#4A3D36", "#F6EAE0", "#C4AE9E", "#F2A783", "#F6B897", "#4A3730", "#5A4239",
                         "#2A2320", "#3E332E", "#4A3D36", "#F08C9C", "#4A3034", "#2F3B33", "#F6B897", "#E58F6B", "#2C2421", "#28211E", "#2E2522", "#000000")
            },
            new ThemeStyle
            {
                Id = "Pixel", Name = "Pixel art", Description = "2D pixelado: esquinas rectas, contornos marcados y sombras duras",
                RadiusS = 0, RadiusM = 0, RadiusL = 0, BorderWidth = 2, Font = "#Pixelify Sans", FontSize = 13,
                Shadow = ShadowKind.Hard, Animation = AnimationKind.Stepped, SquareWindows = true,
                Light = P("#F4F1E8", "#FFFFFF", "#F8F5EC", "#E3DDCB", "#2B2B3A", "#1E1E2A", "#5A5A72", "#4A6CF7", "#3A58E0", "#DCE4FF", "#C9D5FF",
                          "#FFFFFF", "#ECE6D6", "#DDD5BF", "#E0425C", "#FFD9E0", "#D7F0E6", "#4A6CF7", "#4A6CF7", "#F4F1E8", "#F4F1E8", "#F4F1E8", "#2B2B3A"),
                Dark = P("#14131F", "#1F1D30", "#191829", "#0F0E18", "#4B4870", "#F2F0FF", "#A6A3C9", "#7CF29A", "#95F5AE", "#23392C", "#2C4838",
                         "#14131F", "#29273F", "#34314F", "#FF6B8A", "#3A2233", "#1E2A3D", "#7CF29A", "#7CF29A", "#14131F", "#14131F", "#14131F", "#000000")
            },
            new ThemeStyle
            {
                Id = "Retro", Name = "Retro", Description = "Tonos de los 70–80 de día y synthwave de noche, con tipografía de terminal",
                RadiusS = 4, RadiusM = 6, RadiusL = 8, BorderWidth = 2, Font = "#VT323", FontSize = 18, Shadow = ShadowKind.Hard,
                Light = P("#F5E9D3", "#FFF8EA", "#FBF0DC", "#EAD9BC", "#D9C29C", "#3D2B1F", "#7A5F48", "#D9622B", "#C2531F", "#F8DCC8", "#F3CAAE",
                          "#FFF8EA", "#F1E2C7", "#E8D3B0", "#B83A3A", "#F4D3C9", "#CFE3DF", "#E9A23B", "#D9622B", "#F8EEDB", "#F5E9D3", "#F2E2C8", "#7A4A20"),
                Dark = P("#1A1033", "#241645", "#1E123B", "#130B26", "#3D2A6E", "#FCE9FF", "#C4A8E8", "#FF5FA2", "#FF7DB5", "#3D1E4F", "#4D2563",
                         "#1A1033", "#2E1C55", "#3A2468", "#FF6B6B", "#3D1E4F", "#10304A", "#FF8A3D", "#FF5FA2", "#1F1240", "#1A1033", "#24113A", "#FF5FA2")
            },
            new ThemeStyle
            {
                Id = "Minimal", Name = "Minimalista", Description = "Blanco y negro, sin sombras ni adornos",
                RadiusS = 4, RadiusM = 6, RadiusL = 8, Font = "Segoe UI Variable Text, Segoe UI", Shadow = ShadowKind.None, Animation = AnimationKind.None,
                Light = P("#FAFAFA", "#FFFFFF", "#F7F7F7", "#F0F0F0", "#E5E5E5", "#171717", "#737373", "#171717", "#333333", "#F0F0F0", "#E5E5E5",
                          "#FFFFFF", "#F2F2F2", "#E8E8E8", "#DC2626", "#F5F5F5", "#F5F5F5", "#171717", "#171717", "#FAFAFA", "#FAFAFA", "#FAFAFA", "#000000"),
                Dark = P("#0A0A0A", "#141414", "#101010", "#070707", "#262626", "#FAFAFA", "#A3A3A3", "#FAFAFA", "#E5E5E5", "#1F1F1F", "#2A2A2A",
                         "#0A0A0A", "#1C1C1C", "#262626", "#F87171", "#1A1A1A", "#1A1A1A", "#FAFAFA", "#FAFAFA", "#0A0A0A", "#0A0A0A", "#0A0A0A", "#000000")
            },
        };

        public static ThemeStyle Get(string id) => Styles.FirstOrDefault(s => s.Id == id) ?? Styles[0];
    }
}
