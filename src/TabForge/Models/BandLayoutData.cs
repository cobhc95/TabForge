using System.Text.Json;
using System.Text.Json.Serialization;

namespace TabForge.Models;

// Owns: the Band view's saved layout for one song (which rows are shown, their order, own row heights, rows per screen), by track id.
// Does not own: the live layout (BandLayoutState) or the song's own track order.
// Tests: TestBandLayoutSaved, TestBandLayoutSafety.
/// <summary>Reads the layout; a block with wrong types reads as null (the default layout), so it can never stop a song opening.</summary>
internal sealed class TolerantBandLayoutConverter : JsonConverter<BandLayoutData?>
{
    public override BandLayoutData? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
        try { return JsonSerializer.Deserialize<BandLayoutData>(document.RootElement.GetRawText()); }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or FormatException) { return null; }
    }

    public override void Write(Utf8JsonWriter writer, BandLayoutData? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue(); else JsonSerializer.Serialize(writer, value);
    }
}

/// <summary>Saved with the song in .tforge and in the project embedded in a .gp. Null on the song until the Band layout is changed.</summary>
public sealed class BandLayoutData
{
    public List<Guid> Order { get; set; } = new();
    public List<Guid> Shown { get; set; } = new();
    public Dictionary<Guid, double> Heights { get; set; } = new();
    /// <summary>Tracks whose row hides its instrument.</summary>
    public List<Guid> HiddenInstruments { get; set; } = new();
    /// <summary>Rows with an instrument width of their own (pixels).</summary>
    public Dictionary<Guid, double> InstrumentWidths { get; set; } = new();
    /// <summary>0 = follow the Band view setting.</summary>
    public int RowsPerScreen { get; set; }
}
