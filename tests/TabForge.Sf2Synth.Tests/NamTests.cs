using System;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using TabForge.Sf2Synth.Dsp;
using TabForge.Sf2Synth.Nam;
using Xunit;
using Xunit.Abstractions;

namespace TabForge.Sf2Synth.Tests;

// Owns: the checks of the NAM runtime: the published single-layer vectors, synthetic WaveNet (first architecture and A2
//   shapes) and LSTM files against a naive whole-signal reference, the tanh fit, CPU per architecture shape, the resampling
//   round trip (latency, error), FFT convolution against direct convolution, no allocation.
// Does not own: the guitar rig around them (GuitarLayerTests).
// Tests: NamFile, NamWaveNet, NamLstm, NamConv, NamAct, NamAmp, Resampler, FftConvolver.
public sealed class NamTests(ITestOutputHelper output)
{
    // One layer array: A2 = the newer keys (per-layer kernels and gates, bottleneck, head conv, head1x1).
    internal sealed record Arr(int In, int Ch, int B, int[] K, int[] D, string[] Gate, string Act, int H1, int HeadOut, int HeadK, bool HeadBias, bool A2);

    internal static Arr A1(int input, int ch, int head, bool bias, int[] dil, bool gated = false) =>
        new(input, ch, ch, dil.Select(_ => 3).ToArray(), dil, dil.Select(_ => gated ? "gated" : "none").ToArray(), "Tanh", 0, head, 1, bias, false);

    internal static Arr A2(int ch, int[] k, int[] dil, int headK = 16) =>
        new(1, ch, ch, k, dil, dil.Select(_ => "none").ToArray(), "LeakyReLU", 0, 1, headK, true, true);

    internal static JsonObject WaveNetJson(Arr[] arrays, float[] w, bool slim = false)
    {
        JsonNode Layer(Arr a) => a.A2
            ? new JsonObject
            {
                ["input_size"] = a.In, ["condition_size"] = 1, ["channels"] = a.Ch, ["bottleneck"] = a.B,
                ["kernel_sizes"] = new JsonArray(a.K.Select(k => (JsonNode)k).ToArray()), ["dilations"] = new JsonArray(a.D.Select(d => (JsonNode)d).ToArray()),
                ["activation"] = new JsonArray(a.D.Select(_ => (JsonNode)new JsonObject { ["type"] = a.Act, ["negative_slope"] = 0.05 }).ToArray()),
                ["gating_mode"] = new JsonArray(a.Gate.Select(g => (JsonNode)g).ToArray()),
                ["head"] = new JsonObject { ["out_channels"] = a.HeadOut, ["kernel_size"] = a.HeadK, ["bias"] = a.HeadBias },
                ["head1x1"] = new JsonObject { ["active"] = a.H1 > 0, ["out_channels"] = Math.Max(1, a.H1), ["groups"] = 1 },
                ["layer1x1"] = new JsonObject { ["active"] = true, ["groups"] = 1 }, ["conv_pre_film"] = new JsonObject { ["active"] = false },
                ["groups_input"] = 1, ["slimmable"] = null,
            }
            : new JsonObject
            {
                ["input_size"] = a.In, ["condition_size"] = 1, ["head_size"] = a.HeadOut, ["channels"] = a.Ch, ["kernel_size"] = a.K[0],
                ["dilations"] = new JsonArray(a.D.Select(d => (JsonNode)d).ToArray()), ["activation"] = a.Act, ["gated"] = a.Gate[0] == "gated", ["head_bias"] = a.HeadBias,
                ["slimmable"] = new JsonObject { ["method"] = "slice_channels_uniform" },   // full width = the plain model
            };
        var model = new JsonObject
        {
            ["version"] = "0.7.0", ["architecture"] = "WaveNet", ["sample_rate"] = 48000, ["metadata"] = new JsonObject { ["loudness"] = -20.0 },
            ["config"] = new JsonObject { ["layers"] = new JsonArray(arrays.Select(Layer).ToArray()), ["head"] = null, ["head_scale"] = 0.02 },
            ["weights"] = new JsonArray(w.Select(v => (JsonNode)v).ToArray()),
        };
        return !slim ? model : new JsonObject
        {
            ["version"] = "0.7.0", ["architecture"] = "SlimmableContainer",
            ["config"] = new JsonObject { ["submodels"] = new JsonArray(new JsonObject { ["max_value"] = 0.5, ["model"] = JsonNode.Parse("{\"architecture\":\"Linear\",\"config\":{},\"weights\":[]}") }, new JsonObject { ["max_value"] = 1.0, ["model"] = model }) },
        };
    }

    internal static NamModel Model(JsonObject json) => NamFile.Parse(JsonDocument.Parse(json.ToJsonString()).RootElement.Clone(), "test");

    internal static float[] Random(int n, int seed, float scale = 0.3f)
    {
        var r = new Random(seed);
        return Enumerable.Range(0, n).Select(_ => (float)((r.NextDouble() * 2 - 1) * scale)).ToArray();
    }

    private static float[] Run(NamModel m, float[] x)
    {
        var y = (float[])x.Clone();
        var net = m.Build();
        for (var at = 0; at < y.Length; at += 37) net.Process(y.AsSpan(at, Math.Min(37, y.Length - at)));
        return y;
    }

    // Weights from the published single-layer tests: gated ReLU/sigmoid gives head 0.25 at input 0.25; ungated ReLU gives 2 at input 1.
    [Theory]
    [InlineData(true, 0.25f, 0.25f)]
    [InlineData(false, 1f, 2f)]
    public void PublishedLayerVectorsMatch(bool gated, float input, float head)
    {
        var a = new Arr(1, 1, 1, [1], [1], [gated ? "gated" : "none"], "ReLU", 0, 1, 1, false, false);
        float[] layer = gated ? [1, 1, 0, 0, 1, -1, 1, 0] : [1, 0, 1, 1, 0];
        var m = Model(WaveNetJson([a], [1, .. layer, 1, 1]));   // rechannel 1, layer, head rechannel 1, head scale 1
        var y = Run(m, Enumerable.Repeat(input, 4).ToArray());
        Assert.All(y, v => Assert.Equal(head, v, 6));
    }

    public static TheoryData<string> Shapes => new() { "a1", "a1gated", "a2", "a2mixed", "lstm" };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void SyntheticModelsMatchTheNaiveReference(string shape)
    {
        var x = Random(3000, 7, 0.8f);
        var pool = Random(20000, 11);
        float[] want;
        NamModel m;
        if (shape == "lstm")
        {
            (want, var n) = NamReference.Lstm(2, 5, pool, x);
            m = Model(new JsonObject { ["architecture"] = "LSTM", ["config"] = new JsonObject { ["num_layers"] = 2, ["input_size"] = 1, ["hidden_size"] = 5 }, ["weights"] = new JsonArray(pool[..n].Select(v => (JsonNode)v).ToArray()) });
        }
        else
        {
            Arr[] arrays = shape switch
            {
                "a1" => [A1(1, 4, 3, false, [1, 2, 4, 8]), A1(4, 3, 1, true, [16, 1, 2])],
                "a1gated" => [A1(1, 4, 3, false, [1, 2, 4], gated: true), A1(4, 3, 1, true, [8, 1], gated: true)],
                "a2" => [A2(3, [6, 6, 15, 6], [1, 3, 13, 41], 16)],
                _ => [new Arr(1, 4, 3, [3, 5, 2], [1, 7, 2], ["none", "gated", "blended"], "LeakyReLU", 2, 1, 4, true, true)],
            };
            (want, var n) = NamReference.WaveNet(arrays, pool, x);
            m = Model(WaveNetJson(arrays, pool[..n], slim: shape == "a2"));
        }
        var got = Run(m, x);
        var err = got.Zip(want, (a, b) => Math.Abs(a - b)).Max();
        var rms = Math.Sqrt(want.Average(v => (double)v * v));
        output.WriteLine($"{shape} ({m.Architecture}): max error {err:E2}, output rms {rms:E2}");
        Assert.True(err < 1e-5 && rms > 1e-4, $"error {err}, rms {rms}");
    }

    [Fact]
    public void TanhFitIsFloatAccurate()
    {
        var err = Enumerable.Range(-20000, 40001).Max(i => Math.Abs(NamAct.Tanh(i / 2000f) - Math.Tanh(i / 2000.0)));
        Assert.True(err < 1e-6, $"{err}");
    }

    public static readonly int[] Dil10 = [1, 2, 4, 8, 16, 32, 64, 128, 256, 512];
    public static readonly int[] A2K = [6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 15, 15, 6, 6, 6, 6, 6, 6, 6];
    public static readonly int[] A2D = [1, 3, 7, 17, 41, 101, 239, 1, 3, 7, 17, 41, 101, 239, 1, 13, 1, 3, 7, 17, 41, 101, 239];

    [Fact]
    public void CostPerArchitectureIsMeasured()
    {
        int[] lite1 = [1, 2, 4, 8, 16, 32, 64], lite2 = [128, 256, 512, 1, 2, 4, 8, 16, 32, 64, 128, 256, 512];
        foreach (var (name, arrays) in new (string, Arr[])[]
                 {
                     ("standard", [A1(1, 16, 8, false, Dil10), A1(16, 8, 1, true, Dil10)]), ("lite", [A1(1, 12, 6, false, lite1), A1(12, 6, 1, true, lite2)]),
                     ("feather", [A1(1, 8, 4, false, lite1), A1(8, 4, 1, true, lite2)]), ("nano", [A1(1, 4, 2, false, lite1), A1(4, 2, 1, true, lite2)]),
                     ("A2 ch3", [A2(3, A2K, A2D)]), ("A2 ch8", [A2(8, A2K, A2D)]),
                 })
        {
            var (_, n) = NamReference.WaveNet(arrays, new float[100000], new float[1]);
            Report(name, Model(WaveNetJson(arrays, Random(n, 3, 0.1f))));
        }
        var (_, ln) = NamReference.Lstm(1, 24, new float[10000], new float[1]);
        Report("LSTM 1x24", Model(new JsonObject { ["architecture"] = "LSTM", ["config"] = new JsonObject { ["num_layers"] = 1, ["input_size"] = 1, ["hidden_size"] = 24 }, ["weights"] = new JsonArray(Random(ln, 3, 0.1f).Select(v => (JsonNode)v).ToArray()) }));
    }

    private void Report(string name, NamModel m)
    {
        var amp = new NamAmp(m, 48000, 512);
        var x = Random(48000, 5, 0.3f);
        for (var at = 0; at < x.Length / 2; at += 512) amp.Process(x.AsSpan(at, 512));   // past tiered JIT
        var best = double.MaxValue;   // the quietest of three runs: the machine may be busy
        for (var run = 0; run < 3; run++)
        {
            var watch = Stopwatch.StartNew();
            for (var at = 0; at < x.Length; at += 512) amp.Process(x.AsSpan(at, Math.Min(512, x.Length - at)));
            best = Math.Min(best, watch.Elapsed.TotalMilliseconds);
        }
        var before = GC.GetAllocatedBytesForCurrentThread();
        amp.Process(x.AsSpan(0, 512));
        var alloc = GC.GetAllocatedBytesForCurrentThread() - before;
        output.WriteLine($"{name}: {best:F1} ms per second of 48 kHz audio, alloc {alloc} B");
        Assert.Equal(0, alloc);
    }

    private sealed class Identity : INamNet
    {
        public void Reset() { }
        public void Process(Span<float> io) { }
    }

    [Theory]
    [InlineData(44100)]
    [InlineData(96000)]
    public void OtherEngineRatesRoundTripThroughTheModelRate(int rate)
    {
        float[] Through(float[] x)
        {
            var amp = new NamAmp(new NamModel("id", "id", 48000, -18, null, () => new Identity()), rate, 256);
            var y = (float[])x.Clone();
            for (var at = 0; at < y.Length; at += 200) amp.Process(y.AsSpan(at, Math.Min(200, y.Length - at)));
            return y;
        }
        var impulse = new float[4000];
        impulse[1000] = 1;
        var lag = Array.IndexOf(Through(impulse), Through(impulse).Max()) - 1000;
        var y = Through(Enumerable.Range(0, rate).Select(i => (float)(0.5 * Math.Sin(2 * Math.PI * 1000 * i / rate))).ToArray());
        // Residual after the best 1 kHz sine fit = everything the round trip adds.
        var span = Enumerable.Range(rate / 2, rate / 4).ToArray();
        double W(int i) => 2 * Math.PI * 1000 * i / rate;
        double a = span.Sum(i => y[i] * Math.Sin(W(i))) * 2 / span.Length, b = span.Sum(i => y[i] * Math.Cos(W(i))) * 2 / span.Length;
        var err = span.Max(i => Math.Abs(y[i] - a * Math.Sin(W(i)) - b * Math.Cos(W(i))));
        output.WriteLine($"{rate} Hz: latency {lag} samples ({1000.0 * lag / rate:F2} ms), 1 kHz gain {Math.Sqrt(a * a + b * b) / 0.5:F4}, residual {20 * Math.Log10(err / 0.5):F1} dB");
        Assert.True(err < 0.5 * 1e-3, $"{err}");
    }

    [Fact]
    public void FftConvolutionMatchesDirectConvolution()
    {
        var h = Random(1000, 2);
        var x = Random(5000, 4, 1);
        var y = (float[])x.Clone();
        var conv = new FftConvolver(h);
        for (var at = 0; at < y.Length; at += 50) conv.Process(y.AsSpan(at, Math.Min(50, y.Length - at)));
        double err = 0;
        for (var n = FftConvolver.Block; n < x.Length; n++)
        {
            double s = 0;
            for (var k = 0; k < h.Length && k <= n - FftConvolver.Block; k++) s += h[k] * x[n - FftConvolver.Block - k];
            err = Math.Max(err, Math.Abs(s - y[n]));
        }
        Assert.True(err < 1e-4, $"{err}");
    }
}

