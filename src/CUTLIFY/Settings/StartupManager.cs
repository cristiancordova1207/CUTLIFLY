using System;
using Microsoft.Win32;
using Cutlify.Core;

namespace Cutlify.Settings
{
    /// <summary>Inicio con Windows mediante HKCU\...\Run (sin permisos de administrador).</summary>
    public static class StartupManager
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "CUTLIFY";

        public static bool IsEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(ValueName) is string s && s.Length > 0;
            }
            catch { return false; }
        }

        public static void SetEnabled(bool enabled)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKey);
                if (enabled) key.SetValue(ValueName, $"\"{Environment.ProcessPath}\" --tray");
                else key.DeleteValue(ValueName, false);
            }
            catch (Exception ex)
            {
                Logger.Error("Startup", "No se pudo cambiar el inicio con Windows", ex);
            }
        }

        /// <summary>Si el exe cambió de ruta (actualización/reinstalación), corrige la entrada.</summary>
        public static void Refresh()
        {
            if (IsEnabled()) SetEnabled(true);
        }
    }
}
