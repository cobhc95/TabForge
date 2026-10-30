using System.Text;
using System.Text.Json;

namespace TabForge.Audio.Contracts.Builtin;

// Owns: the saved-state bytes of every built-in effect: UTF-8 JSON {"v":1,"params":{key:number},"text":{key:string}}, so a song, a preset and the
//   engine's state reply all carry the same thing and a newer effect version can read an older state (unknown keys ignored, missing keys kept).
// Does not own: which keys an effect has (its BuiltinParameter list and text keys), applying them (the engine's BuiltinEffect.SetState).
// Tests: BuiltinEffectTests.

/// <summary>The parsed state of a built-in: parameter values and text values (file paths) by key. A state may hold only some keys (a partial update).</summary>
public sealed class BuiltinState
{
    public const int Version = 1;
    public const int MaxTextChars = 4096;

    public Dictionary<string, double> Values { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Text { get; } = new(StringComparer.Ordinal);

    /// <summary>A state with one text value, for the editor's "set this file" message.</summary>
    public static byte[] OneText(string key, string value)
    {
        var state = new BuiltinState();
        state.Text[key] = value;
        return state.ToBytes();
    }

    public byte[] ToBytes()
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteNumber("v", Version);
            w.WriteStartObject("params");
            foreach (var (key, value) in Values) w.WriteNumber(key, value);
            w.WriteEndObject();
            w.WriteStartObject("text");
            foreach (var (key, value) in Text) w.WriteString(key, value);
            w.WriteEndObject();
            w.WriteEndObject();
        }
        return stream.ToArray();
    }

    /// <summary>Parses state bytes; throws <see cref="InvalidDataException"/> when they are not a built-in state.</summary>
    public static BuiltinState Parse(byte[] bytes)
    {
        var state = new BuiltinState();
        try
        {
            using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 4 });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("The built-in effect state is not valid.");
            if (root.TryGetProperty("params", out var values) && values.ValueKind == JsonValueKind.Object)
                foreach (var p in values.EnumerateObject())
                    if (p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetDouble(out var d) && double.IsFinite(d)) state.Values[p.Name] = d;
            if (root.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.Object)
                foreach (var p in text.EnumerateObject())
                    if (p.Value.ValueKind == JsonValueKind.String && p.Value.GetString() is { Length: <= MaxTextChars } s) state.Text[p.Name] = s;
        }
        catch (JsonException ex) { throw new InvalidDataException("The built-in effect state is not valid: " + ex.Message); }
        return state;
    }

    public override string ToString() => Encoding.UTF8.GetString(ToBytes());
}
