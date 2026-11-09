using System;
using System.Linq;

namespace TabForge.Sf2Synth.Tests;

// Owns: a deliberately naive whole-signal reference of the .nam WaveNet and LSTM (double maths, explicit history indexing,
//   weights consumed in file order) that returns the output and how many weights the shape takes.
// Does not own: the checks (NamTests).
// Tests: none (it is the reference).
internal static class NamReference
{
    private static double Act(string name, double v) => name switch { "Tanh" => Math.Tanh(v), "ReLU" => Math.Max(0, v), "LeakyReLU" => v > 0 ? v : 0.05 * v, _ => v };
    private static double Sig(double v) => 1 / (1 + Math.Exp(-v));

    public static (float[] Y, int Weights) WaveNet(NamTests.Arr[] arrays, float[] w, float[] input)
    {
        var at = 0;
        double[] Read(int n) { var r = w.Skip(at).Take(n).Select(v => (double)v).ToArray(); at += n; return r; }
        int T = input.Length;
        double[][] layerIn = [input.Select(v => (double)v).ToArray()], prevHead = null!;
        var cond = layerIn[0];
        foreach (var a in arrays)
        {
            var r = Read(a.Ch * a.In);
            var x = Enumerable.Range(0, a.Ch).Select(c => Enumerable.Range(0, T).Select(t => Enumerable.Range(0, a.In).Sum(j => r[c * a.In + j] * layerIn[j][t])).ToArray()).ToArray();
            var hw = a.H1 > 0 ? a.H1 : a.B;
            var head = Enumerable.Range(0, hw).Select(j => prevHead is null ? new double[T] : (double[])prevHead[j].Clone()).ToArray();
            for (var l = 0; l < a.D.Length; l++)
            {
                int g = a.Gate[l] == "none" ? 1 : 2, K = a.K[l], d = a.D[l], zc = g * a.B;
                double[] wc = Read(zc * a.Ch * K), bc = Read(zc), wm = Read(zc), w1 = Read(a.Ch * a.B), b1 = Read(a.Ch);
                double[] wh = a.H1 > 0 ? Read(a.H1 * a.B) : [], bh = a.H1 > 0 ? Read(a.H1) : [];
                var z = new double[zc, T];
                for (var o = 0; o < zc; o++)
                    for (var t = 0; t < T; t++)
                    {
                        var s = bc[o] + wm[o] * cond[t];
                        for (var j = 0; j < a.Ch; j++)
                            for (var k = 0; k < K; k++) { var tt = t - d * (K - 1 - k); if (tt >= 0) s += wc[(o * a.Ch + j) * K + k] * x[j][tt]; }
                        z[o, t] = s;
                    }
                var act = new double[a.B, T];
                for (var i = 0; i < a.B; i++)
                    for (var t = 0; t < T; t++)
                        act[i, t] = a.Gate[l] switch
                        {
                            "gated" => Act(a.Act, z[i, t]) * Sig(z[a.B + i, t]),
                            "blended" => Sig(z[a.B + i, t]) * Act(a.Act, z[i, t]) + (1 - Sig(z[a.B + i, t])) * z[i, t],
                            _ => Act(a.Act, z[i, t]),
                        };
                for (var t = 0; t < T; t++)
                {
                    for (var j = 0; j < hw; j++) head[j][t] += a.H1 > 0 ? bh[j] + Enumerable.Range(0, a.B).Sum(i => wh[j * a.B + i] * act[i, t]) : act[j, t];
                    for (var c = 0; c < a.Ch; c++) x[c][t] += b1[c] + Enumerable.Range(0, a.B).Sum(i => w1[c * a.B + i] * act[i, t]);
                }
            }
            double[] hwts = Read(a.HeadOut * hw * a.HeadK), hb = a.HeadBias ? Read(a.HeadOut) : new double[a.HeadOut];
            prevHead = Enumerable.Range(0, a.HeadOut).Select(o => Enumerable.Range(0, T).Select(t =>
                hb[o] + Enumerable.Range(0, hw).Sum(j => Enumerable.Range(0, a.HeadK).Sum(k => t - (a.HeadK - 1 - k) >= 0 ? hwts[(o * hw + j) * a.HeadK + k] * head[j][t - (a.HeadK - 1 - k)] : 0))).ToArray()).ToArray();
            layerIn = x;
        }
        var scale = Read(1)[0];
        return (prevHead[0].Select(v => (float)(scale * v)).ToArray(), at);
    }

    public static (float[] Y, int Weights) Lstm(int layers, int h, float[] w, float[] input)
    {
        var at = 0;
        double[] Read(int n) { var r = w.Skip(at).Take(n).Select(v => (double)v).ToArray(); at += n; return r; }
        var p = Enumerable.Range(0, layers).Select(l => { var i = l == 0 ? 1 : h; return (In: i, W: Read(4 * h * (i + h)), B: Read(4 * h), H: Read(h), C: Read(h)); }).ToArray();
        double[] hw = Read(h), hb = Read(1);
        var y = new float[input.Length];
        for (var t = 0; t < input.Length; t++)
        {
            double[] below = [input[t]];
            foreach (var c in p)
            {
                var xh = below.Concat(c.H).ToArray();
                var gates = Enumerable.Range(0, 4 * h).Select(r => c.B[r] + Enumerable.Range(0, xh.Length).Sum(k => c.W[r * xh.Length + k] * xh[k])).ToArray();
                for (var i = 0; i < h; i++)
                {
                    c.C[i] = Sig(gates[h + i]) * c.C[i] + Sig(gates[i]) * Math.Tanh(gates[2 * h + i]);
                    c.H[i] = Sig(gates[3 * h + i]) * Math.Tanh(c.C[i]);
                }
                below = c.H;
            }
            y[t] = (float)(hb[0] + Enumerable.Range(0, h).Sum(i => hw[i] * below[i]));
        }
        return (y, at);
    }
}

