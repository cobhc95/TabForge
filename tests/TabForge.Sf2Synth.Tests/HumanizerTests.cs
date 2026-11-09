using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using TabForge.Sf2Synth;
using TabForge.Sf2Synth.Guitar;
using TabForge.Sf2Synth.Humanize;
using TabForge.Sf2Synth.Sf2;
using Xunit;
using Xunit.Abstractions;

namespace TabForge.Sf2Synth.Tests;

// Owns: the checks of the humanizer v3 (a pass over the written notes): levels follow the music (explained share, every change
//   between neighbours explained, identical bars within 2.5 dB), fast runs (constant-ms correlated timing, group accents, picking and
//   string crossing, no flams), strums, the window (mid-song start and an edit change only the neighbourhood), determinism, per-key
//   order under fuzz, human input untouched, feels ordered, voice variation in the synth (bit-identical, no allocation), CPU.
// Does not own: songs and statistics (HumanizerTests.Songs.cs), the demo (HumanizerTests.Demo.cs), the guitar sound (GuitarLayerTests).
// Tests: Humanizer, Dynamics, Timing, Evidence, Strum, Noise, VoiceVariation, SynthOptions, Synthesizer.
public sealed partial class HumanizerTests(ITestOutputHelper output)
{
    private static Part Mixed() => new(30, [.. Riff(16).Notes, .. Run(16, 120, 30, firstBar: 16, startMs: 16 * 2000).Notes]);

    [Fact]
    public void LevelsFollowTheMusicAndEveryChangeIsExplained()
    {
        var song = Mixed();
        var (o, parts) = Play(song, On);
        var db = song.Notes.Select((n, i) => Db(n, o[i])).ToArray();
        var structural = parts.Select(p => Dynamics.Total(p with { Residue = 0 }, HumanTable.Styles[1], HumanTable.Guitar)).ToArray();
        var explained = 1 - Std(db.Zip(structural, (a, b) => a - b)) * Std(db.Zip(structural, (a, b) => a - b)) / (Std(db) * Std(db));
        output.WriteLine($"level std {Std(db):F2} dB, structural {Std(structural):F2} dB, explained variance {explained:P1}, residue max {parts.Max(p => Math.Abs(p.Residue)):F2} dB");
        Assert.InRange(Std(db), 3.8, 5.2);   // natural about 4.5 dB (owner 4.3-6.1)
        Assert.True(explained > 0.8);
        Assert.All(parts, p => Assert.InRange(p.Residue, -HumanTable.ResidueDb, HumanTable.ResidueDb));
        // Between neighbours the change that the rules do not explain is the residue's slow step plus velocity rounding.
        var worst = Enumerable.Range(1, db.Length - 1).Max(i => Math.Abs(db[i] - db[i - 1] - (structural[i] - structural[i - 1])));
        output.WriteLine($"largest unexplained step between neighbours {worst:F2} dB");
        Assert.True(worst < 1);

        // Identical bars (the riff's bars 0-2, 4-6, ...): the same contour within 2.5 dB (after their mean), raw difference reported.
        var bars = song.Notes.Select((n, i) => (n, d: db[i])).Where(x => x.n.Bar < 16 && x.n.Bar % 4 != 3).GroupBy(x => x.n.Bar).Select(g => g.Select(x => x.d).ToArray()).ToList();
        var contour = bars.Skip(1).Max(b => b.Zip(bars[0], (x, y) => Math.Abs(x - b.Average() - (y - bars[0].Average()))).Max());
        var raw = bars.Skip(1).Max(b => b.Zip(bars[0], (x, y) => Math.Abs(x - y)).Max());
        output.WriteLine($"identical bars: contour difference {contour:F2} dB (raw incl. phrase arch {raw:F2} dB)");
        Assert.True(contour <= 2.5);   // half the 4.5 dB level spread
    }

    [Theory]
    [InlineData(100)]
    [InlineData(180)]
    public void FastRunsAreGroupedPickedAndCorrelatedWithoutFlams(double bpm)
    {
        var song = Run(32, bpm);
        var (o, parts) = Play(song, On);
        var s = Singles(song, o);
        var off = s.Select(x => x.Out.OnMs - x.In.OnMs).ToArray();
        var ioi = song.Notes[1].OnMs - song.Notes[0].OnMs;
        var first = song.Notes.Select((n, i) => (n, d: Db(n, o[i]))).Where(x => Math.Round(x.n.Beat * 4) % 4 == 0).Average(x => x.d);
        var rest = song.Notes.Select((n, i) => (n, d: Db(n, o[i]))).Where(x => Math.Round(x.n.Beat * 4) % 4 != 0).Average(x => x.d);
        var up = s.Where(x => Math.Round(x.In.Beat * 4) % 2 == 1).Average(x => x.Out.OnMs - x.In.OnMs);
        var down = s.Where(x => Math.Round(x.In.Beat * 4) % 2 == 0).Average(x => x.Out.OnMs - x.In.OnMs);
        var lag1 = Corr(off[..^1], off[1..]);
        var gaps = s.Zip(s.Skip(1), (a, b) => b.Out.OnMs - a.Out.OnMs).Min();
        output.WriteLine($"{bpm} bpm (16ths {ioi:F0} ms): onset std {Std(off):F1} ms, lag-1 of offsets {lag1:F2}, group accent {first - rest:F2} dB, " +
            $"upstrokes {up - down:F1} ms later, closest onsets {gaps:F1} ms, level std {Std(song.Notes.Select((n, i) => Db(n, o[i]))):F2} dB");
        Assert.InRange(Std(off), 5, 12);
        Assert.True(lag1 > 0.3);   // smooth pushes and pulls, not independent jitter
        Assert.InRange(first - rest, 4, 9);   // owner: the two pulse positions differ by 5-7 dB
        Assert.True(up > down);
        Assert.True(gaps > 0.4 * ioi);
        Assert.Equal(song.Notes.Count, o.Length);
        Assert.All(o, x => Assert.True(x.OffMs > x.OnMs));
    }

    [Fact]
    public void ChordsKeepTheStrumModel()
    {
        var song = Strums(32);
        var (o, _) = Play(song, On);
        var strokes = song.Notes.Select((n, i) => (n, o: o[i])).GroupBy(x => x.n.Stroke).Where(g => g.Count() >= 3).Select(g =>
        {
            var hits = g.OrderBy(x => x.o.OnMs).ToList();
            return (Spread: hits[^1].o.OnMs - hits[0].o.OnMs, Down: hits[0].n.Key == g.Min(x => x.n.Key), Gap: hits.Zip(hits.Skip(1), (a, b) => b.o.OnMs - a.o.OnMs).Max());
        }).ToList();
        var (down, up) = (Median(strokes.Where(x => x.Down).Select(x => x.Spread)), Median(strokes.Where(x => !x.Down).Select(x => x.Spread)));
        output.WriteLine($"strum spread median down {down:F1} ms, up {up:F1} ms, largest gap {strokes.Max(x => x.Gap):F1} ms");
        Assert.InRange(down, 15, 35);
        Assert.True(up < down && strokes.Max(x => x.Gap) < 40);
    }

    [Fact]
    public void AMidSongStartOrAnEditChangesOnlyTheNeighbourhood()
    {
        var song = Mixed();
        var whole = Play(song, On).Out;
        Assert.Equal(whole, Play(song, On).Out);   // deterministic
        const int start = 10;
        var idx = Enumerable.Range(0, song.Notes.Count).Where(i => song.Notes[i].Bar >= start - HumanTable.Before).ToArray();
        var tail = new Part(song.Program, idx.Select(i => song.Notes[i]).ToList());
        var late = Play(tail, On).Out;
        for (var j = 0; j < idx.Length; j++) if (song.Notes[idx[j]].Bar >= start) Assert.Equal(whole[idx[j]], late[j]);

        const int edit = 20;
        var e = song.Notes.FindIndex(n => n.Bar == edit);
        var changed = new Part(song.Program, song.Notes.Select((n, i) => i == e ? n with { Key = n.Key + 2, Velocity = 60 } : n).ToList());
        var after = Play(changed, On).Out;
        var moved = Enumerable.Range(0, whole.Length).Where(i => i != e && whole[i] != after[i]).Select(i => song.Notes[i].Bar).Distinct().ToList();
        output.WriteLine($"edit in bar {edit}: notes changed in bars {string.Join(",", moved)}");
        Assert.All(moved, b => Assert.InRange(b, edit - HumanTable.After, edit + HumanTable.Before));
    }

    [Fact]
    public void EveryNoteKeepsItsNoteOffAndKeyOrderUnderFuzz()
    {
        var r = new Random(3);
        var w = new Writer(130);
        for (var i = 0; i < 3000; i++) w.Note(i / 16, i % 16 * 0.25 + (r.Next(5) == 0 ? 0.125 : 0), 0.05 + r.NextDouble() * 1.5, 40 + r.Next(10), r.Next(3) == 0 ? 90 : r.Next(1, 128), r.Next(6));
        foreach (var o in new[] { On, With(Feel.Loose), With(Feel.Tight) with { HumanizeDrift = false } })
        {
            var (outp, _) = Play(new Part(30, w.Notes), o);
            foreach (var key in Enumerable.Range(0, outp.Length).GroupBy(i => w.Notes[i].Key))
            {
                var seq = key.OrderBy(i => w.Notes[i].OnMs).ThenBy(i => i).Select(i => outp[i]).ToList();
                Assert.All(seq, x => Assert.True(x.OffMs > x.OnMs && x.OnMs >= 0));
                Assert.True(seq.Zip(seq.Skip(1)).All(p => p.Second.OnMs > p.First.OnMs && p.First.OffMs <= p.Second.OnMs));
            }
        }
    }

    [Fact]
    public void HumanInputOtherProgramsAndOptionsOffPassUnchanged()
    {
        foreach (var human in new[] { AlreadyHuman(), AlreadyHuman(wide: true) })
            foreach (var f in new[] { Feel.Tight, Feel.Natural, Feel.Loose })
                Assert.Equal(human.Notes.Select(n => new HumanOut(n.OnMs, n.OffMs, n.Velocity)), Play(human, With(f)).Out);
        var flat = Riff(4);
        HumanOut[] Same(Part p) => p.Notes.Select(n => new HumanOut(n.OnMs, n.OffMs, n.Velocity)).ToArray();
        Assert.Equal(Same(flat), Play(flat, SynthOptions.Default).Out);
        Assert.Equal(Same(flat), Play(flat, On with { HumanizeVelocity = false, HumanizeTiming = false }).Out);
        Assert.Equal(Same(flat), Play(flat with { Program = 0 }, On).Out);
        Assert.NotEqual(Same(flat), Play(flat, On).Out);
    }

    [Fact]
    public void FeelsGrowFromTightToLoose()
    {
        var song = Mixed();
        var sd = new[] { Feel.Tight, Feel.Natural, Feel.Loose }.Select(f => Play(song, With(f)).Out).Select(o =>
            (Db: Std(song.Notes.Select((n, i) => Db(n, o[i]))), Ms: Std(song.Notes.Select((n, i) => o[i].OnMs - n.OnMs)))).ToArray();
        output.WriteLine(string.Join("; ", sd.Select((x, i) => $"{(Feel)i}: {x.Db:F2} dB, {x.Ms:F1} ms")));
        Assert.True(sd[0].Db < sd[1].Db && sd[1].Db < sd[2].Db && sd[0].Ms < sd[1].Ms && sd[1].Ms < sd[2].Ms);
        Assert.InRange(sd[0].Db, 2.5, 3.5);
        Assert.InRange(sd[2].Db, 5.2, 7);
    }

    [Fact]
    public void ThePassIsCheapAndTheSynthVariesVoicesWithoutAllocating()
    {
        var big = new Part(30, Enumerable.Range(0, 10).SelectMany(k => Riff(16, 120, k * 16, k * 16 * 2000).Notes).ToList());
        Play(big, On);
        var watch = Stopwatch.StartNew();
        Play(big, On);
        output.WriteLine($"pass: {watch.Elapsed.TotalMilliseconds * 1000 / big.Notes.Count:F2} us per note ({big.Notes.Count} notes, {big.Notes[^1].OnMs / 60000:F1} min of music in {watch.Elapsed.TotalMilliseconds:F1} ms)");
        Assert.True(watch.Elapsed.TotalMilliseconds * 1000 / big.Notes.Count < 50);

        var s = new Synthesizer(new SoundFont(TestSoundFont.Build()), 48000, 64, effects: true) { Layer = new GuitarLayer(48000), MasterVolume = 0.3f, Options = On };
        float[] l = new float[48000], r = new float[48000];
        long Play1()
        {
            s.Reset();
            s.ProcessMidiMessage(0, 0xC0, 25, 0);
            var before = GC.GetAllocatedBytesForCurrentThread();
            foreach (var k in Six[0]) s.ProcessMidiMessage(0, 0x90, k, 90);
            s.Render(l, r);
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
        Play1();
        var a = l.ToArray();
        Assert.Equal(0, Play1());
        Assert.Equal(a, l);
        Assert.True(l.All(float.IsFinite) && l.Max(MathF.Abs) > 1e-3);
    }
}

