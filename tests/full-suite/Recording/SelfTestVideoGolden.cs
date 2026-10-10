using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Linq;
using System.Security.Cryptography;
using TabForge.Services;
using TabForge.Services.Video;

namespace TabForge;

// Owns: the golden check of the MP4 writer: a fixed moving synthetic clip (1080p, 60 frames, a stereo tone) through the real
// VideoEncoder, decoded back with the Media Foundation source reader, one digest per stream over every sample (timestamp, length, bytes).
// Also owns the checks of the experimental encoder options (TestVideoEncoderOptions): defaults leave the MP4 unchanged, each option still writes a playable
// clip in sync, a rejected property and a failing hardware start never fail an export, and the settings are bounded.
// Does not own: queue, drop and setting behaviour (SelfTestVideoEncoder).
public static partial class SelfTest
{
    private const int GoldenWidth = 1920, GoldenHeight = 1080, GoldenFps = 60, GoldenFrames = 60;

    // Recorded from the encoder before the frame-copy change (docs/VIDEO_RENDER_SPEED.md, option 2). A different value means the MP4 changed.
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
            Check("video golden: decoded frames and timestamps match the recorded digest", video == GoldenVideoDigest, $"video {video}, hardware {hardware}");
            Check("video golden: decoded audio matches the recorded digest", audio == GoldenAudioDigest, $"audio {audio}, hardware {hardware}");
            var (alone, held, released) = BufferRefCounts();
            Check("video golden: the buffer reference count sees a sample holding a buffer", alone >= 1 && held == alone + 1 && released == alone, $"alone {alone}, held {held}, released {released}");
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    private static void TestVideoEncoderOptions()
    {
        var def = new VideoEncoderOptions();
        Check("encoder options: default is Auto with nothing asked of the encoder", def.Encoder == VideoEncoderChoices.Auto && def.Properties(false).Count == 0 && def.Properties(true).Count == 0);
        var fast = new VideoEncoderOptions(FastExport: true);
        var low = new VideoEncoderOptions(LowLatency: true);
        Check("encoder options: fast export asks offline only, low latency live only", fast.Properties(false).Count > 0 && fast.Properties(true).Count == 0 && low.Properties(true).Count > 0 && low.Properties(false).Count == 0);

        // Bounds: an unknown choice falls back to Auto, the module normalises the stored value and the rows exist, experimental and off.
        Check("encoder options: an unknown encoder choice is Auto", VideoEncoderChoices.Normalize("bogus") == VideoEncoderChoices.Auto && VideoEncoderChoices.Normalize(null) == VideoEncoderChoices.Auto && VideoEncoderChoices.Normalize("Hardware") == VideoEncoderChoices.Hardware);
        var settings = new AppSettings { LiveVideo = new LiveVideoSettings { Encoder = "bogus" } };
        new VideoFeatureModule().Normalize(settings);
        Check("encoder options: the module normalises a bad stored encoder", settings.LiveVideo!.Encoder == VideoEncoderChoices.Auto);
        var rows = new VideoFeatureModule().SettingRows(new AppSettings()).Where(r => r.Key is "video.encoder" or "video.fastexport" or "video.lowlatency").ToList();
        Check("encoder options: three rows, labelled experimental, off by default",
            rows.Count == 3 && rows.All(r => r.Title.EndsWith("(experimental)")) && (string)rows.Single(r => r.Key == "video.encoder").Get()! == VideoEncoderChoices.Auto
            && rows.Where(r => r.Key != "video.encoder").All(r => r.Get() is false));
        var encoderRow = rows.Single(r => r.Key == "video.encoder");
        var rowSettings = new AppSettings();
        var row = new VideoFeatureModule().SettingRows(rowSettings).Single(r => r.Key == "video.encoder");
        row.Set("nonsense");
        Check("encoder options: setting the row to a bad value stores Auto", rowSettings.LiveVideo!.Encoder == VideoEncoderChoices.Auto && encoderRow.Choices.SequenceEqual(VideoEncoderChoices.All));

        // Default options reproduce the golden clip.
        var path = Path.Combine(Path.GetTempPath(), $"tf-vopt-{Guid.NewGuid():N}.mp4");
        try
        {
            var hardware = EncodeGoldenClip(path, def);
            var (video, count) = DecodeGoldenStream(path, Mf.FirstVideoStream);
            var (audio, _) = DecodeGoldenStream(path, Mf.FirstAudioStream);
            Check("encoder options: default options give the golden video digest", count == GoldenFrames && video == GoldenVideoDigest, $"video {video}, hardware {hardware}");
            Check("encoder options: default options give the golden audio digest", audio == GoldenAudioDigest, audio);
        }
        finally { try { File.Delete(path); } catch (IOException) { } }

        // Each option on a clip: frame count, audio present, picture close to the source, sync under 50 ms (RunVideoBench checks these).
        RunVideoBench("options software", 1920, 1080, 30, BenchOpenWith(new VideoEncoderOptions(VideoEncoderChoices.Software)));
        RunVideoBench("options hardware", 1920, 1080, 30, BenchOpenWith(new VideoEncoderOptions(VideoEncoderChoices.Hardware)));
        RunVideoBench("options fast export", 1920, 1080, 30, BenchOpenWith(fast));
        RunVideoBench("options low latency set", 1920, 1080, 30, BenchOpenWith(new VideoEncoderOptions { Extra = VideoEncoderOptions.LowLatencySet }));

        // A property the encoder does not know: the export carries on (the note records it when the writer refused it).
        var bogus = new VideoEncoderOptions { Extra = new[] { ("Bogus", new Guid("12345678-1234-1234-1234-123456789abc"), 7) } };
        RunVideoBench("options rejected property", 1920, 1080, 30, BenchOpenWith(bogus));

        // A hardware start that fails: offline retries with software; live with an explicit Hardware choice reports the error; live Auto keeps today's retry.
        var fail = new VideoEncoderOptions(VideoEncoderChoices.Hardware) { SimulateHardwareFailure = true };
        RunVideoBench("options hardware failure offline", 1920, 1080, 30, BenchOpenWith(fail));
        var failPath = Path.Combine(Path.GetTempPath(), $"tf-vopt-{Guid.NewGuid():N}.mp4");
        try
        {
            string? liveError = null;
            try { VideoEncoder.Open(failPath, 1920, 1080, 30, null, live: true, options: fail).Dispose(); } catch (VideoEncoderException e) { liveError = e.Message; }
            Check("encoder options: live Hardware that fails to start reports the error", liveError is not null, liveError);
            using var auto = VideoEncoder.Open(failPath, 1920, 1080, 30, null, live: true, options: new VideoEncoderOptions { SimulateHardwareFailure = true });
            Check("encoder options: live Auto still retries with software", !auto.UsesHardware && (auto.Notes.Any(n => n.Contains("retrying")) || !Mf.HardwareH264Available()), string.Join("; ", auto.Notes));
        }
        finally { try { File.Delete(failPath); } catch (IOException) { } }
    }
}
