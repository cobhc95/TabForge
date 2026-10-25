using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using TabForge.Services.Video;

namespace TabForge;

// Owns: the golden check of the MP4 writer: a fixed moving synthetic clip (1080p, 60 frames, a stereo tone) through the real
// VideoEncoder, decoded back with the Media Foundation source reader, one digest per stream over every sample (timestamp, length, bytes).
// Does not own: queue, drop and setting behaviour (SelfTestVideoEncoder).
public static partial class SelfTest
{
    private const int GoldenWidth = 1920, GoldenHeight = 1080, GoldenFps = 60, GoldenFrames = 60;

    // Recorded from the encoder before the frame-copy change (docs/VIDEO_RENDER_SPEED.md, option 2). A different value means the MP4 changed.
    /// <summary>The video digest was recorded with a hardware encoder; another kind of encoder (a software one on a build machine) writes other bytes, so it is compared only when the same kind ran.</summary>
    private const bool GoldenVideoWasHardware = true;
    private const string GoldenVideoDigest = "5FCA28DBB720739A45247E51639C406D648072B7A73248DC933552C47E310F8D";
    private const string GoldenAudioDigest = "D0AD638BF4FBE24993E8DF3F0851D2A969F18832C9D46527CC35031155741036";

    private static void FillGoldenFrame(byte[] px, int frame)
    {
        for (var y = 0; y < GoldenHeight; y++)
            for (var x = 0; x < GoldenWidth; x++)
            {
                var o = (y * GoldenWidth + x) * 4;
                px[o] = (byte)((x * 255 / GoldenWidth + 2 * frame) & 255);
                px[o + 1] = (byte)((y * 255 / GoldenHeight + 2 * frame) & 255);
                px[o + 2] = (byte)((frame * 4) & 255);
                px[o + 3] = 255;
            }
    }

    /// <summary>Encodes the golden clip; returns whether a hardware encoder was used (the digests are only fixed for the same encoder).</summary>
    private static bool EncodeGoldenClip(string path, VideoEncoderOptions? options = null)
    {
        var audio = new VideoAudioFormat(48000, 2);
        var px = new byte[GoldenWidth * GoldenHeight * 4];
        var perFrame = audio.SampleRate / GoldenFps;
        var tone = new float[perFrame * 2];
        using var enc = VideoEncoder.Open(path, GoldenWidth, GoldenHeight, GoldenFps, audio, options: options);
        var n = 0L;
        for (var f = 0; f < GoldenFrames; f++)
        {
            FillGoldenFrame(px, f);
            enc.WriteFrame(px, TimeSpan.FromTicks(f * 10_000_000L / GoldenFps));
            for (var i = 0; i < perFrame; i++, n++) tone[i * 2] = tone[i * 2 + 1] = 0.3f * MathF.Sin(2 * MathF.PI * 440 * n / audio.SampleRate);
            enc.WriteAudio(tone);
        }
        var hardware = enc.UsesHardware;
        enc.Finish();
        return hardware;
    }

    /// <summary>Reads every sample of one stream back and hashes (timestamp, length, bytes) in order; returns the digest and the sample count.</summary>
    private static (string Digest, int Count) DecodeGoldenStream(string path, uint stream)
    {
        Mf.MFStartup(Mf.Version, 0);
        try
        {
            Mf.MFCreateSourceReaderFromURL(path, null, out var reader);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var count = 0;
            var stamp = new byte[12];
            while (true)
            {
                reader.ReadSample(stream, 0, out _, out var flags, out var time, out var sample);
                if (sample is not null)
                {
                    sample.ConvertToContiguousBuffer(out var buffer);
                    buffer.Lock(out var ptr, out _, out var length);
                    var bytes = new byte[length];
                    Marshal.Copy(ptr, bytes, 0, length);
                    buffer.Unlock();
                    BitConverter.TryWriteBytes(stamp.AsSpan(0, 8), time);
                    BitConverter.TryWriteBytes(stamp.AsSpan(8, 4), length);
                    hash.AppendData(stamp);
                    hash.AppendData(bytes);
                    count++;
                    Marshal.ReleaseComObject(buffer);
                    Marshal.ReleaseComObject(sample);
                }
                if ((flags & Mf.EndOfStream) != 0) break;
            }
            Marshal.ReleaseComObject(reader);
            return (Convert.ToHexString(hash.GetHashAndReset()), count);
        }
        finally { Mf.MFShutdown(); }
    }

    /// <summary>The reference count of a buffer alone, while a sample holds it, and after the sample is released.</summary>
    private static (int Alone, int Held, int Released) BufferRefCounts()
    {
        Mf.MFStartup(Mf.Version, 0);
        try
        {
            Mf.MFCreateMemoryBuffer(16, out var buffer);
            var alone = VideoEncoder.ComRefCount(buffer);
            Mf.MFCreateSample(out var sample);
            sample.AddBuffer(buffer);
            var held = VideoEncoder.ComRefCount(buffer);
            Marshal.ReleaseComObject(sample);
            var released = VideoEncoder.ComRefCount(buffer);
            Marshal.ReleaseComObject(buffer);
            return (alone, held, released);
        }
        finally { Mf.MFShutdown(); }
    }

    private static void TestVideoEncoderGolden()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tf-golden-{Guid.NewGuid():N}.mp4");
        try
        {
            var hardware = EncodeGoldenClip(path);
            var (video, videoCount) = DecodeGoldenStream(path, Mf.FirstVideoStream);
            var (audio, audioCount) = DecodeGoldenStream(path, Mf.FirstAudioStream);
            Check("video golden: 60 video frames and audio chunks decode back", videoCount == GoldenFrames && audioCount > 0, $"video {videoCount}, audio {audioCount}, hardware {hardware}");
            Check("video golden: decoded frames and timestamps match the recorded digest", video == GoldenVideoDigest || hardware != GoldenVideoWasHardware, $"video {video}, hardware {hardware}");
            Check("video golden: decoded audio matches the recorded digest", audio == GoldenAudioDigest, $"audio {audio}, hardware {hardware}");
            var (alone, held, released) = BufferRefCounts();
            Check("video golden: the buffer reference count sees a sample holding a buffer", alone >= 1 && held == alone + 1 && released == alone, $"alone {alone}, held {held}, released {released}");
        }
        finally { TryDelete(path);
 }
    }
}
