using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using TabForge.Services.Video;

namespace TabForge;

/// <summary>
/// Encoder measurement harness (<c>TestVideoEncoderBenchmark</c>, runs only with <c>--only</c>): a procedural clip of 120 frames at
/// 1080p and 4K, 30 and 60 fps, encoded through <see cref="VideoEncoder"/>. Logs 'bench:' lines (write-loop and end-to-end ms per
/// frame, finalise time, size, bitrate, hardware flag, luma PSNR and SSIM against the source, first audio against first video
/// timestamp) and checks loose thresholds. Owns the clip generator, the timing and the decode-and-compare. Does not own encoder
/// settings: a later options object reaches the encoder through the <c>open</c> parameter of <see cref="RunVideoBench"/>.
/// </summary>
public static partial class SelfTest
{
    private const int BenchFrames = 120;
    private const int BenchSampleEvery = 10;
    private static readonly Guid BenchNv12 = new("3231564e-0000-0010-8000-00aa00389b71");   // MFVideoFormat_NV12

    /// <summary>The encoder under test. Today's default settings; a later options object supplies its own opener.</summary>
    private static Func<string, int, int, int, VideoAudioFormat, VideoEncoder> BenchOpenWith(VideoEncoderOptions options, bool live = false) =>
        (path, width, height, fps, audio) => VideoEncoder.Open(path, width, height, fps, audio, live, options);

    private static VideoEncoder BenchDefaultOpen(string path, int width, int height, int fps, VideoAudioFormat audio) =>
        VideoEncoder.Open(path, width, height, fps, audio, live: false);

    private static void TestVideoEncoderBenchmark()
    {
        if (_only is null) { Log.Add("  info  TestVideoEncoderBenchmark is a measurement run: start it with --only TestVideoEncoderBenchmark"); return; }
        RunVideoBench("1080p30", 1920, 1080, 30, BenchDefaultOpen);
        RunVideoBench("1080p60", 1920, 1080, 60, BenchDefaultOpen);
        RunVideoBench("4K30", 3840, 2160, 30, BenchDefaultOpen);
        RunVideoBench("4K60", 3840, 2160, 60, BenchDefaultOpen);
        Log.Add("bench: MFTEnumEx H.264 encoders, hardware flag (4): [" + string.Join("; ", Mf.H264EncoderNames(4)) + "]");
        Log.Add("bench: MFTEnumEx H.264 encoders, sync (1): [" + string.Join("; ", Mf.H264EncoderNames(1)) + "]");
        Log.Add("bench: MFTEnumEx H.264 encoders, async (2): [" + string.Join("; ", Mf.H264EncoderNames(2)) + "]");
        foreach (var (label, w, h, fps) in new[] { ("1080p60", 1920, 1080, 60), ("4K60", 3840, 2160, 60) })
        {
            RunVideoBench(label + " software", w, h, fps, BenchOpenWith(new VideoEncoderOptions(VideoEncoderChoices.Software)));
            RunVideoBench(label + " hardware", w, h, fps, BenchOpenWith(new VideoEncoderOptions(VideoEncoderChoices.Hardware)));
            RunVideoBench(label + " fast-export", w, h, fps, BenchOpenWith(new VideoEncoderOptions(FastExport: true)));
            RunVideoBench(label + " fast-export+software", w, h, fps, BenchOpenWith(new VideoEncoderOptions(VideoEncoderChoices.Software, FastExport: true)));
            // Low latency is a live option; the bench writes offline (no dropped frames), so the same property set is passed as extras.
            RunVideoBench(label + " low-latency", w, h, fps, BenchOpenWith(new VideoEncoderOptions { Extra = VideoEncoderOptions.LowLatencySet }));
        }
    }

    /// <summary>Encodes the clip with <paramref name="open"/>, decodes it back, logs the 'bench:' lines and runs the checks.</summary>
    private static void RunVideoBench(string label, int w, int h, int fps, Func<string, int, int, int, VideoAudioFormat, VideoEncoder> open)
    {
        var path = Path.Combine(Path.GetTempPath(), $"tf-bench-{Guid.NewGuid():N}.mp4");
        try
        {
            var audio = new VideoAudioFormat(48000, 2);
            var perFrame = audio.SampleRate / fps;
            var frame = new byte[w * h * 4];
            var tone = new float[perFrame * 2];
            double loopMs, finaliseMs;
            bool hardware;
            string encoderName;
            string notes;
            long dropped;
            using (var enc = open(path, w, h, fps, audio))
            {
                loopMs = 0;
                long n = 0;
                for (var f = 0; f < BenchFrames; f++)
                {
                    BenchSourceFrame(frame, w, h, f);
                    for (var i = 0; i < perFrame; i++, n++) tone[i * 2] = tone[i * 2 + 1] = 0.3f * MathF.Sin(2 * MathF.PI * 440 * n / audio.SampleRate);
                    var t0 = Stopwatch.GetTimestamp();
                    enc.WriteFrame(frame, TimeSpan.FromTicks(f * 10_000_000L / fps));
                    enc.WriteAudio(tone);
                    loopMs += Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
                }
                hardware = enc.UsesHardware;
                encoderName = enc.EncoderName;
                notes = string.Join("; ", enc.Notes);
                var t1 = Stopwatch.GetTimestamp();
                enc.Finish();
                finaliseMs = Stopwatch.GetElapsedTime(t1).TotalMilliseconds;
                dropped = enc.DroppedFrames;
            }
            var size = File.Exists(path) ? new FileInfo(path).Length : 0;
            var kbps = size * 8 / (BenchFrames / (double)fps) / 1000;
            Log.Add($"bench: {label} write loop {loopMs / BenchFrames:0.00} ms/frame ({loopMs:0} ms) finalise {finaliseMs:0} ms end-to-end {(loopMs + finaliseMs) / BenchFrames:0.00} ms/frame size {size} B bitrate {kbps:0} kbit/s hardware={hardware} encoder={encoderName} dropped={dropped} notes=[{notes}]");

            var r = BenchReadBackCompare(path, w, h);
            Log.Add($"bench: {label} quality luma PSNR min {r.PsnrMin:0.00} dB mean {r.PsnrMean:0.00} dB, SSIM {r.Ssim:0.0000} over {r.Sampled} sampled frames, matrix {(r.Bt709 ? "BT.709" : "BT.601")}");
            Log.Add($"bench: {label} decoded frames {r.Frames} of {BenchFrames}, audio stream {r.Audio}, first audio minus first video {r.SyncMs:0.0} ms");

            Check($"{label}: MP4 is not empty and decodes {BenchFrames} frames (the encoded count)", size > 0 && r.Frames == BenchFrames, $"size {size}, frames {r.Frames}");
            Check($"{label}: an audio stream is present", r.Audio);
            Check($"{label}: luma PSNR at least 30 dB on every sampled frame", r.PsnrMin >= 30, $"min {r.PsnrMin:0.00} dB");
            Check($"{label}: first audio sample within 50 ms of the first video sample", Math.Abs(r.SyncMs) < 50, $"{r.SyncMs:0.0} ms");
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    /// <summary>What the decode-and-compare measured.</summary>
    private readonly record struct BenchReadBack(long Frames, bool Audio, double SyncMs, double PsnrMin, double PsnrMean, double Ssim, int Sampled, bool Bt709);

    /// <summary>
    /// Reads the MP4 back as decoded NV12 and compares the luma plane of every <see cref="BenchSampleEvery"/>th frame with the
    /// regenerated source. The luma matrix (BT.601 or BT.709) is the one that fits the first sampled frame best.
    /// </summary>
    private static BenchReadBack BenchReadBackCompare(string path, int w, int h)
    {
        Mf.MFStartup(Mf.Version, 0);
        try
        {
            Mf.MFCreateSourceReaderFromURL(path, null, out var reader);
            // Without this the reader hands out the compressed H.264 samples (about 50 KB each), not pixels.
            Mf.MFCreateMediaType(out var type);
            type.SetGUID(Mf.MajorType, Mf.Video);
            type.SetGUID(Mf.Subtype, BenchNv12);
            var hr = reader.SetCurrentMediaType(Mf.FirstVideoStream, IntPtr.Zero, type);
            if (hr < 0) throw new InvalidOperationException($"the decoder would not output NV12 (0x{hr:X8})");
            reader.ReadSample(Mf.FirstAudioStream, 0, out _, out _, out var audioTs, out var audioSample);
            var hasAudio = audioSample is not null;
            if (audioSample is not null) Marshal.ReleaseComObject(audioSample);

            var nv12 = new byte[w * h * 3 / 2];
            var src = new byte[w * h * 4];
            var srcY = new byte[w * h];
            var bt709 = false;
            long frames = 0, videoFirstTs = 0;
            var psnrMin = double.MaxValue;
            double psnrSum = 0, ssimSum = 0;
            var sampled = 0;
            while (true)
            {
                reader.ReadSample(Mf.FirstVideoStream, 0, out _, out var flags, out var ts, out var sample);
                if (sample is not null)
                {
                    if (frames == 0) videoFirstTs = ts;
                    if (frames % BenchSampleEvery == 0)
                    {
                        BenchCopy(sample, nv12);
                        var y = nv12.AsSpan(0, w * h);
                        BenchSourceFrame(src, w, h, (int)frames);
                        if (sampled == 0)   // the first sampled frame decides which luma matrix the encoder's converter used
                        {
                            BenchLuma(src, w, h, false, srcY);
                            var bt601 = BenchPsnr(srcY, y);
                            BenchLuma(src, w, h, true, srcY);
                            bt709 = BenchPsnr(srcY, y) > bt601;
                        }
                        BenchLuma(src, w, h, bt709, srcY);
                        var psnr = BenchPsnr(srcY, y);
                        psnrMin = Math.Min(psnrMin, psnr);
                        psnrSum += psnr;
                        ssimSum += BenchSsim(srcY, y, w, h);
                        sampled++;
                    }
                    frames++;
                    Marshal.ReleaseComObject(sample);
                }
                if ((flags & Mf.EndOfStream) != 0) break;
            }
            Marshal.ReleaseComObject(reader);
            return new BenchReadBack(frames, hasAudio, hasAudio ? (audioTs - videoFirstTs) / 10_000.0 : double.NaN,
                psnrMin, psnrSum / sampled, ssimSum / sampled, sampled, bt709);
        }
        finally { Mf.MFShutdown(); }
    }

    /// <summary>Copies a decoded sample's contiguous buffer into <paramref name="dst"/>, as the decoder laid it out.</summary>
    private static void BenchCopy(IMFSample sample, byte[] dst)
    {
        sample.ConvertToContiguousBuffer(out var buffer);
        try
        {
            buffer.Lock(out var p, out _, out var len);
            try { Marshal.Copy(p, dst, 0, Math.Min(len, dst.Length)); }
            finally { buffer.Unlock(); }
        }
        finally { Marshal.ReleaseComObject(buffer); }
    }

    /// <summary>
    /// A procedural top-down BGRA frame: a drifting gradient, a moving white block, a moving dark block and a band of
    /// text-like cells that scrolls sideways. A pure function of the frame index, so nothing is stored.
    /// </summary>
    private static void BenchSourceFrame(byte[] bgra, int w, int h, int f)
    {
        var boxX = f * 8 % (w - 200);
        var boxY = h / 3;
        var dropX = w * 2 / 3;
        var dropY = f * 5 % (h - 100);
        var bandTop = h * 2 / 3;
        var bandBottom = bandTop + h / 6;
        for (var y = 0; y < h; y++)
        {
            var row = y * w * 4;
            var inBox = y >= boxY && y < boxY + 120;
            var inDrop = y >= dropY && y < dropY + 100;
            var inBand = y >= bandTop && y < bandBottom;
            for (var x = 0; x < w; x++)
            {
                var o = row + x * 4;
                byte r = (byte)((x * 255 / w + f) & 255), g = (byte)((y * 255 / h + 2 * f) & 255), b = (byte)((2 * f) & 255);
                if (inBox && x >= boxX && x < boxX + 200) r = g = b = 255;
                else if (inDrop && x >= dropX && x < dropX + 160) r = g = b = 20;
                else if (inBand)
                {
                    var hash = unchecked((uint)((x + 2 * f) / 6) * 374761393u + (uint)(y / 10) * 668265263u);
                    hash = unchecked((hash ^ (hash >> 13)) * 1274126177u);
                    if (((hash >> 16) & 3) == 0) r = g = b = (byte)(200 + ((hash >> 8) & 31));   // one cell in four is ink
                }
                bgra[o] = b; bgra[o + 1] = g; bgra[o + 2] = r; bgra[o + 3] = 255;
            }
        }
    }

    /// <summary>Limited-range (16..235) luma of a BGRA frame: BT.601 when <paramref name="bt709"/> is false, else BT.709.</summary>
    private static void BenchLuma(byte[] bgra, int w, int h, bool bt709, byte[] y)
    {
        for (var i = 0; i < w * h; i++)
        {
            var s = i * 4;
            var r = bgra[s + 2]; var g = bgra[s + 1]; var b = bgra[s];
            y[i] = (byte)(bt709 ? 16 + ((47 * r + 157 * g + 16 * b + 128) >> 8) : 16 + ((66 * r + 129 * g + 25 * b + 128) >> 8));
        }
    }

    /// <summary>PSNR in dB between two 8-bit planes; identical planes are capped at 100 dB.</summary>
    private static double BenchPsnr(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        double se = 0;
        for (var i = 0; i < a.Length; i++) { var d = a[i] - b[i]; se += d * d; }
        var mse = se / a.Length;
        return mse <= 1e-9 ? 100 : 10 * Math.Log10(255.0 * 255.0 / mse);
    }

    /// <summary>Mean SSIM over non-overlapping 8x8 luma blocks (the standard constants for 8-bit data).</summary>
    private static double BenchSsim(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b, int w, int h)
    {
        const double c1 = 6.5025, c2 = 58.5225;   // (0.01 * 255)^2 and (0.03 * 255)^2
        double sum = 0;
        var blocks = 0;
        for (var by = 0; by + 8 <= h; by += 8)
            for (var bx = 0; bx + 8 <= w; bx += 8)
            {
                double ma = 0, mb = 0;
                for (var y = 0; y < 8; y++)
                    for (var x = 0; x < 8; x++) { var o = (by + y) * w + bx + x; ma += a[o]; mb += b[o]; }
                ma /= 64; mb /= 64;
                double va = 0, vb = 0, cov = 0;
                for (var y = 0; y < 8; y++)
                    for (var x = 0; x < 8; x++)
                    {
                        var o = (by + y) * w + bx + x;
                        double da = a[o] - ma, db = b[o] - mb;
                        va += da * da; vb += db * db; cov += da * db;
                    }
                va /= 63; vb /= 63; cov /= 63;
                sum += (2 * ma * mb + c1) * (2 * cov + c2) / ((ma * ma + mb * mb + c1) * (va + vb + c2));
                blocks++;
            }
        return sum / blocks;
    }
}
