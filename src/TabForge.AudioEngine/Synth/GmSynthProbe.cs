using System.Text;
using MeltySynth;
using TabForge.AudioEngine.Plugins;

namespace TabForge.AudioEngine.Synth;

/// <summary>
/// `TabForge.exe --probe-gm &lt;report&gt;`: feeds the engine's General MIDI synth the messages the score compiler produces for each
/// technique and measures what comes out (pitch over time, level, tail), so a difference from what the score says is found
/// by measurement instead of by ear. Runs without a window or an audio device.
/// </summary>
public static class GmSynthProbe
{
    private const int Rate = 48000, Block = 256;

    public sealed record Event(double Ms, int Status, int Data1, int Data2);

    /// <summary>Renders <paramref name="lengthMs"/> of audio for a fresh synth given timed MIDI messages.</summary>
    public static (float[] Left, float[] Right) Render(IEnumerable<Event> events, double lengthMs)
    {
        using var synth = new GmSynth(Rate, Block);
        var pending = events.OrderBy(e => e.Ms).ToList();
        var total = (int)(lengthMs * Rate / 1000);
        var left = new float[total]; var right = new float[total];
        var l = new float[Block]; var r = new float[Block];
        var index = 0;
        for (var start = 0; start < total; start += Block)
        {
            var frames = Math.Min(Block, total - start);
            var block = new List<BlockMidi>();
            while (index < pending.Count && pending[index].Ms * Rate / 1000 < start + frames)
            {
                var e = pending[index++];
                block.Add(new BlockMidi { Frame = Math.Max(0, (int)(e.Ms * Rate / 1000) - start), Status = (byte)e.Status, Data1 = (byte)e.Data1, Data2 = (byte)e.Data2 });
            }
            var transport = new TransportInfo();
            synth.Process(new[] { l, r }, new[] { l, r }, frames, block.ToArray(), in transport);
            Array.Copy(l, 0, left, start, frames); Array.Copy(r, 0, right, start, frames);
        }
        return (left, right);
    }

    /// <summary>Fundamental frequency of a stretch of audio (autocorrelation over 60 to 1500 Hz); 0 when there is no clear pitch.</summary>
    public static double Pitch(float[] samples, double fromMs, double lengthMs = 60, double minHz = 60)
    {
        var start = (int)(fromMs * Rate / 1000); var n = (int)(lengthMs * Rate / 1000);
        if (start < 0 || start + n > samples.Length) return 0;
        double best = 0; var bestLag = 0;
        var minLag = Rate / 1500; var maxLag = (int)(Rate / minHz);
        double energy = 0; for (var i = 0; i < n; i++) energy += samples[start + i] * samples[start + i];
        if (energy < 1e-6) return 0;
        for (var lag = minLag; lag <= maxLag && lag < n / 2; lag++)
        {
            double sum = 0;
            for (var i = 0; i + lag < n; i++) sum += samples[start + i] * samples[start + i + lag];
            if (sum > best) { best = sum; bestLag = lag; }
        }
        return bestLag == 0 || best < energy * 0.3 ? 0 : (double)Rate / bestLag;
    }

    public static double Peak(float[] samples, double fromMs, double lengthMs)
    {
        var start = Math.Max(0, (int)(fromMs * Rate / 1000)); var end = Math.Min(samples.Length, start + (int)(lengthMs * Rate / 1000));
        double peak = 0; for (var i = start; i < end; i++) peak = Math.Max(peak, Math.Abs(samples[i]));
        return peak;
    }

    private static double Semitones(double hz, double reference) => hz <= 0 || reference <= 0 ? double.NaN : 12 * Math.Log2(hz / reference);

    private static IEnumerable<Event> Setup(int program, int rangeSemitones, bool sendRange = true)
    {
        yield return new Event(0, 0xC0, program, 0);
        yield return new Event(0, 0xB0, 7, 100);
        yield return new Event(0, 0xB0, 10, 64);
        if (sendRange)
        {
            yield return new Event(0, 0xB0, 101, 0);
            yield return new Event(0, 0xB0, 100, 0);
            yield return new Event(0, 0xB0, 6, rangeSemitones);
            yield return new Event(0, 0xB0, 38, 0);
            yield return new Event(0, 0xB0, 101, 127);
            yield return new Event(0, 0xB0, 100, 127);
        }
        yield return new Event(0, 0xE0, 0, 0x40);
    }

    private static Event Wheel(double ms, double semitones, int range)
    {
        var value = Math.Clamp(8192 + (int)Math.Round(semitones * 8192.0 / range), 0, 16383);
        return new Event(ms, 0xE0, value & 0x7F, (value >> 7) & 0x7F);
    }

    public static int Run(string[] args)
    {
        var path = args.Length > 1 ? args[1] : "gm-probe.txt";
        var report = new StringBuilder();
        var failures = 0;
        void Check(string name, bool ok, string detail)
        {
            report.AppendLine($"{(ok ? "PASS" : "FAIL")}  {name}  ({detail})");
            if (!ok) failures++;
        }
        const int Flute = 73;   // close to a sine: a clean pitch to measure
        const double A3 = 220.0;

        // 1. plain note
        var (plain, _) = Render(Setup(Flute, 12).Append(new Event(50, 0x90, 57, 100)).Append(new Event(1000, 0x80, 57, 0)), 1200);
        var hz = Pitch(plain, 400);
        Check("a plain note plays at its pitch", Math.Abs(Semitones(hz, A3)) < 0.15, $"{hz:0.0} Hz, expected 220 Hz");

        // 2. bend range set with RPN (the compiler's own setup): +2 semitones, +12 semitones
        foreach (var target in new[] { 1.0, 2.0, 5.0, 12.0, -2.0 })
        {
            var (bent, _) = Render(Setup(Flute, 12).Concat(new[] { new Event(50, 0x90, 57, 100), Wheel(300, target, 12), new Event(1000, 0x80, 57, 0) }), 1200);
            var h = Pitch(bent, 500);
            Check($"pitch wheel {target:+0.0;-0.0} semitones (range 12 by RPN)", Math.Abs(Semitones(h, A3) - target) < 0.2, $"measured {Semitones(h, A3):+0.00;-0.00} semitones");
        }

        // 3. a bend that follows a curve, as the compiler writes it (a wheel message every ~10 ms): pre-bend, release
        var curve = new List<Event> { new Event(50, 0x90, 57, 100) };
        for (var t = 0; t <= 300; t += 10) curve.Add(Wheel(100 + t, 2.0 * t / 300, 12));   // 0 -> +2 over 300 ms
        for (var t = 0; t <= 200; t += 10) curve.Add(Wheel(500 + t, 2.0 * (1 - t / 200.0), 12));   // back down
        curve.Add(new Event(1000, 0x80, 57, 0));
        var (curved, _) = Render(Setup(Flute, 12).Concat(curve), 1200);
        Check("a bend up and release follows the curve", Math.Abs(Semitones(Pitch(curved, 420), A3) - 2) < 0.25 && Math.Abs(Semitones(Pitch(curved, 800), A3)) < 0.25,
            $"peak {Semitones(Pitch(curved, 420), A3):+0.00} st, after release {Semitones(Pitch(curved, 800), A3):+0.00} st");

        // 4. the range survives a "reset all controllers" and an all-notes-off (panic) when the setup is sent again
        var (afterReset, _) = Render(Setup(Flute, 12).Concat(new[] { new Event(20, 0xB0, 121, 0), new Event(30, 0xB0, 123, 0) }).Concat(Setup(Flute, 12).Select(e => e with { Ms = 40 }))
            .Concat(new[] { new Event(60, 0x90, 57, 100), Wheel(200, 2, 12), new Event(1000, 0x80, 57, 0) }), 1200);
        Check("setup sent again after a controller reset restores the bend range", Math.Abs(Semitones(Pitch(afterReset, 500), A3) - 2) < 0.25, $"{Semitones(Pitch(afterReset, 500), A3):+0.00} st");
        var (noSetup, _) = Render(new[] { new Event(0, 0xC0, Flute, 0), new Event(20, 0xB0, 121, 0), new Event(60, 0x90, 57, 100), Wheel(200, 2, 12), new Event(1000, 0x80, 57, 0) }, 1200);
        report.AppendLine($"INFO  wheel value for +2 st (range 12) with NO range setup after a reset plays {Semitones(Pitch(noSetup, 500), A3):+0.00} st (default synth range decides)");

        // 5. pitch wheel is per channel
        var (twoChannels, _) = Render(Setup(Flute, 12).Concat(new[] { new Event(0, 0xC1, Flute, 0), new Event(50, 0x90, 57, 100), new Event(60, 0x91, 57, 100), Wheel(200, 2, 12), new Event(1000, 0x80, 57, 0), new Event(1000, 0x81, 57, 0) }), 1200);
        report.AppendLine($"INFO  two channels, wheel on channel 1 only: pitch {Semitones(Pitch(twoChannels, 500), A3):+0.00} st");

        // 6. modulation wheel (vibrato), sustain pedal, expression, note-off velocity
        var (vibrato, _) = Render(Setup(Flute, 12).Concat(new[] { new Event(50, 0x90, 57, 100), new Event(60, 0xB0, 1, 100), new Event(1000, 0x80, 57, 0) }), 1200);
        var vibPitches = Enumerable.Range(0, 8).Select(i => Pitch(vibrato, 300 + i * 60)).Where(p => p > 0).ToList();
        var spread = vibPitches.Count > 1 ? Semitones(vibPitches.Max(), vibPitches.Min()) : 0;
        Check("modulation wheel (CC1) adds vibrato", spread > 0.05, $"pitch moves {spread:0.00} semitones");
        var (sustained, _) = Render(Setup(Flute, 12).Concat(new[] { new Event(20, 0xB0, 64, 127), new Event(50, 0x90, 57, 100), new Event(300, 0x80, 57, 0) }), 1500);
        Check("sustain pedal (CC64) holds a released note", Peak(sustained, 800, 100) > 0.001, $"level {Peak(sustained, 800, 100):0.0000} after release");
        var (released, _) = Render(Setup(Flute, 12).Concat(new[] { new Event(50, 0x90, 57, 100), new Event(300, 0x80, 57, 0) }), 1500);
        Check("without the pedal the note stops after release", Peak(released, 1100, 100) < 0.005, $"level {Peak(released, 1100, 100):0.0000}");
        var (expr, _) = Render(Setup(Flute, 12).Concat(new[] { new Event(20, 0xB0, 11, 30), new Event(50, 0x90, 57, 100), new Event(1000, 0x80, 57, 0) }), 1200);
        var (loud, _) = Render(Setup(Flute, 12).Concat(new[] { new Event(50, 0x90, 57, 100), new Event(1000, 0x80, 57, 0) }), 1200);
        Check("expression (CC11) lowers the level", Peak(expr, 300, 400) < Peak(loud, 300, 400) * 0.7, $"{Peak(expr, 300, 400):0.000} vs {Peak(loud, 300, 400):0.000}");

        // 7. velocity range and short notes
        var (soft, _) = Render(Setup(Flute, 12).Concat(new[] { new Event(50, 0x90, 57, 30), new Event(1000, 0x80, 57, 0) }), 1200);
        Check("velocity changes the level", Peak(soft, 300, 400) < Peak(loud, 300, 400) * 0.85, $"{Peak(soft, 300, 400):0.000} vs {Peak(loud, 300, 400):0.000}");
        var (quick, _) = Render(Setup(30, 12).Concat(new[] { new Event(50, 0x90, 57, 100), new Event(58, 0x80, 57, 0) }), 600);
        Check("a very short note still sounds", Peak(quick, 50, 80) > 0.001, $"level {Peak(quick, 50, 80):0.0000}");

        // 8. the same note twice quickly (re-attack), a chord, the drum channel
        var (drums, _) = Render(new[] { new Event(0, 0xC9, 0, 0), new Event(50, 0x99, 38, 100), new Event(60, 0x99, 42, 100) }, 500);
        Check("drum channel plays notes (channel 10)", Peak(drums, 50, 200) > 0.01, $"level {Peak(drums, 50, 200):0.000}");

        foreach (var prog in new[] { 25, 26, 27, 28, 29, 30, 24, 32, 33, 34, 35 })
        {
            var line = new StringBuilder($"INFO  program {prog} pitch error by note (semitones):");
            foreach (var midi in Enumerable.Range(0, 20).Select(i => 52 + i * 2))
            {
                var (a, _) = Render(Setup(prog, 12).Concat(new[] { new Event(50, 0x90, midi, 100), new Event(900, 0x80, midi, 0) }), 1000);
                var h = Pitch(a, 400, 60);
                line.Append($" {midi}:{(h <= 0 ? "n/a" : (69 + 12 * Math.Log2(h / 440.0) - midi).ToString("+0.00;-0.00"))}");
            }
            report.AppendLine(line.ToString());
        }
        // Bass range (drop-tuned basses reach C1 = 24): does the GM bass sound at the note's own octave?
        foreach (var prog in new[] { 33, 34 })
        {
            var line = new StringBuilder($"INFO  program {prog} low-range pitch error (semitones):");
            foreach (var midi in new[] { 24, 28, 31, 36, 40, 43, 48 })
            {
                var (a, _) = Render(Setup(prog, 12).Concat(new[] { new Event(50, 0x90, midi, 100), new Event(1400, 0x80, midi, 0) }), 1500);
                var h = Pitch(a, 300, 400, 25);
                line.Append($" {midi}:{(h <= 0 ? "n/a" : (69 + 12 * Math.Log2(h / 440.0) - midi).ToString("+0.00;-0.00"))}");
            }
            report.AppendLine(line.ToString());
        }
        {
            double sum = 0; int count = 0, bad = 0;
            for (var prog = 0; prog < 120; prog++)
                foreach (var midi in (Environment.GetEnvironmentVariable("TABFORGE_PROBE_FULL") == "1" ? Enumerable.Range(40, 61).ToArray() : new[] { 48, 55, 60, 67, 72, 79, 84 }))
                {
                    var (a, _) = Render(Setup(prog, 12).Concat(new[] { new Event(50, 0x90, midi, 100), new Event(700, 0x80, midi, 0) }), 800);
                    var h = Pitch(a, 300, 80);
                    if (h <= 0) continue;
                    var e = Math.Abs(69 + 12 * Math.Log2(h / 440.0) - midi);
                    if (e > 3) continue;   // an octave slip or an instrument that is not tuned at all
                    sum += e; count++; if (e > 0.35) { bad++; if (Environment.GetEnvironmentVariable("TABFORGE_PROBE_FULL") == "1") report.AppendLine($"  off: program {prog} note {midi} error {(69 + 12 * Math.Log2(h / 440.0) - midi):+0.00;-0.00}"); }
                }
            report.AppendLine($"INFO  all programs: mean |error| {sum / count:0.000} semitones over {count} notes, {bad} off by more than 0.35");
        }
        var bank = DlsToSoundFont.LoadWindowsBank();
        foreach (var prog in new[] { 29, 30 })
        {
            var preset = bank.Presets.FirstOrDefault(x => x.BankNumber == 0 && x.PatchNumber == prog);
            if (preset == null) continue;
            foreach (var pr in preset.Regions)
                foreach (var ir in pr.Instrument.Regions)
                    report.AppendLine($"INFO  prog {prog} keys {ir.KeyRangeStart}-{ir.KeyRangeEnd} vel {ir.VelocityRangeStart}-{ir.VelocityRangeEnd} root {ir.RootKey}/{ir.Sample.OriginalPitch} coarse {ir.CoarseTune} fine {ir.FineTune} scale {ir.ScaleTuning} rate {ir.Sample.SampleRate} sampleCorr {ir.Sample.PitchCorrection}");
        }
        report.AppendLine(failures == 0 ? "GM synth probe: all checks passed" : $"GM synth probe: {failures} failure(s)");
        File.WriteAllText(path, report.ToString());
        Console.Out.WriteLine(report.ToString());
        return failures == 0 ? 0 : 1;
    }
}
