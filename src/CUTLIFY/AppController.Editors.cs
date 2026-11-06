using System;
using System.IO;
using Cutlify.Storage;
using Cutlify.UI;

namespace Cutlify
{
    /// <summary>Editores de audio (de MicLY) y vídeo (de CaptLY).</summary>
    public sealed partial class AppController
    {
        private MediaEditorWindow _audioEditor, _videoEditor;

        public void ShowAudioEditor(string file = null) => ShowEditor(ref _audioEditor, MediaEditorWindow.EditorKind.Audio, file);
        public void ShowVideoEditor(string file = null) => ShowEditor(ref _videoEditor, MediaEditorWindow.EditorKind.Video, file);

        /// <summary>Abre la grabación en el editor que le toca (audio o vídeo).</summary>
        public void Edit(HistoryItem item)
        {
            if (item == null || !item.HasFile) return;
            if (item.IsAudio) ShowAudioEditor(item.FilePath);
            else if (item.IsVideo) ShowVideoEditor(item.FilePath);
        }

        private void ShowEditor(ref MediaEditorWindow w, MediaEditorWindow.EditorKind kind, string file)
        {
            if (w == null)
            {
                var win = new MediaEditorWindow(this, kind);
                win.Closed += (_, __) => { if (kind == MediaEditorWindow.EditorKind.Audio) _audioEditor = null; else _videoEditor = null; };
                win.Show();
                w = win;
            }
            if (w.WindowState == System.Windows.WindowState.Minimized) w.WindowState = System.Windows.WindowState.Normal;
            w.Activate();
            if (file != null) w.OpenFile(file);
        }

        /// <summary>La vista previa de la ventana principal bloquea el archivo: se suelta antes de sobrescribirlo.</summary>
        public void ReleaseMedia(string path)
        {
            if (_main?.Current?.FilePath != null && string.Equals(Path.GetFullPath(_main.Current.FilePath), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
                _main.Video.Release();
        }
    }
}
