using System.Runtime.InteropServices;

namespace TabForge.Services.Video;

// Owns: the hand-written Windows Media Foundation COM declarations the video encoder (and the self-test read-back) needs.
// Does not own: any encoding logic (VideoEncoder) or settings.
// Tests: TestVideoEncoderMp4, TestVideoEncoder4k60.
/// <summary>Media Foundation entry points and GUIDs. Vtable order matters: unused methods are placeholders that keep the slots in place.</summary>
internal static class Mf
{
    public const uint Version = 0x00020070;
    public const uint FirstVideoStream = 0xFFFFFFFC, FirstAudioStream = 0xFFFFFFFD;
    public const int EndOfStream = 2;

    public static readonly Guid MajorType = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f"), Subtype = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    public static readonly Guid AvgBitrate = new("20332624-fb0d-4d9e-bd0d-cbf6786c102e"), FrameSize = new("1652c33d-d6b2-4012-b834-72030849a37d");
    public static readonly Guid FrameRate = new("c459a2e8-3d2c-4e44-b132-fee5156c7bb0"), PixelAspect = new("c6376a1e-8d0d-4027-be45-6d9a0ad39bb6");
    public static readonly Guid InterlaceMode = new("e2724bb8-e676-4806-b4b2-a8d6efb44ccd"), DefaultStride = new("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
    public static readonly Guid Mpeg2Profile = new("ad76a80b-2d5c-4e0b-b375-64e520137036");
    public static readonly Guid AudioChannels = new("37e48bf5-645e-4c5b-89de-ada9e29b696a"), AudioRate = new("5faeeae7-0290-4c31-9e8a-c534f68d9dba");
    public static readonly Guid AudioBits = new("f2deb57f-40fa-4764-aa33-ed4f2d1ff669"), AudioBlockAlign = new("322de230-9eeb-43bd-ab7a-ff412251541d");
    public static readonly Guid AudioBytesPerSec = new("1aab75c8-cfef-451c-ab95-ac034b8e1731"), AacPayload = new("bfbabe79-7434-4d1c-94f0-72a3b9e17188");
    public static readonly Guid AacProfileLevel = new("7632f0e6-9538-4d61-acda-ea29c8c14456");
    public static readonly Guid EnableHardware = new("a634a91c-822b-41b9-a494-4de4643612b0"), DisableThrottling = new("08b845d8-2b74-4afe-9d53-be16d2d5ae4f");
    public static readonly Guid Video = new("73646976-0000-0010-8000-00AA00389B71"), Audio = new("73647561-0000-0010-8000-00AA00389B71");
    public static readonly Guid H264 = new("34363248-0000-0010-8000-00AA00389B71"), Rgb32 = new("00000016-0000-0010-8000-00AA00389B71");
    public static readonly Guid Aac = new("00001610-0000-0010-8000-00AA00389B71"), Pcm = new("00000001-0000-0010-8000-00AA00389B71");
    public static readonly Guid VideoEncoderCategory = new("f79eac7d-e545-4387-bdee-d647d7bde42a");

    public static ulong Pack(int hi, int lo) => ((ulong)(uint)hi << 32) | (uint)lo;

    [StructLayout(LayoutKind.Sequential)] public struct TypeInfo { public Guid Major, Sub; }

    [DllImport("mfplat.dll")] public static extern int MFStartup(uint version, uint flags);
    [DllImport("mfplat.dll")] public static extern int MFShutdown();
    [DllImport("mfplat.dll")] public static extern int MFCreateMediaType(out IMFMediaType type);
    [DllImport("mfplat.dll")] public static extern int MFCreateSample(out IMFSample sample);
    [DllImport("mfplat.dll")] public static extern int MFCreateMemoryBuffer(int bytes, out IMFMediaBuffer buffer);
    [DllImport("mfplat.dll")] public static extern int MFCreateAttributes(out IMFAttributes attributes, int size);
    [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode)] public static extern int MFCreateSinkWriterFromURL(string url, IntPtr byteStream, IMFAttributes? attributes, out IMFSinkWriter writer);
    [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode)] public static extern int MFCreateSourceReaderFromURL(string url, IMFAttributes? attributes, out IMFSourceReader reader);
    [DllImport("mfplat.dll")] private static extern int MFTEnumEx(Guid category, uint flags, IntPtr input, ref TypeInfo output, out IntPtr activates, out uint count);
    [DllImport("ole32.dll")] private static extern void CoTaskMemFree(IntPtr p);

    public static readonly Guid FriendlyName = new("314ffbae-5b41-4c95-9c19-4e7d586face3"), HardwareVendor = new("3aecb0cc-035b-4bcc-8185-2b8d551ef3af");
    public static readonly Guid TransformAsync = new("f81a699a-649a-497d-8c73-29f8fed6ad7a");

    /// <summary>The text attribute <paramref name="key"/> of an attribute store, or "" when it is missing.</summary>
    public static string ReadString(IMFAttributes attributes, Guid key)
    {
        var text = new System.Text.StringBuilder(256);
        return attributes.GetString(key, text, text.Capacity, out _) >= 0 ? text.ToString() : "";
    }

    /// <summary>"name (vendor)" of each registered H.264 encoder for the MFTEnumEx <paramref name="flags"/> (1 sync, 2 async, 4 hardware); empty when the call fails.</summary>
    public static List<string> H264EncoderNames(uint flags)
    {
        var names = new List<string>();
        var output = new TypeInfo { Major = Video, Sub = H264 };
        if (MFTEnumEx(VideoEncoderCategory, flags, IntPtr.Zero, ref output, out var activates, out var count) < 0) return names;
        for (var i = 0; i < count; i++)
        {
            var unknown = Marshal.ReadIntPtr(activates, i * IntPtr.Size);
            try
            {
                var a = (IMFAttributes)Marshal.GetObjectForIUnknown(unknown);
                try
                {
                    var vendor = ReadString(a, HardwareVendor);
                    names.Add(ReadString(a, FriendlyName) + (vendor.Length > 0 ? $" ({vendor})" : ""));
                }
                finally { Marshal.ReleaseComObject(a); }
            }
            finally { Marshal.Release(unknown); }
        }
        if (activates != IntPtr.Zero) CoTaskMemFree(activates);
        return names;
    }

    /// <summary>True when a hardware H.264 encoder is registered.</summary>
    public static bool HardwareH264Available()
    {
        var output = new TypeInfo { Major = Video, Sub = H264 };
        if (MFTEnumEx(VideoEncoderCategory, 0x4 /* hardware */, IntPtr.Zero, ref output, out var activates, out var count) < 0) return false;
        for (var i = 0; i < count; i++) Marshal.Release(Marshal.ReadIntPtr(activates, i * IntPtr.Size));
        if (activates != IntPtr.Zero) CoTaskMemFree(activates);
        return count > 0;
    }
}

[ComImport, Guid("2cd2d921-c447-44a7-a13c-4adabfc247e3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFAttributes
{
    void GetItem(); void GetItemType(); void CompareItem(); void Compare();
    void GetUINT32(in Guid key, out int value);
    void GetUINT64(in Guid key, out ulong value);
    void GetDouble();
    void GetGUID(in Guid key, out Guid value);
    void GetStringLength();
    [PreserveSig] int GetString(in Guid key, [MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder value, int size, out int length);
    void GetAllocatedString(); void GetBlobSize(); void GetBlob(); void GetAllocatedBlob(); void GetUnknown();
    void SetItem(); void DeleteItem(); void DeleteAllItems();
    void SetUINT32(in Guid key, int value);
    void SetUINT64(in Guid key, ulong value);
    void SetDouble();
    void SetGUID(in Guid key, in Guid value);
    void SetString(); void SetBlob(); void SetUnknown(); void LockStore(); void UnlockStore();
    void GetCount();
    void GetItemByIndex();
    void CopyAllItems();
}

[ComImport, Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFMediaType : IMFAttributes
{
    void GetMajorType(); void IsCompressedFormat(); void IsEqual(); void GetRepresentation(); void FreeRepresentation();
}

[ComImport, Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFSample
{
    // The 30 IMFAttributes slots (COM interop does not inherit a base interface's slots).
    void A1(); void A2(); void A3(); void A4(); void A5(); void A6(); void A7(); void A8(); void A9(); void A10();
    void A11(); void A12(); void A13(); void A14(); void A15(); void A16(); void A17(); void A18(); void A19(); void A20();
    void A21(); void A22(); void A23(); void A24(); void A25(); void A26(); void A27(); void A28(); void A29(); void A30();
    void GetSampleFlags(); void SetSampleFlags();
    void GetSampleTime(); void SetSampleTime(long time);
    void GetSampleDuration(); void SetSampleDuration(long duration);
    void GetBufferCount(); void GetBufferByIndex();
    void ConvertToContiguousBuffer(out IMFMediaBuffer buffer);
    void AddBuffer(IMFMediaBuffer buffer);
    void RemoveBufferByIndex(); void RemoveAllBuffers(); void GetTotalLength(); void CopyToBuffer();
}

[ComImport, Guid("045FA593-8799-42b8-BC8D-8968C6453507"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFMediaBuffer
{
    void Lock(out IntPtr buffer, out int maxLength, out int currentLength);
    void Unlock();
    void GetCurrentLength();
    void SetCurrentLength(int length);
    void GetMaxLength();
}

[ComImport, Guid("3137f1cd-fe5e-4805-a5d8-fb477448cb3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFSinkWriter
{
    void AddStream(IMFMediaType targetType, out int streamIndex);
    void SetInputMediaType(int streamIndex, IMFMediaType inputType, IMFAttributes? encodingParameters);
    void BeginWriting();
    void WriteSample(int streamIndex, IMFSample sample);
    void SendStreamTick(); void PlaceMarker(); void NotifyEndOfSegment(); void Flush();
    void FinalizeWriter();
}

/// <summary>The sink writer's extra interface: the transforms (encoders) it built for a stream.</summary>
[ComImport, Guid("588d72ab-5bc1-496a-8714-b70617141b25"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFSinkWriterEx
{
    void AddStream(IMFMediaType targetType, out int streamIndex);
    void SetInputMediaType(int streamIndex, IMFMediaType inputType, IMFAttributes? encodingParameters);
    void BeginWriting();
    void WriteSample(int streamIndex, IMFSample sample);
    void SendStreamTick(); void PlaceMarker(); void NotifyEndOfSegment(); void Flush();
    void FinalizeWriter(); void GetServiceForStream(); void GetStatistics();
    [PreserveSig] int GetTransformForStream(int streamIndex, int transformIndex, out Guid category, [MarshalAs(UnmanagedType.IUnknown)] out object? transform);
}

/// <summary>Only a marker: asynchronous (hardware) transforms implement it, synchronous ones do not.</summary>
[ComImport, Guid("2cd0bd52-bcd5-4b89-b62c-eadc0c031e7d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFMediaEventGenerator { }


[ComImport, Guid("70ae66f2-c809-4e4f-8915-bdcb406b7993"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFSourceReader
{
    void GetStreamSelection(); void SetStreamSelection();
    void GetNativeMediaType(uint stream, int typeIndex, out IMFMediaType type);
    [PreserveSig] int GetCurrentMediaType(uint stream, out IMFMediaType type);
    [PreserveSig] int SetCurrentMediaType(uint stream, IntPtr reserved, IMFMediaType type);
    void SetCurrentPosition();
    void ReadSample(uint stream, int flags, out int actualStream, out int streamFlags, out long timestamp, out IMFSample? sample);
}
