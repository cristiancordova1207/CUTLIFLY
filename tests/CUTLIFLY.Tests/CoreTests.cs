using System;
using System.IO;
using System.Linq;
using Cutlifly.Core;
using Cutlifly.Hotkeys;
using Cutlifly.Recording;
using Cutlifly.Settings;
using Cutlifly.Storage;
using Cutlifly.Update;
using Xunit;

namespace Cutlifly.Tests
{
    public class HotkeyTests
    {
        [Theory]
        [InlineData("Win+Shift+S", HotkeyModifiers.Win | HotkeyModifiers.Shift, 'S')]
        [InlineData("Ctrl+Shift+X", HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, 'X')]
        [InlineData("ctrl + alt + f5", HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, 0x74)]
        [InlineData("Alt+PrintScreen", HotkeyModifiers.Alt, 0x2C)]
        public void Parses(string text, HotkeyModifiers mods, int key)
        {
            Assert.True(Hotkey.TryParse(text, out var hk));
            Assert.Equal(mods, hk.Modifiers);
            Assert.Equal(key, hk.Key);
        }

        [Theory]
        [InlineData("")]
        [InlineData("S")]
        [InlineData("Ctrl+Shift")]
        [InlineData("Ctrl+A+B")]
        [InlineData("Ctrl+Banana")]
        public void RejectsInvalid(string text) => Assert.False(Hotkey.TryParse(text, out _));

        [Fact]
        public void RoundTrips()
        {
            Assert.True(Hotkey.TryParse("Shift+Win+R", out var hk));
            Assert.Equal("Win+Shift+R", hk.ToString());
            Assert.True(Hotkey.TryParse(hk.ToString(), out var again));
            Assert.Equal(hk, again);
        }
    }

    public class UpdateTests
    {
        [Theory]
        [InlineData("v1.0.1", 1, 0, 1)]
        [InlineData("1.2", 1, 2, 0)]
        [InlineData("1.0.0+abc123", 1, 0, 0)]
        [InlineData("V2.10.3-beta", 2, 10, 3)]
        public void ParsesVersions(string text, int a, int b, int c) => Assert.Equal(new Version(a, b, c), UpdateService.ParseVersion(text));

        [Fact]
        public void ComparesVersions()
        {
            Assert.True(UpdateService.IsNewer(new Version(1, 0, 1), new Version(1, 0, 0)));
            Assert.True(UpdateService.IsNewer(new Version(1, 10, 0), new Version(1, 9, 9)));
            Assert.False(UpdateService.IsNewer(new Version(1, 0, 0), new Version(1, 0, 0)));
            Assert.False(UpdateService.IsNewer(new Version(0, 9, 0), new Version(1, 0, 0)));
            Assert.False(UpdateService.IsNewer(null, new Version(1, 0, 0)));
        }

        [Fact]
        public void OnlyOfficialUrls()
        {
            Assert.True(UpdateService.IsOfficialUrl("https://github.com/cristiancordova1207/cutlifly/releases/download/v1.0.1/CUTLIFLY.exe"));
            Assert.False(UpdateService.IsOfficialUrl("https://github.com/someone/else/releases/download/v1.0.1/CUTLIFLY.exe"));
            Assert.False(UpdateService.IsOfficialUrl("http://github.com/cristiancordova1207/cutlifly/releases/download/v1.0.1/CUTLIFLY.exe"));
            Assert.False(UpdateService.IsOfficialUrl("https://evil.example/CUTLIFLY.exe"));
            Assert.False(UpdateService.IsOfficialUrl(null));
        }

        [Fact]
        public void ParsesChecksums()
        {
            var a = new string('a', 64);
            var b = new string('B', 64);
            var sums = $"{a}  CUTLIFLY.exe\r\n{b} *CUTLIFLY-Setup.exe\n";
            Assert.Equal(a, UpdateService.ParseChecksum(sums, "CUTLIFLY.exe"));
            Assert.Equal(b.ToLowerInvariant(), UpdateService.ParseChecksum(sums, "CUTLIFLY-Setup.exe"));
            Assert.Null(UpdateService.ParseChecksum(sums, "other.exe"));
            Assert.Null(UpdateService.ParseChecksum("abc  CUTLIFLY.exe", "CUTLIFLY.exe"));
        }
    }

    public class StorageTests
    {
        [Fact]
        public void ExpirationRules()
        {
            var now = new DateTime(2026, 1, 1, 12, 20, 0, DateTimeKind.Utc);
            var created = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            Assert.True(TempCleaner.IsExpired(created, TimeSpan.FromMinutes(5), now));
            Assert.False(TempCleaner.IsExpired(created, TimeSpan.FromHours(1), now));
            Assert.False(TempCleaner.IsExpired(created, null, now)); // Nunca
        }

        [Fact]
        public void Formats()
        {
            Assert.Equal("00:38", HistoryItem.FormatDuration(TimeSpan.FromSeconds(38)));
            Assert.Equal("1:02:03", HistoryItem.FormatDuration(new TimeSpan(1, 2, 3)));
            Assert.Equal("438 MB", HistoryItem.FormatBytes(438L * 1024 * 1024));
            Assert.Equal("12 KB", HistoryItem.FormatBytes(12 * 1024));
        }

        [Fact]
        public void SearchMatchesNameDateAndType()
        {
            var item = new HistoryItem { Kind = ItemKind.Video, FilePath = @"C:\x\CUTLIFLY_20260104_120000.mp4", CreatedUtc = new DateTime(2026, 1, 4, 12, 0, 0, DateTimeKind.Utc) };
            Assert.True(UI.HistoryWindow.Matches(item, "20260104"));
            Assert.True(UI.HistoryWindow.Matches(item, item.DateLabel));
            Assert.True(UI.HistoryWindow.Matches(item, "grabación"));
            Assert.True(UI.HistoryWindow.Matches(item, "vídeo"));
            Assert.False(UI.HistoryWindow.Matches(item, "imagen"));
        }

        /// <summary>Simula "captura creada 12:00, expira 12:05, CUTLIFLY se reabre 12:20": el archivo debe eliminarse al iniciar.</summary>
        [Fact]
        public void CleansExpiredTemporaryFilesOnStartup()
        {
            SettingsService.Current.RetentionMinutes = 5;
            SettingsService.Current.SendExpiredToRecycleBin = false;
            var history = new HistoryService();

            var oldFile = Path.Combine(AppPaths.Temp, $"test_old_{Guid.NewGuid():N}.png");
            var newFile = Path.Combine(AppPaths.Temp, $"test_new_{Guid.NewGuid():N}.png");
            var keptFile = Path.Combine(Path.GetTempPath(), $"test_perm_{Guid.NewGuid():N}.png");
            File.WriteAllBytes(oldFile, new byte[] { 1 });
            File.WriteAllBytes(newFile, new byte[] { 1 });
            File.WriteAllBytes(keptFile, new byte[] { 1 });
            try
            {
                var expired = new HistoryItem { Kind = ItemKind.Image, FilePath = oldFile, CreatedUtc = DateTime.UtcNow.AddMinutes(-20) };
                var fresh = new HistoryItem { Kind = ItemKind.Image, FilePath = newFile, CreatedUtc = DateTime.UtcNow };
                var permanent = new HistoryItem { Kind = ItemKind.Image, FilePath = keptFile, CreatedUtc = DateTime.UtcNow.AddDays(-30), IsPermanent = true };
                history.Items.Add(expired);
                history.Items.Add(fresh);
                history.Items.Add(permanent);

                new TempCleaner(history).CleanExpired();

                Assert.False(File.Exists(oldFile));
                Assert.True(File.Exists(newFile));
                Assert.True(File.Exists(keptFile));
                Assert.DoesNotContain(expired, history.Items);
                Assert.Contains(permanent, history.Items);
            }
            finally
            {
                foreach (var f in new[] { oldFile, newFile, keptFile }) try { File.Delete(f); } catch { }
            }
        }
    }

    public class RecordingTests
    {
        [Fact]
        public void BitrateIsClamped()
        {
            Assert.Equal(1_000_000, ScreenRecorder.ComputeBitrate(16, 16, 30, "Baja"));
            Assert.Equal(60_000_000, ScreenRecorder.ComputeBitrate(7680, 4320, 60, "Alta"));
            Assert.True(ScreenRecorder.ComputeBitrate(1920, 1080, 30, "Alta") > ScreenRecorder.ComputeBitrate(1920, 1080, 30, "Media"));
        }

        /// <summary>Graba 1,5 s de una región real con Media Foundation y verifica el MP4 resultante.</summary>
        [Fact]
        public void RecordsRegionToMp4()
        {
            var output = Path.Combine(Path.GetTempPath(), $"cutlifly_test_{Guid.NewGuid():N}.mp4");
            var thumb = Path.ChangeExtension(output, ".jpg");
            try
            {
                using var rec = new ScreenRecorder(new RecordingOptions
                {
                    Region = new System.Drawing.Rectangle(0, 0, 321, 241), // impar: debe redondear a par
                    Fps = 30,
                    OutputPath = output,
                    ThumbPath = thumb,
                    HardwareEncoding = true
                });
                Assert.Equal(new System.Drawing.Size(320, 240), rec.VideoSize);
                rec.Start();
                System.Threading.Thread.Sleep(700);
                rec.Pause();
                System.Threading.Thread.Sleep(300);
                rec.Resume();
                System.Threading.Thread.Sleep(500);
                rec.Stop();

                Assert.True(rec.FramesWritten > 10, $"frames = {rec.FramesWritten}");
                Assert.InRange(rec.Elapsed.TotalSeconds, 0.9, 2.0);
                var bytes = File.ReadAllBytes(output);
                Assert.True(bytes.Length > 1000);
                Assert.Equal("ftyp", System.Text.Encoding.ASCII.GetString(bytes, 4, 4)); // contenedor MP4
                Assert.True(File.Exists(thumb));
            }
            finally
            {
                try { File.Delete(output); } catch { }
                try { File.Delete(thumb); } catch { }
            }
        }
    }

    public class SettingsTests
    {
        [Fact]
        public void DefaultsMatchSpec()
        {
            var s = new AppSettings();
            Assert.Equal("Win+Shift+S", s.HotkeyCapture);
            Assert.Equal("Win+Shift+R", s.HotkeyRecord);
            Assert.Equal("Ctrl+Shift+X", s.HotkeyOpen);
            Assert.Equal("PNG", s.ImageFormat);
            Assert.True(s.AutoCopyCaptures);
            Assert.True(s.SaveTemporarily);
            Assert.True(s.SendExpiredToRecycleBin);
            Assert.Equal(60, s.RetentionMinutes);
            Assert.Equal(TimeSpan.FromHours(1), s.Retention);
            Assert.Null(new AppSettings { RetentionMinutes = 0 }.Retention);
        }
    }
}
