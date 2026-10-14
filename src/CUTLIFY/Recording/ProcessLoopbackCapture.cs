using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Cutlify.Core;

namespace Cutlify.Recording
{
    /// <summary>
    /// Captura solo el audio de una aplicación (y sus procesos hijos) con el "process loopback" de WASAPI.
    /// Requiere Windows 11 / Windows 10 build 20348 o posterior. No captura el resto del escritorio.
    /// </summary>
    public sealed class ProcessLoopbackCapture : IWaveIn
    {
        private const string VirtualDevice = "VAD\\Process_Loopback";
        private static readonly Guid IID_IAudioClient = new Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");

        public static bool IsSupported => Environment.OSVersion.Version.Build >= 20348;

        private readonly AudioClient _client;
        private Thread _thread;
        private volatile bool _stop;
        private readonly AutoResetEvent _event = new AutoResetEvent(false);

        public WaveFormat WaveFormat { get; set; }
        public event EventHandler<WaveInEventArgs> DataAvailable;
        public event EventHandler<StoppedEventArgs> RecordingStopped;

        private ProcessLoopbackCapture(AudioClient client, WaveFormat format)
        {
            _client = client;
            WaveFormat = format;
        }

        /// <summary>Activa la captura del proceso <paramref name="processId"/> incluyendo sus procesos hijos.</summary>
        public static async Task<ProcessLoopbackCapture> CreateAsync(int processId, bool includeProcessTree = true)
        {
            if (!IsSupported) throw new PlatformNotSupportedException("Grabar el audio de una sola aplicación requiere Windows 11.");

            var format = new WaveFormat(48000, 16, 2);
            // AUDIOCLIENT_ACTIVATION_PARAMS { ActivationType, { TargetProcessId, ProcessLoopbackMode } }
            IntPtr parameters = Marshal.AllocHGlobal(12);
            IntPtr propVariant = Marshal.AllocHGlobal(24);
            try
            {
                Marshal.WriteInt32(parameters, 0, 1); // AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK
                Marshal.WriteInt32(parameters, 4, processId);
                Marshal.WriteInt32(parameters, 8, includeProcessTree ? 0 : 1);
                // PROPVARIANT VT_BLOB { cbSize, pBlobData }
                for (int i = 0; i < 24; i += 4) Marshal.WriteInt32(propVariant, i, 0);
                Marshal.WriteInt16(propVariant, 0, 65);
                Marshal.WriteInt32(propVariant, 8, 12);
                Marshal.WriteIntPtr(propVariant, 8 + IntPtr.Size, parameters);

                var handler = new CompletionHandler();
                ActivateAudioInterfaceAsync(VirtualDevice, IID_IAudioClient, propVariant, handler, out _);
                var raw = await handler.Task.ConfigureAwait(false);
                var client = new AudioClient((NAudio.CoreAudioApi.Interfaces.IAudioClient)raw);

                // LOOPBACK | EVENTCALLBACK | SRC_DEFAULT_QUALITY | AUTOCONVERTPCM
                const uint flagBits = 0x00020000u | 0x00040000u | 0x08000000u | 0x80000000u;
                var flags = unchecked((AudioClientStreamFlags)flagBits);
                client.Initialize(AudioClientShareMode.Shared, flags, 2_000_000, 0, format, Guid.Empty);
                return new ProcessLoopbackCapture(client, format);
            }
            finally
            {
                Marshal.FreeHGlobal(propVariant);
                Marshal.FreeHGlobal(parameters);
            }
        }

        public void StartRecording()
        {
            _client.SetEventHandle(_event.SafeWaitHandle.DangerousGetHandle());
            _client.Start();
            _thread = new Thread(Loop) { IsBackground = true, Name = "CUTLIFY App audio", Priority = ThreadPriority.AboveNormal };
            _thread.Start();
        }

        private void Loop()
        {
            Exception error = null;
            var capture = _client.AudioCaptureClient;
            int blockAlign = WaveFormat.BlockAlign;
            var buffer = new byte[WaveFormat.AverageBytesPerSecond];
            try
            {
                while (!_stop)
                {
                    _event.WaitOne(100);
                    int packet;
                    while (!_stop && (packet = capture.GetNextPacketSize()) > 0)
                    {
                        IntPtr data = capture.GetBuffer(out int frames, out AudioClientBufferFlags bufferFlags);
                        int bytes = frames * blockAlign;
                        if (buffer.Length < bytes) buffer = new byte[bytes];
                        if ((bufferFlags & AudioClientBufferFlags.Silent) != 0) Array.Clear(buffer, 0, bytes);
                        else Marshal.Copy(data, buffer, 0, bytes);
                        capture.ReleaseBuffer(frames);
                        if (bytes > 0) DataAvailable?.Invoke(this, new WaveInEventArgs(buffer, bytes));
                    }
                }
            }
            catch (Exception ex)
            {
                error = ex;
                Logger.Warn("Audio", "La captura de la aplicación se detuvo", ex);
            }
            finally
            {
                try { _client.Stop(); } catch { }
                RecordingStopped?.Invoke(this, new StoppedEventArgs(error));
            }
        }

        public void StopRecording()
        {
            _stop = true;
            _event.Set();
            _thread?.Join(1000);
        }

        public void Dispose()
        {
            StopRecording();
            _client.Dispose();
            _event.Dispose();
        }

        // ---- Interop ----

        [DllImport("Mmdevapi.dll", ExactSpelling = true, PreserveSig = false)]
        private static extern void ActivateAudioInterfaceAsync([MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath,
            [MarshalAs(UnmanagedType.LPStruct)] Guid riid, IntPtr activationParams,
            IActivateCompletionHandler completionHandler, out IActivateOperation operation);

        [ComImport, Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IActivateCompletionHandler
        {
            void ActivateCompleted(IActivateOperation operation);
        }

        [ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IActivateOperation
        {
            void GetActivateResult([MarshalAs(UnmanagedType.Error)] out int activateResult, [MarshalAs(UnmanagedType.IUnknown)] out object activatedInterface);
        }

        /// <summary>Marca el objeto como ágil: ActivateAudioInterfaceAsync lo exige.</summary>
        [ComImport, Guid("94ea2b94-e9cc-49e0-c0ff-ee64ca8f5b90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAgileObject { }

        [ComVisible(true)]
        private sealed class CompletionHandler : IActivateCompletionHandler, IAgileObject
        {
            private readonly TaskCompletionSource<object> _tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            public Task<object> Task => _tcs.Task;

            public void ActivateCompleted(IActivateOperation operation)
            {
                try
                {
                    operation.GetActivateResult(out int hr, out object iface);
                    if (hr != 0) _tcs.TrySetException(Marshal.GetExceptionForHR(hr) ?? new COMException("No se pudo activar la captura", hr));
                    else _tcs.TrySetResult(iface);
                }
                catch (Exception ex) { _tcs.TrySetException(ex); }
            }
        }
    }
}
