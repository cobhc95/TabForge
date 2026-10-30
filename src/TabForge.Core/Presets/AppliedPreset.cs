using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TabForge.Presets;

// Owns: the preset an object was last given (its name, kind and the values as applied) and the comparison that says whether the object was
//   edited since. One record for every selector; it is stored as an optional property beside the values, so old files load unchanged.
// Does not own: the preset stores or the files, any window, or how a selector draws its rows (the Avalonia PresetCombo does that).
// Tests: AppliedPresetTests, PresetMemoryTests.
/// <summary>The preset last applied to an object, with a canonical snapshot of the values it gave it.</summary>
public sealed record AppliedPreset(string Name, string Kind, string Snapshot)
{
    public const int MaxNameChars = 200, MaxKindChars = 80, MaxSnapshotChars = 256 * 1024;

    /// <summary>The record for a pick, or null when there is no usable name (the snapshot is made canonical once, on the way in).</summary>
    public static AppliedPreset? Create(string? name, string kind, string? snapshot)
    {
        var clean = (name ?? "").Trim();
        if (clean.Length is 0 or > MaxNameChars || kind.Length > MaxKindChars) return null;
        var text = Canonical(snapshot);
        if (text.Length > MaxSnapshotChars) return null;
        return new AppliedPreset(clean, kind, text);
    }

    /// <summary>True when the object's current values differ from the ones the preset gave it (both are made canonical first).</summary>
    public bool IsEdited(string? current) => !string.Equals(Snapshot, Canonical(current), StringComparison.Ordinal);

    /// <summary>The same values in one stable form: JSON object keys sorted, numbers written once, text trimmed. Non-JSON stays as it is (trimmed).</summary>
    public static string Canonical(string? text)
    {
        var raw = (text ?? "").Trim();
        if (raw.Length == 0 || (raw[0] is not ('{' or '['))) return raw;   // a base64 state or plain name: no parse, no exception
        try
        {
            if (JsonNode.Parse(raw) is not { } node) return raw;
            var builder = new StringBuilder(raw.Length + 16);
            Write(node, builder);
            return builder.ToString();
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
        {
            return raw;
        }
    }

    private static void Write(JsonNode? node, StringBuilder builder)
    {
        switch (node)
        {
            case JsonObject obj:
                builder.Append('{');
                var first = true;
                foreach (var pair in obj.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    if (!first) builder.Append(',');
                    first = false;
                    builder.Append(JsonSerializer.Serialize(pair.Key)).Append(':');
                    Write(pair.Value, builder);
                }
                builder.Append('}');
                break;
            case JsonArray array:
                builder.Append('[');
                for (var i = 0; i < array.Count; i++)
                {
                    if (i > 0) builder.Append(',');
                    Write(array[i], builder);
                }
                builder.Append(']');
                break;
            case JsonValue value:
                var raw = value.ToJsonString();
                if (decimal.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number))
                    builder.Append(number == decimal.Truncate(number)
                        ? ((long)number).ToString(System.Globalization.CultureInfo.InvariantCulture)
                        : number.ToString("G29", System.Globalization.CultureInfo.InvariantCulture));
                else builder.Append(raw);
                break;
            default:
                builder.Append("null");
                break;
        }
    }
}
