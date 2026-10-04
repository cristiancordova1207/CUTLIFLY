using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cutlify.Core;
using Cutlify.Settings;

namespace Cutlify.Storage
{
    /// <summary>
    /// Historial de capturas y grabaciones (history.json).
    /// "Eliminar archivo" borra el archivo y conserva la entrada (estado Eliminada).
    /// "Quitar del historial" borra la entrada y su miniatura, nunca el archivo.
    /// </summary>
    public class HistoryService
    {
        private const int MaxMemoryItems = 20;
        private const int MaxEntries = 1000;

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
                foreach (var item in list.OrderByDescending(i => i.CreatedUtc))
                {
                    if (item.State == ItemState.Available)
                    {
                        if (string.IsNullOrEmpty(item.FilePath)) SetState(item, ItemState.NotSaved);       // solo portapapeles de una sesión anterior
                        else if (!File.Exists(item.FilePath)) SetState(item, ItemState.Deleted);           // borrado fuera de CUTLIFY
                    }
                    Items.Add(item);
                }
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
                var list = Items.Where(i => !i.IsExternal).ToList();
                var tmp = AppPaths.HistoryFile + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(list, Options));
                File.Move(tmp, AppPaths.HistoryFile, true);
            }
            catch (Exception ex)
            {
                Logger.Error("History", "No se pudo guardar el historial", ex);
            }
        }

        private static void SetState(HistoryItem item, ItemState state)
        {
            item.State = state;
            item.StateChangedUtc = DateTime.UtcNow;
            if (state != ItemState.Available) item.MemoryImage = null;
        }

        public void Add(HistoryItem item)
        {
            if (string.IsNullOrEmpty(item.FileName) && !string.IsNullOrEmpty(item.FilePath)) item.FileName = Path.GetFileName(item.FilePath);
            Items.Insert(0, item);
            // Las capturas solo-portapapeles viven en memoria: limitar cuántas se retienen.
            foreach (var m in Items.Where(i => i.IsMemoryOnly).Skip(MaxMemoryItems).ToList())
            {
                SetState(m, ItemState.NotSaved);
                m.NotifyStateChanged();
            }
            foreach (var old in Items.Skip(MaxEntries).Where(i => i.IsGone).ToList()) RemoveEntry(old, save: false);
            Save();
        }

        /// <summary>Elimina el archivo físico y conserva la entrada con su miniatura.</summary>
        public void DeleteFile(HistoryItem item, ItemState newState = ItemState.Deleted)
        {
            if (item.IsExternal) return;
            if (item.HasFile)
            {
                bool recycle = item.IsPermanent || SettingsService.Current.SendExpiredToRecycleBin;
                FileDeleter.Delete(item.FilePath, recycle);
            }
            SetState(item, newState);
            item.NotifyStateChanged();
            Save();
        }

        /// <summary>Quita la entrada y su miniatura. El archivo (si existe) no se toca.</summary>
        public void RemoveEntry(HistoryItem item, bool save = true)
        {
            Items.Remove(item);
            ThumbnailService.Delete(item.ThumbPath);
            if (save) Save();
        }

        /// <summary>Vacía el historial (entradas y miniaturas). Los archivos no se eliminan.</summary>
        public int Clear()
        {
            int n = Items.Count;
            foreach (var item in Items.ToList()) RemoveEntry(item, save: false);
            Save();
            return n;
        }

        /// <summary>Convierte un elemento temporal/en memoria en permanente tras guardarlo en <paramref name="newPath"/>.</summary>
        public void MarkPermanent(HistoryItem item, string newPath)
        {
            var old = item.FilePath;
            item.FilePath = newPath;
            item.FileName = Path.GetFileName(newPath);
            item.IsPermanent = true;
            item.MemoryImage = null;
            SetState(item, ItemState.Available);
            try { item.SizeBytes = new FileInfo(newPath).Length; } catch { }
            if (!string.IsNullOrEmpty(old) && !string.Equals(old, newPath, StringComparison.OrdinalIgnoreCase)
                && AppPaths.IsInside(old, AppPaths.Temp))
                FileDeleter.Delete(old, false);
            if (!Items.Contains(item)) Items.Insert(0, item);
            item.InvalidateThumbnail();
            item.NotifyStateChanged();
            Save();
        }

        public IEnumerable<HistoryItem> TemporaryItems => Items.Where(i => !i.IsPermanent && !i.IsExternal && i.State == ItemState.Available);
    }
}
