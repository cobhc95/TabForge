using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using TabForge.Sf2Synth;
using TabForge.Sf2Synth.Guitar;
using TabForge.Sf2Synth.Humanize;
using TabForge.Sf2Synth.Sf2;
using Xunit;
using Xunit.Abstractions;

namespace TabForge.Avalonia.Tests;

// Owns: the checks of the humanizer (docs/specs/HUMAN_FEEL_SPEC.md 'What to test'): per feel, flat input reaches the human level
//   spread (dB and velocity steps), onset spread, lag-1 interval and DFA exponents, bar-to-bar pattern correlation, strum spreads
//   and gaps, ghost rate; the 1/f noise exponent; human input untouched; per-key pairing and bounds under fuzz; latency only while
//   on; bit-identical renders; no allocation; CPU; the listening WAVs (TABFORGE_SYNTH_WAV_DIR).
// Does not own: songs, harness and statistics (HumanizerTests.Songs.cs), the guitar sound (GuitarLayerTests).
// Tests: Humanizer, Phrase, Strum, Evidence, Noise, Nuance, SynthOptions, Synthesizer (pre-stage), GmSynth latency.
public sealed partial class HumanizerTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(Feel.Tight, 2.1, 2.9, 9, 15, 6, 8)]
    [InlineData(Feel.Natural, 3.4, 4.6, 15, 24, 11, 14)]
    [InlineData(Feel.Loose, 4.7, 6.3, 20, 31, 18, 24)]
    public void FlatInputReachesTheHumanRangesOfItsFeel(Feel feel, double dbLo, double dbHi, double stepLo, double stepHi, double msLo, double msHi)
    {
        var o = With(feel);
        var style = HumanTable.Styles[(int)feel];
        var line = Line();
        var ml = Match(line, Run(line, o)).Skip(32).ToList();   // after the evidence settles
        var strum = Strums(0.5);
        double[] db = ml.Select(m => Db(m.In, m.Out)).ToArray(), off = ml.Select(m => Ms(m.In, m.Out)).ToArray();
        var bars = Enumerable.Range(0, db.Length / 16 - 1).ToList();
        double BarCorr(double[] x) => bars.Average(b => Corr(x[(b * 16)..(b * 16 + 16)], x[(b * 16 + 16)..(b * 16 + 32)]));
        var onsets = ml.Select(m => (double)m.Out.At).ToArray();
        var ioi = onsets.Zip(onsets.Skip(1), (a, b) => b - a).ToArray();
        var lag1 = Corr(ioi[..^1], ioi[1..]);
        var (dfaS, dfaL) = (Dfa(off, 4, 16), Dfa(off, 32, 256));
        var strokes = Strokes(strum, Run(strum, o)).Skip(32).ToList();
        var (down, up) = (Median(strokes.Where(s => s.Down).Select(s => s.Spread)), Median(strokes.Where(s => !s.Down).Select(s => s.Spread)));
        var sixteen = Strums(0.25);
        // Ghost rate from the count; in the output a stroke 7 dB under the median of its slot 4 and 8 bars away is low: beats must be
        // no lower than 8th offbeats (which never ghost either), 16th offbeats clearly lower.
        var hg = new Humanizer(Rate);
        var st16 = Strokes(sixteen, Run(sixteen, o, hg)).Skip(32).ToDictionary(s => s.At / (Rate / 8), s => s.Db);
        var ghost = hg.Ghosts(0) / (64.0 * 8);
        double Low(Func<long, bool> cls)
        {
            var c = st16.Keys.Where(k => cls(k % 16) && st16.ContainsKey(k - 128) && st16.ContainsKey(k + 128)).ToList();
            return c.Count(k => st16[k] < Median(new[] { -128, -64, 64, 128 }.Select(d => st16[k + d])) - 7) / (double)c.Count;
        }
        var (offLow, eighthLow, beatLow) = (Low(s => s % 2 == 1), Low(s => s % 4 == 2), Low(s => s % 4 == 0));
        var stats = (LineDb: Std(db), StrumDb: Std(strokes.Select(s => s.Db)), Steps: Std(ml.Select(m => (double)m.Out.D2)),
            Ms: Std(off), Early: off.Min(), BarDb: BarCorr(db), BarMs: BarCorr(off));
        output.WriteLine($"{feel}: level std line {stats.LineDb:F2} dB ({stats.Steps:F1} steps) / strum strokes {stats.StrumDb:F2} dB; onset std {stats.Ms:F1} ms " +
            $"(earliest {stats.Early:F1}, look-ahead {style.LookMs}); lag-1 {lag1:F2}; DFA short {dfaS:F2} long {dfaL:F2}; bar corr level {stats.BarDb:F2} " +
            $"timing {stats.BarMs:F2}; strum down {down:F1} up {up:F1} ms, largest gap {strokes.Max(s => s.Gap):F1}; ghosts {ghost:P1} (set {style.Ghost:P0}); low strokes on 16th offbeats {offLow:P1}, 8th offbeats {eighthLow:P1}, beats {beatLow:P1}");

        Assert.InRange(stats.LineDb, dbLo, dbHi);
        Assert.InRange(stats.StrumDb, dbLo, dbHi);
        Assert.InRange(stats.Steps, stepLo, stepHi);
        Assert.InRange(stats.Ms, msLo, msHi);
        Assert.True(stats.Early >= -style.LookMs - 0.1);
        Assert.InRange(lag1, -0.6, -0.3);
        Assert.InRange(dfaS, 0, 0.5);
        Assert.InRange(dfaL, 0.6, 0.9);
        Assert.InRange(stats.BarDb, 0.7, 0.95);
        Assert.InRange(stats.BarMs, 0.3, 0.7);
        Assert.InRange(down, style.StrumMs * 0.75, style.StrumMs * 1.25 * (1 + HumanTable.BiasStrumPerMs * Math.Max(0, style.BiasMs)));
        Assert.True(up < down);
        Assert.True(strokes.Max(s => s.Gap) < 40);
        Assert.InRange(ghost, style.Ghost - 0.03, style.Ghost + 0.03);
        Assert.InRange(beatLow, 0, eighthLow + 0.05);
        Assert.True(offLow > eighthLow + style.Ghost / 2);
    }

    [Fact]
    public void FeelsGrowFromTightToLoose()
    {
        var line = Line();
        var sd = new[] { Feel.Tight, Feel.Natural, Feel.Loose }.Select(f => Match(line, Run(line, With(f))).Skip(32).ToList())
            .Select(m => (Db: Std(m.Select(x => Db(x.In, x.Out))), Ms: Std(m.Select(x => Ms(x.In, x.Out))))).ToArray();
        Assert.True(sd[0].Db < sd[1].Db && sd[1].Db < sd[2].Db && sd[0].Ms < sd[1].Ms && sd[1].Ms < sd[2].Ms);
    }

    [Fact]
    public void NoiseHasItsOneOverFExponent()
    {
        foreach (var (slope, target) in new[] { (HumanTable.PinkDbSlope, 0.65), (HumanTable.PinkMsSlope, 0.7) })
        {
            var (r, p) = (new Rng(), new Pink(slope));
            r.Seed(11);
            p.Reset(r);
            var x = Enumerable.Range(0, 16384).Select(_ => p.Next(r)).ToArray();
            var alpha = Dfa(x, 16, 256);
            output.WriteLine($"pink slope {slope}: std {Std(x):F2}, DFA {alpha:F2} (spec {target})");
            Assert.InRange(Std(x), 0.85, 1.15);
            Assert.InRange(alpha, target - 0.07, target + 0.07);
        }
    }

    [Fact]
    public void HumanInputAndOptionsOffPassUnchanged()
    {
        foreach (var human in new[] { AlreadyHuman(), AlreadyHuman(wide: true) })
            foreach (var f in new[] { Feel.Tight, Feel.Natural, Feel.Loose })
                Assert.Equal(human, Run(human, With(f)));   // 0 steps, 0 ms (after the reported latency)
        var flat = Riff();
        Assert.Equal(flat, Run(flat, SynthOptions.Default));
        Assert.Equal(flat, Run(flat, On with { HumanizeVelocity = false, HumanizeTiming = false }));
        var drums = flat.Select(e => e with { Status = e.Status | 9 }).ToList();
        Assert.Equal(drums, Run(drums, On));   // the drum channel only waits for the look-ahead
        var h = new Humanizer(Rate);
        Run(Arpeggio(), On, h);
        Assert.True(h.Flatness(0).Velocity > 0.99 && h.Flatness(0).Timing > 0.99);
        var tabDynamics = Arpeggio().Select((e, i) => IsOn(e) ? e with { D2 = new[] { 70, 90, 110, 127 }[i % 4] } : e).ToList();
        Assert.NotEqual(tabDynamics.Where(IsOn).Select(e => e.D2), Run(tabDynamics, On).Where(IsOn).Select(e => e.D2));   // 4 levels = flat
    }

    [Fact]
    public void EveryEventKeepsItsKeyOrderAndBoundsUnderFuzz()
    {
        var r = new Random(3);
        var fuzz = new List<Ev>();
        long t = 0;
        for (var i = 0; i < 6000; i++)
        {
            t += r.Next(4) == 0 ? 0 : r.Next(1, 9000);
            var ch = r.Next(4) == 0 ? 9 : r.Next(3);
            var kind = r.Next(20);
            fuzz.Add(kind == 0 ? new(t, 0xC0 | ch, new[] { 24, 25, 30, 33, 41, 0 }[r.Next(6)], 0)
                : kind == 1 ? new(t, 0xB0 | ch, 64, r.Next(2) * 127)
                : new(t, (r.Next(2) == 0 ? 0x90 : 0x80) | ch, 40 + r.Next(12), r.Next(3) == 0 ? 90 : r.Next(1, 128)));
        }
        foreach (var o in new[] { On, With(Feel.Loose), With(Feel.Tight) with { HumanizeDrift = false } })
        {
            var h = new Humanizer(Rate);
            var lat = h.Latency(o);
            var outp = Run(fuzz, o, h);
            Assert.Equal(fuzz.Count, outp.Count);
            Assert.Equal(outp, Run(fuzz, o, h));   // deterministic across Reset
            foreach (var key in fuzz.GroupBy(e => (e.Status & 15) * 128 + e.D1))
            {
                var seq = outp.Where(e => (IsOn(e) || IsOff(e)) && (e.Status & 15) * 128 + e.D1 == key.Key).ToList();
                Assert.Equal(key.Where(e => IsOn(e) || IsOff(e)).Select(IsOn), seq.Select(IsOn));   // in order per key: no note-off before its note-on
                Assert.True(seq.Zip(seq.Skip(1)).All(p => p.Second.At >= p.First.At));
            }
            Assert.All(Match(fuzz, outp), m => Assert.InRange(m.Out.At - m.In.At, -lat, h.MaxDelay - lat));
        }
    }

    [Fact]
    public void LatencyIsReportedOnlyWhileOn()
    {
        var s = Synth(SynthOptions.Default);
        Assert.Equal(0, s.Latency);
        foreach (var f in new[] { Feel.Tight, Feel.Natural, Feel.Loose })
        {
            s.Options = With(f);
            Assert.Equal((int)Math.Round(HumanTable.Styles[(int)f].LookMs * Rate / 1000), s.Latency);
        }
        s.Options = s.Options with { Humanize = false };
        Assert.Equal(0, s.Latency);
    }

    private static Synthesizer Synth(SynthOptions o) =>
        new(new SoundFont(TestSoundFont.Build()), Rate, 64, effects: true) { Layer = new GuitarLayer(Rate), MasterVolume = 0.3f, Options = o };

    private static (float[] L, float[] R) Render(Synthesizer s, List<Ev> song, double tail = 2, Action<int>? at = null)
    {
        var frames = (int)(song[^1].At + tail * Rate);
        float[] l = new float[frames], r = new float[frames];
        var done = 0;
        for (var i = 0; i < song.Count; i++)
        {
            var f = (int)song[i].At;
            if (f > done) { s.Render(l.AsSpan(done, f - done), r.AsSpan(done, f - done)); done = f; }
            at?.Invoke(i);
            s.ProcessMidiMessage(song[i].Status & 15, song[i].Status & 0xF0, song[i].D1, song[i].D2);
        }
        for (; done < frames; done += 512) s.Render(l.AsSpan(done, Math.Min(512, frames - done)), r.AsSpan(done, Math.Min(512, frames - done)));
        return (l, r);
    }

    [Fact]
    public void RendersRepeatBitForBitWithoutNaNOrStuckNotes()
    {
        var s = Synth(On);
        var a = Render(s, Strum());
        s.Reset();
        var b = Render(s, Strum());
        var c = Render(Synth(On), Strum());
        Assert.True(a.L.SequenceEqual(b.L) && a.L.SequenceEqual(c.L) && a.R.SequenceEqual(c.R));
        Assert.True(a.L.All(float.IsFinite) && a.L.Max(MathF.Abs) > 1e-3);
        Assert.False(a.L.SequenceEqual(Render(Synth(SynthOptions.Default), Strum()).L));

        // Options, feel, Reset and all-notes-off flipped mid-song: nothing hangs or keeps sounding.
        var t = Synth(On);
        var flips = new[] { SynthOptions.Default, With(Feel.Loose) with { HumanizeVoice = false }, On with { GuitarLayer = false }, With(Feel.Tight) };
        var song = Riff().Concat(Strum().Select(e => e with { At = e.At + 8L * Rate })).ToList();
        var (l, _) = Render(t, song, 4, i =>
        {
            if (i % 40 == 39) t.Options = flips[i / 40 % 4];
            if (i % 97 == 96) t.NoteOffAll(false);
            if (i == 250) t.Reset();
        });
        Assert.True(l.All(float.IsFinite));
        Assert.Equal(0, ((GuitarLayer)t.Layer!).ActiveStrings);
        Assert.Equal(0, t.ActiveVoices);
        Assert.False(t.Humanizer.Busy);
    }

    [Fact]
    public void PlayingHumanizedAllocatesNothingAndCostsLittle()
    {
        var song = Riff();
        var s = Synth(On);
        Render(s, song);
        s.Reset();
        float[] l = new float[song[^1].At + Rate], r = new float[l.Length];
        var before = GC.GetAllocatedBytesForCurrentThread();
        var done = 0;
        foreach (var e in song)
        {
            if (e.At > done) { s.Render(l.AsSpan(done, (int)e.At - done), r.AsSpan(done, (int)e.At - done)); done = (int)e.At; }
            s.ProcessMidiMessage(e.Status & 15, e.Status & 0xF0, e.D1, e.D2);
        }
        s.Render(l.AsSpan(done), r.AsSpan(done));
        s.NoteOffAll(false);
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());

        var h = new Humanizer(Rate);
        var big = Strums(0.25, 16);
        Run(big, On, h);
        var watch = Stopwatch.StartNew();
        Run(big, On, h);
        var us = watch.Elapsed.TotalMilliseconds * 1000 / big.Count(IsOn);
        output.WriteLine($"humanizer: {us:F2} us per note-on (harness included), {watch.Elapsed.TotalMilliseconds / (big[^1].At / (double)Rate) / 10:F4} % of one core");
        Assert.True(us < 50);
    }

    // ---- listening files: the flat .mid of the earlier pair, humanizer off and each feel (output shifted back by the latency) ----------

    [Fact]
    public void WritesListeningFilesWhenAsked()
    {
        var dir = Environment.GetEnvironmentVariable("TABFORGE_SYNTH_WAV_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        foreach (var (name, song) in Songs())
        {
            var mid = Path.Combine(dir, $"tfsynth4_{name}.mid");
            if (File.Exists(mid)) Assert.Equal(File.ReadAllBytes(mid), Mid(song));   // the very same flat file
            else File.WriteAllBytes(mid, Mid(song));
            foreach (var feel in new Feel?[] { null, Feel.Tight, Feel.Natural, Feel.Loose })
            {
                var s = Synth(feel is { } f ? With(f) : SynthOptions.Default);
                var (l, r) = Render(s, song);
                TestSoundFont.WriteWav($"tfsynth6_{name}_{feel?.ToString().ToLowerInvariant() ?? "flat"}.wav", l[s.Latency..], r[s.Latency..], Rate);
                if (feel is null) continue;
                var m = Match(song, Run(song, s.Options));
                var spread = Strokes(song, Run(song, s.Options)).Select(x => x.Spread).DefaultIfEmpty(0).ToList();
                output.WriteLine($"{name} {feel}: level std {Std(m.Select(x => Db(x.In, x.Out))):F1} dB / {Std(m.Select(x => (double)x.Out.D2)):F1} steps, " +
                    $"onset std {Std(m.Select(x => Ms(x.In, x.Out))):F1} ms, range {m.Min(x => Ms(x.In, x.Out)):F0}..{m.Max(x => Ms(x.In, x.Out)):F0} ms, strum median {Median(spread):F1} ms");
            }
        }
    }
}
