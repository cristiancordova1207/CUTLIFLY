using System;
using System.Runtime.InteropServices;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Cutlify.Recording
{
    /// <summary>Utilidades de audio compartidas por la grabación de pantalla y la grabadora de audio.</summary>
    internal static class AudioUtil
    {
        public static bool IsAccessDenied(Exception ex) =>
            ex is UnauthorizedAccessException || (ex is COMException c && c.HResult == unchecked((int)0x80070005));

        /// <summary>Formato equivalente sin WAVE_FORMAT_EXTENSIBLE, que NAudio sabe convertir.</summary>
        public static WaveFormat PlainFormat(WaveFormat wf) =>
            wf.BitsPerSample == 32 ? WaveFormat.CreateIeeeFloatWaveFormat(wf.SampleRate, wf.Channels) : new WaveFormat(wf.SampleRate, wf.BitsPerSample, wf.Channels);

        /// <summary>Convierte cualquier fuente a estéreo float a <paramref name="sampleRate"/> y la completa con silencio.</summary>
        public static ISampleProvider Normalize(ISampleProvider src, int sampleRate)
        {
            if (src.WaveFormat.Channels == 1) src = new MonoToStereoSampleProvider(src);
            else if (src.WaveFormat.Channels > 2)
            {
                var mux = new MultiplexingSampleProvider(new[] { src }, 2);
                mux.ConnectInputToOutput(0, 0);
                mux.ConnectInputToOutput(1, 1);
                src = mux;
            }
            if (src.WaveFormat.SampleRate != sampleRate) src = new WdlResamplingSampleProvider(src, sampleRate);
            return new PadWithSilence(src);
        }

        /// <summary>Descarta el exceso del búfer para no acumular retraso respecto al reloj de la grabación.</summary>
        public static void TrimLatency(BufferedWaveProvider b, TimeSpan max, ref byte[] scratch)
        {
            if (b.BufferedDuration <= max) return;
            int excess = b.BufferedBytes - (int)(b.WaveFormat.AverageBytesPerSecond * max.TotalSeconds);
            excess -= excess % b.WaveFormat.BlockAlign;
            if (excess <= 0) return;
            if (scratch.Length < excess) scratch = new byte[excess];
            b.ReadFully = false;
            b.Read(scratch, 0, excess);
            b.ReadFully = true;
        }

        /// <summary>Completa con silencio: el mezclador descarta las entradas que devuelven menos muestras.</summary>
        private sealed class PadWithSilence : ISampleProvider
        {
            private readonly ISampleProvider _src;
            public PadWithSilence(ISampleProvider src) => _src = src;
            public WaveFormat WaveFormat => _src.WaveFormat;

            public int Read(float[] buffer, int offset, int count)
            {
                int n = _src.Read(buffer, offset, count);
                if (n < count) Array.Clear(buffer, offset + Math.Max(0, n), count - Math.Max(0, n));
                return count;
            }
        }
    }
}
