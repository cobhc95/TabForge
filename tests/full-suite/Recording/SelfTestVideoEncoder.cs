using System;
using System.IO;
using TabForge.Services.Video;

namespace TabForge;

/// <summary>
/// The MP4 video encoder: 1 s of gradient plus tone at 1080p30 read back with the Media Foundation source reader (frames within 1,
/// audio stream present), a 4K 60 clip, the live-mode drop count and the unsupported-setting errors (part of <see cref="SelfTest"/>).
/// </summary>
public static partial class SelfTest
{
    private static (long Video, bool Audio) ReadBackMp4(string path)
    {
        Mf.MFStartup(Mf.Version, 0);
        try
        {
            Mf.MFCreateSourceReaderFromURL(path, null, out var reader);
            var audio = reader.GetCurrentMediaType(Mf.FirstAudioStream, out _) >= 0;
            long frames = 0;
            while (true)
            {
                reader.ReadSample(Mf.FirstVideoStream, 0, out _, out var flags, out _, out var sample);
                if (sample is not null) { frames++; System.Runtime.InteropServices.Marshal.ReleaseComObject(sample); }
                if ((flags & Mf.EndOfStream) != 0) break;
            }
            System.Runtime.InteropServices.Marshal.ReleaseComObject(reader);
            return (frames, audio);
        }
        finally { Mf.MFShutdown(); }
    }

    /// <summary>Encodes <paramref name="seconds"/> of gradient frames with a sine tone; returns the encoder's hardware flag.</summary>
    private static bool EncodeTestClip(string path, int w, int h, int fps, double seconds, bool live, out long dropped)
    {
        var audio = new VideoAudioFormat(48000, 2);
        using var enc = VideoEncoder.Open(path, w, h, fps, audio, live);
        var frame = new byte[w * h * 4];
        var count = (int)Math.Round(seconds * fps);
        var perFrame = audio.SampleRate / fps;
        var tone = new float[perFrame * 2];
        var n = 0L;
        for (var f = 0; f < count; f++)
        {
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x += 16)
                {
                    var o = (y * w + x) * 4;
                    frame[o] = (byte)(x * 255 / w); frame[o + 1] = (byte)(y * 255 / h); frame[o + 2] = (byte)(f * 255 / count); frame[o + 3] = 255;
                }
            enc.WriteFrame(frame, TimeSpan.FromTicks(f * 10_000_000L / fps));
            for (var i = 0; i < perFrame; i++, n++) tone[i * 2] = tone[i * 2 + 1] = 0.3f * MathF.Sin(2 * MathF.PI * 440 * n / audio.SampleRate);
            enc.WriteAudio(tone);
        }
        var hardware = enc.UsesHardware;
        enc.Finish();
        dropped = enc.DroppedFrames;
        return hardware;
    }

    private static void TestVideoEncoderMp4()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tf-video-{Guid.NewGuid():N}.mp4");
        try
        {
            var hardware = EncodeTestClip(path, 1920, 1080, 30, 1, live: false, out _);
            var size = File.Exists(path) ? new FileInfo(path).Length : 0;
            Check("video encoder: 1080p30 MP4 exists and is not empty", size > 0, $"size {size}");
            var (frames, audio) = ReadBackMp4(path);
            Check("video encoder: 30 frames read back (within 1) and an audio stream is present", Math.Abs(frames - 30) <= 1 && audio, $"frames {frames}, audio {audio}, hardware {hardware}");
            Check("video encoder: bitrate grows with size and frame rate", VideoEncoder.VideoBitrate(3840, 2160, 60) > VideoEncoder.VideoBitrate(1920, 1080, 60) && VideoEncoder.VideoBitrate(1920, 1080, 60) > VideoEncoder.VideoBitrate(1920, 1080, 30));

            var rejected = "";
            try { VideoEncoder.Open(path, 1280, 720, 30).Dispose(); } catch (VideoEncoderException e) { rejected = e.Message; }
            Check("video encoder: an unsupported size is a clear error", rejected.Contains("1280x720"));
            rejected = "";
            try { VideoEncoder.Open(path, 1920, 1080, 24).Dispose(); } catch (VideoEncoderException e) { rejected = e.Message; }
            Check("video encoder: an unsupported frame rate is a clear error", rejected.Contains("24"));

            // Live mode: a burst bigger than the queue never blocks the caller and every frame is either written or counted.
            using var live = VideoEncoder.Open(path, 1920, 1080, 30, null, live: true);
            var burst = new byte[1920 * 1080 * 4];
            var written = 0;
            for (var f = 0; f < 60; f++) if (live.WriteFrame(burst, TimeSpan.FromTicks(f * 333_333L))) written++;
            live.Finish();
            Check("video encoder: live mode counts dropped frames and keeps the rest", live.DroppedFrames == 60 - written && live.DroppedFrames > 0, $"written {written}, dropped {live.DroppedFrames}");

            // After Finish a write is a clear encoder error, never a raw collection exception, and a second Finish stays quiet.
            var late = "";
            try { live.WriteFrame(burst, TimeSpan.Zero); } catch (VideoEncoderException) { late = "encoder"; } catch (Exception e) { late = e.GetType().Name; }
            var lateAudio = "";
            try { live.WriteAudio(new float[8]); } catch (VideoEncoderException) { lateAudio = "encoder"; } catch (Exception e) { lateAudio = e.GetType().Name; }
            Check("video encoder: a frame written after Finish throws VideoEncoderException", late == "encoder", late);
            Check("video encoder: audio written after Finish does not throw a raw exception", lateAudio is "" or "encoder", lateAudio);
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    private static void TestVideoEncoder4k60()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tf-video4k-{Guid.NewGuid():N}.mp4");
        try
        {
            var hardware = EncodeTestClip(path, 3840, 2160, 60, 0.5, live: false, out _);
            var (frames, audio) = ReadBackMp4(path);
            Check($"video encoder: 4K 60 clip encodes and reads back (hardware encoder: {hardware})", File.Exists(path) && Math.Abs(frames - 30) <= 1 && audio, $"frames {frames}, audio {audio}, hardware {hardware}");
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
