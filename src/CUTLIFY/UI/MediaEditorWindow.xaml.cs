using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Cutlify.Core;
using Cutlify.Recording;

namespace Cutlify.UI
{
    /// <summary>
    /// Editores de audio (el de MicLY) y de vídeo (el de CaptLY) dentro de CUTLIFY.
    /// La interfaz es la misma página HTML de esos programas (EditorsWeb/, recursos incrustados) en un WebView2;
    /// este archivo hace de "proceso principal": abre/guarda archivos y ejecuta ffmpeg para el vídeo.
    /// </summary>
    public partial class MediaEditorWindow : Window
    {
        public enum EditorKind { Audio, Video }

        private const string Origin = "https://app.cutlify/";
        private const int ChunkMax = 8 << 20; // respuestas parciales de 8 MB para el <video>

        private readonly AppController _app;
        public EditorKind Kind { get; }
        private readonly HashSet<string> _allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private string _pendingFile;
        private bool _ready;
        private System.Diagnostics.Process _render;

        public MediaEditorWindow(AppController app, EditorKind kind)
        {
            _app = app;
            Kind = kind;
            InitializeComponent();
            bool audio = kind == EditorKind.Audio;
            Title = (audio ? "Editor de audio" : "Editor de vídeo") + " — CUTLIFY";
            TitleText.Text = audio ? "Editor de audio" : "Editor de vídeo";
            TitleIcon.Text = audio ? "" : "";
            SubtitleText.Text = audio ? "·  de MicLY" : "·  de CaptLY";

            SourceInitialized += (_, __) => Native.RoundCorners(new WindowInteropHelper(this).Handle);
            MinButton.Click += (_, __) => WindowState = WindowState.Minimized;
            MaxButton.Click += (_, __) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            CloseButton.Click += (_, __) => Close();
            StateChanged += (_, __) =>
            {
                RootGrid.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
                MaxButton.Content = WindowState == WindowState.Maximized ? "" : "";
            };
            ThemeManager.Changed += SendTheme;
            Closed += (_, __) =>
            {
                ThemeManager.Changed -= SendTheme;
                try { if (_render != null && !_render.HasExited) _render.Kill(true); } catch { }
                Web.Dispose();
            };
            Loaded += async (_, __) => await InitAsync();
        }

        /// <summary>Abre un archivo en el editor (si la página aún carga, se abre al terminar).</summary>
        public void OpenFile(string file)
        {
            if (string.IsNullOrEmpty(file)) return;
            _allowed.Add(file);
            if (_ready) Send("open", file); else _pendingFile = file;
        }

        private async Task InitAsync()
        {
            try
            {
                var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(AppPaths.Root, "WebView2"));
                await Web.EnsureCoreWebView2Async(env);
            }
            catch (Exception ex)
            {
                Logger.Error("Editor", "No se pudo iniciar WebView2", ex);
                Web.Visibility = Visibility.Collapsed;
                ErrorText.Visibility = Visibility.Visible;
                ErrorText.Text = "El editor necesita Microsoft Edge WebView2 Runtime (viene con Windows 11).\nInstálalo desde https://go.microsoft.com/fwlink/p/?LinkId=2124703 y vuelve a abrir el editor.";
                return;
            }
            var core = Web.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
#if !DEBUG
            core.Settings.AreDevToolsEnabled = false;
#endif
            core.AddWebResourceRequestedFilter(Origin + "*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += OnResource;
            core.WebMessageReceived += OnMessage;
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.NavigationCompleted += (_, __) =>
            {
                _ready = true;
                SendTheme();
                if (_pendingFile != null) { Send("open", _pendingFile); _pendingFile = null; }
            };
            core.Navigate(Origin + (Kind == EditorKind.Audio ? "audio.html" : "video.html"));
        }

        // ---------- archivos servidos a la página ----------
        // https://app.cutlify/audio.html, editor.css… → recursos incrustados (EditorsWeb/).
        // https://app.cutlify/file?p=<ruta> → archivo del usuario o de caché, con rangos para que el <video> pueda saltar.
        // Todo en el mismo origen: así Web Audio puede leer el sonido del <video> (createMediaElementSource).
        private void OnResource(object sender, CoreWebView2WebResourceRequestedEventArgs e)
        {
            var env = Web.CoreWebView2.Environment;
            try
            {
                var uri = new Uri(e.Request.Uri);
                var name = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
                if (name == "file")
                {
                    var q = uri.Query.TrimStart('?').Split('&').FirstOrDefault(x => x.StartsWith("p="));
                    var path = q == null ? null : Uri.UnescapeDataString(q.Substring(2));
                    if (path == null || !_allowed.Contains(path) || !File.Exists(path)) { e.Response = env.CreateWebResourceResponse(null, 404, "Not Found", ""); return; }
                    var range = e.Request.Headers.Contains("Range") ? e.Request.Headers.GetHeader("Range") : null;
                    e.Response = FileResponse(env, path, range);
                    return;
                }
                var res = Assembly.GetExecutingAssembly().GetManifestResourceStream("web/" + name);
                e.Response = res == null
                    ? env.CreateWebResourceResponse(null, 404, "Not Found", "")
                    : env.CreateWebResourceResponse(res, 200, "OK", $"Content-Type: {Mime(name)}\r\nCache-Control: no-cache");
            }
            catch (Exception ex)
            {
                Logger.Warn("Editor", "No se pudo servir " + e.Request.Uri, ex);
                e.Response = env.CreateWebResourceResponse(null, 500, "Error", "");
            }
        }

        private static CoreWebView2WebResourceResponse FileResponse(CoreWebView2Environment env, string path, string range)
        {
            var mime = Mime(path);
            var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            long len = fs.Length;
            if (range != null && range.StartsWith("bytes="))
            {
                var se = range.Substring(6).Split(',')[0].Split('-');
                long a = se[0].Length > 0 ? long.Parse(se[0], CultureInfo.InvariantCulture) : Math.Max(0, len - long.Parse(se[1], CultureInfo.InvariantCulture));
                long b = se[0].Length > 0 && se.Length > 1 && se[1].Length > 0 ? long.Parse(se[1], CultureInfo.InvariantCulture) : len - 1;
                b = Math.Min(Math.Min(b, len - 1), a + ChunkMax - 1);
                if (a >= len || b < a)
                {
                    fs.Dispose();
                    return env.CreateWebResourceResponse(null, 416, "Range Not Satisfiable", $"Content-Range: bytes */{len}");
                }
                var buf = new byte[b - a + 1];
                using (fs)
                {
                    fs.Position = a;
                    int read = 0;
                    while (read < buf.Length) { int r = fs.Read(buf, read, buf.Length - read); if (r <= 0) break; read += r; }
                }
                return env.CreateWebResourceResponse(new MemoryStream(buf), 206, "Partial Content",
                    $"Content-Type: {mime}\r\nContent-Length: {buf.Length}\r\nContent-Range: bytes {a}-{b}/{len}\r\nAccept-Ranges: bytes");
            }
            return env.CreateWebResourceResponse(fs, 200, "OK", $"Content-Type: {mime}\r\nContent-Length: {len}\r\nAccept-Ranges: bytes");
        }

        private static string Mime(string name) => Path.GetExtension(name).ToLowerInvariant() switch
        {
            ".html" => "text/html; charset=utf-8",
            ".js" => "text/javascript; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".woff2" => "font/woff2",
            ".ttf" => "font/ttf",
            ".jpg" => "image/jpeg",
            ".png" => "image/png",
            ".mp4" => "video/mp4",
            ".mov" => "video/quicktime",
            ".mkv" => "video/x-matroska",
            ".webm" => "video/webm",
            ".wav" => "audio/wav",
            ".mp3" => "audio/mpeg",
            ".m4a" or ".aac" => "audio/mp4",
            ".ogg" or ".opus" => "audio/ogg",
            ".flac" => "audio/flac",
            _ => "application/octet-stream"
        };

        // ---------- tema: los colores del estilo activo de CUTLIFY pasan a las variables CSS del editor ----------
        private void SendTheme()
        {
            if (!_ready) return;
            var p = ThemeManager.Palette;
            bool dark = ThemeManager.IsDark;
            string H(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            var font = ThemeManager.Style.Font ?? "Segoe UI";
            font = font.StartsWith("#") ? $"'{font.Substring(1)}'" : font;
            var vars = new Dictionary<string, string>
            {
                ["--bg"] = H(p.Bg), ["--panel"] = H(p.Surface), ["--panel2"] = H(p.SurfaceAlt), ["--panel3"] = H(p.Pressed),
                ["--text"] = H(p.Text), ["--muted"] = H(p.TextSecondary), ["--violet"] = H(p.Accent), ["--on-accent"] = H(p.OnAccent),
                ["--red"] = H(p.Danger), ["--grad"] = $"linear-gradient(135deg, {H(p.Gradient1)}, {H(p.Gradient2)})",
                // acentos secundarios de los editores (selección, volumen, cursor, hueco): pastel cozy de MicLY
                ["--mint"] = dark ? "#9ee6df" : "#4fb8ad", ["--pink"] = dark ? "#f6b8d1" : "#e489ad",
                ["--lime"] = dark ? "#c7eea0" : "#7fbf4a", ["--amber"] = dark ? "#f7d48b" : "#e0a93a",
                ["--font"] = font,
                ["--r1"] = $"{ThemeManager.Style.RadiusS}px", ["--r2"] = $"{ThemeManager.Style.RadiusM}px", ["--r3"] = $"{ThemeManager.Style.RadiusL}px",
            };
            Send("theme", new { vars, dark });
        }

        // ---------- mensajes con la página ----------
        private void Send(string ev, object data) => Post(new Dictionary<string, object> { ["event"] = ev, ["data"] = data });

        private void Post(object msg)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => Post(msg))); return; }
            try { Web.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(msg)); } catch (ObjectDisposedException) { }
        }

        private async void OnMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            int id = 0;
            try
            {
                using var doc = JsonDocument.Parse(e.WebMessageAsJson);
                var root = doc.RootElement;
                id = root.GetProperty("id").GetInt32();
                var cmd = root.GetProperty("cmd").GetString();
                var args = root.TryGetProperty("args", out var a) ? a.EnumerateArray().Select(x => x.Clone()).ToArray() : Array.Empty<JsonElement>();
                var result = await Handle(cmd, args);
                Post(new Dictionary<string, object> { ["id"] = id, ["result"] = result });
            }
            catch (Exception ex)
            {
                Logger.Warn("Editor", "Error en el editor", ex);
                Post(new Dictionary<string, object> { ["id"] = id, ["error"] = ex.Message });
            }
        }

        private async Task<object> Handle(string cmd, JsonElement[] a)
        {
            string S(int i) => a.Length > i && a[i].ValueKind == JsonValueKind.String ? a[i].GetString() : null;
            switch (cmd)
            {
                case "openRecorder": _app.ShowAudioRecorder(); return null;
                case "saveXml": return SaveXml(S(0), S(1));
                case "audio.pick":
                    return Pick("Abrir audio para editar", AppPaths.AudioFolder, "Audio|*.wav;*.mp3;*.m4a;*.aac;*.ogg;*.opus;*.flac;*.wma|Todos los archivos|*.*");
                case "audio.info": return await AudioInfo(Allowed(S(0)));
                case "audio.save": return SaveAudio(Allowed(S(0)), S(1), a.Length > 2 && a[2].ValueKind == JsonValueKind.True);
                case "video.pick":
                    return Pick("Abrir vídeo para editar", AppPaths.RecordingsFolder, "Vídeo|*.mp4;*.mkv;*.mov;*.webm|Todos los archivos|*.*");
                case "video.probe": return await ProbeVideo(Allowed(S(0)));
                case "video.thumbs": return await Thumbs(Allowed(S(0)), a[1].GetDouble());
                case "video.wave": return await Wave(Allowed(S(0)));
                case "video.render": return await Render(a[0], a[1]);
                default: throw new InvalidOperationException("Orden desconocida: " + cmd);
            }
        }

        // Solo se sirven/leen archivos que el usuario abrió o que generó el propio editor.
        private string Allowed(string path)
        {
            if (string.IsNullOrEmpty(path) || !_allowed.Contains(path)) throw new UnauthorizedAccessException("Archivo no permitido");
            if (!File.Exists(path)) throw new FileNotFoundException("El archivo ya no existe");
            return path;
        }

        private static string FileUrl(string path) => Origin + "file?p=" + Uri.EscapeDataString(path);

        private string Pick(string title, string folder, string filter)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = title + " — CUTLIFY", InitialDirectory = folder, Filter = filter };
            if (dlg.ShowDialog(this) != true) return null;
            _allowed.Add(dlg.FileName);
            return dlg.FileName;
        }

        private string SaveXml(string defaultPath, string xml)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Exportar a Premiere Pro — CUTLIFY",
                InitialDirectory = Path.GetDirectoryName(defaultPath),
                FileName = Path.GetFileName(defaultPath),
                Filter = "XML de Premiere / Final Cut Pro 7 (*.xml)|*.xml",
                AddExtension = true
            };
            var target = AskSave(dlg);
            if (target == null) return null;
            File.WriteAllText(target, xml, new UTF8Encoding(false));
            AppController.OpenLocationPath(target);
            return target;
        }

        // CUTLIFY_EDITOR_OUT (carpeta): solo para pruebas automáticas, guarda sin diálogo (como FLUXY_*_OUT en CaptLY/MicLY).
        private string AskSave(Microsoft.Win32.SaveFileDialog dlg)
        {
            var test = Environment.GetEnvironmentVariable("CUTLIFY_EDITOR_OUT");
            if (!string.IsNullOrEmpty(test)) return Path.Combine(AppPaths.Ensure(test), dlg.FileName);
            return dlg.ShowDialog(this) == true ? dlg.FileName : null;
        }

        // ---------- audio ----------
        private async Task<object> AudioInfo(string path)
        {
            int rate = 0, channels = 0;
            try
            {
                var ff = Ffmpeg.Find();
                if (ff != null)
                {
                    var j = await Ffmpeg.Run(Ffmpeg.Probe(ff), "-v", "error", "-print_format", "json", "-show_streams", "-select_streams", "a:0", path);
                    using var doc = JsonDocument.Parse(j);
                    var s = doc.RootElement.GetProperty("streams")[0];
                    rate = int.Parse(s.GetProperty("sample_rate").GetString(), CultureInfo.InvariantCulture);
                    channels = s.GetProperty("channels").GetInt32();
                }
                else if (path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                {
                    using var r = new NAudio.Wave.WaveFileReader(path);
                    rate = r.WaveFormat.SampleRate; channels = r.WaveFormat.Channels;
                }
            }
            catch (Exception ex) { Logger.Warn("Editor", "No se pudo leer el formato del audio", ex); }
            return new Dictionary<string, object> { ["path"] = path, ["name"] = Path.GetFileName(path), ["url"] = FileUrl(path), ["rate"] = rate, ["channels"] = channels };
        }

        /// <summary>Guardar: sobrescribe el WAV (o crea un .wav junto a un MP3/M4A). Guardar copia: «nombre (editado).wav».</summary>
        private string SaveAudio(string original, string base64, bool overwrite)
        {
            var bytes = Convert.FromBase64String(base64 ?? "");
            if (bytes.Length < 44) throw new InvalidDataException("Audio vacío");
            var dir = Path.GetDirectoryName(original);
            var stem = Path.GetFileNameWithoutExtension(original);
            string target = overwrite && original.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)
                ? original
                : UniquePath(dir, overwrite ? stem : stem + " (editado)", ".wav");
            _app.ReleaseMedia(target);
            File.WriteAllBytes(target, bytes);
            _allowed.Add(target);
            Logger.Info("Editor", $"Audio editado guardado: {target}");
            return target;
        }

        internal static string UniquePath(string dir, string stem, string ext)
        {
            var p = Path.Combine(dir, stem + ext);
            for (int i = 2; File.Exists(p); i++) p = Path.Combine(dir, $"{stem} {i}{ext}");
            return p;
        }

        // ---------- vídeo (mismo trabajo que editor-main.js de CaptLY) ----------
        private static string CacheDir => AppPaths.Ensure(Path.Combine(AppPaths.Root, "EditorCache"));

        private static string CacheKey(string file)
        {
            var fi = new FileInfo(file);
            using var md5 = MD5.Create();
            var h = md5.ComputeHash(Encoding.UTF8.GetBytes(file + fi.Length + fi.LastWriteTimeUtc.Ticks));
            return BitConverter.ToString(h).Replace("-", "").ToLowerInvariant();
        }

        private async Task<object> ProbeVideo(string file)
        {
            var ff = Ffmpeg.Require();
            var json = await Ffmpeg.Run(Ffmpeg.Probe(ff), "-v", "error", "-print_format", "json", "-show_streams", "-show_format", file);
            using var doc = JsonDocument.Parse(json);
            var streams = doc.RootElement.GetProperty("streams").EnumerateArray().ToList();
            var v = streams.FirstOrDefault(s => s.GetProperty("codec_type").GetString() == "video");
            if (v.ValueKind == JsonValueKind.Undefined) throw new InvalidDataException("Ese archivo no tiene vídeo");
            var au = streams.FirstOrDefault(s => s.GetProperty("codec_type").GetString() == "audio");
            var fr = (v.TryGetProperty("r_frame_rate", out var r) ? r.GetString() : "60/1").Split('/');
            double n = double.Parse(fr[0], CultureInfo.InvariantCulture), d = fr.Length > 1 ? double.Parse(fr[1], CultureInfo.InvariantCulture) : 1;
            object audio = null;
            if (au.ValueKind != JsonValueKind.Undefined)
                audio = new Dictionary<string, object>
                {
                    ["rate"] = au.TryGetProperty("sample_rate", out var sr) && int.TryParse(sr.GetString(), out var rv) ? rv : 48000,
                    ["channels"] = au.TryGetProperty("channels", out var ch) ? ch.GetInt32() : 2
                };
            return new Dictionary<string, object>
            {
                ["path"] = file, ["url"] = FileUrl(file), ["name"] = Path.GetFileName(file),
                ["duration"] = double.Parse(doc.RootElement.GetProperty("format").GetProperty("duration").GetString(), CultureInfo.InvariantCulture),
                ["fps"] = Math.Max(1, (int)Math.Round(n / (d == 0 ? 1 : d))),
                ["width"] = v.GetProperty("width").GetInt32(), ["height"] = v.GetProperty("height").GetInt32(),
                ["audio"] = audio
            };
        }

        // Tira de miniaturas: solo fotogramas clave, así es casi instantáneo.
        private async Task<object> Thumbs(string file, double duration)
        {
            var ff = Ffmpeg.Require();
            int n = (int)Math.Min(240, Math.Max(1, Math.Ceiling(duration)));
            double iv = duration / n;
            int cols = 20, rows = (int)Math.Ceiling(n / (double)cols), w = 160, h = 90;
            var outFile = Path.Combine(CacheDir, CacheKey(file) + ".jpg");
            if (!File.Exists(outFile))
                await Ffmpeg.Run(ff, "-hide_banner", "-loglevel", "error", "-y", "-skip_frame", "nokey", "-i", file, "-an",
                    "-vf", $"fps=1/{F(iv)},scale={w}:{h}:force_original_aspect_ratio=decrease,pad={w}:{h}:-1:-1,tile={cols}x{rows}",
                    "-frames:v", "1", "-q:v", "5", outFile);
            _allowed.Add(outFile);
            return new Dictionary<string, object> { ["url"] = FileUrl(outFile), ["iv"] = iv, ["n"] = n, ["cols"] = cols, ["w"] = w, ["h"] = h };
        }

        // Onda del audio: mono a 4 kHz (float32), suficiente para dibujar.
        private async Task<string> Wave(string file)
        {
            var ff = Ffmpeg.Require();
            var outFile = Path.Combine(CacheDir, CacheKey(file) + ".f32");
            if (!File.Exists(outFile))
                await Ffmpeg.Run(ff, "-hide_banner", "-loglevel", "error", "-y", "-i", file, "-vn", "-ac", "1", "-ar", "4000", "-f", "f32le", outFile);
            _allowed.Add(outFile);
            return FileUrl(outFile);
        }

        private static string F(double v, string f = "F4") => v.ToString(f, CultureInfo.InvariantCulture);

        // MP4 final con cortes, huecos en negro, fades y volumen.
        private async Task<string> Render(JsonElement info, JsonElement clipsJson)
        {
            var ff = Ffmpeg.Require();
            var src = Allowed(info.GetProperty("path").GetString());
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Exportar vídeo — CUTLIFY",
                InitialDirectory = Path.GetDirectoryName(src),
                FileName = Path.GetFileNameWithoutExtension(src) + " (editado).mp4",
                Filter = "Vídeo MP4 (*.mp4)|*.mp4",
                AddExtension = true
            };
            var outFile = AskSave(dlg);
            if (outFile == null) return null;
            int W = info.GetProperty("width").GetInt32(), H = info.GetProperty("height").GetInt32(), FPS = info.GetProperty("fps").GetInt32();
            bool hasAudio = info.TryGetProperty("audio", out var au) && au.ValueKind == JsonValueKind.Object;
            var clips = clipsJson.EnumerateArray().Select(c => new
            {
                s = c.GetProperty("s").GetDouble(), e = c.GetProperty("e").GetDouble(), t = c.GetProperty("t").GetDouble(),
                gain = c.TryGetProperty("gain", out var g) ? g.GetDouble() : 1, fi = c.GetProperty("fi").GetDouble(), fo = c.GetProperty("fo").GetDouble()
            }).OrderBy(c => c.t).ToList();
            if (clips.Count == 0) throw new InvalidOperationException("No hay clips que exportar");

            var inputs = new List<string>();
            var parts = new List<string>();
            var labels = new StringBuilder();
            double prevEnd = 0;
            int k = 0;
            void Gap(double d)
            {
                parts.Add($"color=c=black:s={W}x{H}:r={FPS}:d={F(d)},format=yuv420p[g{k}v]");
                parts.Add($"anullsrc=r=48000:cl=stereo,atrim=0:{F(d)}[g{k}a]");
                labels.Append($"[g{k}v][g{k}a]"); k++;
            }
            for (int i = 0; i < clips.Count; i++)
            {
                var c = clips[i];
                double len = c.e - c.s;
                if (c.t - prevEnd > 0.5 / FPS) Gap(c.t - prevEnd);
                inputs.AddRange(new[] { "-ss", F(c.s), "-t", F(len), "-i", src });
                var vf = new List<string> { "setpts=PTS-STARTPTS", $"fps={FPS}", $"scale={W}:{H}", "format=yuv420p" };
                if (c.fi > 0) vf.Add($"fade=t=in:st=0:d={F(c.fi, "F3")}");
                if (c.fo > 0) vf.Add($"fade=t=out:st={F(len - c.fo, "F3")}:d={F(c.fo, "F3")}");
                parts.Add($"[{i}:v]{string.Join(",", vf)},trim=duration={F(len)}[c{i}v]");
                var af = new List<string> { hasAudio ? $"[{i}:a]asetpts=PTS-STARTPTS,aresample=48000" : "anullsrc=r=48000:cl=stereo", $"volume={F(c.gain)}" };
                if (c.fi > 0) af.Add($"afade=t=in:st=0:d={F(c.fi, "F3")}");
                if (c.fo > 0) af.Add($"afade=t=out:st={F(len - c.fo, "F3")}:d={F(c.fo, "F3")}");
                parts.Add($"{string.Join(",", af)},apad,atrim=0:{F(len)}[c{i}a]");
                labels.Append($"[c{i}v][c{i}a]");
                prevEnd = c.t + len;
            }
            parts.Add($"{labels}concat=n={k + clips.Count}:v=1:a=1[vo][ao]");
            double total = prevEnd;

            Exception last = null;
            foreach (var enc in await Ffmpeg.Encoders(ff))
            {
                var args = new List<string> { "-hide_banner", "-loglevel", "error", "-y", "-progress", "pipe:1" };
                args.AddRange(inputs);
                args.AddRange(new[] { "-filter_complex", string.Join(";", parts), "-map", "[vo]", "-map", "[ao]", "-c:v", enc.Name });
                args.AddRange(enc.Args((FPS * 2).ToString(CultureInfo.InvariantCulture)));
                args.AddRange(new[] { "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart", outFile });
                try
                {
                    await Ffmpeg.RunWithProgress(ff, args, total, p => Send("editor-progress", p), proc => _render = proc);
                    Logger.Info("Editor", $"Vídeo exportado con {enc.Name}: {outFile}");
                    AppController.OpenLocationPath(outFile);
                    return outFile;
                }
                catch (Exception ex) { last = ex; Logger.Warn("Editor", $"Exportar con {enc.Name} falló", ex); }
                finally { _render = null; }
                if (!IsLoaded) break;
            }
            throw last ?? new InvalidOperationException("ffmpeg falló");
        }
    }
}
