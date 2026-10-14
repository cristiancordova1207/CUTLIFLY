using System;
using System.Collections.Generic;

namespace Cutlify.Settings
{
    public enum CaptureMode { Rectangle, Window, FullScreen, FreeForm, AllScreens, ColorPicker }

    public enum MicConsent { Unknown, Granted, Denied }

    public enum AudioTrackKind { Microphone, System, Application }

    /// <summary>Pista de la grabadora de audio (micrófono, escritorio o una aplicación concreta).</summary>
    public class AudioTrackConfig
    {
        public AudioTrackKind Kind { get; set; }
        public string DeviceId { get; set; }     // micrófono / salida (null = predeterminado)
        public string ProcessName { get; set; }  // aplicación (p. ej. "Spotify")
        public string Name { get; set; }
        public float Gain { get; set; } = 1f;    // volumen en la mezcla (0–2)
        public bool Muted { get; set; }          // excluida de la mezcla (la pista se guarda igual)
    }

    /// <summary>Preferencias del usuario (persistidas en settings.json).</summary>
    public class AppSettings
    {
        // General
        public bool StartWithWindows { get; set; }
        public bool MinimizeToTray { get; set; } = true;
        public bool RunInBackground { get; set; } = true;   // false = salir completamente al cerrar
        public bool ShowNotifications { get; set; } = true;
        public bool OpenEditorAfterCapture { get; set; }

        // Capturas
        public string ImageFormat { get; set; } = "PNG";
        public bool AutoCopyCaptures { get; set; } = true;
        public bool SaveTemporarily { get; set; } = true;
        public CaptureMode LastMode { get; set; } = CaptureMode.Rectangle;        // última captura
        public CaptureMode LastRecordMode { get; set; } = CaptureMode.Rectangle;  // última grabación
        public string LastKind { get; set; } = "Capture";                         // Capture | Record | Audio
        public int DefaultDelaySeconds { get; set; }
        public bool AdjustBeforeCapture { get; set; }
        public bool ShowMagnifier { get; set; }
        public string AspectRatio { get; set; } = "Libre";

        // Grabación — calidad
        public string RecordProfile { get; set; } = "Predeterminado";
        public string RecordResolution { get; set; } = "1080p";   // Automática | 720p | 1080p | 1440p | 4K
        public int RecordFps { get; set; } = 30;
        public string RecordCodec { get; set; } = "H.264";        // H.264 | HEVC | AV1
        public string RecordQuality { get; set; } = "Automática"; // Automática | Baja | Media | Alta | Muy alta | Personalizada
        public int RecordBitrateMbps { get; set; }                // 0 = automático
        public bool RecordCursor { get; set; } = true;
        public bool HardwareEncoding { get; set; } = true;
        public bool HighlightClicks { get; set; }
        public bool ShowRecordingStats { get; set; }

        // Grabación — audio
        public bool RecordAudio { get; set; } = true;
        public bool RecordSystemAudio { get; set; } = true;
        public bool RecordMicrophone { get; set; } = true;
        public string SystemAudioDeviceId { get; set; }  // null = predeterminado
        public string MicrophoneDeviceId { get; set; }   // null = predeterminado
        public MicConsent MicrophoneConsent { get; set; } = MicConsent.Unknown;

        // Grabadora de audio
        public List<AudioTrackConfig> AudioTracks { get; set; } = new List<AudioTrackConfig>
        {
            new AudioTrackConfig { Kind = AudioTrackKind.System, Name = "Escritorio" },
            new AudioTrackConfig { Kind = AudioTrackKind.Microphone, Name = "Micrófono" }
        };
        public string AudioMixFormat { get; set; } = "M4A";   // M4A (AAC) | MP3 | WAV
        public bool AudioSeparateTracks { get; set; } = true; // además de la mezcla, una WAV por pista

        // Portapapeles
        public bool CopyRecordingsAsFile { get; set; } = true;

        // Almacenamiento
        public int RetentionMinutes { get; set; } = 60; // 0 = nunca
        public bool SendExpiredToRecycleBin { get; set; } = true;
        public string CapturesFolder { get; set; }      // null = Documentos\CUTLIFY\Capturas
        public string RecordingsFolder { get; set; }    // null = Documentos\CUTLIFY\Grabaciones

        // Atajos
        public string HotkeyCapture { get; set; } = "Win+Shift+S";
        public string HotkeyRecord { get; set; } = "Win+Shift+R";
        public string HotkeyOpen { get; set; } = "Ctrl+Shift+X";
        public string HotkeyRepeat { get; set; } = "Alt+N";
        public string HotkeyAudio { get; set; } = "Win+Shift+A";

        // Editor
        public List<string> RecentColors { get; set; } = new List<string>();

        // Apariencia: estilo (Classic | Cozy | Pixel | Retro | Minimal) y modo (System | Light | Dark)
        public string ThemeStyle { get; set; } = "Classic";
        public string Theme { get; set; } = "System";

        // Actualizaciones: Never | Startup | Daily | Weekly
        public string UpdateCheck { get; set; } = "Startup";
        public DateTime LastUpdateCheckUtc { get; set; }

        public TimeSpan? Retention => RetentionMinutes <= 0 ? (TimeSpan?)null : TimeSpan.FromMinutes(RetentionMinutes);

        /// <summary>Restaura los valores predeterminados conservando el consentimiento del micrófono y el historial.</summary>
        public static AppSettings Defaults(AppSettings keepFrom = null)
        {
            var d = new AppSettings();
            if (keepFrom != null) d.MicrophoneConsent = keepFrom.MicrophoneConsent;
            return d;
        }
    }
}
