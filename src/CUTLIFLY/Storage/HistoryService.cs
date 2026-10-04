using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cutlifly.Core;
using Cutlifly.Settings;

namespace Cutlifly.Storage
{
    /// <summary>Historial de capturas y grabaciones (persistido en history.json).</summary>
    public class HistoryService
    {
        private const int MaxMemoryItems = 20;

        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public ObservableCollection<HistoryItem> Items { get; } = new ObservableCollection<HistoryItem>();

        public void Load()
        {
            Items.Clear();
            try
            {
                if (!File.Exists(AppPaths.HistoryFile)) return;
                var list = JsonSerializer.Deserialize<List<HistoryItem>>(File.ReadAllText(AppPaths.HistoryFile), Options) ?? new List<HistoryItem>();
                foreach (var item in list.Where(i => i.HasFile).OrderByDescending(i => i.CreatedUtc))
                    Items.Add(item);
            }
            catch (Exception ex)
            {
                Logger.Error("History", "No se pudo leer el historial", ex);
            }
        }

        public void Save()
        {
            try
            {
                var list = Items.Where(i => !i.IsMemoryOnly && !i.IsExternal).ToList();
                var tmp = AppPaths.HistoryFile + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(list, Options));
                File.Move(tmp, AppPaths.HistoryFile, true);
            }
            catch (Exception ex)
            {
                Logger.Error("History", "No se pudo guardar el historial", ex);
            }
        }

        public void Add(HistoryItem item)
        {
            Items.Insert(0, item);
            // Las capturas solo-portapapeles viven en memoria: limitar cuántas se retienen.
            var mem = Items.Where(i => i.IsMemoryOnly).Skip(MaxMemoryItems).ToList();
            foreach (var m in mem) Items.Remove(m);
            Save();
        }

        public void Remove(HistoryItem item, bool deleteFile)
        {
            Items.Remove(item);
            if (deleteFile && !item.IsExternal)
            {
                var recycle = item.IsPermanent || SettingsService.Current.SendExpiredToRecycleBin;
                FileDeleter.Delete(item.FilePath, recycle);
                if (!string.IsNullOrEmpty(item.ThumbPath) && AppPaths.IsInside(item.ThumbPath, AppPaths.Temp))
                    FileDeleter.Delete(item.ThumbPath, false);
            }
            Save();
        }

        /// <summary>Convierte un elemento temporal/en memoria en permanente tras guardarlo en <paramref name="newPath"/>.</summary>
        public void MarkPermanent(HistoryItem item, string newPath)
        {
            var old = item.FilePath;
            item.FilePath = newPath;
            item.IsPermanent = true;
            item.MemoryImage = null;
            try { item.SizeBytes = new FileInfo(newPath).Length; } catch { }
            if (!string.IsNullOrEmpty(old) && !string.Equals(old, newPath, StringComparison.OrdinalIgnoreCase)
                && AppPaths.IsInside(old, AppPaths.Temp))
                FileDeleter.Delete(old, false);
            if (!Items.Contains(item)) Items.Insert(0, item);
            item.InvalidateThumbnail();
            item.Refresh();
            Save();
        }

        public IEnumerable<HistoryItem> TemporaryItems => Items.Where(i => !i.IsPermanent && !i.IsExternal);
    }
}
