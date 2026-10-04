using System;
using System.IO;
using Cutlify.Core;
using Cutlify.Settings;

namespace Cutlify.Tests
{
    /// <summary>Raíces aisladas (LocalAppData y Documentos) y configuración nueva para cada prueba.</summary>
    internal sealed class TestEnvironment : IDisposable
    {
        public string Local { get; }
        public string Documents { get; }

        public TestEnvironment()
        {
            var root = Path.Combine(Path.GetTempPath(), "cutlify-tests", Guid.NewGuid().ToString("N"));
            Local = Path.Combine(root, "LocalAppData", "CUTLIFY");
            Documents = Path.Combine(root, "Documents");
            Directory.CreateDirectory(Local);
            Directory.CreateDirectory(Documents);
            AppPaths.OverrideRoots(Local, Documents);
            SettingsService.Current = new AppSettings();
        }

        public string TempFile(string name) => Write(Path.Combine(AppPaths.Temp, name));
        public string UserFile(string name) => Write(Path.Combine(AppPaths.CapturesFolder, name));

        private static string Write(string path)
        {
            File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
            return path;
        }

        public void Dispose()
        {
            try { Directory.Delete(Path.GetDirectoryName(Path.GetDirectoryName(Local)), true); } catch { }
        }
    }
}
