using System;
using System.IO;
using System.Runtime.InteropServices;
using TabForge.Audio.Contracts;
using TabForge.Services.Video;
using EH = TabForge.AudioEngine.EngineHost.Headless;

namespace TabForge;

// Owns: the helpers the live recording checks share: stopping the headless engine and decoding the audio track of a recorded MP4.
// Does not own: the checks themselves (SelfTestLiveAudioFidelity, SelfTestLiveLoudness).
// Tests: TestLiveAudioFidelity, TestLiveRecordingLoudness.
public static partial class SelfTest
{
    private static void StopTestEngine(SharedBlock shared, string wav)
    {
        EH.Detach();
        SettleDisk();
        shared.Dispose();
        TryDelete(wav);
    }

    /// <summary>Decodes the audio track of an MP4 to interleaved 16-bit PCM; returns (rate, channels, samples).</summary>
    private static (int Rate, int Channels, short[] Pcm) ReadAudioPcm(string path)
    {
        var (rate, channels, pcm, _) = DecodeMp4Audio(path);
        return (rate, channels, pcm);
    }

    /// <summary>Decodes the audio stream to 16-bit PCM; returns the rate the reader reports, the channels, the interleaved samples and the native rate.</summary>
    private static (int Rate, int Channels, short[] Pcm, int NativeRate) DecodeMp4Audio(string path)
    {
#if WINDOWS
        Mf.MFStartup(Mf.Version, 0);
        try
        {
            Mf.MFCreateSourceReaderFromURL(path, null, out var reader);
            reader.GetNativeMediaType(Mf.FirstAudioStream, 0, out var native);
            native.GetUINT32(Mf.AudioRate, out var nativeRate);
            Mf.MFCreateMediaType(out var want);
            want.SetGUID(Mf.MajorType, Mf.Audio); want.SetGUID(Mf.Subtype, Mf.Pcm);
            want.SetUINT32(Mf.AudioBits, 16);
            reader.SetCurrentMediaType(Mf.FirstAudioStream, IntPtr.Zero, want);
            reader.GetCurrentMediaType(Mf.FirstAudioStream, out var got);
            got.GetUINT32(Mf.AudioRate, out var rate); got.GetUINT32(Mf.AudioChannels, out var channels);
            var bytes = new System.Collections.Generic.List<byte>();
            while (true)
            {
                reader.ReadSample(Mf.FirstAudioStream, 0, out _, out var flags, out _, out var sample);
                if (sample is not null)
                {
                    sample.ConvertToContiguousBuffer(out var buffer);
                    buffer.Lock(out var ptr, out _, out var length);
                    var chunk = new byte[length]; Marshal.Copy(ptr, chunk, 0, length); bytes.AddRange(chunk);
                    buffer.Unlock(); Marshal.ReleaseComObject(buffer); Marshal.ReleaseComObject(sample);
                }
                if ((flags & Mf.EndOfStream) != 0) break;
            }
            Marshal.ReleaseComObject(reader);
            var pcm = new short[bytes.Count / 2];
            Buffer.BlockCopy(bytes.ToArray(), 0, pcm, 0, pcm.Length * 2);
            return (rate, channels, pcm, nativeRate);
        }
        finally { Mf.MFShutdown(); }
#else
        throw new PlatformNotSupportedException("Decoding MP4 audio needs Media Foundation, which exists on Windows only.");
#endif
    }
}
