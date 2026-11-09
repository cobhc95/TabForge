using System;
using System.Collections.Generic;
using System.Linq;
using TabForge.Sf2Synth;
using TabForge.Sf2Synth.Humanize;

namespace TabForge.Avalonia.Tests;

// Owns: HumanizerTests' written songs (flat: one velocity, exact grid, real meter and tempo; and played-in ones), running the
//   pass, and the statistics (level dB, offsets, strokes, correlation).
// Does not own: the assertions (HumanizerTests.cs), the demo (HumanizerTests.Demo.cs).
// Tests: Humanizer, Dynamics, Timing, Evidence, Strum, Noise.
public sealed partial class HumanizerTests
{
    private static readonly SynthOptions On = new() { Humanize = true };
    private static SynthOptions With(Feel f) => On with { Feel = f };

    /// <summary>A part: its program and written notes.</summary>
    private sealed record Part(int Program, List<HumanNote> Notes);

    /// <summary>Writes notes bar by bar at a tempo and meter; bar keys from <paramref name="firstBar"/>, stroke = bar and position.</summary>
    private sealed class Writer(double bpm, int num = 4, int den = 4, int firstBar = 0, double startMs = 0)
    {
        public readonly List<HumanNote> Notes = [];
        public double BeatMs => 60000 / bpm * 4 / den;
        public double EndMs(int bars) => startMs + bars * num * BeatMs;

        public void Note(int bar, double beat, double beats, int key, int vel = 90, int str = -1, Marks m = Marks.None, double jitMs = 0)
        {
            var on = startMs + (bar * num + beat) * BeatMs + jitMs;
            Notes.Add(new(on, on + beats * BeatMs, key, vel, str, firstBar + bar, firstBar, beat + jitMs / BeatMs, num, den, BeatMs,
                (firstBar + bar) * 10000L + (long)Math.Round(beat * 48), m));
        }

        public void Chord(int bar, double beat, double beats, int[] keys, int vel = 90, Marks m = Marks.None)
        {
            for (var s = 0; s < keys.Length; s++) Note(bar, beat, beats, keys[s], vel, keys.Length - 1 - s, m);
        }
    }

    private static readonly int[] Scale = [40, 42, 43, 45, 47, 48, 50, 52, 54, 55, 57, 59, 60, 62, 64, 66, 67];

    // String of a scale step on a standard-tuned guitar, about three notes a string (5 = low E).
    private static int StringOf(int key) => Math.Clamp(5 - (key - 40) / 5, 0, 5);

    /// <summary>A palm-muted riff (repeated bars and a variation every fourth bar), program 30, 4/4.</summary>
    private static Part Riff(int bars = 16, double bpm = 120, int firstBar = 0, double startMs = 0)
    {
        var w = new Writer(bpm, firstBar: firstBar, startMs: startMs);
        for (var b = 0; b < bars; b++)
        {
            var variation = b % 4 == 3;
            double[] beats = variation ? [0, 0.5, 1, 1.5, 2, 2.75, 3, 3.5] : [0, 0.5, 1, 1.5, 2, 2.5, 3, 3.5];
            int[] keys = variation ? [40, 40, 43, 40, 45, 43, 40, 38] : [40, 40, 40, 47, 40, 40, 45, 43];
            for (var i = 0; i < beats.Length; i++)
            {
                var pm = keys[i] == 40 ? Marks.PalmMute : Marks.None;
                var len = pm != 0 ? 0.35 : 0.48;
                w.Note(b, beats[i], len, keys[i], str: keys[i] < 45 ? 5 : 4, m: pm);
                if (keys[i] > 40) w.Note(b, beats[i], len, keys[i] + 7, str: keys[i] < 45 ? 4 : 3);   // power chord
            }
        }
        return new(30, w.Notes);
    }

    /// <summary>Alternate-picked 16th scale runs up and down, program 27 by default.</summary>
    private static Part Run(int bars = 16, double bpm = 140, int program = 27, int firstBar = 0, double startMs = 0)
    {
        var w = new Writer(bpm, firstBar: firstBar, startMs: startMs);
        for (var b = 0; b < bars; b++)
            for (var j = 0; j < 16; j++)
            {
                var step = b % 2 == 0 ? j : 15 - j;
                var key = Scale[step % Scale.Length];
                w.Note(b, j * 0.25, 0.24, key, str: StringOf(key));
            }
        return new(program, w.Notes);
    }

    private static readonly int[][] Six = [[40, 47, 52, 56, 59, 64], [45, 52, 57, 61, 64], [50, 57, 62, 66], [43, 47, 50, 55, 59, 67]];

    /// <summary>Chord strums down on beats and up on 8th offbeats (written grid), with a fast single-note fill every fourth bar.</summary>
    private static Part Strums(int bars = 16, int program = 25, double bpm = 100, bool fill = false, int firstBar = 0, double startMs = 0)
    {
        var w = new Writer(bpm, firstBar: firstBar, startMs: startMs);
        double[] rhythm = [0, 1, 1.5, 2.5, 3, 3.5];
        for (var b = 0; b < bars; b++)
        {
            var filled = fill && b % 4 == 3;
            foreach (var t in rhythm.Where(t => !filled || t < 2))
                w.Chord(b, t, (rhythm.FirstOrDefault(x => x > t, 4) - t) * 0.95, Six[b % 4]);
            if (filled) for (var j = 0; j < 8; j++) w.Note(b, 2 + j * 0.25, 0.24, new[] { 64, 66, 67, 69, 71, 69, 67, 66 }[j], str: j < 4 ? 1 : 0);
        }
        return new(program, w.Notes);
    }

    /// <summary>A bass line of 8ths with a 16th walking section in every second half.</summary>
    private static Part Bass(int bars = 16, double bpm = 120, int firstBar = 0, double startMs = 0)
    {
        var w = new Writer(bpm, firstBar: firstBar, startMs: startMs);
        int[] root = [28, 33, 26, 31];
        for (var b = 0; b < bars; b++)
        {
            var r = root[b % 4];
            if (b % 8 < 4) for (var j = 0; j < 8; j++) w.Note(b, j * 0.5, 0.45, j == 7 ? r + 7 : r, str: 3);
            else for (var j = 0; j < 16; j++) w.Note(b, j * 0.25, 0.23, r + new[] { 0, 2, 4, 5, 7, 5, 4, 2 }[j % 8], str: 3 - j % 8 / 3);
        }
        return new(33, w.Notes);
    }

    /// <summary>Written as a player would: velocities vary, onsets a few ms off the grid.</summary>
    private static Part AlreadyHuman(bool wide = false)
    {
        var r = new Random(7);
        var w = new Writer(120);
        for (var b = 0; b < 8; b++)
            for (var e = 0; e < 8; e++)
                w.Note(b, e * 0.5, 0.45, Scale[(b + e) % 12], wide ? 55 + r.Next(0, 66) : (e % 4 == 0 ? 96 : 78) + r.Next(-11, 12), jitMs: (r.NextDouble() * 2 - 1) * (wide ? 12 : 8));
        return new(27, w.Notes);
    }

    private static (HumanOut[] Out, LevelParts[] Parts) Play(Part p, SynthOptions o, uint seed = 1)
    {
        var (outp, parts) = (new HumanOut[p.Notes.Count], new LevelParts[p.Notes.Count]);
        Humanizer.Apply(p.Notes.ToArray(), outp, p.Program, o, seed, parts);
        return (outp, parts);
    }

    private static double Db(HumanNote a, HumanOut b) => 40 * Math.Log10((double)b.Velocity / a.Velocity);
    private static double Std(IEnumerable<double> x) { var a = x.ToArray(); var m = a.Average(); return Math.Sqrt(a.Average(v => (v - m) * (v - m))); }
    private static double Median(IEnumerable<double> x) { var a = x.OrderBy(v => v).ToArray(); return a[a.Length / 2]; }

    private static double Corr(IList<double> a, IList<double> b)
    {
        double ma = a.Average(), mb = b.Average(), ab = 0, aa = 0, bb = 0;
        for (var i = 0; i < a.Count; i++) { ab += (a[i] - ma) * (b[i] - mb); aa += (a[i] - ma) * (a[i] - ma); bb += (b[i] - mb) * (b[i] - mb); }
        return ab / Math.Sqrt(aa * bb);
    }

    /// <summary>Single-note strokes in time order: (input, output) pairs.</summary>
    private static List<(HumanNote In, HumanOut Out)> Singles(Part p, HumanOut[] o) =>
        p.Notes.Select((n, i) => (n, o[i])).GroupBy(x => x.n.Stroke).Where(g => g.Count() == 1).Select(g => g.First()).OrderBy(x => x.n.OnMs).ToList();
}
