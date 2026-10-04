using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using Cutlify.Core;
using Cutlify.Settings;

namespace Cutlify.Recording
{
    public sealed class CodecInfo
    {
        public string Name;
        public bool Available;
        public bool Hardware;
        public override string ToString() => Available ? $"{Name}{(Hardware ? " (hardware)" : " (software)")}" : $"{Name} (no disponible)";
    }

    public sealed class RecordingProfile
    {
        public string Name;
        public string Resolution;
        public int Fps;
    }

    /// <summary>Cálculo de resolución, bitrate y compatibilidad real de codecs/FPS.</summary>
    public static class VideoSettings
    {
        public static readonly string[] Resolutions = { "Automática", "720p", "1080p", "1440p", "4K" };
        public static readonly int[] FpsOptions = { 30, 60, 90, 120, 144, 165, 240 };
        public static readonly string[] Codecs = { "H.264", "HEVC", "AV1" };
        public static readonly string[] Qualities = { "Automática", "Baja", "Media", "Alta", "Muy alta", "Personalizada" };
        public static readonly int[] BitrateOptionsMbps = { 0, 10, 20, 30, 50, 80, 100 }; // 0 = automático

        public static readonly RecordingProfile[] Profiles =
        {
            new RecordingProfile { Name = "Predeterminado", Resolution = "1080p", Fps = 30 },
            new RecordingProfile { Name = "1080p 30 FPS", Resolution = "1080p", Fps = 30 },
            new RecordingProfile { Name = "1080p 60 FPS", Resolution = "1080p", Fps = 60 },
            new RecordingProfile { Name = "1080p 120 FPS", Resolution = "1080p", Fps = 120 },
            new RecordingProfile { Name = "1440p 60 FPS", Resolution = "1440p", Fps = 60 },
            new RecordingProfile { Name = "4K 60 FPS", Resolution = "4K", Fps = 60 },
        };

        public static int ResolutionHeight(string r) => r switch
        {
            "720p" => 720, "1080p" => 1080, "1440p" => 1440, "4K" => 2160, _ => 0
        };

        /// <summary>Tamaño de salida: reduce a la resolución elegida conservando la proporción. Nunca amplía.</summary>
        public static Size OutputSize(Size source, string resolution)
        {
            int w = source.Width, h = source.Height;
            int target = ResolutionHeight(resolution);
            if (target > 0 && h > target)
            {
                double scale = target / (double)h;
                w = (int)Math.Round(w * scale);
                h = target;
            }
            return new Size(Math.Max(2, w & ~1), Math.Max(2, h & ~1));
        }

        /// <summary>
        /// Bitrate automático según resolución, FPS, codec y calidad. Los FPS altos escalan de forma
        /// sublineal (fotogramas más parecidos) y HEVC/AV1 necesitan menos bits que H.264.
        /// </summary>
        public static int AutoBitrate(int w, int h, int fps, string codec, string quality)
        {
            double bpp = quality switch
            {
                "Baja" => 0.05,
                "Media" => 0.075,
                "Alta" => 0.12,
                "Muy alta" => 0.18,
                _ => 0.095 // Automática / Personalizada sin valor
            };
            double effectiveFps = 30 * Math.Pow(Math.Max(1, fps) / 30.0, 0.75);
            double codecFactor = codec switch { "HEVC" => 0.65, "AV1" => 0.55, _ => 1.0 };
            double bps = w * (double)h * effectiveFps * bpp * codecFactor;
            return (int)Math.Clamp(bps, 1_000_000, 150_000_000);
        }

        /// <summary>Bitrate final: manual si el usuario eligió un valor (Mbps), si no automático.</summary>
        public static (int bps, bool auto) ResolveBitrate(AppSettings s, Size output)
        {
            if (s.RecordBitrateMbps > 0) return (Math.Clamp(s.RecordBitrateMbps, 1, 500) * 1_000_000, false);
            return (AutoBitrate(output.Width, output.Height, s.RecordFps, s.RecordCodec, s.RecordQuality), true);
        }

        public static Guid Subtype(string codec) => codec switch
        {
            "HEVC" => MF.VideoFormat_HEVC,
            "AV1" => MF.VideoFormat_AV1,
            _ => MF.VideoFormat_H264
        };

        private static List<CodecInfo> _codecs;

        /// <summary>Codecs con codificador Media Foundation registrado en este equipo.</summary>
        public static IReadOnlyList<CodecInfo> DetectCodecs(bool refresh = false)
        {
            if (_codecs != null && !refresh) return _codecs;
            var list = new List<CodecInfo>();
            try
            {
                MF.MFStartup(MF.Version, 1);
                try
                {
                    foreach (var c in Codecs)
                    {
                        int all = MF.CountEncoders(Subtype(c), false);
                        int hw = MF.CountEncoders(Subtype(c), true);
                        list.Add(new CodecInfo { Name = c, Available = all > 0, Hardware = hw > 0 });
                    }
                }
                finally { MF.MFShutdown(); }
            }
            catch (Exception ex)
            {
                Logger.Warn("Recording", "No se pudieron enumerar los codificadores", ex);
                list = Codecs.Select(c => new CodecInfo { Name = c, Available = c == "H.264" }).ToList();
            }
            _codecs = list;
            return list;
        }

        public static bool IsCodecAvailable(string codec) => DetectCodecs().Any(c => c.Name == codec && c.Available);

        // ---- Monitores: frecuencia y resolución máximas reales ----

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
            public int dmFields;
            public int dmPositionX, dmPositionY;
            public int dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
            public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplaySettings(string device, int mode, ref DEVMODE dm);

        public static int MaxRefreshRate()
        {
            int max = 0;
            foreach (var s in System.Windows.Forms.Screen.AllScreens)
            {
                var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
                if (EnumDisplaySettings(s.DeviceName, -1, ref dm)) max = Math.Max(max, dm.dmDisplayFrequency);
            }
            return max <= 1 ? 60 : max;
        }

        public static int MaxScreenHeight() => System.Windows.Forms.Screen.AllScreens.Max(s => s.Bounds.Height);

        /// <summary>FPS que el sistema puede producir de verdad: no más que la frecuencia del monitor más rápido.</summary>
        public static IEnumerable<int> CompatibleFps(int maxRefresh) => FpsOptions.Where(f => f == 30 || f <= maxRefresh + 1);

        public static IEnumerable<string> CompatibleResolutions(int maxHeight) =>
            Resolutions.Where(r => r == "Automática" || r == "720p" || ResolutionHeight(r) <= maxHeight);

        public static bool IsProfileCompatible(RecordingProfile p, int maxRefresh, int maxHeight) =>
            CompatibleFps(maxRefresh).Contains(p.Fps) && CompatibleResolutions(maxHeight).Contains(p.Resolution);
    }
}
