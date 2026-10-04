using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Cutlifly.Capture
{
    /// <summary>Conversión entre System.Drawing.Bitmap (GDI) y BitmapSource (WPF).</summary>
    public static class ImageUtil
    {
        public static BitmapSource ToBitmapSource(Bitmap bmp)
        {
            var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
            try
            {
                var src = BitmapSource.Create(bmp.Width, bmp.Height, 96, 96, PixelFormats.Bgr32, null,
                    data.Scan0, data.Stride * bmp.Height, data.Stride);
                src.Freeze();
                return src;
            }
            finally
            {
                bmp.UnlockBits(data);
            }
        }

        public static Bitmap ToGdi(BitmapSource source)
        {
            var conv = new FormatConvertedBitmap(source, PixelFormats.Bgr32, null, 0);
            var bmp = new Bitmap(conv.PixelWidth, conv.PixelHeight, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
            var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.WriteOnly, bmp.PixelFormat);
            try
            {
                conv.CopyPixels(Int32Rect.Empty, data.Scan0, data.Stride * bmp.Height, data.Stride);
            }
            finally
            {
                bmp.UnlockBits(data);
            }
            return bmp;
        }

        public static void Save(BitmapSource source, string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            BitmapEncoder enc = ext switch
            {
                ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = 92 },
                ".bmp" => new BmpBitmapEncoder(),
                _ => new PngBitmapEncoder()
            };
            enc.Frames.Add(BitmapFrame.Create(ext == ".png" ? source : new FormatConvertedBitmap(source, PixelFormats.Bgr24, null, 0)));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var tmp = path + ".part";
            using (var fs = File.Create(tmp)) enc.Save(fs);
            File.Move(tmp, path, true);
        }

        public static byte[] EncodePng(BitmapSource source)
        {
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(source));
            using var ms = new MemoryStream();
            enc.Save(ms);
            return ms.ToArray();
        }

        public static void SaveThumbnail(Bitmap bmp, string path, int maxWidth = 320)
        {
            double scale = Math.Min(1.0, maxWidth / (double)bmp.Width);
            int w = Math.Max(1, (int)(bmp.Width * scale)), h = Math.Max(1, (int)(bmp.Height * scale));
            using var thumb = new Bitmap(w, h);
            using (var g = Graphics.FromImage(thumb))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                g.DrawImage(bmp, 0, 0, w, h);
            }
            thumb.Save(path, ImageFormat.Jpeg);
        }
    }
}
