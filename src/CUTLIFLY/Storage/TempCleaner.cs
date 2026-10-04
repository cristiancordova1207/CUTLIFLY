using System;
using System.IO;
using System.Linq;
using Cutlifly.Core;
using Cutlifly.Settings;

namespace Cutlifly.Storage
{
    /// <summary>
    /// Elimina archivos temporales caducados. Se basa en la fecha de creación guardada,
    /// así que funciona aunque CUTLIFLY haya estado cerrado (se ejecuta al iniciar y cada minuto).
    /// </summary>
    public class TempCleaner
    {
        private readonly HistoryService _history;

        public TempCleaner(HistoryService history) => _history = history;

        public static bool IsExpired(DateTime createdUtc, TimeSpan? retention, DateTime nowUtc) =>
            retention.HasValue && createdUtc + retention.Value <= nowUtc;

        public int CleanExpired()
        {
            var s = SettingsService.Current;
            var retention = s.Retention;
            if (!retention.HasValue) return 0;
            var now = DateTime.UtcNow;
            int removed = 0;

            foreach (var item in _history.TemporaryItems.Where(i => IsExpired(i.CreatedUtc, retention, now)).ToList())
            {
                _history.Remove(item, deleteFile: true);
                removed++;
            }

            // Archivos huérfanos (p. ej. tras un cierre inesperado) que ya no están en el historial.
            try
            {
                var known = _history.Items.SelectMany(i => new[] { i.FilePath, i.ThumbPath })
                    .Where(p => !string.IsNullOrEmpty(p))
                    .Select(Path.GetFullPath)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var f in new DirectoryInfo(AppPaths.Temp).EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    if (known.Contains(f.FullName)) continue;
                    if (f.Name.EndsWith(".recording", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!IsExpired(f.CreationTimeUtc, retention, now)) continue;
                    if (FileDeleter.Delete(f.FullName, s.SendExpiredToRecycleBin && !f.DirectoryName.EndsWith("thumbs"))) removed++;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("Storage", "Error al revisar huérfanos en Temp", ex);
            }

            if (removed > 0) Logger.Info("Storage", $"Eliminados {removed} temporales caducados");
            return removed;
        }

        /// <summary>Elimina todos los temporales (botón «Limpiar ahora»).</summary>
        public int CleanAll()
        {
            int removed = 0;
            foreach (var item in _history.TemporaryItems.Where(i => !i.IsMemoryOnly).ToList())
            {
                _history.Remove(item, deleteFile: true);
                removed++;
            }
            try
            {
                foreach (var f in new DirectoryInfo(AppPaths.Temp).EnumerateFiles("*", SearchOption.AllDirectories))
                    if (FileDeleter.Delete(f.FullName, false)) removed++;
            }
            catch (Exception ex)
            {
                Logger.Warn("Storage", "Error al limpiar Temp", ex);
            }
            return removed;
        }

        public static (int count, long bytes) GetUsage()
        {
            try
            {
                var files = new DirectoryInfo(AppPaths.Temp).EnumerateFiles("*", SearchOption.AllDirectories)
                    .Where(f => f.DirectoryName == null || !f.DirectoryName.EndsWith("thumbs")).ToList();
                var all = new DirectoryInfo(AppPaths.Temp).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
                return (files.Count, all);
            }
            catch { return (0, 0); }
        }
    }
}
