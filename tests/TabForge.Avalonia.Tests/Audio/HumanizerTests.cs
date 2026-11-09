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

// Owns: the checks of the humanizer: flat input gains human-range velocity spread and bounded delays, human input passes
//   unchanged, per-key order and note-offs kept, bit-identical renders, no allocation, no stuck notes or NaN while options
//   change mid-song, its CPU, and the listening WAVs + flat .mid files (TABFORGE_SYNTH_WAV_DIR).
// Does not own: the guitar sound (GuitarLayerTests), the SoundFont player (Sf2SynthTests).
// Tests: Humanizer, Evidence, Phrase, Nuance, SynthOptions, Synthesizer (pre-stage).
public sealed class HumanizerTests(ITestOutputHelper output)
{
    private const int Rate = 48000;
    private static readonly SynthOptions On = new() { Humanize = true };
    private record struct Ev(long At, int Status, int D1, int D2);

    // ---- songs: flat (velocity 90, exact grid) -------------------------------------------------------------------------------------

    private static void Note(List<Ev> s, int ch, double beat, double beats, int key, int vel = 90, double jitterMs = 0)
    {
        var at = (long)Math.Round(beat * 0.5 * Rate + jitterMs * Rate / 1000);   // 120 bpm
        s.Add(new(at, 0x90 | ch, key, vel));
        s.Add(new(at + (long)(beats * 0.5 * Rate), 0x80 | ch, key, 0));
    }

    private static List<Ev> Song(int program, Action<List<Ev>> notes)
    {
        var s = new List<Ev> { new(0, 0xC0, program, 0) };
        notes(s);
        return s.OrderBy(e => e.At).ThenBy(e => (e.Status & 0xF0) == 0x80 ? 0 : 1).ToList();
    }

    private static List<Ev> Riff() => Song(30, s =>
    {
        for (var bar = 0; bar < 4; bar++)
        {
            var run = bar % 2 == 1;   // palm-muted 8th chugs; every second bar a 16th run down from the half-bar
            for (var e = 0; e < (run ? 4 : 8); e++) foreach (var k in new[] { 40, 47 }) Note(s, 0, bar * 4 + e * 0.5, 0.3, k);
            if (run) for (var e = 0; e < 8; e++) Note(s, 0, bar * 4 + 2 + e * 0.25, 0.23, new[] { 52, 50, 48, 47, 45, 43, 42, 40 }[e]);
        }
    });

    private static List<Ev> Arpeggio() => Song(27, s =>
    {
        int[][] bars = [[57, 60, 64, 69, 64, 60, 64, 60], [48, 55, 60, 64, 67, 64, 60, 55], [43, 50, 55, 59, 62, 59, 55, 50], [40, 47, 52, 55, 59, 55, 52, 47]];
        for (var bar = 0; bar < 4; bar++) for (var e = 0; e < 8; e++) Note(s, 0, bar * 4 + e * 0.5, 0.95, bars[bar][e]);
    });

    private static List<Ev> Strum() => Song(25, s =>
    {
        int[][] chords = [[40, 47, 52, 56, 59, 64], [45, 52, 57, 61, 64], [50, 57, 62, 66], [43, 47, 50, 55, 59, 67]];
        double[] rhythm = [0, 1, 1.5, 2.5, 3];
        for (var bar = 0; bar < 4; bar++)
            for (var i = 0; i < rhythm.Length; i++)
                foreach (var k in chords[bar]) Note(s, 0, bar * 4 + rhythm[i], (i + 1 < rhythm.Length ? rhythm[i + 1] : 4) - rhythm[i] - 0.05, k);
    });

    private static List<Ev> Bass() => Song(33, s =>
    {
        int[][] bars = [[28, 28, 28, 28, 28, 28, 31, 33], [28, 28, 28, 28, 36, 36, 35, 35], [28, 28, 28, 28, 28, 28, 31, 33], [28, 28, 33, 33, 31, 31, 26, 26]];
        for (var bar = 0; bar < 4; bar++) for (var e = 0; e < 8; e++) Note(s, 0, bar * 4 + e * 0.5, 0.45, bars[bar][e]);
    });

    // Written as a player would: velocities vary, onsets a few ms off the grid.
    private static List<Ev> AlreadyHuman()
    {
        var r = new Random(7);
        int[][] bars = [[57, 60, 64, 69, 64, 60, 64, 60], [48, 55, 60, 64, 67, 64, 60, 55], [43, 50, 55, 59, 62, 59, 55, 50], [40, 47, 52, 55, 59, 55, 52, 47]];
        return Song(27, s =>
        {
            for (var bar = 0; bar < 4; bar++)
                for (var e = 0; e < 8; e++) Note(s, 0, bar * 4 + e * 0.5, 0.95, bars[bar][e], (e % 4 == 0 ? 96 : 78) + r.Next(-11, 12), r.NextDouble() * 16 - 8);
        });
    }

    // ---- the humanizer driven like Synthesizer does: add at the frame, commit, take what is due ------------------------------------

    private static List<Ev> Run(List<Ev> song, SynthOptions o, Humanizer? h = null)
    {
        h ??= new Humanizer(Rate);
        h.Reset();
        var res = new List<Ev>();
        long now = 0;
        for (var i = 0; ;)
        {
            for (; i < song.Count && song[i].At == now; i++)
                if (!h.Add(song[i].Status & 15, song[i].Status & 0xF0, song[i].D1, song[i].D2, o.Humanize)) res.Add(song[i]);
            h.Commit(now, o);
            while (h.Take(now, out var e)) res.Add(new(now, e.Command | e.Channel, e.Data1, e.Data2));
            var next = Math.Min(i < song.Count ? song[i].At : long.MaxValue, h.NextDue);
            if (next == long.MaxValue) return res;
            now = next;
        }
    }

    private static bool IsOn(Ev e) => (e.Status & 0xF0) == 0x90 && e.D2 > 0;
    private static bool IsOff(Ev e) => (e.Status & 0xF0) == 0x80 || (e.Status & 0xF0) == 0x90 && e.D2 == 0;
    private static double Std(IEnumerable<double> x) { var a = x.ToArray(); var m = a.Average(); return Math.Sqrt(a.Average(v => (v - m) * (v - m))); }

    // Velocity std, onset offsets vs the input (mean, std, max ms) and the mean spread inside chords (ms).
    private static (double VelStd, double MeanMs, double StdMs, double MaxMs, double SpreadMs) Stats(List<Ev> input, List<Ev> outp)
    {
        var a = input.Where(IsOn).ToList();
        var b = outp.Where(IsOn).ToList();
        var offs = new List<double>();
        var used = new bool[b.Count];
        foreach (var e in a)
        {
            var j = Enumerable.Range(0, b.Count).First(j => !used[j] && b[j].D1 == e.D1 && b[j].At >= e.At);
            used[j] = true;
            offs.Add((b[j].At - e.At) * 1000.0 / Rate);
        }
        var spreads = b.GroupBy(e => a.Where(x => x.At <= e.At).Max(x => x.At)).Where(g => g.Count() > 1).Select(g => (g.Max(e => e.At) - g.Min(e => e.At)) * 1000.0 / Rate).ToList();
        return (Std(b.Select(e => (double)e.D2)), offs.Average(), Std(offs), offs.Max(), spreads.Count > 0 ? spreads.Average() : 0);
    }

    private static void AssertSound(List<Ev> input, List<Ev> outp)
    {
        Assert.Equal(input.Count, outp.Count);
        Assert.All(outp.Where(IsOn), e => Assert.InRange(e.D2, 1, 127));
        foreach (var key in input.Where(IsOn).Select(e => e.D1).Distinct())
        {
            var seq = outp.Where(e => e.D1 == key && (IsOn(e) || IsOff(e))).ToList();
            Assert.Equal(input.Count(e => e.D1 == key && IsOn(e)), seq.Count(IsOn));
            for (var i = 0; i < seq.Count; i += 2) { Assert.True(IsOn(seq[i]) && IsOff(seq[i + 1])); Assert.True(seq[i + 1].At > seq[i].At); }   // on, off, on, off...
        }
    }

    [Fact]
    public void FlatInputGetsHumanSpreadAndBoundedDelays()
    {
        var max = new Humanizer(Rate).MaxDelay * 1000.0 / Rate;
        foreach (var (name, song) in Songs())
        {
            var outp = Run(song, On);
            AssertSound(song, outp);
            var s = Stats(song, outp);
            output.WriteLine($"{name}: velocity std 0 -> {s.VelStd:F1}, onset delay mean {s.MeanMs:F1} ms, std {s.StdMs:F1}, max {s.MaxMs:F1} (bound {max:F1}), chord spread {s.SpreadMs:F1} ms");
            Assert.InRange(s.VelStd, 2.5, 10);
            Assert.InRange(s.MaxMs, 0, max);
            var (ins, outs) = (song.Where(IsOn).ToList(), outp.Where(IsOn).ToList());
            Assert.All(outs, e => Assert.InRange(e.D2, 90 - HumanTable.MaxDelta, 90 + HumanTable.MaxDelta));
            Assert.Equal(Run(song, On), outp);   // deterministic
        }
    }

    private static IEnumerable<(string, List<Ev>)> Songs() => [("riff30", Riff()), ("arp27", Arpeggio()), ("strum25", Strum()), ("bass33", Bass())];

    [Fact]
    public void HumanInputAndOptionsOffPassUnchanged()
    {
        var human = AlreadyHuman();
        Assert.Equal(human, Run(human, On));
        Assert.Equal(Riff(), Run(Riff(), SynthOptions.Default));
        Assert.Equal(Riff(), Run(Riff(), On with { HumanizeVelocity = false, HumanizeTiming = false }));
        var drums = Riff().Select(e => e with { Status = e.Status | 9 }).ToList();
        Assert.Equal(drums, Run(drums, On));   // the drum channel is left alone
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

        // Options flipped mid-song from another thread's point of view: nothing hangs.
        var t = Synth(On);
        var flips = new[] { SynthOptions.Default, On with { HumanizeVoice = false }, On with { GuitarLayer = false }, On };
        var (l, _) = Render(t, Riff(), 4, i => { if (i % 40 == 39) t.Options = flips[i / 40 % 4]; });
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
        var big = Enumerable.Range(0, 50).SelectMany(k => song.Select(e => e with { At = e.At + k * 8L * Rate })).ToList();
        Run(big, On, h);
        var watch = Stopwatch.StartNew();
        Run(big, On, h);
        var us = watch.Elapsed.TotalMilliseconds * 1000 / big.Count(IsOn);
        var seconds = big[^1].At / (double)Rate;
        output.WriteLine($"humanizer: {us:F2} us per note-on (test harness included), {watch.Elapsed.TotalMilliseconds / seconds / 10:F4} % of one core for this riff");
        Assert.True(us < 50);
        var w = Stopwatch.StartNew();
        Render(Synth(SynthOptions.Default), song);
        var off = w.Elapsed.TotalMilliseconds;
        w.Restart();
        Render(Synth(On), song);
        output.WriteLine($"render riff (layer, Debug): off {off:F0} ms, on {w.Elapsed.TotalMilliseconds:F0} ms");
    }

    // ---- listening files -------------------------------------------------------------------------------------------------------------

    [Fact]
    public void WritesListeningFilesWhenAsked()
    {
        var dir = Environment.GetEnvironmentVariable("TABFORGE_SYNTH_WAV_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        foreach (var (name, song) in Songs().Append(("arp27_alreadyhuman", AlreadyHuman())))
        {
            var human = name.EndsWith("alreadyhuman");
            WriteMid(Path.Combine(dir, $"tfsynth4_{name}.mid"), song);
            foreach (var on in human ? [true] : new[] { false, true })
            {
                var (l, r) = Render(Synth(on ? On : SynthOptions.Default), song);
                TestSoundFont.WriteWav($"tfsynth4_{name}{(human ? "" : on ? "_human" : "_flat")}.wav", l, r, Rate);
            }
            var outp = Run(song, On);
            var (a, b) = (Stats(song, song), Stats(song, outp));
            var changed = song.Zip(outp).Count(p => p.First != p.Second);
            output.WriteLine($"{name}: velocity std {a.VelStd:F1} -> {b.VelStd:F1}; onset delay mean {b.MeanMs:F1} ms, std {b.StdMs:F1}, max {b.MaxMs:F1}; chord spread {a.SpreadMs:F1} -> {b.SpreadMs:F1} ms; events changed {changed}/{song.Count}");
        }
    }

    // Standard MIDI file, format 0, 480 ticks per quarter, 120 bpm (960 ticks a second).
    private static void WriteMid(string path, List<Ev> song)
    {
        var t = new List<byte> { 0, 0xFF, 0x51, 3, 0x07, 0xA1, 0x20 };
        long last = 0;
        foreach (var e in song)
        {
            var tick = (long)Math.Round(e.At * 960.0 / Rate);
            var d = tick - last;
            last = tick;
            var v = new List<byte> { (byte)(d & 0x7F) };
            while ((d >>= 7) > 0) v.Insert(0, (byte)(0x80 | (d & 0x7F)));
            t.AddRange(v);
            t.Add((byte)e.Status);
            t.Add((byte)e.D1);
            if ((e.Status & 0xF0) != 0xC0) t.Add((byte)e.D2);
        }
        t.AddRange([0, 0xFF, 0x2F, 0]);
        using var w = new BinaryWriter(File.Create(path));
        w.Write("MThd"u8); w.Write(new byte[] { 0, 0, 0, 6, 0, 0, 0, 1, 0x01, 0xE0 });
        w.Write("MTrk"u8); w.Write(new[] { (byte)(t.Count >> 24), (byte)(t.Count >> 16), (byte)(t.Count >> 8), (byte)t.Count });
        w.Write(t.ToArray());
    }
}
