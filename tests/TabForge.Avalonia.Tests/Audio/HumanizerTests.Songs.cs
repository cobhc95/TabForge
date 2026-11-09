using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TabForge.Sf2Synth;
using TabForge.Sf2Synth.Humanize;

namespace TabForge.Avalonia.Tests;

// Owns: HumanizerTests' songs (flat: velocity 90, exact grid, 120 bpm; and played-in ones), the harness that drives the humanizer
//   as Synthesizer does (times shifted back by the reported latency), the per-note match of output to input, and the statistics
//   (level dB, offsets, strum spread, bar-to-bar correlation, lag-1 interval correlation, DFA exponent), plus the .mid writer.
// Does not own: the assertions (HumanizerTests.cs).
// Tests: Humanizer, Phrase, Strum, Evidence, Noise.
public sealed partial class HumanizerTests
{
    private const int Rate = 48000;
    private static readonly SynthOptions On = new() { Humanize = true };
    private static SynthOptions With(Feel f) => On with { Feel = f };
    private record struct Ev(long At, int Status, int D1, int D2);

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

    private static List<Ev> Riff(int program = 30) => Song(program, s =>
    {
        for (var bar = 0; bar < 4; bar++)
        {
            var run = bar % 2 == 1;   // palm-muted 8th chugs; every second bar a 16th run down from the half-bar
            for (var e = 0; e < (run ? 4 : 8); e++) foreach (var k in new[] { 40, 47 }) Note(s, 0, bar * 4 + e * 0.5, 0.3, k);
            if (run) for (var e = 0; e < 8; e++) Note(s, 0, bar * 4 + 2 + e * 0.25, 0.23, new[] { 52, 50, 48, 47, 45, 43, 42, 40 }[e]);
        }
    });

    private static readonly int[][] ArpBars = [[57, 60, 64, 69, 64, 60, 64, 60], [48, 55, 60, 64, 67, 64, 60, 55], [43, 50, 55, 59, 62, 59, 55, 50], [40, 47, 52, 55, 59, 55, 52, 47]];

    private static List<Ev> Arpeggio() => Song(27, s => { for (var bar = 0; bar < 4; bar++) for (var e = 0; e < 8; e++) Note(s, 0, bar * 4 + e * 0.5, 0.95, ArpBars[bar][e]); });

    private static List<Ev> Strum(int program = 25) => Song(program, s =>
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

    private static readonly int[][] Six = [[40, 47, 52, 56, 59, 64], [40, 45, 52, 57, 60, 64], [43, 47, 50, 55, 59, 67], [43, 48, 52, 55, 60, 64]];

    // 64 bars of six-string strums on every 8th (step 0.5) or 16th (0.25) beat; and 64 bars of a 16th single-note line.
    private static List<Ev> Strums(double step, int bars = 64) => Song(25, s =>
    {
        for (var b = 0; b < bars; b++) for (var t = 0.0; t < 4; t += step) foreach (var k in Six[b % 4]) Note(s, 0, b * 4 + t, step * 0.9, k);
    });

    private static List<Ev> Line() => Song(27, s => { for (var j = 0; j < 64 * 16; j++) Note(s, 0, j * 0.25, 0.22, ArpBars[j / 16 % 4][j % 8]); });

    // Written as a player would: velocities vary, onsets a few ms off the grid (wide = std >= 15 and 12 ms jitter).
    private static List<Ev> AlreadyHuman(bool wide = false)
    {
        var r = new Random(7);
        return Song(27, s =>
        {
            for (var bar = 0; bar < 4; bar++)
                for (var e = 0; e < 8; e++)
                    Note(s, 0, bar * 4 + e * 0.5, 0.95, ArpBars[bar][e], wide ? 55 + r.Next(0, 66) : (e % 4 == 0 ? 96 : 78) + r.Next(-11, 12), (r.NextDouble() * 2 - 1) * (wide ? 12 : 8));
        });
    }

    private static IEnumerable<(string, List<Ev>)> Songs() => [("riff30", Riff()), ("arp27", Arpeggio()), ("strum25", Strum()), ("bass33", Bass())];
    private static IEnumerable<(string, List<Ev>)> ListeningSongs() => [.. Songs(), ("riff29", Riff(29)), ("strum27", Strum(27))];

    // ---- harness: add at the frame, commit, take what is due; output times shifted back by the latency ----------------------------

    private static List<Ev> Run(List<Ev> song, SynthOptions o, Humanizer? h = null)
    {
        h ??= new Humanizer(Rate);
        h.Reset();
        var lat = h.Latency(o);
        var res = new List<Ev>();
        long now = 0;
        for (var i = 0; ;)
        {
            for (; i < song.Count && song[i].At == now; i++)
                if (!h.Add(song[i].Status & 15, song[i].Status & 0xF0, song[i].D1, song[i].D2, o.Humanize)) res.Add(song[i]);
            h.Commit(now, o);
            while (h.Take(now, out var e)) res.Add(new(now - lat, e.Command | e.Channel, e.Data1, e.Data2));
            var next = Math.Min(i < song.Count ? song[i].At : long.MaxValue, h.NextDue);
            if (next == long.MaxValue) return res;
            now = next;
        }
    }

    private static bool IsOn(Ev e) => (e.Status & 0xF0) == 0x90 && e.D2 > 0;
    private static bool IsOff(Ev e) => (e.Status & 0xF0) == 0x80 || (e.Status & 0xF0) == 0x90 && e.D2 == 0;
    private static double Std(IEnumerable<double> x) { var a = x.ToArray(); var m = a.Average(); return Math.Sqrt(a.Average(v => (v - m) * (v - m))); }
    private static double Median(IEnumerable<double> x) { var a = x.OrderBy(v => v).ToArray(); return a[a.Length / 2]; }

    private static double Corr(IList<double> a, IList<double> b)
    {
        double ma = a.Average(), mb = b.Average(), ab = 0, aa = 0, bb = 0;
        for (var i = 0; i < a.Count; i++) { ab += (a[i] - ma) * (b[i] - mb); aa += (a[i] - ma) * (a[i] - ma); bb += (b[i] - mb) * (b[i] - mb); }
        return ab / Math.Sqrt(aa * bb);
    }

    /// <summary>Each input note-on with its output: (input index among note-ons, input, output); per key in order.</summary>
    private static List<(int I, Ev In, Ev Out)> Match(List<Ev> input, List<Ev> outp)
    {
        var a = input.Where(IsOn).ToList();
        var queues = outp.Where(IsOn).GroupBy(e => (e.Status & 15) * 128 + e.D1).ToDictionary(g => g.Key, g => new Queue<Ev>(g));
        return a.Select((e, i) => (i, e, queues[(e.Status & 15) * 128 + e.D1].Dequeue())).ToList();
    }

    private static double Db(Ev a, Ev b) => 40 * Math.Log10((double)b.D2 / a.D2);
    private static double Ms(Ev a, Ev b) => (b.At - a.At) * 1000.0 / Rate;

    /// <summary>Strokes (notes sharing an input instant): spread ms, downstroke (lowest key first), level dB (power mean of the strings), largest gap ms.</summary>
    private static List<(long At, double Spread, bool Down, double Db, double Gap)> Strokes(List<Ev> input, List<Ev> outp) =>
        Match(input, outp).GroupBy(m => m.In.At).Where(g => g.Count() >= 3).Select(g =>
        {
            var hits = g.OrderBy(m => m.Out.At).ToList();
            var gap = hits.Zip(hits.Skip(1), (x, y) => Ms(x.Out, y.Out)).Max();
            return (g.Key, Ms(hits[0].Out, hits[^1].Out), hits[0].In.D1 == g.Min(m => m.In.D1), 10 * Math.Log10(g.Average(m => Math.Pow(10, Db(m.In, m.Out) / 10))), gap);
        }).ToList();

    // Detrended fluctuation analysis (order 1): slope of log F(s) over box sizes lo..hi (doubling).
    private static double Dfa(double[] x, int lo, int hi)
    {
        var m = x.Average();
        var y = new double[x.Length];
        var c = 0.0;
        for (var i = 0; i < x.Length; i++) y[i] = c += x[i] - m;
        var pts = new List<(double, double)>();
        for (var s = lo; s <= hi; s *= 2)
        {
            double f = 0; var boxes = y.Length / s;
            for (var b = 0; b < boxes; b++)
            {
                double sx = 0, sy = 0, sxx = 0, sxy = 0;
                for (var i = 0; i < s; i++) { var v = y[b * s + i]; sx += i; sy += v; sxx += i * i; sxy += i * v; }
                var k = (s * sxy - sx * sy) / (s * sxx - sx * sx); var c0 = (sy - k * sx) / s;
                for (var i = 0; i < s; i++) { var d = y[b * s + i] - c0 - k * i; f += d * d; }
            }
            pts.Add((Math.Log(s), 0.5 * Math.Log(f / (boxes * s))));
        }
        double mx = pts.Average(p => p.Item1), my = pts.Average(p => p.Item2);
        return pts.Sum(p => (p.Item1 - mx) * (p.Item2 - my)) / pts.Sum(p => (p.Item1 - mx) * (p.Item1 - mx));
    }

    // Standard MIDI file, format 0, 480 ticks per quarter, 120 bpm (960 ticks a second).
    private static byte[] Mid(List<Ev> song)
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
        var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms))
        {
            w.Write("MThd"u8); w.Write(new byte[] { 0, 0, 0, 6, 0, 0, 0, 1, 0x01, 0xE0 });
            w.Write("MTrk"u8); w.Write(new[] { (byte)(t.Count >> 24), (byte)(t.Count >> 16), (byte)(t.Count >> 8), (byte)t.Count });
            w.Write(t.ToArray());
        }
        return ms.ToArray();
    }
}
