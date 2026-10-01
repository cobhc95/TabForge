using TabForge.Models;
using TabForge.Plugins;
using TabForge.Presets;
using TabForge.Services;

namespace TabForge.Controllers;

public enum TrackEditKind { ToggleMute, ToggleSolo, Rename, SetColor, SelectInstrument }

public sealed record TrackEditRequest(int TrackIndex, TrackEditKind Kind, string? Value = null);

public sealed record TrackInstrumentPreset(string Name, int Program, string Rig, string Map);

/// <summary>Track construction and structural operations independent of mixer controls.</summary>
public sealed class TrackController
{
    private static readonly string[] TrackColours = { "#F61A16", "#ED2224", "#F4E014", "#2248E8", "#35B954", "#D850C6" };

    public static readonly IReadOnlyList<TrackInstrumentPreset> InstrumentPresets = new[]
    {
        new TrackInstrumentPreset("Acoustic Guitar (Steel)", 25, "Acoustic", "Generic Guitar"),
        new TrackInstrumentPreset("Acoustic Guitar (Nylon)", 24, "Nylon", "Generic Guitar"),
        new TrackInstrumentPreset("Clean Electric Guitar", 27, "Clean", "Generic Guitar"),
        new TrackInstrumentPreset("Electric Guitar (Jazz)", 26, "Jazz Clean", "Generic Guitar"),
        new TrackInstrumentPreset("Overdriven Guitar", 29, "Crunch", "Generic Guitar"),
        new TrackInstrumentPreset("Distortion Guitar", 30, "Modern Rhythm", "Generic Guitar"),
        new TrackInstrumentPreset("Lead Guitar (Distortion)", 30, "Lead", "Generic Guitar"),
        new TrackInstrumentPreset("Electric Bass (Finger)", 33, "Bass DI", "Generic Bass"),
        new TrackInstrumentPreset("Electric Bass (Pick)", 34, "Bass Pick", "Generic Bass"),
        new TrackInstrumentPreset("Slap Bass", 36, "Slap Bass", "Generic Bass"),
        new TrackInstrumentPreset("Grand Piano", 0, "Piano", "Piano"),
        new TrackInstrumentPreset("Electric Piano", 4, "E-Piano", "Piano"),
        new TrackInstrumentPreset("Organ", 16, "Organ", "Piano"),
        new TrackInstrumentPreset("Strings", 48, "Strings", "Piano"),
        new TrackInstrumentPreset("Drum Kit (Standard)", 0, "GM Drum Kit", "GM Drums"),
        new TrackInstrumentPreset("Drum Kit (Power)", 16, "Power Kit", "GM Drums"),
        new TrackInstrumentPreset("Drum Kit (Jazz)", 32, "Jazz Kit", "GM Drums")
    };

    public TrackModel CreateTrack(SongProject project, TrackKind kind)
    {
        // A new track matches the song's bar count (an empty project starts with 32 bars).
        var measures = project.Tracks.Count > 0 ? Math.Max(1, project.Tracks.Max(t => t.Measures.Count)) : 32;
        var color = TrackColours[project.Tracks.Count % TrackColours.Length];
        return kind switch
        {
            TrackKind.Bass => new TrackModel { Name = "Bass", Kind = TrackKind.Bass, ColorHex = color, InstrumentName = "Electric Bass", MidiChannel = FindMidiChannel(project), MidiProgram = 33, StringTunings = new() { 43, 38, 33, 28 }, Measures = TemplateFactory.Measures(measures), Rig = new RigPreset { Name = "Bass", ArticulationMap = "Generic Bass" } },
            TrackKind.Drums => NewDrumTrack(new TrackModel { Name = "Drums", Kind = TrackKind.Drums, ColorHex = color, InstrumentName = "Drum Kit", MidiChannel = 9, MidiProgram = 0, StringTunings = new() { 49, 46, 42, 38, 36 }, Measures = TemplateFactory.Measures(measures), Rig = new RigPreset { Name = "GM Drum Kit", ArticulationMap = "GM Drums" } }),
            TrackKind.Keys => new TrackModel { Name = "Piano", Kind = TrackKind.Keys, ColorHex = color, InstrumentName = "Grand Piano", MidiChannel = FindMidiChannel(project), MidiProgram = 0, StringTunings = new() { 96, 91, 86, 81, 76, 71 }, Measures = TemplateFactory.Measures(measures), Rig = new RigPreset { Name = "Piano", ArticulationMap = "Piano" } },
            _ => new TrackModel { Name = "Guitar", Kind = TrackKind.Guitar, ColorHex = color, InstrumentName = "Electric Guitar", MidiChannel = FindMidiChannel(project), MidiProgram = 29, StringTunings = new() { 64, 59, 55, 50, 45, 40 }, Measures = TemplateFactory.Measures(measures), Rig = new RigPreset { Name = "Overdriven Guitar", ArticulationMap = "Generic Guitar" } }
        };
    }

    /// <summary>A new drum track takes the default drum map's TAB lines (not a separate hard-coded list).</summary>
    private static TrackModel NewDrumTrack(TrackModel track)
    {
        DrumMaps.Apply(track, DrumMaps.GuitarPro5);
        return track;
    }

    public bool DeleteTrack(SongProject project, int index)
    {
        if (project.Tracks.Count <= 1 || index < 0 || index >= project.Tracks.Count) return false;
        project.Tracks.RemoveAt(index);
        return true;
    }

    public bool MoveTrack(SongProject project, int from, int to)
    {
        if (from < 0 || from >= project.Tracks.Count) return false;
        to = Math.Clamp(to, 0, project.Tracks.Count - 1);
        return from != to && project.MoveTrack(from, to);
    }

    public bool ApplyEdit(SongProject project, TrackEditRequest request)
    {
        if (request.TrackIndex < 0 || request.TrackIndex >= project.Tracks.Count) return false;
        var track = project.Tracks[request.TrackIndex];
        switch (request.Kind)
        {
            case TrackEditKind.ToggleMute:
                track.Mute = !track.Mute;
                return true;
            case TrackEditKind.ToggleSolo:
                track.Solo = !track.Solo;
                return true;
            case TrackEditKind.Rename:
                if (string.Equals(track.Name, request.Value, StringComparison.Ordinal)) return false;
                track.Name = request.Value ?? "";
                return true;
            case TrackEditKind.SetColor:
                if (string.Equals(track.ColorHex, request.Value, StringComparison.Ordinal)) return false;
                track.ColorHex = request.Value ?? track.ColorHex;
                return true;
            case TrackEditKind.SelectInstrument:
                return ApplyInstrument(project, track, request.Value ?? "");
            default:
                return false;
        }
    }

    private static bool ApplyInstrument(SongProject project, TrackModel track, string selected)
    {
        // Any catalogue sound (all 128 GM programs + drum kits): the program is sent on the
        // track channel; drum kits move the track to the GM drum channel (10) and back when leaving it.
        var entry = InstrumentCatalog.Find(selected);
        var preset = InstrumentPresets.FirstOrDefault(item => item.Name == selected);
        if (entry is null && preset is null) return false;
        track.MidiProgram = entry?.Program ?? preset!.Program;
        track.InstrumentName = entry?.Name ?? preset!.Name;
        track.Rig.Name = preset?.Rig ?? track.InstrumentName;
        track.Rig.ArticulationMap = preset?.Map ?? entry!.Map;
        var drum = entry?.IsDrumKit ?? preset!.Map == "GM Drums";
        if (drum && track.MidiChannel != 9) track.MidiChannel = 9;
        else if (!drum && track.MidiChannel == 9) track.MidiChannel = FindMidiChannel(project);
        // An empty track takes the kind and default strings of the new instrument (a bass sound gets 4 strings);
        // a track with notes keeps its strings and kind, so no written note ever moves.
        if (!drum) TrackSetup.AdoptInstrumentKind(track, track.InstrumentName, track.MidiChannel);
        return true;
    }

    private static int FindMidiChannel(SongProject project)
    {
        for (var channel = 0; channel < 16; channel++)
            if (channel != 9 && project.Tracks.All(track => track.MidiChannel != channel)) return channel;
        return 0;
    }
}
