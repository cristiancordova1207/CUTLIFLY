using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Cutlify.Core;

namespace Cutlify.Storage
{
    /// <summary>Miniaturas JPEG pequeñas en %LOCALAPPDATA%\CUTLIFY\Thumbnails, independientes del archivo original.</summary>
    public static class ThumbnailService
    {
        public const int MaxWidth = 320;

        public static string PathFor(string id) => Path.Combine(AppPaths.Thumbs, id + ".jpg");

        public static string Create(BitmapSource source, string id)
        {
            try
            {
                double scale = Math.Min(1.0, MaxWidth / (double)source.PixelWidth);
                BitmapSource scaled = scale < 1 ? new TransformedBitmap(source, new ScaleTransform(scale, scale)) : source;
                var enc = new JpegBitmapEncoder { QualityLevel = 80 };
                enc.Frames.Add(BitmapFrame.Create(new FormatConvertedBitmap(scaled, PixelFormats.Bgr24, null, 0)));
                var path = PathFor(id);
                using (var fs = File.Create(path)) enc.Save(fs);
                return path;
            }
            catch (Exception ex)
            {
                Logger.Warn("Storage", "No se pudo crear la miniatura", ex);
                return null;
            }
        }

        public static void Delete(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && AppPaths.IsInside(path, AppPaths.Thumbs) && File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex)
            {
                Logger.Warn("Storage", "No se pudo borrar la miniatura", ex);
            }
        }
    }
}
