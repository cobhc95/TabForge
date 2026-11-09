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

namespace TabForge.Sf2Synth.Tests;

// Owns: the one listening demo (TABFORGE_SYNTH_WAV_DIR): six flat parts (palm-muted riff 30, fast 16th runs on 30 and 27, clean strums
//   with a fill 27, acoustic strum 25, bass 33) humanized with Natural, rendered by the synth (DI, captured amp), then flat piano
//   chords and a drum groove on the bank (TABFORGE_GS_SF2, else the test font), 1 s apart, one gain,
//   and the flat .mid they came from.
// Does not own: the parts (HumanizerTests.Songs.cs), the checks (HumanizerTests.cs).
// Tests: Humanizer through Synthesizer and GuitarLayer.
public sealed partial class HumanizerTests
{
    private const int Rate = 48000;

    private static List<(Part Part, double Bpm)> DemoParts() =>
    [
        (Riff(6, 120), 120), (Run(4, 140, 30), 140), (Run(4, 140, 27), 140),
        (Strums(4, 27, 110, fill: true), 110), (Strums(4, 25, 110), 110), (Bass(6, 120), 120),
    ];

    [Fact]
    public void WritesTheDemoWhenAsked()
    {
        var dir = Environment.GetEnvironmentVariable("TABFORGE_SYNTH_WAV_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        var takes = new List<(float[] L, float[] R)>();
        var watch = new Stopwatch();
        foreach (var (part, _) in DemoParts())
        {
            var (o, lp) = Play(part, On);
            var db = part.Notes.Select((n, i) => Db(n, o[i])).ToArray();
            var rest = db.Select((d, i) => d - Dynamics.Total(lp[i] with { Residue = 0 }, HumanTable.Styles[1], HumanTable.Of(part.Program)!)).ToArray();
            output.WriteLine($"program {part.Program}: level std {Std(db):F2} dB, explained {1 - Std(rest) * Std(rest) / (Std(db) * Std(db)):P0}, onset std {Std(part.Notes.Select((n, i) => o[i].OnMs - n.OnMs)):F1} ms");
            var ev = part.Notes.SelectMany((n, i) => new[] { (At: o[i].OnMs, On: true, n.Key, o[i].Velocity), (At: o[i].OffMs, On: false, n.Key, Velocity: 0) })
                .OrderBy(e => e.At).ThenBy(e => e.On ? 1 : 0).ToList();
            var s = new Synthesizer(new SoundFont(TestSoundFont.Build()), Rate, 64, effects: true) { Layer = new GuitarLayer(Rate), MasterVolume = 0.3f, Options = On };
            s.ProcessMidiMessage(0, 0xC0, part.Program, 0);
            var frames = (int)((ev[^1].At / 1000 + 0.8) * Rate);
            float[] l = new float[frames], r = new float[frames];
            var done = 0;
            watch.Start();
            foreach (var e in ev)
            {
                var f = Math.Clamp((int)Math.Round(e.At / 1000 * Rate), done, frames);
                if (f > done) { s.Render(l.AsSpan(done, f - done), r.AsSpan(done, f - done)); done = f; }
                s.ProcessMidiMessage(0, e.On ? 0x90 : 0x80, e.Key, e.Velocity);
            }
            s.Render(l.AsSpan(done), r.AsSpan(done));
            watch.Stop();
            takes.Add((l, r));
        }
        var bank = Environment.GetEnvironmentVariable("TABFORGE_GS_SF2");
        var font = string.IsNullOrEmpty(bank) ? new SoundFont(TestSoundFont.Build()) : new SoundFont(bank);
        takes.Add(Flat(font, 0, PianoChords()));
        takes.Add(Flat(font, 9, DrumGroove()));
        var gap = new float[Rate];
        var all = (L: takes.SelectMany(t => t.L.Concat(gap)).ToArray(), R: takes.SelectMany(t => t.R.Concat(gap)).ToArray());
        var gain = 0.9 / all.L.Concat(all.R).Max(MathF.Abs);
        TestSoundFont.WriteWav("tfsynth_FULL_DEMO_v5.wav", all.L.Select(v => (float)(v * gain)).ToArray(), all.R.Select(v => (float)(v * gain)).ToArray(), Rate);
        File.WriteAllBytes(Path.Combine(dir, "demo_v3_flat.mid"), DemoMid());
        output.WriteLine($"demo {all.L.Length / (double)Rate:F1} s, gain {20 * Math.Log10(gain):F1} dB, peak 0.90, render cpu {watch.Elapsed.TotalSeconds / (all.L.Length / (double)Rate) * 100:F1} % of one core (6 parts, one channel each)");
        Assert.True(all.L.All(float.IsFinite));
    }

    // Piano (program 0): C, Am, F, G as quarter-note chords at 100 bpm, flat velocity 90 (the humanizer leaves pianos alone).
    private static List<(double Ms, int Key, int Vel, double OffMs)> PianoChords()
    {
        int[][] chords = [[48, 60, 64, 67], [45, 57, 60, 64], [41, 57, 60, 65], [43, 55, 59, 62]];
        return [.. chords.SelectMany((c, bar) => Enumerable.Range(0, 4).SelectMany(q => c.Select(k => (Ms: (bar * 4 + q) * 600.0, Key: k, Vel: 90, OffMs: (bar * 4 + q) * 600.0 + 550))))];
    }

    // Drums (channel 10): 4 bars at 110 bpm, kick 1 and 3, snare 2 and 4, closed hat 8ths, flat velocity 100 (no drum humanizer yet).
    private static List<(double Ms, int Key, int Vel, double OffMs)> DrumGroove()
    {
        const double e = 60000 / 110.0 / 2;
        return [.. Enumerable.Range(0, 32).SelectMany(i => new[] { (Ms: i * e, Key: 42, Vel: 100, OffMs: i * e + 100) }
            .Concat(i % 4 == 0 ? [(i * e, i % 8 == 0 ? 36 : 38, 100, i * e + 100)] : []))];
    }

    // One flat part on the bank (no layer, no humanizer), then 0.8 s.
    private static (float[] L, float[] R) Flat(SoundFont font, int channel, List<(double Ms, int Key, int Vel, double OffMs)> notes)
    {
        var s = new Synthesizer(font, Rate, 64, effects: true) { MasterVolume = 0.3f };
        if (channel != 9) s.ProcessMidiMessage(channel, 0xC0, 0, 0);
        var ev = notes.SelectMany(n => new[] { (At: n.Ms, Key: n.Key, Vel: n.Vel), (At: n.OffMs, Key: n.Key, Vel: 0) }).OrderBy(x => x.At).ThenBy(x => x.Vel > 0 ? 1 : 0).ToList();
        var frames = (int)((ev[^1].At / 1000 + 0.8) * Rate);
        float[] l = new float[frames], r = new float[frames];
        var done = 0;
        foreach (var x in ev)
        {
            var f = Math.Clamp((int)Math.Round(x.At / 1000 * Rate), done, frames);
            if (f > done) { s.Render(l.AsSpan(done, f - done), r.AsSpan(done, f - done)); done = f; }
            s.ProcessMidiMessage(channel, x.Vel > 0 ? 0x90 : 0x80, x.Key, x.Vel);
        }
        s.Render(l.AsSpan(done), r.AsSpan(done));
        return (l, r);
    }

    // Standard MIDI file, format 0, 480 ticks per quarter: per part its tempo, meter and program (channel = part), flat notes, then 1.8 s.
    private static byte[] DemoMid()
    {
        var ev = new List<(long Tick, byte[] Data)>();
        long at = 0;
        var ch = 0;
        foreach (var (part, bpm) in DemoParts())
        {
            var us = (int)Math.Round(60_000_000 / bpm);
            ev.Add((at, [0xFF, 0x51, 3, (byte)(us >> 16), (byte)(us >> 8), (byte)us]));
            ev.Add((at, [0xFF, 0x58, 4, 4, 2, 24, 8]));
            ev.Add((at, [(byte)(0xC0 | ch), (byte)part.Program]));
            var beatMs = part.Notes[0].BeatMs;
            long T(double ms) => at + (long)Math.Round(ms / beatMs * 480);
            foreach (var n in part.Notes)
            {
                ev.Add((T(n.OnMs), [(byte)(0x90 | ch), (byte)n.Key, (byte)n.Velocity]));
                ev.Add((T(n.OffMs), [(byte)(0x80 | ch), (byte)n.Key, 0]));
            }
            at = T(part.Notes.Max(n => n.OffMs) + 1800);
            ch = ch + 1 == 9 ? 10 : ch + 1;
        }
        var t = new List<byte>();
        long last = 0;
        foreach (var (tick, data) in ev.OrderBy(e => e.Tick).ThenBy(e => (e.Data[0] & 0xF0) == 0x80 ? 0 : 1))
        {
            var d = tick - last;
            last = tick;
            var v = new List<byte> { (byte)(d & 0x7F) };
            while ((d >>= 7) > 0) v.Insert(0, (byte)(0x80 | (d & 0x7F)));
            t.AddRange(v);
            t.AddRange(data);
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

