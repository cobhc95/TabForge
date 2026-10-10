using System.Text.Json;
using System.Text.Json.Nodes;

namespace TabForge.Plugins;

public enum MidiParamKind { Number, Choice, Check, Text, MapPreset, NoteSet }

/// <summary>One parameter of a MIDI processor: the window builds its control from this. Numbers with a <see cref="Scale"/> above 1 are shown in steps of 1/Scale.</summary>
public sealed record MidiParamInfo(string Key, string Label, MidiParamKind Kind, double Min = 0, double Max = 1, double Default = 0, string Tip = "", int Scale = 1, string Unit = "",
    IReadOnlyList<(int Value, string Label)>? Choices = null, string DefaultText = "", string Group = "Main")
{
    public string Format(double v) => Kind == MidiParamKind.Choice ? Choices?.FirstOrDefault(c => c.Value == (int)Math.Round(v)).Label ?? v.ToString("0") : (Scale > 1 ? v.ToString("0.0#") : v.ToString("0")) + Unit;
}

/// <summary>A processor type of the catalog. Parameters travel to the engine as a flat JSON object keyed by <see cref="MidiParamInfo.Key"/>.</summary>
public sealed record MidiProcessorInfo(string Type, string Name, string Category, string Description, string Keywords, IReadOnlyList<MidiParamInfo> Params)
{
    /// <summary>Precomputed lowercase text the search box filters on (name, description, keywords).</summary>
    public string SearchText { get; } = $"{Name} {Category} {Description} {Keywords}".ToLowerInvariant();
}

/// <summary>
/// The MIDI processors TabForge offers in front of a plug-in (functional spec: docs/MIDI_PROCESSORS_SPEC.md). Each entry describes its
/// parameters; the engine (TabForge.AudioEngine/Midi) implements the same type names. Generative and audio-coupled processors follow later.
/// </summary>
public static partial class MidiProcessorCatalog
{
    private static readonly IReadOnlyList<(int, string)> AnyChannel = new[] { (0, "Any") }.Concat(Enumerable.Range(1, 16).Select(i => (i, i.ToString()))).ToArray();
    private static readonly IReadOnlyList<(int, string)> Channel16 = Enumerable.Range(1, 16).Select(i => (i, i.ToString())).ToArray();

    private static MidiParamInfo InCh(string tip = "Only this channel is processed; the others pass through untouched") =>
        new("InCh", "Input channel", MidiParamKind.Choice, 0, 16, 0, tip, Choices: AnyChannel);
    private static MidiParamInfo Num(string key, string label, double min, double max, double def, string tip = "", int scale = 1, string unit = "") =>
        new(key, label, MidiParamKind.Number, min, max, def, tip, scale, unit);
    private static MidiParamInfo Check(string key, string label, bool def, string tip = "") => new(key, label, MidiParamKind.Check, 0, 1, def ? 1 : 0, tip);
    private static MidiParamInfo Choice(string key, string label, double def, string tip, params (int, string)[] choices) =>
        new(key, label, MidiParamKind.Choice, choices.Min(c => c.Item1), choices.Max(c => c.Item1), def, tip, Choices: choices);

    private static MidiParamInfo RootParam() =>
        Choice("Root", "Key", 0, "Root note of the key", (0, "C"), (1, "C#"), (2, "D"), (3, "D#"), (4, "E"), (5, "F"), (6, "F#"), (7, "G"), (8, "G#"), (9, "A"), (10, "A#"), (11, "B"));
    private static MidiParamInfo ScaleParam() =>
        Choice("Scale", "Scale", 1, "", (0, "Chromatic"), (1, "Major"), (2, "Natural minor"), (3, "Harmonic minor"), (4, "Melodic minor"), (5, "Dorian"), (6, "Phrygian"), (7, "Lydian"),
            (8, "Mixolydian"), (9, "Pentatonic major"), (10, "Pentatonic minor"), (11, "Whole tone"));

    private static readonly IReadOnlyList<(int, string)> ChannelRows = new[] { (0, "Off") }.Concat(Enumerable.Range(1, 16).Select(i => (i, i.ToString()))).ToArray();

    private static readonly MidiProcessorInfo[] Raw = BuildRaw();

    /// <summary>Types that act on notes and therefore get the common "Applies to" note selector.</summary>
    public static readonly IReadOnlySet<string> NoteScoped = new HashSet<string> { "channelRoute", "noteRange", "transpose", "noteMap", "velocity", "humanize", "delay",
        "scaleSnap", "chord", "sanitizer", "noteHold", "repeater", "arp", "noteRandom", "modalRandom", "scaleVariation", "velVariation" };

    private static readonly Dictionary<string, string[]> AdvancedKeys = new()
    {
        ["velocity"] = new[] { "Makeup", "Min", "Max", "Random", "Mix", "InLow", "InHigh", "Low", "High" },
        ["transpose"] = new[] { "Premultiply", "Low", "High" },
        ["humanize"] = new[] { "BaseDelayMs", "Baseline" },
        ["delay"] = new[] { "Samples", "NotesOnly" },
        ["channelMap"] = Enumerable.Range(1, 16).Select(i => $"Ch{i}").ToArray(),
        ["channelRoute"] = new[] { "Low", "High", "Note" },
        ["noteMap"] = new[] { "Overrides" },
    };

    public static readonly IReadOnlyList<MidiProcessorInfo> All = Raw.Select(Scope).ToArray();

    private static MidiProcessorInfo Scope(MidiProcessorInfo info)
    {
        var adv = AdvancedKeys.GetValueOrDefault(info.Type) ?? Array.Empty<string>();
        var list = info.Params.Select(p => adv.Contains(p.Key) ? p with { Group = "Advanced" } : p).ToList();
        if (NoteScoped.Contains(info.Type))
        {
            list.Add(new MidiParamInfo("NoteMode", "Applies to", MidiParamKind.Choice, 0, 2, 0, "Which notes this processor touches; the rest pass through unchanged (a note-off always follows its note-on)",
                Choices: new[] { (0, "All notes"), (1, "Only these notes"), (2, "All except these notes") }, Group: "Applies to"));
            list.Add(new MidiParamInfo("NoteSet", "Notes", MidiParamKind.NoteSet, Tip: "Note numbers or names: 36, 38, 40-45, C2, D#3. Learn adds the next note you play", Group: "Applies to"));
        }
        return info with { Params = list };
    }

    public sealed record SearchHit(MidiProcessorInfo Info, MidiParamInfo? Param, string Display);

    private static readonly List<(SearchHit Hit, string Text)> Index = BuildIndex();

    private static List<(SearchHit, string)> BuildIndex()
    {
        var index = new List<(SearchHit, string)>();
        foreach (var info in All)
        {
            index.Add((new SearchHit(info, null, info.Name), info.SearchText));
            foreach (var p in info.Params)
            {
                var choices = p.Choices is null ? "" : string.Join(' ', p.Choices.Select(c => c.Label));
                index.Add((new SearchHit(info, p, $"{info.Name} › {p.Label}"), $"{info.Name} {p.Label} {p.Tip} {choices} {p.Key}".ToLowerInvariant()));
            }
        }
        return index;
    }

    /// <summary>Processor matches first, then "Processor › Parameter" matches; every word of the query must appear (all processors when empty).</summary>
    public static IReadOnlyList<SearchHit> SearchAll(string query)
    {
        var words = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return All.Select(i => new SearchHit(i, null, i.Name)).ToList();
        var hits = new List<SearchHit>();
        foreach (var (hit, text) in Index)
            if (hit.Param is null && words.All(w => text.Contains(w, StringComparison.Ordinal))) hits.Add(hit);
        foreach (var (hit, text) in Index)
            if (hit.Param is not null && words.All(w => text.Contains(w, StringComparison.Ordinal))) hits.Add(hit);
        return hits;
    }

    /// <summary>Built-in whole-list presets (name, processors).</summary>
    public static IReadOnlyList<(string Name, Func<List<PluginMidiProcessor>> Build)> BuiltInPresets { get; } = new (string, Func<List<PluginMidiProcessor>>)[]
    {
        ("Drums: GM → kit map", () => new() { Make("noteMap", o => { o["From"] = "General MIDI"; o["To"] = "Acoustic kit map (unverified)"; }) }),
        ("Humanize light", () => new() { Make("humanize", o => { o["TimingMs"] = 4; o["VelRandom"] = 5; o["BaseDelayMs"] = 10; }) }),
        ("Drums: GM → kit map + humanize", () => new() { Make("noteMap", o => { o["From"] = "General MIDI"; o["To"] = "Acoustic kit map (unverified)"; }), Make("humanize", o => { o["TimingMs"] = 4; o["VelRandom"] = 6; o["BaseDelayMs"] = 10; }) }),
        ("Kick and snare: softer", () => new() { Make("velocity", o => { o["Mul"] = 0.8; o["NoteMode"] = 1; o["NoteSet"] = "36,38"; }) }),
    };

    private static PluginMidiProcessor Make(string type, Action<JsonObject> set)
    {
        var p = Create(Find(type)!);
        var o = JsonNode.Parse(p.Params)!.AsObject();
        set(o);
        p.Params = o.ToJsonString();
        return p;
    }

    private static readonly Dictionary<string, MidiProcessorInfo> ByType = All.ToDictionary(p => p.Type, StringComparer.Ordinal);

    public static MidiProcessorInfo? Find(string type) => ByType.GetValueOrDefault(type);

    /// <summary>Catalog entries matching every word of <paramref name="query"/> in name, description or keywords (all when empty).</summary>
    public static IEnumerable<MidiProcessorInfo> Search(string query)
    {
        var words = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length == 0 ? All : All.Where(p => words.All(w => p.SearchText.Contains(w, StringComparison.Ordinal)));
    }

    /// <summary>A new list entry with the catalog's default parameters.</summary>
    public static PluginMidiProcessor Create(MidiProcessorInfo info) => new() { Type = info.Type, Enabled = true, Params = DefaultParams(info).ToJsonString() };

    public static JsonObject DefaultParams(MidiProcessorInfo info)
    {
        var o = new JsonObject();
        foreach (var p in info.Params)
        {
            if (p.Kind is MidiParamKind.MapPreset or MidiParamKind.Text or MidiParamKind.NoteSet) o[p.Key] = p.DefaultText;
            else o[p.Key] = p.Default;
        }
        return o;
    }

    /// <summary>The processor's parameters as an editable object (never null; a broken or missing value falls back to the default).</summary>
    public static JsonObject ParseParams(PluginMidiProcessor processor, MidiProcessorInfo info)
    {
        JsonObject? o = null;
        try { if (processor.Params.Length is > 0 and <= PluginMidiProcessor.MaxParamsChars) o = JsonNode.Parse(processor.Params) as JsonObject; }
        catch (JsonException ex) { Services.Trace.Error(Services.Trace.Engine, "MIDI processor: parse parameters: " + ex.Message); }
        o ??= new JsonObject();
        foreach (var p in info.Params)
        {
            var ok = o.TryGetPropertyValue(p.Key, out var v) && v is JsonValue jv && (p.Kind is MidiParamKind.MapPreset or MidiParamKind.Text or MidiParamKind.NoteSet ? jv.TryGetValue<string>(out _) : jv.TryGetValue<double>(out _));
            if (!ok) o[p.Key] = p.Kind is MidiParamKind.MapPreset or MidiParamKind.Text or MidiParamKind.NoteSet ? p.DefaultText : p.Default;
        }
        return o;
    }

    /// <summary>The JSON the engine gets: the stored parameters, with drum-map presets resolved into a note table (the engine reads no files).</summary>
    public static string EngineParams(PluginMidiProcessor processor)
    {
        if (processor.Type != "noteMap") return processor.Params.Length is > 0 and <= PluginMidiProcessor.MaxParamsChars ? processor.Params : "{}";
        var info = Find("noteMap")!;
        var o = ParseParams(processor, info);
        var table = DrumMapLibrary.BuildTable(o["From"]!.GetValue<string>(), o["To"]!.GetValue<string>(), o["Overrides"]!.GetValue<string>());
        return new JsonObject { ["InCh"] = o["InCh"]!.GetValue<double>(), ["NoteMode"] = o["NoteMode"]!.GetValue<double>(), ["NoteSet"] = o["NoteSet"]!.GetValue<string>(), ["Map"] = new JsonArray(table.Select(n => (JsonNode?)JsonValue.Create(n)).ToArray()) }.ToJsonString();
    }
}
