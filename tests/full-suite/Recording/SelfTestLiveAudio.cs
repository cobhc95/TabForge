using System;
using System.IO;
using TabForge.Services.Video;

namespace TabForge;

// Owns: the live recording audio checks: the MP4's audio is stereo and its decoded sample count matches the duration, a tone survives the
// chunked live path without gaps, zero runs or jumps, and no high-band hiss is recorded. Also the grabber repeating the last frame.
// Does not own: the engine tap protocol or the encoder basics (SelfTestLiveVideo, SelfTestVideoEncoder).
public static partial class SelfTest
{
    /// <summary>Decodes the audio track of an MP4 to interleaved 16-bit PCM; returns (rate, channels, samples).</summary>
    private static (int Rate, int Channels, short[] Pcm) ReadAudioPcm(string path)
    {
        var (rate, channels, pcm, _) = DecodeMp4Audio(path);
        return (rate, channels, pcm);
    }

    /// <summary>Records <paramref name="seconds"/> of a 440 Hz stereo tone fed in 5 ms chunks at the given rate; returns the decoded audio.</summary>
    private static (int Rate, int Channels, short[] Pcm, double Seconds) RecordTone(int rate, double seconds, string path, bool picture = true)
    {
        var session = LiveVideoSession.Start(path, 1920, 1080, 30, b => picture);
        var chunk = rate / 200; long first = 0;
        var data = new float[chunk * 2];
        while (first < seconds * rate)
        {
            var d = new float[chunk * 2];
            for (var i = 0; i < chunk; i++) d[2 * i] = d[2 * i + 1] = 0.5f * MathF.Sin(2 * MathF.PI * 440f * (first + i) / rate);
            session.OnAudio(rate, first, d, chunk); first += chunk;
            System.Threading.Thread.Sleep(5);
        }
        var elapsed = session.Elapsed.TotalSeconds;
        session.Finish();
        var (r, c, pcm) = ReadAudioPcm(path);
        return (r, c, pcm, elapsed);
    }

    private static void TestLiveAudioContinuity()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"tf-liveaudio-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            foreach (var rate in new[] { 48000, 44100 })
            {
                var (_, c, pcm, _) = RecordTone(rate, 3.0, Path.Combine(dir, $"tone{rate}.mp4"), picture: rate == 48000 ? false : true);
                var frames = pcm.Length / Math.Max(1, c);
                Check($"live audio {rate}: the MP4 audio is stereo", c == 2, $"channels {c}");
                Check($"live audio {rate}: the decoded length matches 3 s", Math.Abs(frames / (double)rate - 3.0) < 0.1, $"{frames} frames = {frames / (double)rate:0.000} s");
                // Skip the encoder's priming and tail; look at the left channel of the middle second.
                int from = rate / 2 * 2, to = Math.Min(pcm.Length - 2, rate * 5);
                int zeroRun = 0, maxZero = 0, maxJump = 0;
                for (var i = from; i < to; i += 2)
                {
                    zeroRun = Math.Abs(pcm[i]) < 8 ? zeroRun + 1 : 0; maxZero = Math.Max(maxZero, zeroRun);
                    maxJump = Math.Max(maxJump, Math.Abs(pcm[i + 2] - pcm[i]));
                }
                // 440 Hz at 0.5 peaks near 16384; the largest step per sample is 2*pi*440/rate*16384, about 940 at 48 kHz.
                Check($"live audio {rate}: the tone has no zero runs or jumps", maxZero < 40 && maxJump < 1500, $"longest near-zero run {maxZero} samples, largest step {maxJump}");
            }
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    /// <summary>Share of the energy of the left channel (frames from..from+len) that lies in bins between 15 and 20 kHz.</summary>
    private static double HighBandShare(short[] pcm, int rate, int from, int len)
    {
        double total = 0, high = 0;
        for (var i = 0; i < len; i++) { double v = pcm[2 * (from + i)]; total += v * v; }
        for (var f = 15000.0; f <= 20000.0; f += 250)
        {
            double re = 0, im = 0, w = 2 * Math.PI * f / rate;
            for (var i = 0; i < len; i++) { double v = pcm[2 * (from + i)]; re += v * Math.Cos(w * i); im -= v * Math.Sin(w * i); }
            high += (re * re + im * im) * 2 / len / 21;   // per-bin power of a real signal, spread over the 21 probed bins
        }
        return total <= 0 ? 0 : high * 21 / total;
    }

    /// <summary>A 3 kHz tone 12 dB over full scale (the output ceiling passes floats up to 8) through the real tap and a live session decodes without high-band energy.</summary>
    private static void TestLiveAudioNoHiss()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"tf-hiss-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            foreach (var (rate, amp) in new[] { (48000, 4f), (44100, 4f), (48000, 0.5f) })
            {
                var path = Path.Combine(dir, $"hiss{rate}-{amp}.mp4");
                var session = LiveVideoSession.Start(path, 1920, 1080, 30, b => true);
                var tap = new TabForge.AudioEngine.Mixing.MasterTap { Sink = (r, first, data, frames) => session.OnAudio(r, first, data, frames) };
                tap.Enable(true, rate);
                var block = new float[480 * 2]; long n = 0;
                // 2.5 s of input at real-time pace; the check reads the second from 1 s to 2 s.
                for (var b = 0; b < 250; b++)
                {
                    for (var i = 0; i < 480; i++, n++) block[2 * i] = block[2 * i + 1] = amp * MathF.Sin(2 * MathF.PI * 3000f * n / rate);
                    tap.Write(block, 0, 480); System.Threading.Thread.Sleep(10);
                }
                System.Threading.Thread.Sleep(100); tap.Enable(false, rate); System.Threading.Thread.Sleep(100);
                session.Finish();
                var (r2, c, pcm) = ReadAudioPcm(path);
                var share = HighBandShare(pcm, r2, r2, r2);
                Check($"live audio: a {amp} amplitude tone at {rate} Hz has no hiss above 15 kHz", r2 == rate && share < 1e-4, $"rate {r2}, high-band share {share:E2}");
            }
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    private static void TestGrabberRepeatsLastFrame()
    {
        // A picture source that gives one frame and then "nothing changed": the session must keep writing the last picture.
        var dir = Path.Combine(Path.GetTempPath(), $"tf-repeat-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var calls = 0;
            var path = Path.Combine(dir, "repeat.mp4");
            using var inner = new DesktopRegionGrabber(1920, 1080, () => new ScreenRect(0, 0, 640, 360));
            var session = LiveVideoSession.Start(path, 1920, 1080, 30, b => { calls++; return inner.Grab(b); });
            var chunk = new float[480]; long first = 0;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (watch.Elapsed.TotalSeconds < 2) { session.OnAudio(48000, first, chunk, 240); first += 240; System.Threading.Thread.Sleep(5); }
            var written = session.FramesWritten;
            session.Finish();
            Check("live video: a desktop that does not change still gives a steady frame rate", written > 40, $"{written} frames of 2 s at 30 fps, {calls} grabs, source {inner.Source}");
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }
}
