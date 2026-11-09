using System;
using System.IO;
using System.Text;

namespace TabForge.Sf2Synth.Tests;

// Owns: a tiny synthetic SoundFont 2 for the Sf2Synth tests: preset 0:0 "Sine" (looped 440 Hz sine at 44 kHz, root 69, release
//   0.25 s) and 128:0 "Kit" (an unlooped noise burst), plus WAV writing for listening files.
// Does not own: the code under test.
// Tests: Sf2SynthTests, GuitarLayerTests.
internal static class TestSoundFont
{
    public const int SampleRate = 44000, SineLength = 4000, LoopStart = 1000, LoopEnd = 3000, NoiseLength = 2000, Guard = 46;

    public static byte[] Build()
    {
        var smpl = new MemoryStream();
        var s = new BinaryWriter(smpl);
        for (var i = 0; i < SineLength; i++) s.Write((short)(Math.Sin(2 * Math.PI * i / 100) * 16000));   // period 100 samples = 440 Hz
        for (var i = 0; i < Guard; i++) s.Write((short)0);
        var seed = 12345u;
        for (var i = 0; i < NoiseLength; i++) { seed = seed * 1664525 + 1013904223; s.Write((short)((int)(seed >> 16) - 32768 >> 1)); }
        for (var i = 0; i < Guard; i++) s.Write((short)0);

        var pdta = new MemoryStream();
        var p = new BinaryWriter(pdta);
        Chunk(p, "phdr", w => { Phdr(w, "Sine", 0, 0, 0); Phdr(w, "Kit", 0, 128, 1); Phdr(w, "EOP", 0, 0, 2); });
        Chunk(p, "pbag", w => { Bag(w, 0, 0); Bag(w, 1, 0); Bag(w, 2, 0); });
        Chunk(p, "pmod", w => w.Write(new byte[10]));
        Chunk(p, "pgen", w => { Gen(w, 41, 0); Gen(w, 41, 1); Gen(w, 0, 0); });
        Chunk(p, "inst", w => { Name(w, "SineInst"); w.Write((ushort)0); Name(w, "Noise"); w.Write((ushort)1); Name(w, "EOI"); w.Write((ushort)2); });
        Chunk(p, "ibag", w => { Bag(w, 0, 0); Bag(w, 3, 0); Bag(w, 4, 0); });
        Chunk(p, "imod", w => w.Write(new byte[10]));
        Chunk(p, "igen", w => { Gen(w, 54, 1); Gen(w, 38, -2400); Gen(w, 53, 0); Gen(w, 53, 1); Gen(w, 0, 0); });
        Chunk(p, "shdr", w =>
        {
            Shdr(w, "Sine", 0, SineLength, LoopStart, LoopEnd, 69);
            var n = SineLength + Guard;
            Shdr(w, "Noise", n, n + NoiseLength, n, n + NoiseLength, 60);
            Shdr(w, "EOS", 0, 0, 0, 0, 0);
        });

        var body = new MemoryStream();
        var b = new BinaryWriter(body);
        b.Write("sfbk"u8);
        List(b, "INFO", w => Chunk(w, "ifil", x => { x.Write((ushort)2); x.Write((ushort)4); }));
        List(b, "sdta", w => Chunk(w, "smpl", x => x.Write(smpl.ToArray())));
        List(b, "pdta", w => w.Write(pdta.ToArray()));
        var file = new MemoryStream();
        var f = new BinaryWriter(file);
        f.Write("RIFF"u8);
        f.Write((int)body.Length);
        f.Write(body.ToArray());
        return file.ToArray();
    }

    private static void Chunk(BinaryWriter w, string id, Action<BinaryWriter> write)
    {
        var m = new MemoryStream();
        write(new BinaryWriter(m));
        w.Write(Encoding.ASCII.GetBytes(id));
        w.Write((int)m.Length);
        w.Write(m.ToArray());
    }

    private static void List(BinaryWriter w, string type, Action<BinaryWriter> write) =>
        Chunk(w, "LIST", x => { x.Write(Encoding.ASCII.GetBytes(type)); write(x); });

    private static void Name(BinaryWriter w, string name) { var b = new byte[20]; Encoding.ASCII.GetBytes(name).CopyTo(b, 0); w.Write(b); }
    private static void Phdr(BinaryWriter w, string name, int program, int bank, int bag) { Name(w, name); w.Write((ushort)program); w.Write((ushort)bank); w.Write((ushort)bag); w.Write(new byte[12]); }
    private static void Bag(BinaryWriter w, int gen, int mod) { w.Write((ushort)gen); w.Write((ushort)mod); }
    private static void Gen(BinaryWriter w, int op, short amount) { w.Write((ushort)op); w.Write(amount); }
    private static void Shdr(BinaryWriter w, string name, int start, int end, int loopStart, int loopEnd, int root)
    {
        Name(w, name); w.Write(start); w.Write(end); w.Write(loopStart); w.Write(loopEnd); w.Write(SampleRate); w.Write((byte)root); w.Write((sbyte)0); w.Write((ushort)0); w.Write((ushort)1);
    }

    /// <summary>Cents between the period found by autocorrelation (searched within ±6 % of the expected one) and <paramref name="hz"/>.</summary>
    public static double CentsOff(float[] x, int rate, int from, int window, double hz)
    {
        var expected = rate / hz;
        window = (int)Math.Round(Math.Floor(window / expected) * expected);   // whole periods: no window bias on a pure tone
        int lo = (int)(expected * 0.94), hi = (int)Math.Ceiling(expected * 1.06), best = lo;
        double Corr(int lag) { double s = 0; for (var i = from; i < from + window; i++) s += x[i] * x[i + lag]; return s; }
        var bestValue = double.MinValue;
        for (var lag = lo; lag <= hi; lag++) { var c = Corr(lag); if (c > bestValue) (bestValue, best) = (c, lag); }
        double a = Corr(best - 1), c2 = Corr(best + 1);
        var shift = 0.5 * (a - c2) / (a - 2 * bestValue + c2);
        return 1200 * Math.Log2(expected / (best + (double.IsFinite(shift) ? shift : 0)));
    }

    /// <summary>Writes 16-bit stereo when TABFORGE_SYNTH_WAV_DIR names a folder; false (nothing written) otherwise.</summary>
    public static bool WriteWav(string name, float[] left, float[] right, int rate)
    {
        var folder = Environment.GetEnvironmentVariable("TABFORGE_SYNTH_WAV_DIR");
        if (string.IsNullOrEmpty(folder)) return false;
        Directory.CreateDirectory(folder);
        using var w = new BinaryWriter(File.Create(Path.Combine(folder, name)));
        w.Write("RIFF"u8); w.Write(36 + left.Length * 4); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)2); w.Write(rate); w.Write(rate * 4); w.Write((short)4); w.Write((short)16);
        w.Write("data"u8); w.Write(left.Length * 4);
        for (var i = 0; i < left.Length; i++)
        {
            w.Write((short)Math.Clamp(Math.Round(left[i] * 32767), -32768, 32767));
            w.Write((short)Math.Clamp(Math.Round(right[i] * 32767), -32768, 32767));
        }
        return true;
    }
}

