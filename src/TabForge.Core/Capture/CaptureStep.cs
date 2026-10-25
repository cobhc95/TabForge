using System.Text.Json;

namespace TabForge.Capture;

// Owns: one step of a capture script: its verb, the value the verb carries and its named options, with typed reads that fail with the step named.
// Does not own: running steps (the capture host), the list of verbs that exist (the host's handler table).
// Tests: TabForge.Avalonia.Tests.CaptureScriptTests.

/// <summary>A script step such as <c>{"bar": 12}</c> or <c>{"shot": "x", "target": "score"}</c>: the first property names the verb and carries its value, the others are options.</summary>
public sealed class CaptureStep
{
    private readonly Dictionary<string, JsonElement> _options;

    public CaptureStep(string verb, JsonElement value, Dictionary<string, JsonElement> options)
    {
        Verb = verb;
        Value = value;
        _options = options;
    }

    public string Verb { get; }

    /// <summary>What the first property holds (true, a name, a number, an array ...).</summary>
    public JsonElement Value { get; }

    /// <summary>The verb's value as text; fails when it is not a string.</summary>
    public string Text => Value.ValueKind == JsonValueKind.String ? Value.GetString()! : throw Bad("a text");

    /// <summary>The verb's value as a whole number.</summary>
    public int Int => Value.ValueKind == JsonValueKind.Number && Value.TryGetInt32(out var n) ? n : throw Bad("a whole number");

    /// <summary>The verb's value as true or false.</summary>
    public bool Flag => Value.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => throw Bad("true or false") };

    /// <summary>The verb's value as an array of numbers.</summary>
    public double[] Numbers => Value.ValueKind == JsonValueKind.Array ? Value.EnumerateArray().Select(Number).ToArray() : throw Bad("a list of numbers");

    public bool Has(string option) => _options.ContainsKey(option);

    /// <summary>An optional text option; <paramref name="fallback"/> when absent.</summary>
    public string? Option(string name, string? fallback = null) => _options.TryGetValue(name, out var v) ? (v.ValueKind == JsonValueKind.String ? v.GetString() : throw Bad($"option {name} as a text")) : fallback;

    /// <summary>An optional whole-number option.</summary>
    public int Option(string name, int fallback) => _options.TryGetValue(name, out var v) ? (v.TryGetInt32(out var n) ? n : throw Bad($"option {name} as a whole number")) : fallback;

    /// <summary>An optional number option.</summary>
    public double Option(string name, double fallback) => _options.TryGetValue(name, out var v) ? Number(v) : fallback;

    /// <summary>An optional list-of-numbers option; null when absent.</summary>
    public double[]? NumbersOption(string name) => _options.TryGetValue(name, out var v) ? v.EnumerateArray().Select(Number).ToArray() : null;

    /// <summary>An optional option that is a list of step lists (for example the groups of a frame sequence); null when absent.</summary>
    public IReadOnlyList<IReadOnlyList<CaptureStep>>? Groups(string name) =>
        _options.TryGetValue(name, out var v) ? v.EnumerateArray().Select(CaptureScript.ParseSteps).ToList() : null;

    public override string ToString() => $"{{\"{Verb}\": {Value.GetRawText()}}}";

    private double Number(JsonElement element) => element.ValueKind == JsonValueKind.Number ? element.GetDouble() : throw Bad("a number");

    private FormatException Bad(string wanted) => new($"Step {this}: expected {wanted}.");
}
