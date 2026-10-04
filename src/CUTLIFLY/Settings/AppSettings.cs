using System;

namespace Cutlifly.Settings
{
    public enum CaptureMode { Rectangle, Window, FullScreen }

    /// <summary>Preferencias del usuario (persistidas en settings.json).</summary>
    public class AppSettings
    {
        // General
        public bool StartWithWindows { get; set; }
        public bool MinimizeToTray { get; set; } = true;
        public bool RunInBackground { get; set; } = true;
        public bool ShowNotifications { get; set; } = true;
        public bool OpenEditorAfterCapture { get; set; }

        // Capturas
        public string ImageFormat { get; set; } = "PNG";
        public bool AutoCopyCaptures { get; set; } = true;
        public bool SaveTemporarily { get; set; } = true;
        public CaptureMode LastMode { get; set; } = CaptureMode.Rectangle;
        public int DefaultDelaySeconds { get; set; }

        // Grabación
        public int RecordFps { get; set; } = 30;
        public string RecordQuality { get; set; } = "Media";
        public bool RecordCursor { get; set; } = true;
        public bool HardwareEncoding { get; set; } = true;

        // Portapapeles
        public bool CopyRecordingsAsFile { get; set; } = true;

        // Almacenamiento
        public int RetentionMinutes { get; set; } = 60; // 0 = nunca
        public bool SendExpiredToRecycleBin { get; set; } = true;
        public string PicturesFolder { get; set; }
        public string VideosFolder { get; set; }

        // Atajos
        public string HotkeyCapture { get; set; } = "Win+Shift+S";
        public string HotkeyRecord { get; set; } = "Win+Shift+R";
        public string HotkeyOpen { get; set; } = "Ctrl+Shift+X";

        // Apariencia: System | Light | Dark
        public string Theme { get; set; } = "System";

        // Actualizaciones: Never | Startup | Daily | Weekly
        public string UpdateCheck { get; set; } = "Startup";
        public DateTime LastUpdateCheckUtc { get; set; }
        public string SkippedVersion { get; set; }

        public TimeSpan? Retention => RetentionMinutes <= 0 ? (TimeSpan?)null : TimeSpan.FromMinutes(RetentionMinutes);
    }
}
