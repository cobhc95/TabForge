using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using TabForge.Audio.Contracts;
using TabForge.Services.Video;
using EH = TabForge.AudioEngine.EngineHost.Headless;
using System.Linq;

namespace TabForge;

// Owns: BS.1770 integrated loudness of decoded audio (a measuring helper for the recording-loudness tests).
// Does not own: the recording path (SelfTestLiveAudioFidelity) or the gain itself (LiveVideoSession).
public static partial class SelfTest
{
    /// <summary>Integrated loudness in LUFS (K-weighted at 48 kHz coefficients, 400 ms blocks, -70 absolute and -10 relative gates) and the sample peak in dBFS.</summary>
    private static (double Lufs, double PeakDb) MeasureLoudness(float[] interleaved, int channels, int rate)
    {
        var frames = interleaved.Length / channels;
        var k = new double[channels][];
        for (var c = 0; c < channels; c++)
        {
            k[c] = new double[frames];
            double x1 = 0, x2 = 0, y1 = 0, y2 = 0, u1 = 0, u2 = 0, v1 = 0, v2 = 0;
            for (var i = 0; i < frames; i++)
            {
                var x = (double)interleaved[i * channels + c];
                var y = 1.53512485958697 * x - 2.69169618940638 * x1 + 1.19839281085285 * x2 + 1.69065929318241 * y1 - 0.73248077421585 * y2;
                x2 = x1; x1 = x; y2 = y1; y1 = y;
                var v = y - 2 * u1 + u2 + 1.99004745483398 * v1 - 0.99007225036621 * v2;
                u2 = u1; u1 = y; v2 = v1; v1 = v;
                k[c][i] = v;
            }
        }
        int block = rate * 4 / 10, step = rate / 10;
        var energies = new System.Collections.Generic.List<double>();
        for (var s = 0; s + block <= frames; s += step)
        {
            double e = 0;
            for (var c = 0; c < channels; c++) { double sum = 0; for (var i = s; i < s + block; i++) sum += k[c][i] * k[c][i]; e += sum / block; }
            energies.Add(e);
        }
        double Lk(double e) => -0.691 + 10 * Math.Log10(Math.Max(e, 1e-20));
        var kept = energies.Where(e => Lk(e) > -70).ToList();
        if (kept.Count == 0) return (-120, 20 * Math.Log10(Math.Max(interleaved.Select(Math.Abs).DefaultIfEmpty(0).Max(), 1e-9)));
        var rel = Lk(kept.Average()) - 10;
        kept = kept.Where(e => Lk(e) > rel).ToList();
        var peak = interleaved.Select(Math.Abs).DefaultIfEmpty(0).Max();
        return (Lk(kept.Average()), 20 * Math.Log10(Math.Max(peak, 1e-9)));
    }

    /// <summary>RMS in dB of the loudest 100 ms window: ignores a window an overloaded machine dropped audio in.</summary>
    private static double LoudestWindowDb(float[] x)
    {
        double best = 1e-20;
        for (var s = 0; s + 4800 <= x.Length; s += 2400) { double e = 0; for (var i = s; i < s + 4800; i++) e += (double)x[i] * x[i]; best = Math.Max(best, e / 4800); }
        return 10 * Math.Log10(best);
    }

    private static float[] Stereo(float[] mono) { var o = new float[mono.Length * 2]; for (var i = 0; i < mono.Length; i++) o[2 * i] = o[2 * i + 1] = mono[i]; return o; }

    /// <summary>The real engine plays the chord; the recording is the nominal mix (decoded MP4 within 1.5 dB of the tap at gain 1), a +6 dB tap gain raises it by 6 dB, and a hot gain never reaches full scale.</summary>
    private static void TestLiveRecordingLoudness()
    {
        var shared = SharedBlock.Create($"tf-selftest-{Guid.NewGuid():N}");
        var wav = WriteChordWav(30);
        var path = Path.Combine(Path.GetTempPath(), $"tf-loudness-{Guid.NewGuid():N}.mp4");
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
            var raw = new List<float>(); var phaseTwo = new List<float>(); var hot = new List<float>();
            var phase = 0;
            EH.Tap.Sink = (r, first, data, frames) =>
            {
                lock (raw)
                {
                    var into = phase == 0 ? raw : phase == 1 ? phaseTwo : hot;
                    for (var i = 0; i < frames; i++) into.Add(data[i * 2]);
                }
                if (phase == 0) session.OnAudio(r, first, (float[])data.Clone(), frames);
            };
            EH.Command(EngineCommand.SetMasterTap, w => w.Write(true));
            EH.Collect();
            Thread.Sleep(2500);
            var defaultGain = EH.Tap.Gain;
            lock (raw) { phase = 1; } EH.Tap.Gain = 2f; Thread.Sleep(2000);
            lock (raw) { phase = 2; } EH.Tap.Gain = 40f; Thread.Sleep(1500);
            EH.Command(EngineCommand.SetMasterTap, w => w.Write(false));
            EH.Collect(); Thread.Sleep(50);
            session.Finish();
            EH.Tap.Sink = null; EH.Tap.Gain = 1f;
            float[] a, b, c;
            lock (raw) { a = raw.GetRange(9600, Math.Min(raw.Count - 9600, 86400)).ToArray(); b = phaseTwo.GetRange(9600, Math.Min(phaseTwo.Count - 9600, 57600)).ToArray(); c = hot.ToArray(); }
            var (_, channels, pcm) = ReadAudioPcm(path);
            var decoded = new float[Math.Min(pcm.Length - 24000 * channels, 81600 * channels)];
            for (var i = 0; i < decoded.Length; i++) decoded[i] = pcm[24000 * channels + i] / 32768f;
            var nominal = MeasureLoudness(Stereo(a), 2, 48000);
            var mp4 = MeasureLoudness(decoded, channels, 48000);
            var plusSix = LoudestWindowDb(b) - LoudestWindowDb(a);
            var hotPeak = c.Length == 0 ? 1f : c.Max(Math.Abs);
            Check("recording loudness: the default tap gain is 1 and the MP4 holds the nominal mix level (within 1.5 dB)",
                defaultGain == 1f && Math.Abs(mp4.Lufs - nominal.Lufs) < 1.5,
                $"gain {defaultGain}, nominal {nominal.Lufs:F2} LUFS peak {nominal.PeakDb:F2}, mp4 {mp4.Lufs:F2} LUFS peak {mp4.PeakDb:F2}");
            Check("recording loudness: a tap gain of 2 (+6 dB) raises the loudness by 6 dB (+-0.5)",
                Math.Abs(plusSix - 6.02) < 0.5, $"{plusSix:F2} dB");
            Check("recording loudness: a hot gain (+32 dB) is limited under full scale", c.Length > 4800 && hotPeak <= 0.97f, $"peak {hotPeak:F4} over {c.Length} frames");
        }
        finally { StopTestEngine(shared, wav); try { File.Delete(path); } catch (IOException) { } }
    }
}
