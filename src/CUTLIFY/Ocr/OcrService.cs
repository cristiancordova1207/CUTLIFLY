using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using Cutlify.Capture;
using WpfImaging = System.Windows.Media.Imaging;

namespace Cutlify
{
    /// <summary>OCR local con Windows.Media.Ocr (incluido en Windows 10/11, sin servicios externos).</summary>
    public static class OcrService
    {
        public static bool IsAvailable
        {
            get
            {
                try { return OcrEngine.TryCreateFromUserProfileLanguages() != null; }
                catch { return false; }
            }
        }

        public static async Task<string> RecognizeAsync(WpfImaging.BitmapSource image)
        {
            var engine = OcrEngine.TryCreateFromUserProfileLanguages()
                ?? throw new InvalidOperationException("Windows no tiene ningún idioma con reconocimiento de texto (OCR) instalado. Añádelo en Configuración → Hora e idioma → Idioma.");

            var source = ImageUtil.FlattenOnWhite(image);
            uint max = OcrEngine.MaxImageDimension;
            double scale = Math.Min(1.0, Math.Min(max / (double)source.PixelWidth, max / (double)source.PixelHeight));
            if (scale < 1)
            {
                var t = new WpfImaging.TransformedBitmap(source, new System.Windows.Media.ScaleTransform(scale, scale));
                t.Freeze();
                source = t;
            }

            var png = ImageUtil.EncodePng(source);
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream))
            {
                writer.WriteBytes(png);
                await writer.StoreAsync();
                await writer.FlushAsync();
                writer.DetachStream();
            }
            stream.Seek(0);
            var decoder = await BitmapDecoder.CreateAsync(stream);
            using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            var result = await engine.RecognizeAsync(bitmap);
            var text = string.Join(Environment.NewLine, result.Lines.Select(l => l.Text));
            return string.IsNullOrWhiteSpace(text) ? "" : text;
        }
    }
}
