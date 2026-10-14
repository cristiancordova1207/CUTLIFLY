using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Cutlify.Core;
using Cutlify.Recording;
using Cutlify.Settings;
using Cutlify.Storage;
using Xunit;

namespace Cutlify.Tests
{
    public class AudioRecorderTests
    {
        private static AudioTrackSource Tone(string name, double freq, int rate, int channels) =>
            AudioTrackSource.FromProvider(new AudioTrackConfig { Kind = AudioTrackKind.Application, Name = name },
                new SignalGenerator(rate, channels) { Frequency = freq, Gain = 0.25, Type = SignalGeneratorType.Sin });

        [Theory]
        [InlineData("M4A", ".m4a")]
        [InlineData("WAV", ".wav")]
        public void RecordsSeparateSyncedTracksAndMix(string format, string ext)
        {
            using var env = new TestEnvironment();
            var basePath = AppPaths.UniqueFile(AppPaths.Temp, "Audio", "");
            using var rec = new MultiTrackAudioRecorder(new[]
            {
                Tone("Micrófono", 220, 44100, 1),   // mono 44,1 kHz
                Tone("Spotify", 440, 48000, 2),     // estéreo 48 kHz
                Tone("Escritorio", 880, 96000, 2)   // se remuestrea
            }, basePath, format, separateTracks: true);
            rec.Start();
            Thread.Sleep(700);
            rec.Pause();
            Thread.Sleep(300);
            rec.Resume();
            Thread.Sleep(500);
            var result = rec.Stop();

            Assert.Equal(3, result.TrackPaths.Count);
            Assert.Equal(new[] { "Micrófono", "Spotify", "Escritorio" }, result.TrackNames);
            Assert.InRange(result.Duration.TotalSeconds, 1.0, 1.8); // la pausa no cuenta
            var lengths = result.TrackPaths.Select(p =>
            {
                using var r = new WaveFileReader(p);
                Assert.Equal(48000, r.WaveFormat.SampleRate);
                Assert.Equal(2, r.WaveFormat.Channels);
                return r.Length;
            }).ToList();
            Assert.True(lengths.Distinct().Count() == 1, "las pistas deben tener exactamente la misma duración (sincronizadas)");
            Assert.EndsWith(ext, result.MixPath);
            Assert.True(File.Exists(result.MixPath));
            if (format == "M4A") Assert.Equal("ftyp", Encoding.ASCII.GetString(File.ReadAllBytes(result.MixPath), 4, 4));
            Assert.False(File.Exists(basePath + "_mezcla.tmp.wav"));
            Assert.NotEmpty(result.Peaks);
            Assert.True(result.Peaks.Max() > 0.1f);
        }

        [Fact]
        public void MutedTrackIsExcludedFromMixOnly()
        {
            using var env = new TestEnvironment();
            var a = Tone("A", 440, 48000, 2);
            a.Config.Muted = true;
            var basePath = AppPaths.UniqueFile(AppPaths.Temp, "Audio", "");
            using var rec = new MultiTrackAudioRecorder(new[] { a, Tone("B", 0, 48000, 2) }, basePath, "WAV", true);
            rec.Start();
            Thread.Sleep(600);
            var result = rec.Stop();
            // Mezcla en silencio (A silenciada, B a 0 Hz = continua 0), pista A con señal.
            float MaxAbs(string p)
            {
                using var r = new AudioFileReader(p);
                var buf = new float[r.WaveFormat.SampleRate];
                float m = 0; int n;
                while ((n = r.Read(buf, 0, buf.Length)) > 0) for (int i = 0; i < n; i++) m = Math.Max(m, Math.Abs(buf[i]));
                return m;
            }
            Assert.True(MaxAbs(result.TrackPaths[0]) > 0.1f);
            Assert.True(MaxAbs(result.MixPath) < 0.3f);
        }

        [Fact]
        public void DeletingAudioItemRemovesAllTracks()
        {
            using var env = new TestEnvironment();
            SettingsService.Current.SendExpiredToRecycleBin = false;
            var h = new HistoryService();
            var item = new HistoryItem { Kind = ItemKind.Audio, FilePath = env.TempFile("Audio_x.m4a") };
            item.TrackFiles.Add(env.TempFile("Audio_x_Microfono.wav"));
            item.TrackFiles.Add(env.TempFile("Audio_x_Spotify.wav"));
            h.Add(item);
            h.DeleteFile(item);
            Assert.All(item.AllFiles, f => Assert.False(File.Exists(f)));
            Assert.Equal(ItemState.Deleted, item.State);
            Assert.Equal("Audio", item.TypeLabel);
        }

        [Fact]
        public void DefaultsAndSafeNames()
        {
            var s = new AppSettings();
            Assert.Equal("Win+Shift+A", s.HotkeyAudio);
            Assert.Equal("M4A", s.AudioMixFormat);
            Assert.True(s.AudioSeparateTracks);
            Assert.Equal(new[] { AudioTrackKind.System, AudioTrackKind.Microphone }, s.AudioTracks.Select(t => t.Kind).ToArray());
            Assert.Equal("Micrófono_USB", MultiTrackAudioRecorder.SafeName("Micrófono USB"));
            Assert.DoesNotContain(':', MultiTrackAudioRecorder.SafeName("a:b/c"));
        }

        [Fact]
        public void ListsOpenApplicationsWithoutThrowing()
        {
            var apps = AudioApps.List();
            Assert.NotNull(apps);
            Assert.All(apps, a => Assert.False(string.IsNullOrEmpty(a.ProcessName)));
        }
    }
}
