using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Cutlify.Recording
{
    /// <summary>ffmpeg del sistema para el editor de vídeo (igual que CaptLY: PATH o el paquete de winget).</summary>
    public static class Ffmpeg
    {
        public sealed class Encoder
        {
            public string Name;
            public Func<string, string[]> Args; // recibe el GOP
        }

        // Codificadores por hardware primero (AMD, NVIDIA, Intel); libx264 si no hay ninguno o fallan.
        // Calidad constante como en CaptLY (CQP 15, preajuste "Calidad"). H.264 para que el MP4 se abra en cualquier sitio.
        private const string QP = "15";
        private static readonly Encoder[] All =
        {
            new Encoder { Name = "h264_amf", Args = g => new[] { "-usage", "transcoding", "-quality", "quality", "-rc", "cqp", "-qp_i", QP, "-qp_p", QP, "-qp_b", QP, "-g", g } },
            new Encoder { Name = "h264_nvenc", Args = g => new[] { "-preset", "p5", "-tune", "hq", "-rc", "constqp", "-qp", QP, "-g", g } },
            new Encoder { Name = "h264_qsv", Args = g => new[] { "-preset", "medium", "-global_quality", QP, "-g", g } },
            new Encoder { Name = "libx264", Args = g => new[] { "-preset", "veryfast", "-crf", "18", "-g", g } },
        };

        private static string _found;

        public static string Find()
        {
            if (_found != null && File.Exists(_found)) return _found;
            var cands = new List<string>();
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
                if (dir.Trim().Length > 0) cands.Add(Path.Combine(dir.Trim().Trim('"'), "ffmpeg.exe"));
            var wg = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Packages");
            try
            {
                foreach (var d in Directory.GetDirectories(wg).Where(d => Path.GetFileName(d).IndexOf("ffmpeg", StringComparison.OrdinalIgnoreCase) >= 0))
                    foreach (var b in Directory.GetDirectories(d)) cands.Add(Path.Combine(b, "bin", "ffmpeg.exe"));
            }
            catch { }
            return _found = cands.FirstOrDefault(File.Exists);
        }

        public static string Require() =>
            Find() ?? throw new FileNotFoundException("Falta ffmpeg. Instálalo con «winget install Gyan.FFmpeg» y vuelve a intentarlo.");

        public static string Probe(string ffmpeg) => Path.Combine(Path.GetDirectoryName(ffmpeg), "ffprobe.exe");

        /// <summary>Codificadores disponibles en este ffmpeg, en orden de preferencia (libx264 siempre al final).</summary>
        public static async Task<List<Encoder>> Encoders(string ffmpeg)
        {
            string list = "";
            try { list = await Run(ffmpeg, "-hide_banner", "-encoders"); } catch { }
            var r = All.Where(e => e.Name != "libx264" && Regex.IsMatch(list, $@"\b{e.Name}\b")).ToList();
            r.Add(All.Last());
            return r;
        }

        private static ProcessStartInfo Start(string exe, IEnumerable<string> args)
        {
            var psi = new ProcessStartInfo(exe)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            return psi;
        }

        private static string LastLine(string err, string fallback)
        {
            var l = (err ?? "").Trim().Split('\n').LastOrDefault()?.Trim();
            return string.IsNullOrEmpty(l) ? fallback : l;
        }

        /// <summary>Ejecuta y devuelve la salida estándar; lanza con la última línea de error si falla.</summary>
        public static async Task<string> Run(string exe, params string[] args)
        {
            using var p = Process.Start(Start(exe, args));
            var outTask = p.StandardOutput.ReadToEndAsync();
            var errTask = p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync();
            var output = await outTask; var err = await errTask;
            if (p.ExitCode != 0) throw new InvalidOperationException(LastLine(err, "ffmpeg falló"));
            return output;
        }

        /// <summary>Ejecuta ffmpeg con «-progress pipe:1» e informa del avance (0..1) según out_time_us.</summary>
        public static async Task RunWithProgress(string exe, IEnumerable<string> args, double total, Action<double> progress, Action<Process> started)
        {
            using var p = new Process { StartInfo = Start(exe, args) };
            var err = new StringBuilder();
            p.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (err) err.AppendLine(e.Data); };
            p.OutputDataReceived += (_, e) =>
            {
                if (e.Data == null || !e.Data.StartsWith("out_time_us=")) return;
                if (long.TryParse(e.Data.Substring(12), NumberStyles.Integer, CultureInfo.InvariantCulture, out var us) && total > 0)
                    progress(Math.Min(1, Math.Max(0, us / 1e6 / total)));
            };
            p.Start();
            started?.Invoke(p);
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            await p.WaitForExitAsync();
            if (p.ExitCode != 0) throw new InvalidOperationException(LastLine(err.ToString(), "ffmpeg falló"));
        }
    }
}
