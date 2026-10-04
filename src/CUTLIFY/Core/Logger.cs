using System;
using System.IO;
using System.Linq;

namespace Cutlify.Core
{
    /// <summary>Log local de errores. Nunca registra contenido de capturas.</summary>
    public static class Logger
    {
        private static readonly object Sync = new object();

        public static void Info(string area, string message) => Write("INFO", area, message, null);
        public static void Warn(string area, string message, Exception ex = null) => Write("WARN", area, message, ex);
        public static void Error(string area, string message, Exception ex = null) => Write("ERROR", area, message, ex);

        private static void Write(string level, string area, string message, Exception ex)
        {
            try
            {
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] [{area}] {message}";
                if (ex != null) line += Environment.NewLine + "    " + ex.GetType().Name + ": " + ex;
                var file = Path.Combine(AppPaths.Logs, $"cutlify-{DateTime.Now:yyyyMMdd}.log");
                lock (Sync) File.AppendAllText(file, line + Environment.NewLine);
            }
            catch
            {
                // El log nunca debe tirar la aplicación.
            }
        }

        public static void PurgeOld(int days = 14)
        {
            try
            {
                foreach (var f in new DirectoryInfo(AppPaths.Logs).GetFiles("cutlify-*.log")
                             .Where(f => f.LastWriteTimeUtc < DateTime.UtcNow.AddDays(-days)))
                    f.Delete();
            }
            catch (Exception ex)
            {
                Warn("Logs", "No se pudieron purgar logs antiguos", ex);
            }
        }
    }
}
