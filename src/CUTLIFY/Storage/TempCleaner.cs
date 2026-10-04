using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Cutlify.Core;
using Cutlify.Settings;

namespace Cutlify.Storage
{
    /// <summary>
    /// Gestiona los temporales. La caducidad se calcula con la fecha de creación guardada, así que
    /// funciona aunque CUTLIFY haya estado cerrado (se ejecuta al iniciar y cada minuto).
    /// Las entradas caducadas se conservan en el historial con estado "Expirada".
    /// </summary>
    public class TempCleaner
    {
        public const string MarkerExtension = ".recording";

        private readonly HistoryService _history;

        public TempCleaner(HistoryService history) => _history = history;

        public static bool IsExpired(DateTime createdUtc, TimeSpan? retention, DateTime nowUtc) =>
            retention.HasValue && createdUtc + retention.Value <= nowUtc;

        public int CleanExpired()
        {
            var s = SettingsService.Current;
            var retention = s.Retention;
            int removed = 0;
            var now = DateTime.UtcNow;

            if (retention.HasValue)
            {
                foreach (var item in _history.TemporaryItems.Where(i => !i.IsMemoryOnly && IsExpired(i.CreatedUtc, retention, now)).ToList())
                {
                    _history.DeleteFile(item, ItemState.Expired);
                    removed++;
                }
            }

            try
            {
                // Temporales huérfanos (p. ej. de entradas quitadas del historial) que ya caducaron.
                var live = _history.Items.Where(i => i.State == ItemState.Available && !string.IsNullOrEmpty(i.FilePath))
                    .Select(i => Path.GetFullPath(i.FilePath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (retention.HasValue)
                {
                    foreach (var f in new DirectoryInfo(AppPaths.Temp).EnumerateFiles())
                    {
                        if (live.Contains(f.FullName) || f.Name.EndsWith(MarkerExtension, StringComparison.OrdinalIgnoreCase)) continue;
                        if (File.Exists(f.FullName + MarkerExtension)) continue; // grabación en curso
                        if (!IsExpired(f.CreationTimeUtc, retention, now)) continue;
                        if (FileDeleter.Delete(f.FullName, s.SendExpiredToRecycleBin)) removed++;
                    }
                }

                // Miniaturas sin entrada en el historial.
                var thumbs = _history.Items.Select(i => i.ThumbPath).Where(p => !string.IsNullOrEmpty(p))
                    .Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var f in new DirectoryInfo(AppPaths.Thumbs).EnumerateFiles())
                    if (!thumbs.Contains(f.FullName) && f.CreationTimeUtc < now.AddHours(-1))
                        ThumbnailService.Delete(f.FullName);
            }
            catch (Exception ex)
            {
                Logger.Warn("Storage", "Error al revisar huérfanos", ex);
            }

            if (removed > 0) Logger.Info("Storage", $"Eliminados {removed} temporales caducados");
            return removed;
        }

        /// <summary>Elimina todos los temporales (botón «Limpiar ahora»); las entradas quedan como "Eliminada".</summary>
        public int CleanAll()
        {
            int removed = 0;
            foreach (var item in _history.TemporaryItems.Where(i => !i.IsMemoryOnly).ToList())
            {
                _history.DeleteFile(item);
                removed++;
            }
            try
            {
                foreach (var f in new DirectoryInfo(AppPaths.Temp).EnumerateFiles())
                {
                    if (f.Name.EndsWith(MarkerExtension, StringComparison.OrdinalIgnoreCase) || File.Exists(f.FullName + MarkerExtension)) continue;
                    if (FileDeleter.Delete(f.FullName, false)) removed++;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("Storage", "Error al limpiar temporales", ex);
            }
            return removed;
        }

        /// <summary>
        /// Tras un cierre inesperado durante una grabación: el MP4 sin finalizar no es reproducible,
        /// así que se elimina y se registra en el historial como "Grabación incompleta".
        /// </summary>
        public int RecoverInterruptedRecordings()
        {
            int n = 0;
            foreach (var marker in Directory.GetFiles(AppPaths.Temp, "*" + MarkerExtension))
            {
                try
                {
                    var item = JsonSerializer.Deserialize<HistoryItem>(File.ReadAllText(marker)) ?? new HistoryItem { Kind = ItemKind.Video };
                    var video = marker.Substring(0, marker.Length - MarkerExtension.Length);
                    item.Kind = ItemKind.Video;
                    item.FileName = Path.GetFileName(video);
                    item.FilePath = null;
                    item.State = ItemState.Incomplete;
                    item.StateChangedUtc = DateTime.UtcNow;
                    if (File.Exists(video)) FileDeleter.Delete(video, false);
                    if (!_history.Items.Any(i => i.Id == item.Id)) _history.Add(item);
                    File.Delete(marker);
                    n++;
                    Logger.Warn("Recording", $"Grabación interrumpida registrada como incompleta: {item.FileName}");
                }
                catch (Exception ex)
                {
                    Logger.Error("Recording", "No se pudo procesar una grabación interrumpida", ex);
                }
            }
            return n;
        }

        public static (int count, long bytes) GetUsage()
        {
            try
            {
                var files = new DirectoryInfo(AppPaths.Temp).EnumerateFiles().Where(f => !f.Name.EndsWith(MarkerExtension)).ToList();
                return (files.Count, files.Sum(f => f.Length));
            }
            catch { return (0, 0); }
        }
    }
}
