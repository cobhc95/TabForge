using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace TabForge.Sf2Synth.Nam;

// Owns: stacked-LSTM inference for .nam files: per layer W (4H x (in+H), row-major, gates i f g o), bias 4H, initial h and c;
//   then a linear head on the last h. One frame at a time, no allocation.
// Does not own: file parsing (NamFile), levels and resampling (NamAmp).
// Tests: NamTests.
internal sealed class NamLstm : INamNet
{
    private sealed class Cell(int input, int hidden)
    {
        public readonly int In = input, H = hidden;
        public readonly float[] W = new float[4 * hidden * (input + hidden)], B = new float[4 * hidden], Xh = new float[input + hidden],
            C = new float[hidden], H0 = new float[hidden], C0 = new float[hidden], G = new float[4 * hidden];
    }

    private readonly Cell[] _cells;
    private readonly float[] _headW;
    private readonly float _headB;

    public NamLstm(JsonElement config, float[] w)
    {
        int layers = config.GetProperty("num_layers").GetInt32(), input = config.GetProperty("input_size").GetInt32(), h = config.GetProperty("hidden_size").GetInt32();
        _cells = Enumerable.Range(0, layers).Select(i => new Cell(i == 0 ? input : h, h)).ToArray();
        var at = 0;
        float[] Take(int n) { if (at + n > w.Length) throw new FormatException("too few weights"); var r = w.AsSpan(at, n).ToArray(); at += n; return r; }
        foreach (var c in _cells)
        {
            Take(c.W.Length).CopyTo(c.W, 0);
            Take(c.B.Length).CopyTo(c.B, 0);
            Take(c.H).CopyTo(c.H0, 0);
            Take(c.H).CopyTo(c.C0, 0);
        }
        _headW = Take(h);
        _headB = Take(1)[0];
        if (at != w.Length) throw new FormatException($"weights: {w.Length}, expected {at}");
        Reset();
    }

    public void Reset()
    {
        foreach (var c in _cells) { c.H0.CopyTo(c.Xh, c.In); c.C0.CopyTo(c.C, 0); }
    }

    public void Process(Span<float> io)
    {
        for (var n = 0; n < io.Length; n++) io[n] = Frame(io[n]);
    }

    private float Frame(float x)
    {
        _cells[0].Xh[0] = x;
        for (var l = 0; l < _cells.Length; l++)
        {
            var c = _cells[l];
            if (l > 0) Array.Copy(_cells[l - 1].Xh, _cells[l - 1].In, c.Xh, 0, c.In);
            int cols = c.Xh.Length, h = c.H;
            for (var r = 0; r < 4 * h; r++) c.G[r] = c.B[r] + Dot(c.W.AsSpan(r * cols, cols), c.Xh);
            for (var i = 0; i < h; i++)
            {
                var cell = NamAct.Sigmoid(c.G[h + i]) * c.C[i] + NamAct.Sigmoid(c.G[i]) * NamAct.Tanh(c.G[2 * h + i]);
                c.C[i] = cell;
                c.Xh[c.In + i] = NamAct.Sigmoid(c.G[3 * h + i]) * NamAct.Tanh(cell);
            }
        }
        var last = _cells[^1];
        return _headB + Dot(_headW, last.Xh.AsSpan(last.In));
    }

    private static float Dot(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        var i = 0;
        var acc = Vector<float>.Zero;
        if (a.Length >= Vector<float>.Count)
        {
            var va = MemoryMarshal.Cast<float, Vector<float>>(a);
            var vb = MemoryMarshal.Cast<float, Vector<float>>(b);
            for (var v = 0; v < va.Length; v++) acc += va[v] * vb[v];
            i = va.Length * Vector<float>.Count;
        }
        var s = Vector.Dot(acc, Vector<float>.One);
        for (; i < a.Length; i++) s += a[i] * b[i];
        return s;
    }
}
