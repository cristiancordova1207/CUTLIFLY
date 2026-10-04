using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;
using Cutlify.Capture;
using Cutlify.Clipboard;
using Cutlify.Core;
using Cutlify.Notifications;
using Cutlify.Settings;
using Cutlify.Storage;
using Cutlify.UI;

namespace Cutlify
{
    /// <summary>Acciones sobre capturas y grabaciones: copiar, guardar, abrir, fijar, eliminar, OCR…</summary>
    public sealed partial class AppController
    {
        public static BitmapSource GetImage(HistoryItem item)
        {
            if (item.MemoryImage != null) return item.MemoryImage;
            try { return item.HasFile ? HistoryItem.LoadImage(item.FilePath) : null; }
            catch (Exception ex) { Logger.Warn("History", "No se pudo abrir la imagen", ex); return null; }
        }

        public void CopyImage(BitmapSource image)
        {
            if (image == null) return;
            if (ClipboardService.CopyImage(image))
            {
                if (SettingsService.Current.ShowNotifications) ToastWindow.ShowToast("Captura copiada al portapapeles", "Puedes pegarla con Ctrl + V", image);
            }
            else DialogWindow.Ask(DialogOwner, "No se pudo copiar", "Otra aplicación está usando el portapapeles. Inténtalo de nuevo.", 0, "Cerrar");
        }

        public void Copy(HistoryItem item)
        {
            if (!item.IsAvailable) return;
            if (item.IsVideo)
            {
                if (ClipboardService.CopyFile(item.FilePath))
                {
                    if (SettingsService.Current.ShowNotifications)
                        ToastWindow.ShowToast("Archivo copiado", "Pégalo con Ctrl + V en apps que acepten archivos.", item.Thumbnail, item.DurationLabel);
                }
                else DialogWindow.Ask(DialogOwner, "No se pudo copiar el archivo", "", 0, "Cerrar");
            }
            else CopyImage(GetImage(item));
        }

        /// <summary>Guardar: directo en la carpeta de capturas/grabaciones con nombre automático.</summary>
        public void Save(HistoryItem item, BitmapSource edited = null) => SaveCore(item, edited, askLocation: false);

        /// <summary>Guardar como: elige formato y ubicación.</summary>
        public void SaveAs(HistoryItem item, BitmapSource edited = null) => SaveCore(item, edited, askLocation: true);

        private void SaveCore(HistoryItem item, BitmapSource edited, bool askLocation)
        {
            if (item == null) return;
            if (item.IsVideo) { SaveVideo(item, askLocation); return; }
            var image = edited ?? GetImage(item);
            if (image == null) return;
            var s = SettingsService.Current;
            bool alpha = image.Format == System.Windows.Media.PixelFormats.Bgra32;
            string ext = s.ImageFormat == "JPG" && !alpha ? ".jpg" : ".png";
            string target = AppPaths.UniqueFile(AppPaths.CapturesFolder, "Captura", ext, item.CreatedLocal);
            if (askLocation)
            {
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Guardar como — CUTLIFY",
                    InitialDirectory = AppPaths.CapturesFolder,
                    FileName = Path.GetFileNameWithoutExtension(target),
                    Filter = "Imagen PNG (*.png)|*.png|Imagen JPG (*.jpg)|*.jpg",
                    FilterIndex = ext == ".jpg" ? 2 : 1,
                    AddExtension = true
                };
                if (dlg.ShowDialog(DialogOwner) != true) return;
                target = dlg.FileName;
            }
            try
            {
                ImageUtil.Save(image, target);
                HistoryItem saved = item;
                if (item.IsExternal || item.IsPermanent || !item.IsAvailable)
                {
                    saved = new HistoryItem { Kind = ItemKind.Image, Width = image.PixelWidth, Height = image.PixelHeight };
                    saved.ThumbPath = ThumbnailService.Create(image, saved.Id);
                    History.MarkPermanent(saved, target);
                    _main?.ShowItem(saved);
                }
                else
                {
                    item.Width = image.PixelWidth;
                    item.Height = image.PixelHeight;
                    if (edited != null) { ThumbnailService.Delete(item.ThumbPath); item.ThumbPath = ThumbnailService.Create(image, item.Id); }
                    History.MarkPermanent(item, target);
                }
                if (s.ShowNotifications) ToastWindow.ShowToast("Captura guardada", target, image, null, () => OpenLocationPath(target));
            }
            catch (Exception ex)
            {
                Logger.Error("Storage", "No se pudo guardar la imagen", ex);
                DialogWindow.Ask(DialogOwner, "No se pudo guardar la captura.", ex.Message, 0, "Cerrar");
            }
        }

        public void SaveVideo(HistoryItem item, bool askLocation)
        {
            if (!item.HasFile) return;
            var s = SettingsService.Current;
            string target = AppPaths.UniqueFile(AppPaths.RecordingsFolder, "Grabacion", ".mp4", item.CreatedLocal);
            if (askLocation)
            {
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Guardar grabación como — CUTLIFY",
                    InitialDirectory = AppPaths.RecordingsFolder,
                    FileName = Path.GetFileNameWithoutExtension(target),
                    Filter = "Vídeo MP4 (*.mp4)|*.mp4",
                    AddExtension = true
                };
                if (dlg.ShowDialog(DialogOwner) != true) return;
                target = dlg.FileName;
            }
            try
            {
                if (_main?.Current == item) _main.Video.Release();
                File.Copy(item.FilePath, target, true);
                if (item.IsExternal)
                {
                    var copy = new HistoryItem { Kind = ItemKind.Video, DurationMs = item.DurationMs };
                    History.MarkPermanent(copy, target);
                    item = copy;
                }
                else History.MarkPermanent(item, target);
                if (_main?.Current == item) _main.ShowItem(item);
                if (s.ShowNotifications) ToastWindow.ShowToast("Grabación guardada", target, item.Thumbnail, item.DurationLabel, () => OpenLocationPath(target));
            }
            catch (Exception ex)
            {
                Logger.Error("Storage", "No se pudo guardar el vídeo", ex);
                DialogWindow.Ask(DialogOwner, "No se pudo guardar la grabación.", ex.Message, 0, "Cerrar");
            }
        }

        public void Open(HistoryItem item)
        {
            if (!item.IsAvailable) return;
            if (!item.HasFile) { if (!item.IsVideo) ShowMainWindow(item); return; }
            try { Process.Start(new ProcessStartInfo(item.FilePath) { UseShellExecute = true }); }
            catch (Exception ex) { Logger.Warn("UI", "No se pudo abrir el archivo", ex); }
        }

        public void OpenLocation(HistoryItem item)
        {
            if (item.HasFile) OpenLocationPath(item.FilePath);
            else if (item.IsMemoryOnly) DialogWindow.Ask(DialogOwner, "Solo en el portapapeles", "Esta captura no tiene archivo. Pulsa Guardar para crearlo.", 0, "Aceptar");
        }

        private static void OpenLocationPath(string path)
        {
            try { Process.Start("explorer.exe", $"/select,\"{path}\""); }
            catch (Exception ex) { Logger.Warn("UI", "No se pudo abrir la ubicación", ex); }
        }

        public static void OpenFolder(string folder)
        {
            try { Process.Start(new ProcessStartInfo(AppPaths.Ensure(folder)) { UseShellExecute = true }); }
            catch (Exception ex) { Logger.Warn("UI", "No se pudo abrir la carpeta", ex); }
        }

        public static void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { Logger.Warn("UI", "No se pudo abrir el enlace", ex); }
        }

        public void Pin(BitmapSource image)
        {
            if (image != null) new PinWindow(image).Show();
        }

        /// <summary>Elimina el archivo físico. La entrada se conserva como «Eliminada» con su miniatura.</summary>
        public void DeleteFile(HistoryItem item, Window owner)
        {
            if (item.IsExternal) { _main?.ShowItem(null); return; }
            if (!item.IsAvailable) return;
            if (item.IsPermanent &&
                DialogWindow.Ask(owner, "¿Eliminar este archivo?", $"{item.Name} se enviará a la Papelera de reciclaje.\nLa entrada seguirá en el historial como «Eliminada».", 1, "Cancelar", "Eliminar") != 1)
                return;
            if (_main?.Current == item) _main.Video.Release();
            History.DeleteFile(item);
            if (_main?.Current == item) _main.ShowItem(item);
        }

        /// <summary>Quita la entrada y su miniatura. Nunca borra el archivo.</summary>
        public void RemoveFromHistory(HistoryItem item)
        {
            if (_main?.Current == item) _main.Video.Release();
            History.RemoveEntry(item);
        }

        public void ClearHistory(Window owner)
        {
            if (History.Items.Count == 0) return;
            if (DialogWindow.Ask(owner, "¿Limpiar el historial?",
                    $"Se quitarán {History.Items.Count} entradas y sus miniaturas.\nLas capturas y grabaciones guardadas NO se eliminan.", 1, "Cancelar", "Limpiar historial") != 1)
                return;
            _main?.Video.Release();
            History.Clear();
        }

        public void ConfirmCleanTemp(Window owner)
        {
            var (count, bytes) = TempCleaner.GetUsage();
            if (count == 0)
            {
                DialogWindow.Ask(owner, "No hay archivos temporales", "La carpeta temporal ya está vacía.", 0, "Aceptar");
                return;
            }
            if (DialogWindow.Ask(owner, $"¿Eliminar {count} archivos temporales?",
                    $"Se recuperarán {HistoryItem.FormatBytes(bytes)}. Las capturas guardadas no se tocan.", 1, "Cancelar", "Limpiar") != 1)
                return;
            if (_main?.Current != null && !_main.Current.IsPermanent) _main.Video.Release();
            Cleaner.CleanAll();
        }

        /// <summary>Mueve a otra carpeta los archivos guardados por CUTLIFY (solo si el usuario lo confirma).</summary>
        public int MoveSavedFiles(string fromFolder, string toFolder, ItemKind kind)
        {
            int moved = 0;
            _main?.Video.Release();
            foreach (var item in History.Items.Where(i => i.Kind == kind && i.IsPermanent && i.HasFile && AppPaths.IsInside(i.FilePath, fromFolder)).ToList())
            {
                try
                {
                    var target = Path.Combine(AppPaths.Ensure(toFolder), Path.GetFileName(item.FilePath));
                    if (File.Exists(target)) target = AppPaths.UniqueFile(toFolder, Path.GetFileNameWithoutExtension(item.FilePath), Path.GetExtension(item.FilePath));
                    File.Move(item.FilePath, target);
                    item.FilePath = target;
                    item.FileName = Path.GetFileName(target);
                    moved++;
                }
                catch (Exception ex) { Logger.Warn("Storage", "No se pudo mover un archivo", ex); }
            }
            History.Save();
            return moved;
        }

        public static string Describe(HistoryItem i)
        {
            var sb = new System.Text.StringBuilder();
            void Line(string k, string v) { if (!string.IsNullOrWhiteSpace(v)) sb.AppendLine($"{k}: {v}"); }
            Line("Nombre", i.Name);
            Line("Tipo", i.TypeLabel);
            Line("Resolución", i.DimensionsLabel);
            Line("Formato", i.FormatLabel + (i.IsVideo && !string.IsNullOrEmpty(i.Codec) ? $" ({i.Codec})" : ""));
            if (i.IsVideo)
            {
                Line("FPS", i.FpsLabel);
                if (i.DroppedFrames > 0) Line("Fotogramas perdidos", i.DroppedFrames.ToString());
                Line("Bitrate", i.BitrateLabel);
                Line("Duración", i.DurationLabel);
                Line("Audio", i.Audio);
            }
            Line("Tamaño", i.SizeLabel);
            Line("Fecha", $"{i.DateLabel} {i.CreatedLocal:HH:mm:ss}");
            Line("Ubicación", i.HasFile ? i.FilePath : i.IsMemoryOnly ? "Solo en memoria / portapapeles" : "—");
            Line("Estado", i.StatusLabel);
            return sb.ToString().TrimEnd();
        }

        public void ShowDetails(HistoryItem item, Window owner)
        {
            var d = new DialogWindow("Detalles", Describe(item), 0, "Cerrar");
            if (owner != null && owner.IsVisible) { d.Owner = owner; d.WindowStartupLocation = WindowStartupLocation.CenterOwner; }
            d.ShowDialog();
        }

        /// <summary>OCR local con el motor de Windows (sin servicios externos).</summary>
        public async void ExtractText(BitmapSource image)
        {
            if (image == null) return;
            try
            {
                var text = await OcrService.RecognizeAsync(image);
                new OcrWindow(text) { Owner = DialogOwner }.Show();
            }
            catch (Exception ex)
            {
                Logger.Warn("OCR", "No se pudo extraer el texto", ex);
                DialogWindow.Ask(DialogOwner, "No se pudo extraer el texto", ex.Message, 0, "Cerrar");
            }
        }
    }
}
