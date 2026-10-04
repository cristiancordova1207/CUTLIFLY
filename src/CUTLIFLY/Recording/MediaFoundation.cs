using System;
using System.Runtime.InteropServices;

namespace Cutlifly.Recording
{
    /// <summary>Interop mínimo de Media Foundation (Sink Writer → MP4/H.264).</summary>
    internal static class MF
    {
        public const int Version = 0x00020070;

        public static readonly Guid MT_MAJOR_TYPE = new Guid("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
        public static readonly Guid MT_SUBTYPE = new Guid("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
        public static readonly Guid MT_AVG_BITRATE = new Guid("20332624-fb0d-4d9e-bd0d-cbf6786c102e");
        public static readonly Guid MT_INTERLACE_MODE = new Guid("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
        public static readonly Guid MT_FRAME_SIZE = new Guid("1652c33d-d6b2-4012-b834-72030849a37d");
        public static readonly Guid MT_FRAME_RATE = new Guid("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
        public static readonly Guid MT_PIXEL_ASPECT_RATIO = new Guid("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
        public static readonly Guid MT_DEFAULT_STRIDE = new Guid("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
        public static readonly Guid MediaType_Video = new Guid("73646976-0000-0010-8000-00AA00389B71");
        public static readonly Guid VideoFormat_H264 = new Guid("34363248-0000-0010-8000-00AA00389B71");
        public static readonly Guid VideoFormat_RGB32 = new Guid("00000016-0000-0010-8000-00AA00389B71");
        public static readonly Guid READWRITE_ENABLE_HARDWARE_TRANSFORMS = new Guid("a634a91c-822b-41b9-a494-4de4643612b0");
        public static readonly Guid SINK_WRITER_DISABLE_THROTTLING = new Guid("08b845d8-2b74-4afe-9d53-be16d2d5ae4f");
        public static readonly Guid TRANSCODE_CONTAINERTYPE = new Guid("150ff23f-4abc-478b-ac4f-e1916fba1cca");
        public static readonly Guid TranscodeContainerType_MPEG4 = new Guid("dc6cd05d-b9d0-40ef-bd35-fa622c1ab28a");

        [DllImport("mfplat.dll", ExactSpelling = true, PreserveSig = false)]
        public static extern void MFStartup(int version, int flags);

        [DllImport("mfplat.dll", ExactSpelling = true, PreserveSig = false)]
        public static extern void MFShutdown();

        [DllImport("mfplat.dll", ExactSpelling = true, PreserveSig = false)]
        public static extern void MFCreateAttributes(out IMFAttributes attributes, int initialSize);

        [DllImport("mfplat.dll", ExactSpelling = true, PreserveSig = false)]
        public static extern void MFCreateMediaType(out IMFMediaType mediaType);

        [DllImport("mfplat.dll", ExactSpelling = true, PreserveSig = false)]
        public static extern void MFCreateMemoryBuffer(int maxLength, out IMFMediaBuffer buffer);

        [DllImport("mfplat.dll", ExactSpelling = true, PreserveSig = false)]
        public static extern void MFCreateSample(out IMFSample sample);

        [DllImport("mfreadwrite.dll", ExactSpelling = true, PreserveSig = false)]
        public static extern void MFCreateSinkWriterFromURL([MarshalAs(UnmanagedType.LPWStr)] string url, IntPtr byteStream,
            IMFAttributes attributes, out IMFSinkWriter writer);

        public static long Pack(int hi, int lo) => ((long)hi << 32) | (uint)lo;
    }

    // Los métodos no usados se declaran como "huecos" para mantener el orden de la vtable.

    [ComImport, Guid("2cd2d921-c447-44a7-a13c-4adabfc247e3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMFAttributes
    {
        void GetItem(); void GetItemType(); void CompareItem(); void Compare(); void GetUINT32(); void GetUINT64();
        void GetDouble(); void GetGUID(); void GetStringLength(); void GetString(); void GetAllocatedString();
        void GetBlobSize(); void GetBlob(); void GetAllocatedBlob(); void GetUnknown(); void SetItem(); void DeleteItem();
        void DeleteAllItems();
        void SetUINT32([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, int value);
        void SetUINT64([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, long value);
        void SetDouble();
        void SetGUID([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, [In, MarshalAs(UnmanagedType.LPStruct)] Guid value);
        void SetString(); void SetBlob(); void SetUnknown(); void LockStore(); void UnlockStore(); void GetCount();
        void GetItemByIndex(); void CopyAllItems();
    }

    [ComImport, Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMFMediaType
    {
        void GetItem(); void GetItemType(); void CompareItem(); void Compare(); void GetUINT32(); void GetUINT64();
        void GetDouble(); void GetGUID(); void GetStringLength(); void GetString(); void GetAllocatedString();
        void GetBlobSize(); void GetBlob(); void GetAllocatedBlob(); void GetUnknown(); void SetItem(); void DeleteItem();
        void DeleteAllItems();
        void SetUINT32([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, int value);
        void SetUINT64([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, long value);
        void SetDouble();
        void SetGUID([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, [In, MarshalAs(UnmanagedType.LPStruct)] Guid value);
        void SetString(); void SetBlob(); void SetUnknown(); void LockStore(); void UnlockStore(); void GetCount();
        void GetItemByIndex(); void CopyAllItems();
        // IMFMediaType
        void GetMajorType(); void IsCompressedFormat(); void IsEqual(); void GetRepresentation(); void FreeRepresentation();
    }

    [ComImport, Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMFSample
    {
        void GetItem(); void GetItemType(); void CompareItem(); void Compare(); void GetUINT32(); void GetUINT64();
        void GetDouble(); void GetGUID(); void GetStringLength(); void GetString(); void GetAllocatedString();
        void GetBlobSize(); void GetBlob(); void GetAllocatedBlob(); void GetUnknown(); void SetItem(); void DeleteItem();
        void DeleteAllItems(); void SetUINT32(); void SetUINT64(); void SetDouble(); void SetGUID();
        void SetString(); void SetBlob(); void SetUnknown(); void LockStore(); void UnlockStore(); void GetCount();
        void GetItemByIndex(); void CopyAllItems();
        // IMFSample
        void GetSampleFlags(); void SetSampleFlags(); void GetSampleTime();
        void SetSampleTime(long time);
        void GetSampleDuration();
        void SetSampleDuration(long duration);
        void GetBufferCount(); void GetBufferByIndex(); void ConvertToContiguousBuffer();
        void AddBuffer(IMFMediaBuffer buffer);
    }

    [ComImport, Guid("045FA593-8799-42b8-BC8D-8968C6453507"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMFMediaBuffer
    {
        void Lock(out IntPtr buffer, out int maxLength, out int currentLength);
        void Unlock();
        void GetCurrentLength(out int length);
        void SetCurrentLength(int length);
        void GetMaxLength(out int length);
    }

    [ComImport, Guid("3137f1cd-fe5e-4805-a5d8-fb477448cb3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMFSinkWriter
    {
        void AddStream(IMFMediaType targetMediaType, out int streamIndex);
        void SetInputMediaType(int streamIndex, IMFMediaType inputMediaType, IMFAttributes encodingParameters);
        void BeginWriting();
        void WriteSample(int streamIndex, IMFSample sample);
        void SendStreamTick(int streamIndex, long timestamp);
        void PlaceMarker(); void NotifyEndOfSegment(); void Flush();
        void Finalize_();
        void GetServiceForStream(); void GetStatistics();
    }
}
