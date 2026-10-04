using System;
using System.IO;

namespace Cutlifly.Core
{
    /// <summary>Rutas locales de CUTLIFLY (%LOCALAPPDATA%\CUTLIFLY).</summary>
    public static class AppPaths
    {
        public static string Root { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CUTLIFLY");

        public static string Temp => Ensure(Path.Combine(Root, "Temp"));
        public static string Thumbs => Ensure(Path.Combine(Root, "Temp", "thumbs"));
        public static string Logs => Ensure(Path.Combine(Root, "Logs"));
        public static string Updates => Ensure(Path.Combine(Root, "Updates"));
        public static string SettingsFile => Path.Combine(Ensure(Root), "settings.json");
        public static string HistoryFile => Path.Combine(Ensure(Root), "history.json");

        public static string DefaultPicturesFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "CUTLIFLY");

        public static string DefaultVideosFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "CUTLIFLY");

        public static string Ensure(string dir)
        {
            Directory.CreateDirectory(dir);
            return dir;
        }

        public static bool IsInside(string path, string folder)
        {
            if (string.IsNullOrEmpty(path)) return false;
            var full = Path.GetFullPath(path);
            var root = Path.GetFullPath(folder).TrimEnd('\\') + "\\";
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
    }
}
