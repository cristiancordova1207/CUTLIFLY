using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NAudio.Wave.SampleProviders;
using Cutlify.Capture;
using Cutlify.Core;
using Cutlify.Recording;
using Cutlify.Settings;
using Cutlify.Storage;
using Cutlify.Update;
using Xunit;

namespace Cutlify.Tests
{
    public class BrandingTests
    {
        [Fact]
        public void NameIsCutlify()
        {
            var asm = typeof(AppController).Assembly;
            Assert.Equal("CUTLIFY", asm.GetName().Name);
            Assert.Equal("CUTLIFY", asm.GetCustomAttribute<AssemblyProductAttribute>()?.Product);
            Assert.Equal("CUTLIFY.exe", UpdateService.ExeAsset);
            Assert.Equal("CUTLIFY-Setup.exe", UpdateService.SetupAsset);
            using var env = new TestEnvironment();
            Assert.Equal("CUTLIFY", Path.GetFileName(AppPaths.Root));
            Assert.Equal("CUTLIFY", Path.GetFileName(AppPaths.DocumentsRoot));
        }
    }

    public class StorageLocationTests
    {
        [Fact]
        public void CreatesDocumentsFolders()
        {
            using var env = new TestEnvironment();
            AppPaths.EnsureUserFolders();
            Assert.True(Directory.Exists(Path.Combine(env.Documents, "CUTLIFY")));
            Assert.True(Directory.Exists(Path.Combine(env.Documents, "CUTLIFY", "Capturas")));
            Assert.True(Directory.Exists(Path.Combine(env.Documents, "CUTLIFY", "Grabaciones")));
            Assert.True(Directory.Exists(Path.Combine(env.Local, "Thumbnails")));
            Assert.True(Directory.Exists(Path.Combine(env.Local, "Temporales")));
        }

        [Fact]
        public void ChangesAndRestoresLocation()
        {
            using var env = new TestEnvironment();
            var custom = Path.Combine(env.Documents, "Otra", "Capturas");
            SettingsService.Current.CapturesFolder = custom;
            Assert.Equal(custom, AppPaths.CapturesFolder);
            Assert.True(Directory.Exists(custom));
            SettingsService.Current.CapturesFolder = null;
            Assert.Equal(AppPaths.DefaultCapturesFolder, AppPaths.CapturesFolder);
        }

        [Fact]
        public void UniqueOrganizedNames()
        {
            using var env = new TestEnvironment();
            var when = new DateTime(2026, 10, 4, 14, 32, 18);
            var a = AppPaths.UniqueFile(AppPaths.CapturesFolder, "Captura", ".png", when);
            Assert.Equal("Captura_2026-10-04_14-32-18.png", Path.GetFileName(a));
            File.WriteAllText(a, "x");
            var b = AppPaths.UniqueFile(AppPaths.CapturesFolder, "Captura", ".png", when);
            Assert.Equal("Captura_2026-10-04_14-32-18_2.png", Path.GetFileName(b));
        }
    }

    public class SettingsPersistenceTests
    {
        [Fact]
        public void DefaultsForNewUser()
        {
            var s = new AppSettings();
            Assert.Equal("1080p", s.RecordResolution);
            Assert.Equal(30, s.RecordFps);
            Assert.Equal("H.264", s.RecordCodec);
            Assert.Equal("Automática", s.RecordQuality);
            Assert.Equal(0, s.RecordBitrateMbps); // automático
            Assert.True(s.RecordAudio);
            Assert.True(s.RecordSystemAudio && s.RecordMicrophone);
            Assert.Equal("Predeterminado", s.RecordProfile);
            Assert.Null(s.CapturesFolder);   // Documentos\CUTLIFY\Capturas
            Assert.Null(s.RecordingsFolder);
            Assert.Equal("Alt+N", s.HotkeyRepeat);
            Assert.False(s.StartWithWindows);
            var p = VideoSettings.Profiles.First();
            Assert.Equal(("Predeterminado", "1080p", 30), (p.Name, p.Resolution, p.Fps));
        }

        [Fact]
        public void SettingsPersistAcrossRestarts()
        {
            using var env = new TestEnvironment();
            var s = SettingsService.Current;
            s.RecordResolution = "1080p";
            s.RecordFps = 60;
            s.RecordBitrateMbps = 50;
            s.RecordSystemAudio = false;
            s.RecordMicrophone = true;
            s.CapturesFolder = Path.Combine(env.Documents, "X");
            SettingsService.Save();

            SettingsService.Current = new AppSettings();
            SettingsService.Load();
            s = SettingsService.Current;
            Assert.Equal(60, s.RecordFps);
            Assert.Equal(50, s.RecordBitrateMbps);
            Assert.False(s.RecordSystemAudio);
            Assert.True(s.RecordMicrophone);
            Assert.Equal(Path.Combine(env.Documents, "X"), s.CapturesFolder);
        }

        [Fact]
        public void RestoreDefaultsKeepsMicConsent()
        {
            using var env = new TestEnvironment();
            var s = SettingsService.Current;
            s.RecordFps = 120; s.RecordCodec = "HEVC"; s.RecordBitrateMbps = 80; s.RecordAudio = false;
            s.CapturesFolder = @"C:\otra"; s.MicrophoneConsent = MicConsent.Denied;
            SettingsService.Reset();
            s = SettingsService.Current;
            Assert.Equal(30, s.RecordFps);
            Assert.Equal("1080p", s.RecordResolution);
            Assert.Equal("H.264", s.RecordCodec);
            Assert.Equal(0, s.RecordBitrateMbps);
            Assert.True(s.RecordAudio);
            Assert.Null(s.CapturesFolder);
            Assert.Equal(MicConsent.Denied, s.MicrophoneConsent);
        }
    }

    public class VideoSettingsTests
    {
        [Fact]
        public void AutoBitrateDependsOnResolutionFpsCodecAndQuality()
        {
            int b1080 = VideoSettings.AutoBitrate(1920, 1080, 30, "H.264", "Automática");
            int b1440 = VideoSettings.AutoBitrate(2560, 1440, 30, "H.264", "Automática");
            int b60 = VideoSettings.AutoBitrate(1920, 1080, 60, "H.264", "Automática");
            int hevc = VideoSettings.AutoBitrate(1920, 1080, 30, "HEVC", "Automática");
            int high = VideoSettings.AutoBitrate(1920, 1080, 30, "H.264", "Alta");
            Assert.InRange(b1080, 4_000_000, 9_000_000);
            Assert.True(b1440 > b1080);
            Assert.True(b60 > b1080 && b60 < b1080 * 2); // sublineal con los FPS
            Assert.True(hevc < b1080);
            Assert.True(high > b1080);
        }

        [Fact]
        public void ManualBitrateOverridesAuto()
        {
            var s = new AppSettings { RecordBitrateMbps = 50 };
            var (bps, auto) = VideoSettings.ResolveBitrate(s, new Size(1920, 1080));
            Assert.Equal(50_000_000, bps);
            Assert.False(auto);
            var (abps, isAuto) = VideoSettings.ResolveBitrate(new AppSettings(), new Size(1920, 1080));
            Assert.True(isAuto);
            Assert.Equal(VideoSettings.AutoBitrate(1920, 1080, 30, "H.264", "Automática"), abps);
        }

        [Fact]
        public void OutputSizeDownscalesWithoutUpscaling()
        {
            Assert.Equal(new Size(1920, 1080), VideoSettings.OutputSize(new Size(3840, 2160), "1080p"));
            Assert.Equal(new Size(1200, 700), VideoSettings.OutputSize(new Size(1200, 700), "1080p"));
            Assert.Equal(new Size(3840, 2160), VideoSettings.OutputSize(new Size(3840, 2160), "Automática"));
            Assert.Equal(new Size(320, 240), VideoSettings.OutputSize(new Size(321, 241), "Automática"));
        }

        [Fact]
        public void FpsLimitedByRealRefreshRate()
        {
            Assert.Equal(new[] { 30, 60 }, VideoSettings.CompatibleFps(60).ToArray());
            Assert.Contains(144, VideoSettings.CompatibleFps(144));
            Assert.DoesNotContain(165, VideoSettings.CompatibleFps(144));
            Assert.False(VideoSettings.IsProfileCompatible(VideoSettings.Profiles.First(p => p.Name == "4K 60 FPS"), 60, 1080));
            Assert.True(VideoSettings.IsProfileCompatible(VideoSettings.Profiles.First(), 60, 1080));
        }

        [Fact]
        public void DetectsCodecsWithoutThrowing()
        {
            var codecs = VideoSettings.DetectCodecs(refresh: true);
            Assert.Equal(new[] { "H.264", "HEVC", "AV1" }, codecs.Select(c => c.Name).ToArray());
        }
    }

    public class HistoryStateTests
    {
        private static HistoryItem NewItem(TestEnvironment env, string name, bool permanent = false)
        {
            var item = new HistoryItem { Kind = ItemKind.Image, FilePath = permanent ? env.UserFile(name) : env.TempFile(name), IsPermanent = permanent };
            item.ThumbPath = Path.Combine(AppPaths.Thumbs, item.Id + ".jpg");
            File.WriteAllBytes(item.ThumbPath, new byte[] { 1 });
            return item;
        }

        [Fact]
        public void DeletingFileKeepsEntryAndThumbnail()
        {
            using var env = new TestEnvironment();
            SettingsService.Current.SendExpiredToRecycleBin = false;
            var h = new HistoryService();
            var item = NewItem(env, "a.png");
            h.Add(item);
            h.DeleteFile(item);
            Assert.Contains(item, h.Items);
            Assert.Equal(ItemState.Deleted, item.State);
            Assert.False(File.Exists(item.FilePath));
            Assert.True(File.Exists(item.ThumbPath));
            Assert.True(item.IsGone);
            Assert.Equal("Eliminada", item.StatusLabel);
        }

        [Fact]
        public void RemovingEntryDeletesThumbnailButNeverTheFile()
        {
            using var env = new TestEnvironment();
            var h = new HistoryService();
            var item = NewItem(env, "keep.png", permanent: true);
            h.Add(item);
            h.RemoveEntry(item);
            Assert.DoesNotContain(item, h.Items);
            Assert.False(File.Exists(item.ThumbPath));
            Assert.True(File.Exists(item.FilePath));
        }

        [Fact]
        public void ClearHistoryKeepsFiles()
        {
            using var env = new TestEnvironment();
            var h = new HistoryService();
            var a = NewItem(env, "1.png", true);
            var b = NewItem(env, "2.png", true);
            h.Add(a); h.Add(b);
            Assert.Equal(2, h.Clear());
            Assert.Empty(h.Items);
            Assert.True(File.Exists(a.FilePath) && File.Exists(b.FilePath));
            Assert.False(File.Exists(a.ThumbPath) || File.Exists(b.ThumbPath));
        }

        [Fact]
        public void StatesSurviveRestart()
        {
            using var env = new TestEnvironment();
            SettingsService.Current.SendExpiredToRecycleBin = false;
            var h = new HistoryService();
            var deleted = NewItem(env, "d.png");
            var external = NewItem(env, "x.png", true);
            var memory = new HistoryItem { Kind = ItemKind.Image, FileName = "m.png", MemoryImage = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgr32, null, new byte[16], 8) };
            h.Add(deleted); h.Add(external); h.Add(memory);
            h.DeleteFile(deleted);
            File.Delete(external.FilePath); // borrado fuera de CUTLIFY

            var reloaded = new HistoryService();
            reloaded.Load();
            Assert.Equal(3, reloaded.Items.Count);
            Assert.Equal(ItemState.Deleted, reloaded.Items.Single(i => i.Id == deleted.Id).State);
            Assert.Equal(ItemState.Deleted, reloaded.Items.Single(i => i.Id == external.Id).State);
            Assert.Equal(ItemState.NotSaved, reloaded.Items.Single(i => i.Id == memory.Id).State);
        }

        [Fact]
        public void InterruptedRecordingIsMarkedIncomplete()
        {
            using var env = new TestEnvironment();
            var h = new HistoryService();
            var video = env.TempFile("Grabacion_2026-10-04_10-00-00.mp4");
            var item = new HistoryItem { Kind = ItemKind.Video, FilePath = video, Fps = 30 };
            File.WriteAllText(video + TempCleaner.MarkerExtension, JsonSerializer.Serialize(item));

            Assert.Equal(1, new TempCleaner(h).RecoverInterruptedRecordings());
            var entry = Assert.Single(h.Items);
            Assert.Equal(ItemState.Incomplete, entry.State);
            Assert.Equal("Grabación incompleta", entry.StatusLabel);
            Assert.False(File.Exists(video));
            Assert.False(File.Exists(video + TempCleaner.MarkerExtension));
        }

        [Fact]
        public void ThumbnailIsSmallAndIndependent()
        {
            using var env = new TestEnvironment();
            var src = BitmapSource.Create(1280, 720, 96, 96, PixelFormats.Bgr32, null, new byte[1280 * 720 * 4], 1280 * 4);
            var path = ThumbnailService.Create(src, "test");
            Assert.True(File.Exists(path));
            Assert.True(AppPaths.IsInside(path, AppPaths.Thumbs));
            var thumb = HistoryItem.LoadImage(path);
            Assert.Equal(ThumbnailService.MaxWidth, thumb.PixelWidth);
            Assert.True(new FileInfo(path).Length < 60_000);
        }
    }

    public class CaptureTests
    {
        [Fact]
        public void FreeFormOutsideIsTransparent()
        {
            using var frozen = new Bitmap(100, 100);
            using (var g = Graphics.FromImage(frozen)) g.Clear(System.Drawing.Color.Red);
            var tri = new[] { new System.Drawing.Point(10, 10), new System.Drawing.Point(90, 10), new System.Drawing.Point(10, 90) };
            var img = ImageUtil.FreeFormCrop(frozen, new Rectangle(10, 10, 80, 80), tri);
            var px = new byte[4];
            img.CopyPixels(new System.Windows.Int32Rect(75, 75, 1, 1), px, 4, 0); // fuera del triángulo
            Assert.Equal(0, px[3]);
            img.CopyPixels(new System.Windows.Int32Rect(5, 5, 1, 1), px, 4, 0);   // dentro
            Assert.Equal(255, px[3]);
            Assert.Equal(255, px[2]); // rojo
        }
    }

    public class AudioRecordingTests
    {
        /// <summary>Graba vídeo + audio sintético y comprueba que el MP4 contiene una pista AAC (mp4a).</summary>
        [Fact]
        public void RecordsMixedAudioTrackIntoMp4()
        {
            var output = Path.Combine(Path.GetTempPath(), $"cutlify_audio_{Guid.NewGuid():N}.mp4");
            try
            {
                var tone = new SignalGenerator(44100, 1) { Frequency = 440, Gain = 0.2, Type = SignalGeneratorType.Sin };
                using var rec = new ScreenRecorder(new RecordingOptions
                {
                    Region = new Rectangle(0, 0, 320, 240),
                    Fps = 30,
                    OutputPath = output,
                    Audio = AudioCapture.FromProvider(tone)
                });
                rec.Start();
                Thread.Sleep(1500);
                rec.Stop();

                Assert.True(rec.FramesWritten > 10);
                Assert.InRange(rec.AudioFramesWritten, 48000, 48000 * 2); // ~1,5 s a 48 kHz
                var bytes = File.ReadAllBytes(output);
                Assert.Contains("mp4a", Encoding.ASCII.GetString(bytes));
                Assert.True(rec.Stats.RealFps > 0);
            }
            finally
            {
                try { File.Delete(output); } catch { }
            }
        }
    }
}
