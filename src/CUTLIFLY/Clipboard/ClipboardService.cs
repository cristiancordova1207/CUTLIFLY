using System;
using System.Collections.Specialized;
using System.IO;
using System.Windows.Media.Imaging;
using Cutlifly.Capture;
using Cutlifly.Core;
using WinForms = System.Windows.Forms;

namespace Cutlifly.Clipboard
{
    /// <summary>
    /// Portapapeles de Windows. Las imágenes se publican como CF_BITMAP/CF_DIB y "PNG"
    /// (compatibles con navegadores, Discord, Telegram, WhatsApp, Office...).
    /// Los vídeos se publican como archivo (CF_HDROP): Explorer, Discord, Telegram, WhatsApp Desktop
    /// lo aceptan con Ctrl+V; las apps que no aceptan archivos simplemente no pegarán nada.
    /// </summary>
    public static class ClipboardService
    {
        public static bool CopyImage(BitmapSource image)
        {
            try
            {
                using var gdi = ImageUtil.ToGdi(image);
                var data = new WinForms.DataObject();
                data.SetData(WinForms.DataFormats.Bitmap, true, gdi);
                data.SetData("PNG", false, new MemoryStream(ImageUtil.EncodePng(image)));
                WinForms.Clipboard.SetDataObject(data, true, 10, 60);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("Clipboard", "No se pudo copiar la imagen", ex);
                return false;
            }
        }

        public static bool CopyFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return false;
                var files = new StringCollection { path };
                var data = new WinForms.DataObject();
                data.SetFileDropList(files);
                // DROPEFFECT_COPY para que el destino copie (no mueva) el archivo.
                data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(1)));
                WinForms.Clipboard.SetDataObject(data, true, 10, 60);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("Clipboard", "No se pudo copiar el archivo", ex);
                return false;
            }
        }
    }
}
