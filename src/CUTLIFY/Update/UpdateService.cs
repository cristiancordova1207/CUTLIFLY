using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cutlify.Core;

namespace Cutlify.Update
{
    public sealed class ReleaseInfo
    {
        public Version Version;
        public string Tag;
        public string Notes;
        public string HtmlUrl;
        public string ExeUrl;
        public long ExeSize;
        public string SetupUrl;
        public long SetupSize;
        public string ChecksumsUrl;
    }

    /// <summary>
    /// Actualizaciones desde las Releases oficiales de GitHub de CUTLIFY.
    /// Solo se aceptan descargas de github.com/{Repository}/releases/download/, se exige que la
    /// versión sea mayor que la instalada y se verifica el SHA-256 publicado en SHA256SUMS.txt.
    /// </summary>
    public static class UpdateService
    {
        /// <summary>
        /// Repositorio PÚBLICO que solo contiene las Releases oficiales (binarios). El código fuente vive en un
        /// repositorio privado; se fija en compilación (propiedad MSBuild ReleasesRepository) y no es configurable por el usuario.
        /// </summary>
        public static string Repository { get; } =
            Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == "ReleasesRepository")?.Value is { Length: > 0 } r ? r : "cristiancordova1207/cutlify-releases";
        public const string ExeAsset = "CUTLIFY.exe";
        public const string SetupAsset = "CUTLIFY-Setup.exe";
        public const string ChecksumsAsset = "SHA256SUMS.txt";

        public static string ReleasesPage => $"https://github.com/{Repository}/releases";
        private static string LatestApi => $"https://api.github.com/repos/{Repository}/releases/latest";
        private static string DownloadPrefix => $"https://github.com/{Repository}/releases/download/";

        public static Version CurrentVersion
        {
            get
            {
                var info = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                return ParseVersion(info) ?? Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
            }
        }

        public static string CurrentVersionText => $"{CurrentVersion.Major}.{CurrentVersion.Minor}.{Math.Max(0, CurrentVersion.Build)}";

        public static Version ParseVersion(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            text = text.Trim().TrimStart('v', 'V');
            int cut = text.IndexOfAny(new[] { '+', '-', ' ' });
            if (cut >= 0) text = text.Substring(0, cut);
            if (!Version.TryParse(text, out var v)) return null;
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }

        public static bool IsNewer(Version remote, Version local) => remote != null && local != null && remote > local;

        private static HttpClient CreateClient()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd($"CUTLIFY/{CurrentVersionText}");
            c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return c;
        }

        /// <summary>Consulta la última Release. Devuelve null si no hay versión más reciente.</summary>
        public static async Task<ReleaseInfo> CheckAsync(CancellationToken ct = default)
        {
            using var client = CreateClient();
            using var resp = await client.GetAsync(LatestApi, ct).ConfigureAwait(false);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null; // aún no hay releases
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            var root = doc.RootElement;
            if (root.TryGetProperty("draft", out var d) && d.GetBoolean()) return null;
            if (root.TryGetProperty("prerelease", out var p) && p.GetBoolean()) return null;

            var info = new ReleaseInfo
            {
                Tag = root.GetProperty("tag_name").GetString(),
                Notes = root.TryGetProperty("body", out var b) ? b.GetString() : "",
                HtmlUrl = root.TryGetProperty("html_url", out var h) ? h.GetString() : ReleasesPage
            };
            info.Version = ParseVersion(info.Tag);
            foreach (var a in root.GetProperty("assets").EnumerateArray())
            {
                var name = a.GetProperty("name").GetString();
                var url = a.GetProperty("browser_download_url").GetString();
                long size = a.GetProperty("size").GetInt64();
                if (!IsOfficialUrl(url)) continue;
                if (name == ExeAsset) { info.ExeUrl = url; info.ExeSize = size; }
                else if (name == SetupAsset) { info.SetupUrl = url; info.SetupSize = size; }
                else if (name == ChecksumsAsset) info.ChecksumsUrl = url;
            }
            return IsNewer(info.Version, CurrentVersion) ? info : null;
        }

        public static bool IsOfficialUrl(string url) =>
            url != null && url.StartsWith(DownloadPrefix, StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == "https";

        /// <summary>¿Se instaló con CUTLIFY-Setup.exe? (el desinstalador de Inno Setup está junto al exe)</summary>
        public static bool IsInstalled =>
            File.Exists(Path.Combine(Path.GetDirectoryName(Environment.ProcessPath) ?? "", "unins000.exe"));

        public static string ParseChecksum(string sums, string fileName)
        {
            foreach (var line in sums.Split('\n'))
            {
                var parts = line.Trim().Split(new[] { ' ', '\t', '*' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && parts[1].Equals(fileName, StringComparison.OrdinalIgnoreCase) && parts[0].Length == 64)
                    return parts[0].ToLowerInvariant();
            }
            return null;
        }

        /// <summary>Descarga y verifica la actualización. Devuelve la ruta del archivo verificado.</summary>
        public static async Task<string> DownloadAsync(ReleaseInfo release, IProgress<double> progress, CancellationToken ct = default)
        {
            bool useSetup = IsInstalled && release.SetupUrl != null;
            string url = useSetup ? release.SetupUrl : release.ExeUrl;
            string name = useSetup ? SetupAsset : ExeAsset;
            long expectedSize = useSetup ? release.SetupSize : release.ExeSize;
            if (url == null) throw new InvalidOperationException("La Release no contiene el archivo de CUTLIFY.");
            if (release.ChecksumsUrl == null) throw new InvalidOperationException("La Release no incluye SHA256SUMS.txt; no se puede verificar la descarga.");
            if (!IsOfficialUrl(url) || !IsOfficialUrl(release.ChecksumsUrl)) throw new InvalidOperationException("URL de descarga no oficial.");

            using var client = CreateClient();
            var sums = await client.GetStringAsync(release.ChecksumsUrl, ct).ConfigureAwait(false);
            var expectedHash = ParseChecksum(sums, name) ?? throw new InvalidOperationException("No hay hash para " + name);

            var dir = AppPaths.Updates;
            foreach (var old in Directory.GetFiles(dir)) { try { File.Delete(old); } catch { } }
            var target = Path.Combine(dir, $"{Path.GetFileNameWithoutExtension(name)}-{release.Version}.exe");
            var part = target + ".part";

            using (var resp = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                resp.EnsureSuccessStatusCode();
                long total = resp.Content.Headers.ContentLength ?? expectedSize;
                using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var dst = File.Create(part);
                var buffer = new byte[81920];
                long read = 0;
                int n;
                while ((n = await src.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
                {
                    await dst.WriteAsync(buffer, 0, n, ct).ConfigureAwait(false);
                    read += n;
                    if (total > 0) progress?.Report(read * 100.0 / total);
                }
            }

            var fi = new FileInfo(part);
            if (expectedSize > 0 && fi.Length != expectedSize) throw new InvalidOperationException("La descarga está incompleta.");
            string hash;
            using (var fs = File.OpenRead(part)) hash = Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
            if (hash != expectedHash) { File.Delete(part); throw new InvalidOperationException("La verificación SHA-256 falló. La descarga se descartó."); }

            File.Move(part, target, true);
            if (!useSetup)
            {
                var fv = ParseVersion(FileVersionInfo.GetVersionInfo(target).ProductVersion);
                if (fv == null || fv != release.Version) throw new InvalidOperationException("El archivo descargado no corresponde a la versión esperada.");
            }
            return target;
        }

        /// <summary>Instala la actualización y cierra CUTLIFY. La configuración en %LOCALAPPDATA% no se toca.</summary>
        public static void InstallAndRestart(string downloaded)
        {
            var current = Environment.ProcessPath;
            if (Path.GetFileName(downloaded).StartsWith("CUTLIFY-Setup", StringComparison.OrdinalIgnoreCase))
            {
                // El instalador reemplaza los archivos y vuelve a abrir CUTLIFY (entrada [Run] para modo silencioso).
                Process.Start(new ProcessStartInfo(downloaded, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS") { UseShellExecute = true });
            }
            else
            {
                int pid = Environment.ProcessId;
                var script = Path.Combine(AppPaths.Updates, "update.cmd");
                var sb = new StringBuilder();
                sb.AppendLine("@echo off");
                sb.AppendLine(":wait");
                sb.AppendLine($"tasklist /FI \"PID eq {pid}\" 2>nul | find \"{pid}\" >nul && (timeout /t 1 /nobreak >nul & goto wait)");
                sb.AppendLine("set n=0");
                sb.AppendLine(":copy");
                sb.AppendLine($"copy /y \"{downloaded}\" \"{current}\" >nul && goto ok");
                sb.AppendLine("set /a n+=1");
                sb.AppendLine("if %n% geq 20 goto fail");
                sb.AppendLine("timeout /t 1 /nobreak >nul");
                sb.AppendLine("goto copy");
                sb.AppendLine(":ok");
                sb.AppendLine($"del \"{downloaded}\" >nul 2>&1");
                sb.AppendLine($"start \"\" \"{current}\" --updated");
                sb.AppendLine("goto end");
                sb.AppendLine(":fail");
                sb.AppendLine($"start \"\" \"{current}\"");
                sb.AppendLine(":end");
                sb.AppendLine("(goto) 2>nul & del \"%~f0\"");
                File.WriteAllText(script, sb.ToString(), Encoding.Default);
                Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{script}\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                });
            }
            Logger.Info("Update", "Instalando actualización y reiniciando");
        }
    }
}
