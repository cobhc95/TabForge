using System.IO;
using System.Text.Json;
using TabForge.Models;
using TabForge.Plugins;

namespace TabForge.Services;

/// <summary>Validates a deserialized score before it can be attached to a document or rendered.</summary>
public static class ProjectValidator
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
        // inner exception); a malformed file must always be an InvalidDataException (Audit 3 M-07).
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

    private static void ValidateJsonShapeCore(ReadOnlySpan<byte> json)
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

        while (reader.Read())
        {
            var parent = frames.Count == 0 ? null : frames[^1];
            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                if (reader.ValueSpan.Length > 256)
                    throw Invalid("The project contains an overlong JSON property name.");
                if (parent is { IsArray: false })
                {
                    parent.PropertyName = reader.GetString();
                    if (string.Equals(parent.Name, "ArticulationBindings", StringComparison.OrdinalIgnoreCase) &&
                        ++parent.PropertyCount > InputLimits.MaxHotkeyBindings)
                        throw Invalid("A project plug-in contains too many articulation bindings.");
                    if (string.Equals(parent.Name, "ArticulationBindings", StringComparison.OrdinalIgnoreCase) &&
                        ++totalBindings > InputLimits.MaxTotalArticulationBindings)
                        throw Invalid("The project contains too many articulation bindings overall.");
                }
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

            if (reader.TokenType == JsonTokenType.String)
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
            else if (parent is { IsArray: false })
            {
                parent.PropertyName = null;
            }
        }
    }

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
            "notes" => InputLimits.MaxNotesPerCell,
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
        "instructions" or "notice" or "text" or "directions" => InputLimits.MaxUserTextLength,
        "lyrics" => InputLimits.MaxLyricsLength,
        "importedfrom" or "path" => InputLimits.MaxPathLength,
        "colorhex" => 16,
        "bendtypename" or "bendstylename" or "clef" or "tripletfeelkind" or "beammode" or "stemdirection" => 128,
        _ => null
    };

    public static void Validate(SongProject project)
    {
        if (project is null) throw Invalid("The project is empty or invalid.");
        if (project.FormatVersion is < 1 or > 2) throw Invalid("This project uses an unsupported format version.");
        RequireText(project.Title, InputLimits.MaxTitleLength, "project title");
        RequireText(project.Subtitle, InputLimits.MaxTitleLength, "project subtitle");
        RequireText(project.Artist, InputLimits.MaxTitleLength, "artist name");
        RequireText(project.Album, InputLimits.MaxTitleLength, "album name");
        RequireText(project.MusicAuthor, InputLimits.MaxTitleLength, "music author");
        RequireText(project.LyricsAuthor, InputLimits.MaxTitleLength, "lyrics author");
        RequireText(project.Copyright, InputLimits.MaxTitleLength, "copyright text");
        RequireText(project.TabAuthor, InputLimits.MaxTitleLength, "tab author");
        RequireText(project.Instructions, InputLimits.MaxUserTextLength, "instructions");
        RequireText(project.Notice, InputLimits.MaxUserTextLength, "notice");
        RequireText(project.Lyrics, InputLimits.MaxLyricsLength, "lyrics");
        if (project.ImportedFrom is not null) RequireText(project.ImportedFrom, InputLimits.MaxPathLength, "source path");
        if (project.Tempo is < InputLimits.MinTempo or > InputLimits.MaxTempo)
            throw Invalid("The project tempo must be between 20 and 400 BPM.");
        ValidateTimeSignature(project.TimeSignatureNumerator, project.TimeSignatureDenominator, "project");
        if (project.KeySignature is < -7 or > 7) throw Invalid("The project key signature is outside the supported range.");
        // Routing links loaded from disk must not form a loop (the wiring window refuses them; a hand-edited or older
        // file could still contain one). Loops are broken here, before anything reaches the engine.
        if (project.Tracks is { Count: > 0 } linked)
            foreach (var cleared in RoutingLinks.BreakCycles(linked))
                System.Diagnostics.Debug.WriteLine($"Cleared a looping routing link: {cleared}");

        var tracks = project.Tracks ?? throw Invalid("The project has no valid track list.");
        if (tracks.Count > InputLimits.MaxTracks) throw Invalid("The project contains too many tracks.");
        var markers = project.Markers ?? throw Invalid("The project has no valid marker list.");
        if (markers.Count > InputLimits.MaxMarkers) throw Invalid("The project contains too many markers or sections.");

        long totalMeasures = 0;
        long totalCells = 0;
        long totalNotes = 0;
        long totalCurvePoints = 0;
        long totalTechniques = 0;
        var trackIds = new HashSet<Guid>();
        var longestTrack = 0;
        for (var trackIndex = 0; trackIndex < tracks.Count; trackIndex++)
        {
            var track = tracks[trackIndex] ?? throw Invalid("The project contains an empty track entry.");
            if (track.Id == Guid.Empty || !trackIds.Add(track.Id)) throw Invalid("The project contains duplicate or invalid track identifiers.");
            RequireText(track.Name, InputLimits.MaxTitleLength, "track name");
            RequireText(track.ColorHex, 16, "track colour", allowLineBreaks: false);
            RequireText(track.InstrumentName, InputLimits.MaxTitleLength, "instrument name");
            if (!Enum.IsDefined(track.Kind)) throw Invalid("The project contains an unsupported track type.");
            if (track.NumberOfFrets is < 1 or > InputLimits.MaxFrets || track.Capo is < 0 or > 48 ||
                track.MidiChannel is < 0 or > 15 || track.MidiProgram is < 0 or > 127 ||
                track.MidiOutputDeviceId is < -1 or > 100_000 || track.Volume is < 0 or > 127 ||
                track.Pan is < 0 or > 127 || track.Chorus is < 0 or > 127 || track.Reverb is < 0 or > 127 ||
                track.Transpose is < -127 or > 127)
                throw Invalid("A track contains numeric values outside the supported range.");

            var tunings = track.StringTunings ?? throw Invalid("A track has no valid tuning list.");
            if (tunings.Count > InputLimits.MaxStringsPerTrack) throw Invalid("A track contains too many strings.");
            if (tunings.Any(tuning => tuning is < 0 or > 127)) throw Invalid("A track contains an invalid string tuning.");
            ValidateRig(track.Rig);
            if (!SoundSources.All.Contains(track.SoundSource)) throw Invalid("A track has an unknown sound source.");
            if (!AudioInputs.All.Contains(track.AudioInput)) throw Invalid("A track has an unknown audio input.");
            var clips = track.AudioClips ?? throw Invalid("A track has no valid audio clip list.");
            if (clips.Count > 4096) throw Invalid("A track has too many audio clips.");
            foreach (var clip in clips)
            {
                if (clip is null) throw Invalid("The project contains an empty audio clip.");
                RequireText(clip.File, InputLimits.MaxPathLength, "audio file path");
                if (clip.Lane is < 0 or > 255) throw Invalid("An audio clip is on an invalid lane.");
                if (clip.Notes is { } notes && (notes.Count > 200_000 || notes.Any(n => n is null || !double.IsFinite(n.StartSec) || !double.IsFinite(n.LengthSec)
                    || n.StartSec < 0 || n.LengthSec is < 0 or > 86_400 || n.Pitch is < 0 or > 127 || n.Velocity is < 0 or > 127)))
                    throw Invalid("A MIDI clip contains invalid notes.");
                RequireText(clip.Name, InputLimits.MaxTitleLength, "audio clip name");
                if (!double.IsFinite(clip.StartSec) || !double.IsFinite(clip.OffsetSec) || !double.IsFinite(clip.SourceLengthSec) || !double.IsFinite(clip.FileLengthSec)
                    || clip.StartSec is < 0 or > 86_400 || clip.OffsetSec < 0 || clip.SourceLengthSec is <= 0 or > 86_400 || clip.FileLengthSec < 0
                    || !double.IsFinite(clip.GainDb) || clip.GainDb is < -96 or > 24 || !double.IsFinite(clip.Pitch) || clip.Pitch is < -24 or > 24
                    || !double.IsFinite(clip.Speed) || clip.Speed is < 0.25 or > 4)
                    throw Invalid("An audio clip has values outside the supported range.");
            }
            if (track.Lanes is null || track.Lanes.Count > 256 || track.Lanes.Any(l => l is null)) throw Invalid("A track has an invalid clip lane list.");
            if (track.MixerGroup is { Length: > 64 }) throw Invalid("A track has an overlong mixer group name.");

            var measures = track.Measures ?? throw Invalid("A track has no valid measure list.");
            if (measures.Count > InputLimits.MaxMeasuresPerTrack)
                throw Invalid("A track contains too many measures.");
            totalMeasures += measures.Count;
            longestTrack = Math.Max(longestTrack, measures.Count);
            if (totalMeasures > InputLimits.MaxTotalMeasures) throw Invalid("The project contains too many measures overall.");

            for (var measureIndex = 0; measureIndex < measures.Count; measureIndex++)
            {
                var measure = measures[measureIndex] ?? throw Invalid("The project contains an empty measure entry.");
                ValidateMeasure(measure, track, ref totalCells, ref totalNotes,
                    ref totalCurvePoints, ref totalTechniques);
            }
        }

        ValidateMixer(project.Mixer);

        foreach (var marker in markers)
        {
            if (marker is null) throw Invalid("The project contains an empty section marker.");
            if (marker.MeasureIndex < 0 || marker.MeasureIndex >= longestTrack)
                throw Invalid("A section marker points outside the project measures.");
            if (marker.LengthBars is int length && (length < 1 || length > longestTrack))
                throw Invalid("A section length is outside the project measures.");
            RequireText(marker.Title, InputLimits.MaxTitleLength, "section title");
            RequireText(marker.ColorHex, 16, "section colour", allowLineBreaks: false);
        }
    }

    private static void ValidateMixer(MixerSettings? mixer)
    {
        if (mixer is null) throw Invalid("The project has no valid mixer settings.");
        if (!MixerGrouping.All.Contains(mixer.Grouping)) throw Invalid("The mixer uses an unknown grouping.");
        var groups = mixer.Groups ?? throw Invalid("The mixer has no valid group list.");
        if (groups.Count > 32) throw Invalid("The mixer has too many groups.");
        foreach (var (name, levels) in groups)
        {
            RequireText(name, 64, "mixer group name", allowLineBreaks: false);
            if (levels is null || levels.Volume is < 0 or > 200 || levels.Pan is < -64 or > 63 || levels.Pitch is < -24 or > 24)
                throw Invalid("A mixer group has values outside the supported range.");
        }
        var buses = mixer.Buses ?? throw Invalid("The mixer has no valid bus list.");
        if (buses.Count > 32) throw Invalid("The mixer has too many group buses.");
        foreach (var (name, bus) in buses)
        {
            RequireText(name, 64, "mixer bus name", allowLineBreaks: false);
            if (bus is null) throw Invalid("A mixer group bus is invalid.");
            ValidateRig(bus.Rig);
        }
        if (mixer.Master is null) throw Invalid("The mixer has no valid master chain.");
        ValidateRig(mixer.Master.Rig);
        if (mixer.MonitorFx is not null) ValidateRig(mixer.MonitorFx.Rig);
        if (mixer.MasterPan is < -64 or > 63) throw Invalid("The master pan is outside the supported range.");
    }

    private static void ValidateRig(RigPreset? rig)
    {
        if (rig is null) throw Invalid("A track has no valid instrument settings.");
        RequireText(rig.Name, InputLimits.MaxTitleLength, "instrument preset name");
        RequireText(rig.ArticulationMap, InputLimits.MaxTitleLength, "articulation map");
        var plugins = rig.Plugins ?? throw Invalid("A track has no valid plug-in list.");
        if (plugins.Count > InputLimits.MaxPluginsPerTrack) throw Invalid("A track contains too many plug-in entries.");
        foreach (var plugin in plugins)
        {
            if (plugin is null || !Enum.IsDefined(plugin.Type)) throw Invalid("The project contains an invalid plug-in entry.");
            RequireText(plugin.Name, InputLimits.MaxTitleLength, "plug-in name");
            RequireText(plugin.Path, InputLimits.MaxPathLength, "plug-in path");
            if (plugin.Format is not ("" or "VST2" or "VST3") || plugin.Wet is < 0 or > 100 || !double.IsFinite(plugin.OutputDb) || plugin.OutputDb is < -60 or > 12
                || !PluginRoles.All.Contains(plugin.RoleMode) || !PluginPins.All.Contains(plugin.Pins) || (plugin.Vendor ?? "").Length > 256)
                throw Invalid("The project contains an invalid plug-in entry.");
            if (plugin.State is { Length: > InputLimits.MaxPluginStateChars }) throw Invalid("A plug-in's saved state is too large.");
            if ((plugin.SidechainTrackId ?? "").Length > 64 || (plugin.MidiOutTrackId ?? "").Length > 64) throw Invalid("A plug-in has an invalid track link.");
            var processors = plugin.MidiProcessors ?? throw Invalid("A plug-in contains an invalid MIDI processor list.");
            if (processors.Count > 32) throw Invalid("A plug-in contains too many MIDI processors.");
            foreach (var processor in processors)
            {
                if (processor is null) throw Invalid("A plug-in contains an invalid MIDI processor.");
                RequireText(processor.Type, 48, "MIDI processor type", allowLineBreaks: false);
                if ((processor.Params ?? "").Length > PluginMidiProcessor.MaxParamsChars) throw Invalid("A MIDI processor's settings are too large.");
            }
            var bindings = plugin.ArticulationBindings ?? throw Invalid("A plug-in contains an invalid articulation map.");
            if (bindings.Count > InputLimits.MaxHotkeyBindings) throw Invalid("A plug-in contains too many articulation bindings.");
            foreach (var pair in bindings)
            {
                RequireText(pair.Key, 128, "articulation name");
                RequireText(pair.Value, 128, "articulation value");
            }
        }
    }

    private static void ValidateMeasure(MeasureModel measure, TrackModel track,
        ref long totalCells, ref long totalNotes, ref long totalCurvePoints, ref long totalTechniques)
    {
        if (measure.Number < 0 || measure.Number > InputLimits.MaxMeasuresPerTrack)
            throw Invalid("A measure has an invalid number.");
        if (measure.TimeSigNum is { } numerator && numerator is < 1 or > InputLimits.MaxTimeSignatureNumerator)
            throw Invalid("A measure has an invalid time-signature numerator.");
        if (measure.TimeSigDenom is { } denominator && !InputLimits.IsValidTimeSignatureDenominator(denominator))
            throw Invalid("A measure has an invalid time-signature denominator.");
        if (measure.KeySignature is { } key && key is < -7 or > 7)
            throw Invalid("A measure has an invalid key signature.");
        if (measure.TempoChange is { } tempo && tempo is < InputLimits.MinTempo or > InputLimits.MaxTempo)
            throw Invalid("A measure has a tempo outside 20–400 BPM.");
        if (measure.MidBarTempos is { } points && (points.Count > 64 ||
            points.Any(p => p is null || p.Tempo is < InputLimits.MinTempo or > InputLimits.MaxTempo || !double.IsFinite(p.Slot) || p.Slot is < 0 or > 1024)))
            throw Invalid("A measure has invalid tempo changes inside the bar.");
        if (measure.RepeatCount is < 2 or > TabForge.Playback.PlaybackOrder.MaxRepeats || measure.AlternateEnding is < 0 or > 8 || measure.AlternateEndingMask is < 0 or > 0xFF)
            throw Invalid("A measure has an invalid repeat or ending value.");

        RequireText(measure.Clef, 32, "clef", allowLineBreaks: false);
        RequireText(measure.SectionName, InputLimits.MaxTitleLength, "section name");
        RequireText(measure.TripletFeelKind, 32, "rhythm style", allowLineBreaks: false);
        RequireText(measure.Directions, InputLimits.MaxUserTextLength, "navigation directions");

        var cells = measure.Cells ?? throw Invalid("A measure has no valid beat list.");
        var voice2 = measure.Voice2Cells ?? throw Invalid("A measure has an invalid second-voice list.");
        if (cells.Count is < 1 or > InputLimits.MaxCellsPerMeasure || voice2.Count > InputLimits.MaxCellsPerMeasure)
            throw Invalid("A measure contains too many or too few beats.");
        totalCells += cells.Count + voice2.Count;
        if (totalCells > InputLimits.MaxTotalCells) throw Invalid("The project contains too many beats overall.");
        var measureNotes = 0;
        foreach (var cell in cells.Concat(voice2))
        {
            if (cell is null) throw Invalid("The project contains an empty beat entry.");
            ValidateCell(cell, track, ref totalNotes, ref totalCurvePoints, ref totalTechniques, ref measureNotes);
        }
        if (measureNotes > InputLimits.MaxNotesPerMeasure)
            throw Invalid("A measure contains too many notes.");
    }

    private static void ValidateCell(TabCell cell, TrackModel track, ref long totalNotes,
        ref long totalCurvePoints, ref long totalTechniques, ref int measureNotes)
    {
        if (cell.Notes is null || cell.WhammyPoints is null) throw Invalid("A beat contains an invalid note or bend list.");
        if (cell.Notes.Count > InputLimits.MaxNotesPerCell || cell.WhammyPoints.Count > InputLimits.MaxCurvePoints)
            throw Invalid("A beat contains too many notes or bend points.");
        if (cell.DurationDenominator is not (1 or 2 or 4 or 8 or 16 or 32 or 64) || cell.Dots is < 0 or > 2 ||
            cell.SoundDurationPercent is < 1 or > 200 || cell.OctaveShiftSemitones is not (-24 or -12 or 0 or 12 or 24) ||
            cell.Accent is < 0 or > 2 || cell.TremoloPickDenominator is not (0 or 1 or 2 or 4 or 8 or 16 or 32 or 64))
            throw Invalid("A beat contains numeric values outside the supported range.");
        if ((cell.TupletNumerator == 0) != (cell.TupletDenominator == 0) ||
            cell.TupletNumerator is < 0 or > 64 || cell.TupletDenominator is < 0 or > 64)
            throw Invalid("A beat contains an invalid tuplet ratio.");
        if (cell.RhythmicPosition is { } position && (!double.IsFinite(position) || position is < 0 or > InputLimits.MaxCellsPerMeasure * 2))
            throw Invalid("A beat has an invalid rhythmic position.");
        // BeamMode / StemDirection are enums: the converter maps any unknown text to Auto.
        RequireOptionalText(cell.ChordName, InputLimits.MaxTitleLength, "chord name");
        RequireOptionalText(cell.Text, InputLimits.MaxUserTextLength, "beat text");
        RequireText(cell.Lyrics, InputLimits.MaxLyricsLength, "beat lyrics");

        foreach (var point in cell.WhammyPoints)
        {
            ValidateCurvePoint(point, maxOffset: 60);
            if (++totalCurvePoints > InputLimits.MaxTotalCurvePoints)
                throw Invalid("The project contains too many bend points overall.");
        }
        foreach (var note in cell.Notes)
        {
            if (note is null) throw Invalid("The project contains an empty note entry.");
            if (note.StringIndex < 0 || note.StringIndex >= track.StringTunings.Count ||
                note.Fret is < 0 or > InputLimits.MaxFrets || note.MidiValue is < 0 or > 127 ||
                note.Velocity is < 0 or > 127 || note.SlideTargetMidi is < 0 or > 127 ||
                note.TrillTargetMidi is < 0 or > 127 || note.TrillDurationDenominator is not (0 or 1 or 2 or 4 or 8 or 16 or 32 or 64))
                throw Invalid("A note contains an invalid string, fret, or MIDI value.");
            if (note.LeftHandFinger is < 0 or > 4) note.LeftHandFinger = null;
            if (note.RightHandFinger is < 0 or > 4) note.RightHandFinger = null;
            if (!double.IsFinite(note.GraceOnsetOffsetSlots) || Math.Abs(note.GraceOnsetOffsetSlots) > 512 ||
                !double.IsFinite(note.GraceDurationSlots) || note.GraceDurationSlots is < 0 or > 512)
                throw Invalid("A note contains an invalid grace-note position or duration.");
            RequireText(note.BendTypeName, 128, "bend type", allowLineBreaks: false);
            RequireText(note.BendStyleName, 128, "bend style", allowLineBreaks: false);
            if (note.Techniques is null || note.Techniques.Count > InputLimits.MaxTechniquesPerNote ||
                note.Techniques.Any(technique => !InputLimits.IsSafeText(technique, 128, allowLineBreaks: false)))
                throw Invalid("A note contains too many or invalid techniques.");
            totalTechniques += note.Techniques.Count;
            if (totalTechniques > InputLimits.MaxTotalTechniques)
                throw Invalid("The project contains too many note techniques overall.");
            if (note.BendPoints is null || note.BendPoints.Count > InputLimits.MaxCurvePoints)
                throw Invalid("A note contains too many bend points.");
            foreach (var point in note.BendPoints)
            {
                ValidateCurvePoint(point, maxOffset: 60);
                if (++totalCurvePoints > InputLimits.MaxTotalCurvePoints)
                    throw Invalid("The project contains too many bend points overall.");
            }
            totalNotes++;
            measureNotes++;
            if (totalNotes > InputLimits.MaxTotalNotes) throw Invalid("The project contains too many notes overall.");
        }
    }

    private static void ValidateCurvePoint(BendPointModel? point, double maxOffset)
    {
        if (point is null || !double.IsFinite(point.Offset) || !double.IsFinite(point.Value) ||
            point.Offset is < 0 || point.Offset > maxOffset || Math.Abs(point.Value) > 10_000)
            throw Invalid("A bend curve contains an invalid point.");
    }

    private static void ValidateTimeSignature(int numerator, int denominator, string description)
    {
        if (numerator is < 1 or > InputLimits.MaxTimeSignatureNumerator ||
            !InputLimits.IsValidTimeSignatureDenominator(denominator))
            throw Invalid($"The {description} time signature is outside the supported range.");
    }

    private static void RequireText(string? value, int limit, string description, bool allowLineBreaks = true)
    {
        if (!InputLimits.IsSafeText(value, limit, allowLineBreaks))
            throw Invalid($"The project contains an invalid or overlong {description}.");
    }

    private static void RequireOptionalText(string? value, int limit, string description)
    {
        if (value is not null) RequireText(value, limit, description);
    }

    private static InvalidDataException Invalid(string message) => new(message);
}
