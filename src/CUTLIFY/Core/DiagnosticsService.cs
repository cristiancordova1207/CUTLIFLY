using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using Cutlify.Recording;
using Cutlify.Settings;
using Cutlify.Update;

namespace Cutlify.Core
{
    /// <summary>Informe local del sistema (no se envía a ningún sitio).</summary>
    public static class DiagnosticsService
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public int dwLength, dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplayDevices(string device, int index, ref DISPLAY_DEVICE dd, int flags);
        [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);

        public static string Run()
        {
            var sb = new StringBuilder();
            void Line(string k, string v) => sb.AppendLine($"{k}: {v}");
            Line("CUTLIFY", UpdateService.CurrentVersionText);
            Line("Windows", $"{RegistryString(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName")} (build {Environment.OSVersion.Version.Build}.{RegistryString(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "UBR")})");
            Line("CPU", RegistryString(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString")?.Trim() + $" · {Environment.ProcessorCount} hilos");
            var mem = new MEMORYSTATUSEX { dwLength = Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (GlobalMemoryStatusEx(ref mem)) Line("RAM", $"{mem.ullTotalPhys / 1024d / 1024 / 1024:0.#} GB");
            Line("GPU", string.Join("; ", Gpus()));
            foreach (var s in System.Windows.Forms.Screen.AllScreens)
                Line("Monitor", $"{s.DeviceName.TrimStart('\\', '.')} {s.Bounds.Width}×{s.Bounds.Height} · {Native.DpiScaleAt(s.Bounds.X + 1, s.Bounds.Y + 1) * 100:0}%{(s.Primary ? " (principal)" : "")}");
            Line("Frecuencia máxima", $"{VideoSettings.MaxRefreshRate()} Hz");
            foreach (var c in VideoSettings.DetectCodecs(refresh: true)) Line("Codec", c.ToString());
            Line("Audio del sistema", string.Join("; ", AudioCapture.ListDevices(false).Select(d => d.Name).DefaultIfEmpty("ninguno")));
            Line("Micrófonos", string.Join("; ", AudioCapture.ListDevices(true).Select(d => d.Name).DefaultIfEmpty("ninguno")));
            Line("Permiso de micrófono (CUTLIFY)", MicConsentLabel(SettingsService.Current.MicrophoneConsent));
            Line("Permiso de micrófono (Windows)", WindowsMicrophoneAccess());
            Line("OCR local", OcrService.IsAvailable ? "disponible" : "sin idioma de OCR instalado");
            Line("Datos", AppPaths.Root);
            return sb.ToString().TrimEnd();
        }

        public static string MicConsentLabel(MicConsent c) => c switch
        {
            MicConsent.Granted => "Permitido",
            MicConsent.Denied => "Denegado («Ahora no»)",
            _ => "Aún no solicitado"
        };

        /// <summary>Estado del acceso al micrófono para apps de escritorio según la privacidad de Windows.</summary>
        public static string WindowsMicrophoneAccess()
        {
            const string key = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone";
            var global = Registry.GetValue(@"HKEY_CURRENT_USER\" + key, "Value", null) as string;
            var desktop = Registry.GetValue(@"HKEY_CURRENT_USER\" + key + @"\NonPackaged", "Value", null) as string;
            if (global == "Deny") return "Bloqueado (acceso al micrófono desactivado)";
            if (desktop == "Deny") return "Bloqueado para aplicaciones de escritorio";
            return "Permitido";
        }

        private static string[] Gpus()
        {
            var list = new System.Collections.Generic.List<string>();
            var dd = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            for (int i = 0; EnumDisplayDevices(null, i, ref dd, 0); i++)
            {
                if (!list.Contains(dd.DeviceString)) list.Add(dd.DeviceString);
                dd.cb = Marshal.SizeOf<DISPLAY_DEVICE>();
            }
            return list.Count == 0 ? new[] { "desconocida" } : list.ToArray();
        }

        private static string RegistryString(string path, string name)
        {
            try { return Registry.LocalMachine.OpenSubKey(path)?.GetValue(name)?.ToString(); }
            catch { return null; }
        }
    }
}
