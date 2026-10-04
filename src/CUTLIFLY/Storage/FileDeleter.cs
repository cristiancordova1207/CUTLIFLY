using System;
using System.IO;
using Microsoft.VisualBasic.FileIO;
using Cutlifly.Core;

namespace Cutlifly.Storage
{
    public static class FileDeleter
    {
        /// <summary>Elimina un archivo enviándolo a la Papelera (por defecto) o de forma permanente.</summary>
        public static bool Delete(string path, bool recycle)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return true;
            try
            {
                if (recycle)
                    FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, UICancelOption.DoNothing);
                else
                    File.Delete(path);
                return !File.Exists(path);
            }
            catch (Exception ex)
            {
                Logger.Warn("Storage", $"No se pudo eliminar {Path.GetFileName(path)}", ex);
                return false;
            }
        }
    }
}
