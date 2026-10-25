using System.Text.Json;
using System.Text.Json.Nodes;
using TabForge.Plugins;

namespace TabForge.Avalonia.Views.NoteMaps;

/// <summary>
/// The note map processor's entry in the MIDI processing window: the Edit map... button, the drum map it opens (the processor's To
/// map, with the From map or General MIDI as the source) and the parameter text written back when the editor saves a map.
/// Owns: the button's text and tip, the choice of maps for the editor, and the parameter text that stores the saved map's name.
/// Does not own: the editor window (DrumMapEditorWindow), the map files (DrumMapLibrary), the list and the undo (MidiProcessingWindow).
/// Tests: TabForge.Avalonia.Tests.NoteMapEditEntryTests.
/// </summary>
public static class NoteMapEditEntry
{
    /// <summary>The catalog id of the note map processor.</summary>
    public const string ProcessorType = "noteMap";
    public const string EditButtonText = "Edit map…";
    public const string EditButtonTip = "See or change which note each drum of the song sends to the instrument";

    private const string FromKey = "From";
    private const string ToKey = "To";
    private const string FallbackSourceName = "General MIDI";

    /// <summary>The drum map editor for a processor's parameter text: its To map, against its From map (General MIDI when that is missing).</summary>
    public static DrumMapEditorWindow OpenEditor(string paramsJson)
    {
        var values = ParseValues(paramsJson);
        return new DrumMapEditorWindow(
            DrumMapLibrary.Find(TextOf(values, ToKey)),
            DrumMapLibrary.Find(TextOf(values, FromKey)) ?? DrumMapLibrary.Find(FallbackSourceName));
    }

    /// <summary>The parameter text with its To map set to the saved map's name; the other values stay as they were.</summary>
    public static string ParamsWithMap(string paramsJson, string mapName)
    {
        var values = ParseValues(paramsJson);
        values[ToKey] = mapName;
        return values.ToJsonString();
    }

    /// <summary>Reads the parameter text; text that is not a JSON object counts as no values, as the processor catalog reads it.</summary>
    private static JsonObject ParseValues(string paramsJson)
    {
        try { return JsonNode.Parse(paramsJson) as JsonObject ?? new JsonObject(); }
        catch (JsonException) { return new JsonObject(); }
    }

    private static string TextOf(JsonObject values, string key) =>
        values[key] is JsonValue value && value.TryGetValue(out string? text) ? text : "";
}
