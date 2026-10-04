using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cutlify.Core;

namespace Cutlify.Settings
{
    public static class SettingsService
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public static AppSettings Current { get; internal set; } = new AppSettings();

        public static event Action Changed;

        public static void Load()
        {
            try
            {
                if (File.Exists(AppPaths.SettingsFile))
                    Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsFile), Options) ?? new AppSettings();
            }
            catch (Exception ex)
            {
                Logger.Error("Settings", "Configuración dañada; se usan valores por defecto", ex);
                Current = new AppSettings();
            }
            Current.StartWithWindows = StartupManager.IsEnabled();
        }

        public static void Save()
        {
            try
            {
                var tmp = AppPaths.SettingsFile + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(Current, Options));
                File.Move(tmp, AppPaths.SettingsFile, true);
            }
            catch (Exception ex)
            {
                Logger.Error("Settings", "No se pudo guardar la configuración", ex);
            }
            Changed?.Invoke();
        }

        public static void Reset()
        {
            Current = AppSettings.Defaults(Current);
            StartupManager.SetEnabled(false);
            Save();
        }
    }
}
