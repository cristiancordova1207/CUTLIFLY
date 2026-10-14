using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Cutlify.Clipboard;
using Cutlify.Core;
using Cutlify.Notifications;
using Cutlify.Recording;
using Cutlify.Settings;
using Cutlify.Storage;
using Cutlify.UI;

namespace Cutlify
{
    /// <summary>Grabadora de audio multipista (micrófono, escritorio y aplicaciones concretas).</summary>
    public sealed partial class AppController
    {
        private MultiTrackAudioRecorder _audioRec;
        private AudioRecorderWindow _audioWindow;
        private bool _audioStarting;

        public bool IsAudioRecording => _audioRec != null;
        public MultiTrackAudioRecorder AudioRecorder => _audioRec;
        public event Action AudioStateChanged;

        public void ShowAudioRecorder(bool activate = true)
        {
            if (_audioWindow == null)
            {
                _audioWindow = new AudioRecorderWindow(this) { ShowActivated = activate };
                _audioWindow.Closed += (_, __) => _audioWindow = null;
                _audioWindow.Show();
            }
            if (activate) _audioWindow.Activate();
        }

        /// <summary>Atajo global: inicia o detiene la grabación de audio con las pistas configuradas.</summary>
        public void ToggleAudioRecording()
        {
            if (IsAudioRecording) StopAudioRecording();
            else
            {
                ShowAudioRecorder(activate: false); // muestra el estado sin quitar el foco (p. ej. a un juego)
                _ = StartAudioRecording();
            }
        }

        public async Task<bool> StartAudioRecording()
        {
            if (IsAudioRecording || _audioStarting) return false;
            var s = SettingsService.Current;
            var configs = s.AudioTracks.ToList();
            if (configs.Count == 0) { ShowAudioRecorder(); return false; }
            _audioStarting = true;
            var sources = new List<AudioTrackSource>();
            var warnings = new List<string>();
            try
            {
                foreach (var c in configs)
                {
                    if (c.Kind == AudioTrackKind.Microphone && !EnsureMicrophoneConsent(_audioWindow))
                    {
                        warnings.Add($"{c.Name}: no tiene permiso para usar el micrófono.");
                        continue;
                    }
                    AudioTrackSource src = null;
                    try
                    {
                        src = await AudioTrackSource.CreateAsync(c);
                        src.Start();
                        sources.Add(src);
                    }
                    catch (Exception ex)
                    {
                        src?.Dispose();
                        Logger.Warn("Audio", $"No se pudo iniciar la pista «{c.Name}»", ex);
                        if (c.Kind == AudioTrackKind.Microphone && AudioUtil.IsAccessDenied(ex)) ShowWindowsMicrophoneBlocked(_audioWindow);
                        warnings.Add($"{c.Name}: {(ex is InvalidOperationException || ex is PlatformNotSupportedException ? ex.Message : "no está disponible.")}");
                    }
                }
                if (sources.Count == 0)
                {
                    DialogWindow.Ask(_audioWindow, "No se pudo iniciar ninguna pista de audio.", string.Join("\n", warnings), 0, "Cerrar");
                    return false;
                }

                var basePath = AppPaths.UniqueFile(AppPaths.Temp, "Audio", "");
                var rec = new MultiTrackAudioRecorder(sources, basePath, s.AudioMixFormat, s.AudioSeparateTracks);
                rec.Start();
                _audioRec = rec;
                sources = null;
                _tray?.SetTooltip("CUTLIFY — Grabando audio…");
                AudioStateChanged?.Invoke();
                Logger.Info("Audio", $"Grabación de audio iniciada: {string.Join(", ", rec.Tracks.Select(t => t.Name))}");
                if (s.ShowNotifications)
                    ToastWindow.ShowToast("Grabando audio", $"{rec.Tracks.Count} pista(s): {string.Join(", ", rec.Tracks.Select(t => t.Name))}" +
                        (warnings.Count > 0 ? "\n" + string.Join("\n", warnings) : ""), null, null, () => ShowAudioRecorder(),
                        new (string, Action)[] { ("Detener", StopAudioRecording) });
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("Audio", "No se pudo iniciar la grabación de audio", ex);
                DialogWindow.Ask(_audioWindow, "No se pudo iniciar la grabación de audio.", ex.Message, 0, "Cerrar");
                return false;
            }
            finally
            {
                if (sources != null) foreach (var src in sources) src.Dispose();
                _audioStarting = false;
            }
        }

        public void ToggleAudioPause()
        {
            if (_audioRec == null) return;
            if (_audioRec.IsPaused) _audioRec.Resume(); else _audioRec.Pause();
            AudioStateChanged?.Invoke();
        }

        public async void StopAudioRecording()
        {
            var rec = _audioRec;
            if (rec == null) return;
            _audioRec = null;
            AudioStateChanged?.Invoke();
            _tray?.SetTooltip("CUTLIFY — Screen Capture & Recording");
            try
            {
                var result = await Task.Run(() => rec.Stop());
                var item = BuildAudioItem(result);
                History.Add(item);
                Logger.Info("Audio", $"Audio guardado ({item.DurationLabel}, {item.SizeLabel}, {item.TrackFiles.Count} pistas)");
                var s = SettingsService.Current;
                bool copied = s.CopyRecordingsAsFile && ClipboardService.CopyFile(item.FilePath);
                if (s.ShowNotifications)
                    ToastWindow.ShowToast(copied ? "Audio guardado y copiado como archivo" : "Audio guardado",
                        $"{item.DurationLabel}{(item.TrackFiles.Count > 1 ? $"  ·  {item.TrackFiles.Count} pistas" : "")}  ·  {item.FormatLabel}",
                        item.Thumbnail, item.DurationLabel, () => ShowMainWindow(item),
                        new (string, Action)[] { ("Abrir", () => ShowMainWindow(item)) });
            }
            catch (Exception ex)
            {
                Logger.Error("Audio", "Error al finalizar la grabación de audio", ex);
                DialogWindow.Ask(null, "No se pudo guardar la grabación de audio.", ex.Message, 0, "Cerrar");
            }
        }

        private static HistoryItem BuildAudioItem(AudioRecordingResult r)
        {
            var item = new HistoryItem
            {
                Kind = ItemKind.Audio,
                FilePath = r.MixPath,
                FileName = Path.GetFileName(r.MixPath),
                TrackFiles = r.TrackPaths,
                TrackNames = r.TrackNames,
                DurationMs = (long)r.Duration.TotalMilliseconds,
                Audio = r.TrackNames.Count > 0 ? string.Join(" + ", r.TrackNames) : null
            };
            item.SizeBytes = item.AllFiles.Where(File.Exists).Sum(f => new FileInfo(f).Length);
            item.ThumbPath = ThumbnailService.CreateWaveform(r.Peaks, item.Id);
            return item;
        }

        /// <summary>Guarda la mezcla y cada pista en Documentos\CUTLIFY\Audio (o donde elija el usuario).</summary>
        public void SaveAudio(HistoryItem item, bool askLocation)
        {
            if (!item.HasFile) return;
            var ext = Path.GetExtension(item.FilePath);
            string target = AppPaths.UniqueFile(AppPaths.AudioFolder, "Audio", ext, item.CreatedLocal);
            if (askLocation)
            {
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Guardar audio como — CUTLIFY",
                    InitialDirectory = AppPaths.AudioFolder,
                    FileName = Path.GetFileNameWithoutExtension(target),
                    Filter = $"Audio (*{ext})|*{ext}",
                    AddExtension = true
                };
                if (dlg.ShowDialog(DialogOwner) != true) return;
                target = dlg.FileName;
            }
            try
            {
                if (_main?.Current == item) _main.Video.Release();
                File.Copy(item.FilePath, target, true);
                var baseName = Path.Combine(Path.GetDirectoryName(target), Path.GetFileNameWithoutExtension(target));
                var tracks = new List<string>();
                for (int i = 0; i < item.TrackFiles.Count; i++)
                {
                    if (!File.Exists(item.TrackFiles[i])) continue;
                    var name = i < item.TrackNames.Count ? item.TrackNames[i] : $"Pista{i + 1}";
                    var t = $"{baseName}_{MultiTrackAudioRecorder.SafeName(name)}.wav";
                    File.Copy(item.TrackFiles[i], t, true);
                    tracks.Add(t);
                }
                History.MarkPermanent(item, target, tracks);
                if (_main?.Current == item) _main.ShowItem(item);
                if (SettingsService.Current.ShowNotifications)
                    ToastWindow.ShowToast("Audio guardado", target + (tracks.Count > 0 ? $"\n+ {tracks.Count} pistas WAV" : ""), item.Thumbnail, item.DurationLabel, () => OpenLocationPath(target));
            }
            catch (Exception ex)
            {
                Logger.Error("Storage", "No se pudo guardar el audio", ex);
                DialogWindow.Ask(DialogOwner, "No se pudo guardar el audio.", ex.Message, 0, "Cerrar");
            }
        }

        private void StopAudioForExit()
        {
            try
            {
                var rec = _audioRec;
                if (rec == null) return;
                _audioRec = null;
                var result = rec.Stop();
                History.Add(BuildAudioItem(result));
            }
            catch (Exception ex) { Logger.Error("Audio", "Error al detener el audio al salir", ex); }
        }
    }
}
