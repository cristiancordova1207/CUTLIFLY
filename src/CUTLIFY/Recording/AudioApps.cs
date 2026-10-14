using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using Cutlify.Core;

namespace Cutlify.Recording
{
    public sealed class AudioApp
    {
        public string ProcessName;   // p. ej. "Spotify"
        public string Title;         // nombre para mostrar
        public int ProcessId;        // proceso raíz (incluye sus hijos)
        public bool PlayingAudio;
        public override string ToString() => PlayingAudio ? $"{Title}  ·  reproduciendo" : Title;
    }

    /// <summary>Aplicaciones abiertas que se pueden grabar por separado (con ventana o con sesión de audio).</summary>
    public static class AudioApps
    {
        public static List<AudioApp> List()
        {
            var parents = ParentMap();
            var result = new Dictionary<string, AudioApp>(StringComparer.OrdinalIgnoreCase);
            int self = Environment.ProcessId;

            void Add(Process p, bool playing)
            {
                try
                {
                    if (p.Id == self || p.Id == 0 || p.SessionId == 0) return;
                    var name = p.ProcessName;
                    int root = RootOf(p.Id, name, parents);
                    if (result.TryGetValue(name, out var existing)) { existing.PlayingAudio |= playing; return; }
                    string title = name;
                    try { title = p.MainModule?.FileVersionInfo?.FileDescription is { Length: > 0 } d ? d : name; } catch { }
                    result[name] = new AudioApp { ProcessName = name, Title = title, ProcessId = root, PlayingAudio = playing };
                }
                catch { }
            }

            // 1) Procesos con sesión de audio en el dispositivo de salida predeterminado.
            try
            {
                using var e = new MMDeviceEnumerator();
                var dev = e.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                var sessions = dev.AudioSessionManager.Sessions;
                for (int i = 0; i < sessions.Count; i++)
                {
                    var s = sessions[i];
                    if (s.IsSystemSoundsSession) continue;
                    try { using var p = Process.GetProcessById((int)s.GetProcessID); Add(p, s.State.ToString().EndsWith("Active")); } catch { }
                }
            }
            catch (Exception ex) { Logger.Warn("Audio", "No se pudieron leer las sesiones de audio", ex); }

            // 2) Aplicaciones con ventana (para elegirlas antes de que suenen).
            foreach (var p in Process.GetProcesses())
            {
                using (p)
                {
                    try { if (p.MainWindowHandle != IntPtr.Zero && !string.IsNullOrWhiteSpace(p.MainWindowTitle)) Add(p, false); } catch { }
                }
            }
            return result.Values.OrderByDescending(a => a.PlayingAudio).ThenBy(a => a.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        /// <summary>Proceso raíz con el mismo nombre (Spotify, Chrome… usan procesos hijos para el audio).</summary>
        public static int? FindRootProcess(string processName)
        {
            var parents = ParentMap();
            var procs = Process.GetProcessesByName(processName);
            try
            {
                if (procs.Length == 0) return null;
                return procs.Select(p => RootOf(p.Id, processName, parents)).GroupBy(x => x).OrderByDescending(g => g.Count()).First().Key;
            }
            finally { foreach (var p in procs) p.Dispose(); }
        }

        private static int RootOf(int pid, string name, Dictionary<int, (int parent, string exe)> parents)
        {
            int current = pid;
            for (int guard = 0; guard < 32 && parents.TryGetValue(current, out var info); guard++)
            {
                if (!parents.TryGetValue(info.parent, out var up)) break;
                if (!string.Equals(System.IO.Path.GetFileNameWithoutExtension(up.exe), name, StringComparison.OrdinalIgnoreCase)) break;
                current = info.parent;
            }
            return current;
        }

        // ---- Toolhelp: pid → (padre, exe) ----

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct PROCESSENTRY32
        {
            public int dwSize, cntUsage, th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public int th32ModuleID, cntThreads, th32ParentProcessID, pcPriClassBase, dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
        }

        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(int flags, int pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32FirstW(IntPtr snap, ref PROCESSENTRY32 e);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32NextW(IntPtr snap, ref PROCESSENTRY32 e);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);

        private static Dictionary<int, (int parent, string exe)> ParentMap()
        {
            var map = new Dictionary<int, (int, string)>();
            var snap = CreateToolhelp32Snapshot(0x2, 0);
            if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return map;
            try
            {
                var e = new PROCESSENTRY32 { dwSize = Marshal.SizeOf<PROCESSENTRY32>() };
                if (Process32FirstW(snap, ref e))
                    do { map[e.th32ProcessID] = (e.th32ParentProcessID, e.szExeFile); } while (Process32NextW(snap, ref e));
            }
            finally { CloseHandle(snap); }
            return map;
        }
    }
}
