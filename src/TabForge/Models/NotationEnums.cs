using System.Text.Json;
using System.Text.Json.Serialization;

namespace TabForge.Models;

/// <summary>Which notation a score editor shows: tablature with the staff, tablature only, or the staff only.</summary>
public enum NotationMode { TabAndStaff, TabOnly, StaffOnly }

/// <summary>Beaming override for a beat (stored by name in .tforge files).</summary>
[JsonConverter(typeof(TolerantEnumConverter<BeamMode>))]
public enum BeamMode { Auto, Force, Break }

/// <summary>Stem direction override for a beat (stored by name in .tforge files).</summary>
[JsonConverter(typeof(TolerantEnumConverter<StemDirection>))]
public enum StemDirection { Auto, Up, Down, Invert }

/// <summary>
/// Clef names. Clefs stay text because imported files carry many spellings the engraver interprets
/// (line number and 8va/8vb suffix, e.g. "G2", "F4", "C3", "G8vb"); these are the ones TabForge writes.
/// </summary>
public static class Clefs
{
    /// <summary>the standard octave treble clef (the guitar default).</summary>
    public const string Guitar = "G8";
    public const string Treble = "G";
    public const string Bass = "F4";
    public const string Alto = "C";
    public static readonly string[] Cycle = { Guitar, Treble, Bass, Alto };
}

/// <summary>
/// Reads an enum by name (case-insensitive) or number; anything unknown becomes the default (first)
/// value instead of failing the whole file, matching how the old free-text values rendered.
/// Always writes the name, so files stay identical to the old string format.
/// </summary>
public sealed class TolerantEnumConverter<T> : JsonConverter<T> where T : struct, Enum
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String &&
            Enum.TryParse<T>(reader.GetString(), ignoreCase: true, out var named) && Enum.IsDefined(named))
            return named;
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var number) &&
            Enum.IsDefined(typeof(T), number))
            return (T)Enum.ToObject(typeof(T), number);
        if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray) reader.Skip();
        return default;
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
