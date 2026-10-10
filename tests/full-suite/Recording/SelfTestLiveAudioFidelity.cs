using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using TabForge.Audio.Contracts;
using TabForge.Services.Video;
using EH = TabForge.AudioEngine.EngineHost.Headless;

namespace TabForge;

// Owns: the whole live recording audio path against ground truth: the real engine (Null driver, which reads the mix through the same
// byte[] wrapper as WASAPI, ASIO and DirectSound) plays a known harmonic chord; the tap's output and the decoded MP4 are fitted to it.
// Does not own: the encoder basics (SelfTestVideoEncoder), the tap protocol (SelfTestLiveVideo), the tone-only checks (SelfTestLiveAudio).
public static partial class SelfTest
{
    private const int ChordPeriod = 480;   // 100 Hz at 48 kHz: every partial is a multiple of 100 Hz, so the chord repeats every 480 frames

    private static float ChordSample(long n)
    {
        var v = 0.0;
        for (var k = 1; k <= 12; k++) v += Math.Sin(2 * Math.PI * 100 * k * n / 48000.0 + k) / k;
        return (float)(0.05 * v);
    }

    private static string WriteChordWav(double seconds)
    {
        var path = Path.Combine(Path.GetTempPath(), $"tf-selftest-{Guid.NewGuid():N}.wav");
        using var writer = new NAudio.Wave.WaveFileWriter(path, NAudio.Wave.WaveFormat.CreateIeeeFloatWaveFormat(48000, 2));
        var block = new float[4800 * 2];
        for (var done = 0; done < seconds * 48000; done += 4800)
        {
            for (var i = 0; i < 4800; i++) block[i * 2] = block[i * 2 + 1] = ChordSample(done + i);
            writer.WriteSamples(block, 0, block.Length);
        }
        return path;
    }

    /// <summary>Fits <paramref name="x"/> to the chord at the best of its 480 phases and gain: SNR in dB, the gain, the mean, and the
    /// power-weighted centroid (Hz) of the 100 Hz harmonics (1..239) of x.</summary>
    private static (double Snr, double Gain, double Mean, double Centroid) FitChord(float[] x)
    {
        var period = new double[ChordPeriod];
        for (var i = 0; i < ChordPeriod; i++) period[i] = ChordSample(i);
        double bestDot = double.NegativeInfinity, bestRef = 1; var bestLag = 0;
        for (var lag = 0; lag < ChordPeriod; lag++)
        {
            double dot = 0, rr = 0;
            for (var i = 0; i < x.Length; i++) { var r = period[(i + lag) % ChordPeriod]; dot += x[i] * r; rr += r * r; }
            if (dot > bestDot) { bestDot = dot; bestRef = rr; bestLag = lag; }
        }
        var gain = bestDot / bestRef;
        double sig = 0, err = 0, mean = 0;
        for (var i = 0; i < x.Length; i++) { var r = gain * period[(i + bestLag) % ChordPeriod]; sig += r * r; err += (x[i] - r) * (x[i] - r); mean += x[i]; }
        double num = 0, den = 0;
        for (var k = 1; k < 240; k++)
        {
            double re = 0, im = 0, w = 2 * Math.PI * 100 * k / 48000;
            for (var i = 0; i < x.Length; i++) { re += x[i] * Math.Cos(w * i); im -= x[i] * Math.Sin(w * i); }
            var p = re * re + im * im; num += p * 100 * k; den += p;
        }
        return (10 * Math.Log10(sig / Math.Max(err, 1e-30)), gain, mean / x.Length, den <= 0 ? 0 : num / den);
    }

    /// <summary><see cref="FitChord"/> per 0.1 s window that holds sound, each at its own phase; returns the median SNR. On a loaded machine the
    /// engine's disk thread can start the clip late or leave a gap (silence, then a phase jump): that is the clip streamer, not the tap.
    /// Gain, mean and centroid are those of the whole signal; NaN SNR when fewer than 5 windows hold sound.</summary>
    private static (double Snr, double Gain, double Mean, double Centroid) FitChordWindows(float[] x)
    {
        var snr = new List<double>();
        for (var at = 0; at + 4800 <= x.Length; at += 4800)
        {
            var w = x[at..(at + 4800)]; var loud = 0f;
            foreach (var v in w) loud = Math.Max(loud, Math.Abs(v));
            if (loud > 0.01f) snr.Add(FitChord(w).Snr);
        }
        snr.Sort();
        var whole = FitChord(x);
        return (snr.Count < 5 ? double.NaN : snr[snr.Count / 2], whole.Gain, whole.Mean, whole.Centroid);
    }

    /// <summary>The live recording carries the mix: chord through the real engine, tap, live session and AAC, fitted to the source.</summary>
    private static void TestLiveAudioFidelity()
    {
        var shared = SharedBlock.Create($"tf-selftest-{Guid.NewGuid():N}");
        var wav = WriteChordWav(30);   // long enough that the clip still plays when the engine's disk thread was slow to start it
        var path = Path.Combine(Path.GetTempPath(), $"tf-fidelity-{Guid.NewGuid():N}.mp4");
        try
        {
            EH.Attach(shared, (spec, _, _) => throw new InvalidOperationException($"selftest: unexpected plug-in {spec.Path}"));
            EH.Configure(NullConfig(48000, 256, manual: false));
            EH.LoadChain(0, "song", false, new List<PluginSpec>());
            EH.SetClips(0, new List<ClipSpec> { new(wav, 0, 0, 30, 0, 0, 1) }, owner: 1);
            EH.Command(EngineCommand.SetTrackMix, w => { w.Write(0); w.Write(100); w.Write(64); });
            EH.Collect();
            EH.SetPlaying(1, true, 0.0);
            var until = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < until && shared.Peak(0) <= 0.01f) { EH.Collect(); Thread.Sleep(2); }
            var session = LiveVideoSession.Start(path, 1920, 1080, 30, _ => true);
            var raw = new List<float>();
            EH.Tap.Sink = (r, first, data, frames) => { lock (raw) for (var i = 0; i < frames; i++) raw.Add(data[i * 2]); session.OnAudio(r, first, (float[])data.Clone(), frames); };
            EH.Command(EngineCommand.SetMasterTap, w => w.Write(true));
            EH.Collect();
            Thread.Sleep(2500);
            EH.Command(EngineCommand.SetMasterTap, w => w.Write(false));
            EH.Collect();
            Thread.Sleep(50);
            session.Finish();
            EH.Tap.Sink = null;
            float[] tap; lock (raw) tap = raw.GetRange(9600, Math.Max(0, Math.Min(raw.Count - 9600, 86400))).ToArray();   // 1.8 s after the first 0.2 s
            var (_, channels, pcm) = ReadAudioPcm(path);
            var decoded = new float[Math.Max(0, Math.Min(pcm.Length / channels - 24000, 81600))];   // 1.7 s after the encoder priming
            for (var i = 0; i < decoded.Length; i++) decoded[i] = pcm[(i + 24000) * channels] / 32768f;
            var source = new float[48000];
            for (var i = 0; i < source.Length; i++) source[i] = ChordSample(i);
            var want = FitChord(source); var before = FitChordWindows(tap); var after = FitChordWindows(decoded);
            // The byte-widening fault gives about -35 dB, a mean near +0.45 and a centroid of 6 to 13 kHz; the clean path gives about 147 dB at the tap.
            Check("live audio fidelity: the tap hands the recording the engine's mix (chord fitted at >= 60 dB SNR, no DC)",
                before.Snr >= 60 && Math.Abs(before.Mean) < 1e-3,
                $"tap SNR {before.Snr:F1} dB, gain {before.Gain:F3}, mean {before.Mean:F4}, centroid {before.Centroid:F0} Hz (source {want.Centroid:F0} Hz)");
            Check("live audio fidelity: the MP4 holds the chord (>= 30 dB SNR after AAC, centroid within 25%, no DC)",
                after.Snr >= 30 && Math.Abs(after.Centroid - want.Centroid) < 0.25 * want.Centroid && Math.Abs(after.Mean) < 1e-2,
                $"mp4 SNR {after.Snr:F1} dB, gain {after.Gain:F3}, mean {after.Mean:F4}, centroid {after.Centroid:F0} Hz (source {want.Centroid:F0} Hz)");
        }
        finally { StopTestEngine(shared, wav); try { File.Delete(path); } catch (IOException) { } }
    }
}
