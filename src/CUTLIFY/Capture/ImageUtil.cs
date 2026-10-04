using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Cutlify.Capture
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

        /// <summary>Recorte de forma libre: fuera del polígono queda transparente.</summary>
        public static BitmapSource FreeFormCrop(Bitmap frozen, Rectangle bounds, System.Drawing.Point[] polygon)
        {
            using var argb = new Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(argb))
            using (var path = new System.Drawing.Drawing2D.GraphicsPath())
            {
                path.AddPolygon(polygon.Select(p => new System.Drawing.Point(p.X - bounds.X, p.Y - bounds.Y)).ToArray());
                g.Clear(System.Drawing.Color.Transparent);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.SetClip(path);
                g.DrawImage(frozen, new Rectangle(0, 0, bounds.Width, bounds.Height), bounds, GraphicsUnit.Pixel);
            }
            var data = argb.LockBits(new Rectangle(0, 0, argb.Width, argb.Height), ImageLockMode.ReadOnly, argb.PixelFormat);
            try
            {
                var src = BitmapSource.Create(argb.Width, argb.Height, 96, 96, PixelFormats.Bgra32, null, data.Scan0, data.Stride * argb.Height, data.Stride);
                src.Freeze();
                return src;
            }
            finally { argb.UnlockBits(data); }
        }

        /// <summary>Compone sobre blanco las imágenes con transparencia (para formatos sin alfa: DIB, JPG).</summary>
        public static BitmapSource FlattenOnWhite(BitmapSource source)
        {
            if (source.Format != PixelFormats.Bgra32 && source.Format != PixelFormats.Pbgra32) return source;
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                var rect = new Rect(0, 0, source.PixelWidth, source.PixelHeight);
                dc.DrawRectangle(System.Windows.Media.Brushes.White, null, rect);
                dc.DrawImage(source, rect);
            }
            var rtb = new RenderTargetBitmap(source.PixelWidth, source.PixelHeight, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }

        public static Bitmap ToGdi(BitmapSource source)
        {
            source = FlattenOnWhite(source);
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
            enc.Frames.Add(BitmapFrame.Create(ext == ".png" ? source : new FormatConvertedBitmap(FlattenOnWhite(source), PixelFormats.Bgr24, null, 0)));
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
