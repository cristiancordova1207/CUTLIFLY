using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using Cutlifly.Core;

namespace Cutlifly.Capture
{
    /// <summary>Captura GDI en píxeles físicos (el proceso es PerMonitorV2).</summary>
    public static class ScreenCapture
    {
        public static Rectangle VirtualScreen => SystemInformation.VirtualScreen;

        public static Bitmap CaptureVirtualScreen() => CaptureRegion(VirtualScreen);

        public static Bitmap CaptureRegion(Rectangle r)
        {
            if (r.Width <= 0 || r.Height <= 0) throw new ArgumentException("Región vacía");
            var bmp = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppRgb);
            using (var g = Graphics.FromImage(bmp))
            {
                var hdcDest = g.GetHdc();
                var hdcSrc = Native.GetDC(IntPtr.Zero);
                try
                {
                    if (!Native.BitBlt(hdcDest, 0, 0, r.Width, r.Height, hdcSrc, r.X, r.Y, Native.SRCCOPY | Native.CAPTUREBLT))
                        throw new InvalidOperationException("BitBlt falló");
                }
                finally
                {
                    Native.ReleaseDC(IntPtr.Zero, hdcSrc);
                    g.ReleaseHdc(hdcDest);
                }
            }
            return bmp;
        }

        public static Bitmap Crop(Bitmap source, Rectangle r)
        {
            r.Intersect(new Rectangle(0, 0, source.Width, source.Height));
            if (r.Width <= 0 || r.Height <= 0) throw new ArgumentException("Región fuera de la pantalla");
            return source.Clone(r, PixelFormat.Format32bppRgb);
        }
    }
}
