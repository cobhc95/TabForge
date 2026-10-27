using System;
using System.Linq;
using TabForge.Models;
using TabForge.Platform.Theme;
using TabForge.Plugins;
using TabForge.Presets;

namespace TabForge.Services;

// Owns: the model of a new track (the kind's template, instrument, MIDI channel, tuning, colour and empty bars matching the song).
// Does not own: the document edit that adds the track, the Add track window or prompt, or the track list.
// Tests: NewTrackFactoryTests in TabForge.Avalonia.Tests.
/// <summary>Builds the model of a new track from its kind. No UI types, so the app can share it.</summary>
public static class NewTrackFactory
{
    /// <summary>The colour a new track of this kind starts with (null: the kind cycles the palette).</summary>
    public static string? KindColour(TrackKind kind) => kind switch
    {
        TrackKind.Guitar => "#A12424",   // dark red
        TrackKind.Bass => "#B8930F",     // dark yellow
        TrackKind.Drums => "#1F3F9E",    // dark blue
        TrackKind.Audio => "#7CC4F2",    // light blue
        _ => null
    };

    /// <summary>A new track of <paramref name="kind"/>: its template, its colour (the kind's, else the palette in turn) and the song's bar count (32 in an empty song).</summary>
    public static TrackModel CreateTrack(SongProject project, TrackKind kind)
    {
        // A new track matches the song's bar count (an empty project starts with 32 bars).
        var measures = project.Tracks.Count > 0 ? Math.Max(1, project.Tracks.Max(t => t.Measures.Count)) : 32;
        // A new track takes its kind's colour (a song's own track colours are never changed); other kinds cycle the palette.
        var color = KindColour(kind) ?? NamedColourSwatches.TrackRotationColours[project.Tracks.Count % NamedColourSwatches.TrackRotationColours.Count];
        var track = BuildTrack(project, kind, color, measures);
        // A new instrument track starts on its instrument group's colour and keeps following it until the user picks one.
        if (!track.IsAudio) { track.ColorFollowsInstrument = true; TrackColouring.FollowInstrument(track); track.MidiInputs = new List<string> { AudioInputs.AllMidi }; }   // an instrument track hears every MIDI input; an audio track none
        return track;
    }

    private static TrackModel BuildTrack(SongProject project, TrackKind kind, string color, int measures)
    {
        return kind switch
        {
            TrackKind.Audio => NewAudioTrack(project, color, measures),
            TrackKind.Bass => new TrackModel { Name = "Bass", Kind = TrackKind.Bass, ColorHex = color, InstrumentName = "Electric Bass", MidiChannel = FindMidiChannel(project), MidiProgram = 33, StringTunings = new() { 43, 38, 33, 28 }, Measures = TemplateFactory.Measures(measures), Rig = new RigPreset { Name = "Bass", ArticulationMap = "Generic Bass" } },
            TrackKind.Drums => NewDrumTrack(new TrackModel { Name = "Drums", Kind = TrackKind.Drums, ColorHex = color, InstrumentName = "Drum Kit", MidiChannel = 9, MidiProgram = 0, StringTunings = new() { 49, 46, 42, 38, 36 }, Measures = TemplateFactory.Measures(measures), Rig = new RigPreset { Name = "GM Drum Kit", ArticulationMap = "GM Drums" } }),
            TrackKind.Keys => new TrackModel { Name = "Piano", Kind = TrackKind.Keys, ColorHex = color, InstrumentName = "Grand Piano", MidiChannel = FindMidiChannel(project), MidiProgram = 0, StringTunings = new() { 96, 91, 86, 81, 76, 71 }, Measures = TemplateFactory.Measures(measures), Rig = new RigPreset { Name = "Piano", ArticulationMap = "Piano" } },
            _ => new TrackModel { Name = "Guitar", Kind = TrackKind.Guitar, ColorHex = color, InstrumentName = "Electric Guitar", MidiChannel = FindMidiChannel(project), MidiProgram = 29, StringTunings = new() { 64, 59, 55, 50, 45, 40 }, Measures = TemplateFactory.Measures(measures), Rig = new RigPreset { Name = "Overdriven Guitar", ArticulationMap = "Generic Guitar" } }
        };
    }

    /// <summary>An audio track: no instrument, tuning or MIDI sound, input 1, one empty clip lane, "Audio n" name, empty bars matching the song.</summary>
    private static TrackModel NewAudioTrack(SongProject project, string color, int measures)
    {
        var number = 1;
        while (project.Tracks.Any(t => string.Equals(t.Name, $"Audio {number}", StringComparison.Ordinal))) number++;
        return new TrackModel
        {
            Name = $"Audio {number}", Kind = TrackKind.Audio, ColorHex = color, InstrumentName = "", MidiProgram = 0, MidiChannel = 0, NumberOfFrets = 0,
            StringTunings = new(), MidiSound = false, AudioInput = AudioInputs.Input1, Lanes = new() { new ClipLane() },
            Measures = TemplateFactory.Measures(measures), Rig = new RigPreset { Name = "Audio", ArticulationMap = "Generic Guitar" }
        };
    }

    /// <summary>A new drum track takes the default drum map's TAB lines (not a separate hard-coded list).</summary>
    private static TrackModel NewDrumTrack(TrackModel track)
    {
        DrumMaps.Apply(track, DrumMaps.GuitarPro5);
        return track;
    }

    public static int FindMidiChannel(SongProject project)
    {
        for (var channel = 0; channel < 16; channel++)
            if (channel != 9 && project.Tracks.All(track => track.MidiChannel != channel)) return channel;
        return 0;
    }
}
