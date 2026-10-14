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

        /// <summary>Miniatura de forma de onda (grabaciones de audio).</summary>
        public static string CreateWaveform(float[] peaks, string id, bool dark = false)
        {
            const int w = 320, h = 180;
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                var bg = new LinearGradientBrush(Color.FromRgb(0xEE, 0xE9, 0xFF), Color.FromRgb(0xFF, 0xE3, 0xEF), 45);
                dc.DrawRectangle(bg, null, new System.Windows.Rect(0, 0, w, h));
                var bar = new SolidColorBrush(Color.FromRgb(0x7C, 0x5C, 0xFF));
                int bars = 64;
                double bw = w / (double)bars;
                for (int i = 0; i < bars; i++)
                {
                    float v = 0;
                    if (peaks != null && peaks.Length > 0)
                    {
                        int a = i * peaks.Length / bars, b = Math.Max(a + 1, (i + 1) * peaks.Length / bars);
                        for (int k = a; k < b && k < peaks.Length; k++) v = Math.Max(v, peaks[k]);
                    }
                    double bh = Math.Max(3, Math.Sqrt(v) * (h - 40));
                    dc.DrawRoundedRectangle(bar, null, new System.Windows.Rect(i * bw + 1, (h - bh) / 2, Math.Max(1, bw - 2), bh), 1.5, 1.5);
                }
            }
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return Create(rtb, id);
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
