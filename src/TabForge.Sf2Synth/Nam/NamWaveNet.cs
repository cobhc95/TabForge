using System.Text.Json;

namespace TabForge.Sf2Synth.Nam;

// Owns: WaveNet inference for .nam files, both the first architecture (fixed kernel, Tanh, optional sigmoid gate, 1x1 head)
//   and A2 (per-layer kernel sizes and activations, gating/blending, bottleneck, optional head1x1, K-tap head conv).
//   Per layer: z = conv(x) + mixin(cond); a = act(z) (gated: P(top)·S(bottom); blended: S·P + (1-S)·top); head += a or
//   head1x1(a); x += layer1x1(a). Arrays chain residual and head; output = head_scale · last head. Blocks of up to NamConv.Max frames, no allocation.
// Does not own: file parsing and model choice (NamFile), levels and resampling (NamAmp).
// Tests: NamTests.
internal sealed class NamWaveNet : INamNet
{
    private enum Gate { None, Gated, Blended }

    private sealed record Layer(NamConv Conv, NamConv Mix, NamConv? L1x1, NamConv? H1x1, NamAct Act, NamAct Sec, Gate Mode, int B);

    private sealed record Array_(NamConv Rechannel, Layer[] Layers, NamConv Head, int HeadIn, int Channels);

    private readonly Array_[] _arrays;
    private readonly float[] _x, _in, _z, _a, _head, _headOut, _cond = new float[M];
    private readonly float _scale;
    private const int M = NamConv.Max;

    public NamWaveNet(JsonElement config, float[] w)
    {
        if (config.TryGetProperty("head", out var post) && post.ValueKind != JsonValueKind.Null) throw new FormatException("post-stack head not supported");
        if (config.TryGetProperty("condition_dsp", out var cd) && cd.ValueKind != JsonValueKind.Null) throw new FormatException("condition_dsp not supported");
        _arrays = config.GetProperty("layers").EnumerateArray().Select(ArrayOf).ToArray();
        var at = 0;
        foreach (var a in _arrays)
        {
            at = a.Rechannel.Load(w, at);
            foreach (var l in a.Layers)
            {
                at = l.Mix.Load(w, l.Conv.Load(w, at));
                if (l.L1x1 is { } p) at = p.Load(w, at);
                if (l.H1x1 is { } q) at = q.Load(w, at);
            }
            at = a.Head.Load(w, at);
        }
        if (at != w.Length - 1) throw new FormatException($"weights: {w.Length}, expected {at + 1}");
        _scale = w[at];
        var wide = _arrays.Max(a => Math.Max(a.Channels, Math.Max(a.HeadIn, a.Head.Out)));
        var z = _arrays.Max(a => a.Layers.Max(l => l.Conv.Out));
        (_x, _in, _z, _a, _head, _headOut) = (new float[wide * M], new float[wide * M], new float[z * M], new float[z * M], new float[wide * M], new float[wide * M]);
    }

    private static Array_ ArrayOf(JsonElement c)
    {
        int I(string k, int def = 0) => c.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : def;
        JsonElement? O(string k) => c.TryGetProperty(k, out var v) && v.ValueKind != JsonValueKind.Null ? v : null;
        bool On(JsonElement? o, bool def) => o is { } e && e.TryGetProperty("active", out var a) ? a.GetBoolean() : def;
        foreach (var p in c.EnumerateObject())
            if ((p.Name.EndsWith("_film") && On(p.Value, false)) || (p.Name.StartsWith("groups") && p.Value.ValueKind == JsonValueKind.Number && p.Value.GetInt32() != 1)
                || (p.Name == "slimmable" && p.Value.ValueKind != JsonValueKind.Null))
                throw new FormatException($"{p.Name} not supported");
        var ch = I("channels");
        var b = I("bottleneck", ch);
        var dil = c.GetProperty("dilations").EnumerateArray().Select(d => d.GetInt32()).ToArray();
        var ks = O("kernel_sizes") is { } kz ? kz.EnumerateArray().Select(k => k.GetInt32()).ToArray() : dil.Select(_ => I("kernel_size")).ToArray();
        NamAct ActAt(string k, int i, NamAct def) => O(k) is not { } e ? def : NamAct.Parse(e.ValueKind == JsonValueKind.Array ? e[i] : e);
        Gate GateAt(int i)
        {
            if (O("gating_mode") is not { } g) return O("gated") is { ValueKind: JsonValueKind.True } ? Gate.Gated : Gate.None;
            var s = (g.ValueKind == JsonValueKind.Array ? g[i] : g).GetString();
            return s switch { "gated" => Gate.Gated, "blended" => Gate.Blended, "none" => Gate.None, _ => throw new FormatException($"gating_mode {s}") };
        }
        var h1 = O("head1x1");
        var h1On = On(h1, false);
        var headIn = h1On ? h1!.Value.GetProperty("out_channels").GetInt32() : b;
        var l1On = On(O("layer1x1"), true);
        if (!l1On && b != ch) throw new FormatException("bottleneck needs layer1x1");
        var cond = I("condition_size", 1);
        var layers = dil.Select((d, i) =>
        {
            var mode = GateAt(i);
            var g = mode == Gate.None ? 1 : 2;
            return new Layer(new NamConv(ch, g * b, ks[i], d, true), new NamConv(cond, g * b), l1On ? new NamConv(b, ch, bias: true) : null,
                h1On ? new NamConv(b, headIn, bias: true) : null, ActAt("activation", i, new(Act.Tanh)),
                ActAt("secondary_activation", i, new(Act.Sigmoid)), mode, b);
        }).ToArray();
        var head = O("head") is { } h
            ? new NamConv(headIn, h.GetProperty("out_channels").GetInt32(), h.GetProperty("kernel_size").GetInt32(),
                h.TryGetProperty("head_dilation", out var hd) ? hd.GetInt32() : 1, h.GetProperty("bias").GetBoolean())
            : new NamConv(headIn, I("head_size"), bias: O("head_bias") is { ValueKind: JsonValueKind.True });
        return new Array_(new NamConv(I("input_size", 1), ch), layers, head, headIn, ch);
    }

    public void Reset()
    {
        foreach (var a in _arrays)
        {
            a.Rechannel.Reset(); a.Head.Reset();
            foreach (var l in a.Layers) { l.Conv.Reset(); l.Mix.Reset(); l.L1x1?.Reset(); l.H1x1?.Reset(); }
        }
    }

    public void Process(Span<float> io)
    {
        for (var at = 0; at < io.Length; at += M) Block(io.Slice(at, Math.Min(M, io.Length - at)));
    }

    // Rows of stride M: x = residual, head = skip sum, z = conv + mixin, a = activated.
    private void Block(Span<float> io)
    {
        var n = io.Length;
        io.CopyTo(_cond);
        for (var ai = 0; ai < _arrays.Length; ai++)
        {
            var arr = _arrays[ai];
            if (ai > 0) Array.Copy(_x, _in, arr.Rechannel.In * M);
            arr.Rechannel.Run(ai == 0 ? _cond : _in, _x, n);
            if (ai == 0) Array.Clear(_head, 0, arr.HeadIn * M); else Array.Copy(_headOut, _head, arr.HeadIn * M);
            foreach (var l in arr.Layers) Step(l, n);
            arr.Head.Run(_head, _headOut, n);
        }
        for (var i = 0; i < n; i++) io[i] = _scale * _headOut[i];
    }

    private void Step(Layer l, int n)
    {
        l.Conv.Run(_x, _z, n);
        l.Mix.Run(_cond, _z, n, add: true);
        for (var i = 0; i < l.B; i++)
        {
            var a = _a.AsSpan(i * M, n);
            var top = _z.AsSpan(i * M, n);
            top.CopyTo(a);
            l.Act.Apply(a);
            if (l.Mode == Gate.None) continue;
            var s = _z.AsSpan((l.B + i) * M, n);
            l.Sec.Apply(s);
            if (l.Mode == Gate.Gated) for (var t = 0; t < n; t++) a[t] *= s[t];
            else for (var t = 0; t < n; t++) a[t] = top[t] + s[t] * (a[t] - top[t]);
        }
        if (l.H1x1 is { } h1) h1.Run(_a, _head, n, add: true);
        else for (var i = 0; i < l.B; i++) NamConv.Axpy(_head.AsSpan(i * M, n), 1, _a.AsSpan(i * M, n));
        l.L1x1?.Run(_a, _x, n, add: true);
    }
}
