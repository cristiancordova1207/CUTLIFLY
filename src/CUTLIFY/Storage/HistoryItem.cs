using System;
using System.ComponentModel;
using System.IO;
using System.Text.Json.Serialization;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Cutlify.Storage
{
    public enum ItemKind { Image, Video }

    /// <summary>Estado del archivo físico. La entrada del historial se conserva aunque el archivo ya no exista.</summary>
    public enum ItemState { Available, Deleted, Expired, Incomplete, NotSaved }

    /// <summary>Elemento del historial: captura o grabación.</summary>
    public class HistoryItem : INotifyPropertyChanged
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public ItemKind Kind { get; set; }
        public ItemState State { get; set; } = ItemState.Available;
        public string FilePath { get; set; }
        public string FileName { get; set; }
        public string ThumbPath { get; set; }
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public DateTime? StateChangedUtc { get; set; }
        public bool IsPermanent { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public long SizeBytes { get; set; }

        // Grabaciones
        public long DurationMs { get; set; }
        public int Fps { get; set; }
        public double RealFps { get; set; }
        public long DroppedFrames { get; set; }
        public string Codec { get; set; }
        public int BitrateKbps { get; set; }
        public bool BitrateAuto { get; set; }
        public string Audio { get; set; }

        /// <summary>Captura que solo existe en memoria (modo solo portapapeles).</summary>
        [JsonIgnore] public BitmapSource MemoryImage { get; set; }

        /// <summary>Archivo abierto por arrastrar/soltar: nunca se borra ni se guarda en el historial.</summary>
        [JsonIgnore] public bool IsExternal { get; set; }

        [JsonIgnore] public bool IsMemoryOnly => MemoryImage != null && string.IsNullOrEmpty(FilePath);
        [JsonIgnore] public bool HasFile => !string.IsNullOrEmpty(FilePath) && File.Exists(FilePath);
        [JsonIgnore] public bool IsAvailable => State == ItemState.Available && (HasFile || MemoryImage != null);
        [JsonIgnore] public bool IsGone => !IsAvailable;
        [JsonIgnore] public bool IsVideo => Kind == ItemKind.Video;
        [JsonIgnore] public DateTime CreatedLocal => CreatedUtc.ToLocalTime();

        [JsonIgnore]
        public string Name => !string.IsNullOrEmpty(FileName) ? FileName
            : !string.IsNullOrEmpty(FilePath) ? Path.GetFileName(FilePath)
            : $"Captura_{CreatedLocal:yyyy-MM-dd_HH-mm-ss}";

        [JsonIgnore] public string TypeLabel => IsVideo ? "Grabación" : "Captura";

        [JsonIgnore]
        public string StatusLabel => IsExternal ? "Externo" : State switch
        {
            ItemState.Deleted => "Eliminada",
            ItemState.Expired => "Expirada",
            ItemState.Incomplete => "Grabación incompleta",
            ItemState.NotSaved => "No guardada",
            _ => IsPermanent ? "Guardada" : IsMemoryOnly ? "Solo portapapeles" : "Temporal"
        };

        /// <summary>Icono del estado (el estado nunca se comunica solo con color).</summary>
        [JsonIgnore]
        public string StatusGlyph => State switch
        {
            ItemState.Deleted => "",
            ItemState.Expired => "",
            ItemState.Incomplete => "",
            ItemState.NotSaved => "",
            _ => IsPermanent ? "" : IsMemoryOnly ? "" : ""
        };

        [JsonIgnore] public string DateLabel => CreatedLocal.ToString("dd/MM/yyyy");
        [JsonIgnore] public string TimeLabel => CreatedLocal.ToString("HH:mm");
        [JsonIgnore] public string DimensionsLabel => Width > 0 ? $"{Width} × {Height}" : "";
        [JsonIgnore] public string SizeLabel => IsMemoryOnly ? "En memoria" : SizeBytes > 0 ? FormatBytes(SizeBytes) : "";
        [JsonIgnore] public string DurationLabel => IsVideo ? FormatDuration(TimeSpan.FromMilliseconds(DurationMs)) : "";
        [JsonIgnore] public string FormatLabel => IsVideo ? "MP4" : (Path.GetExtension(Name).TrimStart('.').ToUpperInvariant() is { Length: > 0 } e ? e : "PNG");

        [JsonIgnore]
        public string FpsLabel => !IsVideo || Fps == 0 ? "" :
            RealFps > 0 && RealFps < Fps * 0.97 ? $"{Fps} FPS objetivo · {RealFps:0.#} FPS reales" : $"{Fps} FPS";

        [JsonIgnore]
        public string BitrateLabel => BitrateKbps <= 0 ? "" : (BitrateAuto ? "Automático · " : "") + $"{BitrateKbps / 1000.0:0.#} Mbps";

        [JsonIgnore]
        public string RelativeTime
        {
            get
            {
                var d = DateTime.UtcNow - CreatedUtc;
                if (d.TotalSeconds < 60) return "Ahora";
                if (d.TotalMinutes < 60) return $"Hace {(int)d.TotalMinutes} min";
                if (d.TotalHours < 24) return $"Hace {(int)d.TotalHours} h";
                return $"Hace {(int)d.TotalDays} d";
            }
        }

        private ImageSource _thumb;

        [JsonIgnore]
        public ImageSource Thumbnail
        {
            get
            {
                if (_thumb != null) return _thumb;
                try
                {
                    if (File.Exists(ThumbPath)) _thumb = LoadImage(ThumbPath, 320);
                    else if (MemoryImage != null) _thumb = MemoryImage;
                    else if (Kind == ItemKind.Image && HasFile) _thumb = LoadImage(FilePath, 320);
                }
                catch { }
                return _thumb;
            }
        }

        public static BitmapImage LoadImage(string path, int decodeWidth = 0)
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad; // no bloquear el archivo
            bi.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            if (decodeWidth > 0) bi.DecodePixelWidth = decodeWidth;
            bi.UriSource = new Uri(path);
            bi.EndInit();
            bi.Freeze();
            return bi;
        }

        public static string FormatBytes(long b)
        {
            if (b <= 0) return "0 KB";
            if (b < 1024 * 1024) return $"{Math.Max(1, b / 1024)} KB";
            if (b < 1024L * 1024 * 1024) return $"{b / 1024d / 1024d:0.#} MB";
            return $"{b / 1024d / 1024d / 1024d:0.##} GB";
        }

        public static string FormatDuration(TimeSpan t) =>
            t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"mm\:ss");

        public void InvalidateThumbnail()
        {
            _thumb = null;
            Raise(nameof(Thumbnail));
        }

        /// <summary>Refresco periódico del tiempo relativo.</summary>
        public void Refresh() => Raise(nameof(RelativeTime));

        /// <summary>Notifica un cambio de estado/archivo (eliminada, expirada, guardada…).</summary>
        public void NotifyStateChanged()
        {
            foreach (var p in new[] { nameof(StatusLabel), nameof(StatusGlyph), nameof(Name), nameof(SizeLabel),
                                      nameof(DurationLabel), nameof(IsAvailable), nameof(IsGone), nameof(State) })
                Raise(p);
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
