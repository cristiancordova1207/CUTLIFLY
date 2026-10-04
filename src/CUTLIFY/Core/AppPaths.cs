using System;
using System.IO;
using Cutlify.Settings;

namespace Cutlify.Core
{
    /// <summary>
    /// Rutas de CUTLIFY.
    /// - Datos internos: %LOCALAPPDATA%\CUTLIFY (configuración, historial, miniaturas, temporales, logs).
    /// - Archivos del usuario: Documentos\CUTLIFY\Capturas y \Grabaciones (configurables).
    /// Los temporales viven en %LOCALAPPDATA% para no sincronizar cientos de capturas rápidas con OneDrive.
    /// </summary>
    public static class AppPaths
    {
        private static string _root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CUTLIFY");
        private static string _documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        /// <summary>Redirige las raíces (solo pruebas).</summary>
        internal static void OverrideRoots(string localRoot, string documents)
        {
            _root = localRoot;
            _documents = documents;
        }

        public static string Root => _root;
        public static string Temp => Ensure(Path.Combine(_root, "Temporales"));
        public static string Thumbs => Ensure(Path.Combine(_root, "Thumbnails"));
        public static string Logs => Ensure(Path.Combine(_root, "Logs"));
        public static string Updates => Ensure(Path.Combine(_root, "Updates"));
        public static string SettingsFile => Path.Combine(Ensure(_root), "settings.json");
        public static string HistoryFile => Path.Combine(Ensure(_root), "history.json");

        public static string DocumentsRoot => Path.Combine(_documents, "CUTLIFY");
        public static string DefaultCapturesFolder => Path.Combine(DocumentsRoot, "Capturas");
        public static string DefaultRecordingsFolder => Path.Combine(DocumentsRoot, "Grabaciones");

        public static string CapturesFolder =>
            Ensure(string.IsNullOrWhiteSpace(SettingsService.Current.CapturesFolder) ? DefaultCapturesFolder : SettingsService.Current.CapturesFolder);

        public static string RecordingsFolder =>
            Ensure(string.IsNullOrWhiteSpace(SettingsService.Current.RecordingsFolder) ? DefaultRecordingsFolder : SettingsService.Current.RecordingsFolder);

        /// <summary>Crea Documentos\CUTLIFY y sus subcarpetas (primer inicio y cada arranque).</summary>
        public static void EnsureUserFolders()
        {
            try
            {
                Ensure(DocumentsRoot);
                _ = CapturesFolder;
                _ = RecordingsFolder;
                _ = Temp;
                _ = Thumbs;
            }
            catch (Exception ex)
            {
                Logger.Error("Storage", "No se pudieron crear las carpetas de CUTLIFY", ex);
            }
        }

        public static string Ensure(string dir)
        {
            Directory.CreateDirectory(dir);
            return dir;
        }

        public static bool IsInside(string path, string folder)
        {
            if (string.IsNullOrEmpty(path)) return false;
            var full = Path.GetFullPath(path);
            var root = Path.GetFullPath(folder).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Nombre único y organizado: Captura_2026-10-04_14-32-18.png (añade _2, _3… si existe).</summary>
        public static string UniqueFile(string folder, string prefix, string extension, DateTime? when = null)
        {
            var stamp = (when ?? DateTime.Now).ToString("yyyy-MM-dd_HH-mm-ss");
            var path = Path.Combine(folder, $"{prefix}_{stamp}{extension}");
            for (int i = 2; File.Exists(path); i++)
                path = Path.Combine(folder, $"{prefix}_{stamp}_{i}{extension}");
            return path;
        }
    }
}
