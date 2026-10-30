using System.IO;
using System.Text.Json;
using TabForge.Models;

namespace TabForge.Services;

// Owns: the streaming shape check of a project's JSON (depth, array sizes, text lengths, the format-version and root probes) before it is deserialized.
// Does not own: the value checks and repairs of the loaded song (ProjectValidator.cs).
// Tests: no dedicated test class.
public static partial class ProjectValidator
{
    private sealed class JsonFrame(bool isArray, string? name, int depth)
    {
        public bool IsArray { get; } = isArray;
        public string? Name { get; } = name;
        public int Depth { get; } = depth;
        public string? PropertyName { get; set; }
        public int ItemCount { get; set; }
        public int PropertyCount { get; set; }
        public int MeasureNotes { get; set; }
        public int TrackMeasures { get; set; }
    }

    /// <summary>Rejects oversized known collections before the JSON deserializer materializes them.</summary>
    public static void ValidateJsonShape(ReadOnlySpan<byte> json)
    {
        // Invalid UTF-8 surfaces as System.Text.DecoderFallbackException (directly or as InvalidOperationException's
        // inner exception); a malformed file must always be an InvalidDataException.
        try { ValidateJsonShapeCore(json); }
        catch (System.Text.DecoderFallbackException ex)
        {
            throw new InvalidDataException("The project contains invalid UTF-8 text.", ex);
        }
        catch (InvalidOperationException ex) when (ex.InnerException is System.Text.DecoderFallbackException)
        {
            throw new InvalidDataException("The project contains invalid UTF-8 text.", ex);
        }
    }

    /// <summary>
    /// Fused shape + root-property check (additive): validates like <see cref="ValidateJsonShape(ReadOnlySpan{byte})"/>
    /// in a single <see cref="Utf8JsonReader"/> pass and reports whether the root object names
    /// <c>FormatVersion</c>. Replaces the Load path's two full scans (shape + <c>HasRootProperty</c>).
    /// </summary>
    public static void ValidateJsonShape(ReadOnlySpan<byte> json, out bool hasFormatVersion) =>
        ValidateJsonShape(json, nameof(SongProject.FormatVersion), out hasFormatVersion);

    /// <summary>
    /// Fused shape + root-property check for any root name (additive; existing callers unchanged).
    /// </summary>
    public static void ValidateJsonShape(ReadOnlySpan<byte> json, string rootProperty, out bool hasRoot)
    {
        try { ValidateJsonShapeCore(json, rootProperty, out hasRoot); }
        catch (System.Text.DecoderFallbackException ex)
        {
            throw new InvalidDataException("The project contains invalid UTF-8 text.", ex);
        }
        catch (InvalidOperationException ex) when (ex.InnerException is System.Text.DecoderFallbackException)
        {
            throw new InvalidDataException("The project contains invalid UTF-8 text.", ex);
        }
    }

    private static void ValidateJsonShapeCore(ReadOnlySpan<byte> json) =>
        ValidateJsonShapeCore(json, rootProperty: null, out _);

    private static void ValidateJsonShapeCore(ReadOnlySpan<byte> json, string? rootProperty, out bool hasRoot)
    {
        var reader = new Utf8JsonReader(json, new JsonReaderOptions { MaxDepth = InputLimits.MaxJsonDepth });
        var frames = new List<JsonFrame>();
        long totalMeasures = 0;
        long totalCells = 0;
        long totalNotes = 0;
        long totalCurves = 0;
        long totalTechniques = 0;
        long totalStrings = 0;
        long totalPlugins = 0;
        long totalBindings = 0;
        var totalTracks = 0;
        var totalMarkers = 0;
        hasRoot = false;

        while (reader.Read())
        {
            var parent = frames.Count == 0 ? null : frames[^1];
            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                ReadPropertyName(ref reader, frames, parent, rootProperty, ref hasRoot, ref totalBindings);
                continue;
            }

            if (parent is { IsArray: true } && reader.TokenType is not (JsonTokenType.EndArray or JsonTokenType.EndObject) &&
                reader.CurrentDepth == parent.Depth + 1)
            {
                CountArrayItem(parent, frames, ref totalTracks, ref totalMeasures, ref totalCells,
                    ref totalNotes, ref totalCurves, ref totalTechniques, ref totalStrings, ref totalPlugins, ref totalMarkers);
            }

            if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
            {
                string? name = parent switch
                {
                    { IsArray: false } => parent.PropertyName,
                    { IsArray: true } => parent.Name,
                    _ => null
                };
                if (parent is { IsArray: false }) parent.PropertyName = null;
                frames.Add(new JsonFrame(reader.TokenType == JsonTokenType.StartArray, name, reader.CurrentDepth));
                continue;
            }

            if (reader.TokenType is JsonTokenType.EndObject or JsonTokenType.EndArray)
            {
                if (frames.Count > 0) frames.RemoveAt(frames.Count - 1);
                continue;
            }

            if (reader.TokenType == JsonTokenType.String) CheckStringValue(ref reader, parent);
            else if (parent is { IsArray: false })
            {
                parent.PropertyName = null;
            }
        }
    }

    /// <summary>A property name: its length, the root property probe and the articulation binding counts.</summary>
    private static void ReadPropertyName(ref Utf8JsonReader reader, List<JsonFrame> frames, JsonFrame? parent, string? rootProperty, ref bool hasRoot, ref long totalBindings)
    {
        if (reader.ValueSpan.Length > 256)
            throw Invalid("The project contains an overlong JSON property name.");
        // Fused root check: a direct child name of the root object (single pass, no second scan).
        // Root properties are few; GetString here keeps the case-insensitive match of HasRootProperty.
        if (parent is { IsArray: false })
        {
            var propertyName = reader.GetString();
            if (rootProperty is not null && !hasRoot && frames.Count == 1 &&
                string.Equals(propertyName, rootProperty, StringComparison.OrdinalIgnoreCase))
                hasRoot = true;
            parent.PropertyName = propertyName;
            if (string.Equals(parent.Name, "ArticulationBindings", StringComparison.OrdinalIgnoreCase) &&
                ++parent.PropertyCount > InputLimits.MaxHotkeyBindings)
                throw Invalid("A project plug-in contains too many articulation bindings.");
            if (string.Equals(parent.Name, "ArticulationBindings", StringComparison.OrdinalIgnoreCase) &&
                ++totalBindings > InputLimits.MaxTotalArticulationBindings)
                throw Invalid("The project contains too many articulation bindings overall.");
        }
    }

    /// <summary>A string value: no text may be longer than the limit of its property (an array of techniques has its own).</summary>
    private static void CheckStringValue(ref Utf8JsonReader reader, JsonFrame? parent)
    {
        if (parent is { IsArray: false, PropertyName: not null })
        {
            var maximumCharacters = MaximumTextLength(parent.PropertyName);
            if (maximumCharacters is { } limit && reader.ValueSpan.Length > limit * 6L + 8)
                throw Invalid("The project contains an overlong text value.");
            parent.PropertyName = null;
        }
        else if (parent is { IsArray: true } && string.Equals(parent.Name, "Techniques", StringComparison.OrdinalIgnoreCase) &&
                 reader.ValueSpan.Length > 128 * 6 + 8)
        {
            throw Invalid("The project contains an overlong note technique.");
        }
    }

    /// <summary>The item limit of a "notes" array: a MIDI clip's notes (frames[^2] is the clip, an item of an AudioClips array) or a beat's notes.</summary>
    private static int NotesLimitFor(List<JsonFrame> frames) =>
        frames.Count >= 2 && frames[^2] is { IsArray: false, Name: var owner } && string.Equals(owner, "AudioClips", StringComparison.OrdinalIgnoreCase)
            ? InputLimits.MaxNotesPerMidiClip : InputLimits.MaxNotesPerCell;

    private static void CountArrayItem(JsonFrame array, List<JsonFrame> frames,
        ref int totalTracks, ref long totalMeasures, ref long totalCells, ref long totalNotes,
        ref long totalCurves, ref long totalTechniques, ref long totalStrings, ref long totalPlugins, ref int totalMarkers)
    {
        array.ItemCount++;
        var name = array.Name ?? "";
        var perArrayLimit = name.ToLowerInvariant() switch
        {
            "tracks" => InputLimits.MaxTracks,
            "measures" => InputLimits.MaxMeasuresPerTrack,
            "cells" or "voice2cells" => InputLimits.MaxCellsPerMeasure,
            "notes" => NotesLimitFor(frames),
            "markers" => InputLimits.MaxMarkers,
            "stringtunings" => InputLimits.MaxStringsPerTrack,
            "plugins" => InputLimits.MaxPluginsPerTrack,
            "bendpoints" or "whammypoints" => InputLimits.MaxCurvePoints,
            "techniques" => InputLimits.MaxTechniquesPerNote,
            _ => int.MaxValue
        };
        if (array.ItemCount > perArrayLimit)
            throw Invalid($"The project contains too many {name} entries in one collection.");

        switch (name.ToLowerInvariant())
        {
            case "tracks":
                if (++totalTracks > InputLimits.MaxTracks) throw Invalid("The project contains too many tracks.");
                break;
            case "measures":
                if (++totalMeasures > InputLimits.MaxTotalMeasures) throw Invalid("The project contains too many measures overall.");
                for (var i = frames.Count - 2; i >= 0; i--)
                {
                    var ancestor = frames[i];
                    if (!ancestor.IsArray && string.Equals(ancestor.Name, "Tracks", StringComparison.OrdinalIgnoreCase))
                    {
                        if (++ancestor.TrackMeasures > InputLimits.MaxMeasuresPerTrack)
                            throw Invalid("A track contains too many measures.");
                        break;
                    }
                }
                break;
            case "cells":
            case "voice2cells":
                if (++totalCells > InputLimits.MaxTotalCells) throw Invalid("The project contains too many beats overall.");
                break;
            case "notes":
                if (++totalNotes > InputLimits.MaxTotalNotes) throw Invalid("The project contains too many notes overall.");
                for (var i = frames.Count - 2; i >= 0; i--)
                {
                    var ancestor = frames[i];
                    if (!ancestor.IsArray && string.Equals(ancestor.Name, "Measures", StringComparison.OrdinalIgnoreCase))
                    {
                        if (++ancestor.MeasureNotes > InputLimits.MaxNotesPerMeasure)
                            throw Invalid("A measure contains too many notes.");
                        break;
                    }
                }
                break;
            case "markers":
                if (++totalMarkers > InputLimits.MaxMarkers) throw Invalid("The project contains too many markers or sections.");
                break;
            case "stringtunings":
                if (++totalStrings > InputLimits.MaxTotalStringTuningValues)
                    throw Invalid("The project contains too many string-tuning values overall.");
                break;
            case "plugins":
                if (++totalPlugins > InputLimits.MaxTotalPluginSlots) throw Invalid("The project contains too many plug-in entries overall.");
                break;
            case "bendpoints":
            case "whammypoints":
                if (++totalCurves > InputLimits.MaxTotalCurvePoints) throw Invalid("The project contains too many bend points overall.");
                break;
            case "techniques":
                if (++totalTechniques > InputLimits.MaxTotalTechniques) throw Invalid("The project contains too many note techniques overall.");
                break;
        }
    }

    private static int? MaximumTextLength(string propertyName) => propertyName.ToLowerInvariant() switch
    {
        "title" or "subtitle" or "artist" or "album" or "musicauthor" or "lyricsauthor" or "copyright" or
            "tabauthor" or "name" or "instrumentname" or "sectionname" or "chordname" or "articulationmap" => InputLimits.MaxTitleLength,
        "instructions" or "text" or "directions" => InputLimits.MaxUserTextLength,
        "notice" => InputLimits.MaxNoticeLength,
        "lyrics" => InputLimits.MaxLyricsLength,
        "importedfrom" or "path" => InputLimits.MaxPathLength,
        "devicename" => TabForge.Plugins.ActiveOnRule.MaxDeviceNameChars,
        "colorhex" => 16,
        "bendtypename" or "bendstylename" or "clef" or "tripletfeelkind" or "beammode" or "stemdirection" => 128,
        _ => null
    };
}
