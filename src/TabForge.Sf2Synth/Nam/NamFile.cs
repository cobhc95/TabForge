using System.Text.Json;

namespace TabForge.Sf2Synth.Nam;

// Owns: reading a .nam file (JSON: version, architecture, config, weights, sample_rate, metadata) into a model that builds
//   independent runtime instances; WaveNet, LSTM, Sequential (chain) and SlimmableContainer (A2: the widest submodel).
//   Unsupported features throw FormatException with a short reason. Off the audio thread.
// Does not own: inference (NamWaveNet, NamLstm), levels and rate (NamAmp).
// Tests: NamTests.
internal interface INamNet
{
    void Reset();
    void Process(Span<float> io);   // in place, one sample per frame
}

internal sealed record NamModel(string Name, string Architecture, double Rate, double? Loudness, double? InputDbu, Func<INamNet> Build);

internal static class NamFile
{
    public static NamModel Load(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        return Parse(doc.RootElement.Clone(), Path.GetFileNameWithoutExtension(path));
    }

    public static NamModel Parse(JsonElement root, string name)
    {
        var arch = root.GetProperty("architecture").GetString()!;
        var config = root.GetProperty("config");
        var rate = root.TryGetProperty("sample_rate", out var sr) && sr.ValueKind == JsonValueKind.Number ? sr.GetDouble() : 48000;
        var meta = root.TryGetProperty("metadata", out var m) && m.ValueKind == JsonValueKind.Object ? m : default;
        double? Meta(string key) => meta.ValueKind == JsonValueKind.Object && meta.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
        if (meta.ValueKind == JsonValueKind.Object && meta.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String) name = n.GetString()!;
        switch (arch)
        {
            case "SlimmableContainer":
                var widest = config.GetProperty("submodels").EnumerateArray().MaxBy(s => s.GetProperty("max_value").GetDouble());
                var inner = Parse(widest.GetProperty("model"), name);
                return inner with { Architecture = "A2 " + inner.Architecture };
            case "Sequential":
                var parts = config.GetProperty("models").EnumerateArray().Select(c => Parse(c, name)).ToArray();
                return new(name, arch, rate, parts[^1].Loudness, parts[0].InputDbu, () => new Chain(parts.Select(p => p.Build()).ToArray()));
        }
        var w = root.GetProperty("weights").EnumerateArray().Select(e => e.GetSingle()).ToArray();
        Func<INamNet> build = arch switch
        {
            "WaveNet" => () => new NamWaveNet(config, w),
            "LSTM" => () => new NamLstm(config, w),
            _ => throw new FormatException($"architecture {arch} not supported"),
        };
        build();   // validate now, off the audio thread
        return new(name, arch, rate, Meta("loudness"), Meta("input_level_dbu"), build);
    }

    private sealed class Chain(INamNet[] parts) : INamNet
    {
        public void Reset() { foreach (var p in parts) p.Reset(); }
        public void Process(Span<float> io) { foreach (var p in parts) p.Process(io); }
    }
}
