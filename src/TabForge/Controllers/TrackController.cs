using TabForge.Documents;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Presets;
using TabForge.Services;

namespace TabForge.Controllers;

public enum TrackEditKind { ToggleMute, ToggleSolo, Rename, SetColor, SelectInstrument }

public sealed record TrackEditRequest(int TrackIndex, TrackEditKind Kind, string? Value = null);

public sealed record TrackInstrumentPreset(string Name, int Program, string Rig, string Map);

// Owns: the track-level edit requests (rename, instrument, tuning, mute and solo, add and remove) applied to a song.
// Does not own: the track list drawing and the engine's track state.
// Tests: TestEditControllers, TestTrackRowRightClick, TestInstrumentChoiceStrings.
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
        // A new track takes its kind's colour (a song's own track colours are never changed); other kinds cycle the palette.
        var color = KindColour(kind) ?? TrackColours[project.Tracks.Count % TrackColours.Length];
        return kind switch
        {
            TrackKind.Audio => NewAudioTrack(project, color, measures),
            TrackKind.Bass => new TrackModel { Name = "Bass", Kind = TrackKind.Bass, ColorHex = color, InstrumentName = "Electric Bass", MidiChannel = FindMidiChannel(project), MidiProgram = 33, StringTunings = new() { 43, 38, 33, 28 }, Measures = TemplateFactory.Measures(measures), Rig = new RigPreset { Name = "Bass", ArticulationMap = "Generic Bass" } },
            TrackKind.Drums => NewDrumTrack(new TrackModel { Name = "Drums", Kind = TrackKind.Drums, ColorHex = color, InstrumentName = "Drum Kit", MidiChannel = 9, MidiProgram = 0, StringTunings = new() { 49, 46, 42, 38, 36 }, Measures = TemplateFactory.Measures(measures), Rig = new RigPreset { Name = "GM Drum Kit", ArticulationMap = "GM Drums" } }),
            TrackKind.Keys => new TrackModel { Name = "Piano", Kind = TrackKind.Keys, ColorHex = color, InstrumentName = "Grand Piano", MidiChannel = FindMidiChannel(project), MidiProgram = 0, StringTunings = new() { 96, 91, 86, 81, 76, 71 }, Measures = TemplateFactory.Measures(measures), Rig = new RigPreset { Name = "Piano", ArticulationMap = "Piano" } },
            _ => new TrackModel { Name = "Guitar", Kind = TrackKind.Guitar, ColorHex = color, InstrumentName = "Electric Guitar", MidiChannel = FindMidiChannel(project), MidiProgram = 29, StringTunings = new() { 64, 59, 55, 50, 45, 40 }, Measures = TemplateFactory.Measures(measures), Rig = new RigPreset { Name = "Overdriven Guitar", ArticulationMap = "Generic Guitar" } }
        };
    }

    /// <summary>The colour a new track of this kind starts with (null: the kind cycles the palette).</summary>
    public static string? KindColour(TrackKind kind) => kind switch
    {
        TrackKind.Guitar => "#A12424",   // dark red
        TrackKind.Bass => "#B8930F",     // dark yellow
        TrackKind.Drums => "#1F3F9E",    // dark blue
        TrackKind.Audio => "#7CC4F2",    // light blue
        _ => null
    };

    /// <summary>A new track whose kind was changed in the Add track window takes the new kind's colour, unless a colour was picked there.</summary>
    public static void RecolourNewTrack(TrackModel track, string colourBefore)
    {
        if (track.ColorHex == colourBefore && KindColour(track.Kind) is { } colour) track.ColorHex = colour;
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

    // ---- document edits (DocumentEdits): one undo transaction, one dirty change, one timeline invalidation per logical edit ----

    /// <summary>Adds <paramref name="track"/> at <paramref name="index"/> (clamped; null: at the end).</summary>
    public EditResult AddTrack(DocumentSession document, TrackModel track, int? index = null) =>
        DocumentEdits.Run(document, project =>
        {
            project.Tracks.Insert(Math.Clamp(index ?? project.Tracks.Count, 0, project.Tracks.Count), track);
            return true;
        });

    /// <summary>The add-track prompt's "Audio" choice (and the menu's "Audio track"): a new audio track at the end, one undo step; no dialog.</summary>
    public EditResult<TrackModel> AddAudioTrack(DocumentSession document)
    {
        TrackModel? created = null;
        var result = DocumentEdits.Run(document, project =>
        {
            created = CreateTrack(project, TrackKind.Audio);
            project.Tracks.Add(created);
            return true;
        });
        return new EditResult<TrackModel>(result.Changed, created, result.Capture);
    }

    /// <summary>
    /// Turns an instrument track into an audio track, one undo step: its notation becomes one MIDI clip (song time 0, on the first free
    /// lane; nothing is lost) and its bars are emptied (headers and bar count kept). Instrument, tuning and MIDI sound are cleared to the
    /// audio defaults; name, colour, mix, plug-in chain and the existing clips are kept. The track stays silent unless an instrument
    /// plug-in is on it. False for an audio track or one not in the song; nothing changes then.
    /// </summary>
    public EditResult ConvertInstrumentToAudio(DocumentSession document, TrackModel track) =>
        DocumentEdits.Run(document, project =>
        {
            if (track.IsAudio || !project.Tracks.Contains(track)) return false;
            var clip = NotationToMidiClip.Build(project, track);
            if (clip is not null)
            {
                clip.Lane = ClipLanes.FreeLane(track, 0, clip.EndSec);
                ClipLanes.Ensure(track, clip.Lane + 1);
                track.AudioClips.Add(clip);
            }
            NotationToMidiClip.Empty(track);
            track.Kind = TrackKind.Audio;
            track.InstrumentName = "";
            track.MidiProgram = 0;
            track.MidiChannel = 0;
            track.NumberOfFrets = 0;
            track.StringTunings = new();
            track.MidiSound = false;
            track.AudioInput = AudioInputs.Input1;
            ClipLanes.Ensure(track, 1);
            track.Rig.Name = "Audio";
            track.Rig.ArticulationMap = "Generic Guitar";
            return true;
        });

    /// <summary>
    /// Turns an audio track into an instrument track, one undo step (<see cref="ConvertInstrumentToAudio"/> is the way back).
    /// The track takes <paramref name="instrument"/> (a catalogue sound name, as in the instrument picker), the kind, strings, frets and
    /// MIDI channel that sound implies, and keeps its clips, plug-in chain, input and mix. Its bars are already empty, so notes can be
    /// entered at once. Every clip moves down one lane (lane 0 is left free under the new tab lane). False for a track that is not audio
    /// or an unknown instrument; nothing changes then. With <paramref name="barAt"/> (song seconds to bar and fraction) the track's MIDI clips
    /// are written into the new notation and removed in the same step (<see cref="MidiClipToTab"/>); a clip that fits nothing stays.
    /// </summary>
    public EditResult ConvertAudioToInstrument(DocumentSession document, TrackModel track, string instrument, Func<double, (int Bar, double Fraction)>? barAt = null) =>
        DocumentEdits.Run(document, project =>
        {
            if (!track.IsAudio || !project.Tracks.Contains(track)) return false;
            var entry = InstrumentCatalog.Find(instrument);
            var preset = InstrumentPresets.FirstOrDefault(item => item.Name == instrument);
            if (entry is null && preset is null) return false;
            var drum = entry?.IsDrumKit ?? preset!.Map == "GM Drums";
            var kind = drum ? TrackKind.Drums : TrackSetup.KindOf(entry?.Name ?? preset!.Name, 0, TrackKind.Guitar);
            var model = CreateTrack(project, kind);
            track.Kind = kind;
            track.StringTunings = model.StringTunings;
            track.NumberOfFrets = model.NumberOfFrets;
            track.MidiSound = true;
            track.MidiChannel = model.MidiChannel;
            track.Measures = track.Measures.Count == model.Measures.Count ? track.Measures : model.Measures;
            if (drum) DrumMaps.Apply(track, DrumMaps.GuitarPro5);
            ApplyInstrument(project, track, instrument);
            foreach (var clip in track.AudioClips) clip.Lane++;
            track.Lanes.Insert(0, new ClipLane());
            if (barAt is not null)
                foreach (var clip in track.AudioClips.Where(c => c.IsMidi).ToList())
                    if (MidiClipToTab.Write(project, track, clip, barAt) > 0) track.AudioClips.Remove(clip);
            return true;
        });

    public EditResult DeleteTrack(DocumentSession document, int index) =>
        DocumentEdits.Run(document, project => DeleteTrack(project, index));

    /// <summary>Moves a track; <paramref name="before"/> is the undo state taken when a drag started (null: taken now).</summary>
    public EditResult MoveTrack(DocumentSession document, int from, int to, UndoSnapshot? before = null) =>
        DocumentEdits.Run(document, project => MoveTrack(project, from, to), before);

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
        if (track.IsAudio) return false;   // an audio track has no instrument and never converts
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
