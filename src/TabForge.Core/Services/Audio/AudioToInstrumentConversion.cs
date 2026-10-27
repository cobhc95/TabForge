using TabForge.Models;
using TabForge.Playback;
using TabForge.Ports;

namespace TabForge.Services;

// Owns: Convert to instrument track on an audio track (one undo step): the instrument's kind, strings, frets and MIDI channel, the drum map,
//   the clip lane shift, the MIDI clips written into notation, the song-time bar lookup for that write, and ApplyInstrument (the instrument
//   setup that the track's instrument select also uses). The saved answers of the clip question are here too.
// Does not own: the instrument picker, the clip prompts and saving the answer (the hosts ask), the window refresh afterwards.
// Tests: no dedicated test class.
public static class AudioToInstrumentConversion
{
    /// <summary>The values of the editing setting ConvertMidiClips: Ask prompts each time; the other two are remembered answers.</summary>
    public const string AskAnswer = "Ask", WriteAnswer = "Write as notation", KeepAnswer = "Keep as MIDI";

    /// <summary>
    /// Turns an audio track into an instrument track, one undo step (the way back is the instrument-to-audio edit).
    /// The track takes <paramref name="instrument"/> (a catalogue sound name, as in the instrument picker), the kind, strings, frets and
    /// MIDI channel that sound implies, and keeps its clips, plug-in chain, input and mix. Its bars are already empty, so notes can be
    /// entered at once. Every clip moves down one lane (lane 0 is left free under the new tab lane). False for a track that is not audio
    /// or an unknown instrument; nothing changes then. With <paramref name="barAt"/> (song seconds to bar and fraction) the track's MIDI clips
    /// are written into the new notation and removed in the same step (<see cref="MidiClipToTab"/>); a clip that fits nothing stays.
    /// </summary>
    public static EditOutcome ConvertAudioToInstrument(IEditTarget document, TrackModel track, string instrument, Func<double, (int Bar, double Fraction)>? barAt = null) =>
        document.Run(project =>
        {
            if (!track.IsAudio || !project.Tracks.Contains(track)) return false;
            var entry = InstrumentCatalog.Find(instrument);
            var preset = InstrumentCatalog.Presets.FirstOrDefault(item => item.Name == instrument);
            if (entry is null && preset is null) return false;
            var drum = entry?.IsDrumKit ?? preset!.Map == "GM Drums";
            var kind = drum ? TrackKind.Drums : TrackSetup.KindOf(entry?.Name ?? preset!.Name, 0, TrackKind.Guitar);
            var model = NewTrackFactory.CreateTrack(project, kind);
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

    /// <summary>The source bar and position at a song time (first bar that covers it), for placing MIDI clips into notation.</summary>
    public static (int Bar, double Fraction) BarAtSongSec(SongProject project, double songSec)
    {
        var bars = MidiTimelineBuilder.Build(project, new PlaybackOptions { RepeatExpansion = true, RespectMuteSolo = false, SkipClips = true }).Bars.ToArray();
        var ms = songSec * 1000;
        foreach (var bar in bars)
            if (ms < bar.EndMs) return (bar.Bar, bar.EndMs > bar.StartMs ? bar.SlotFraction(ms) : 0);
        return bars.Length == 0 ? (0, 0) : (bars[^1].Bar, 1);
    }

    /// <summary>True when the catalogue sound or preset is a drum kit; false for a pitched or unknown name.</summary>
    public static bool IsDrumSound(string selected) => InstrumentCatalog.Find(selected) is { } entry
        ? entry.IsDrumKit
        : InstrumentCatalog.Presets.FirstOrDefault(item => item.Name == selected)?.Map == "GM Drums";

    /// <summary>Sets the instrument of a MIDI track to a catalogue sound or a preset (false for an audio track or an unknown sound).</summary>
    public static bool ApplyInstrument(SongProject project, TrackModel track, string selected)
    {
        // Any catalogue sound (all 128 GM programs + drum kits): the program is sent on the
        // track channel; drum kits move the track to the GM drum channel (10) and back when leaving it.
        if (track.IsAudio) return false;   // an audio track has no instrument and never converts
        var entry = InstrumentCatalog.Find(selected);
        var preset = InstrumentCatalog.Presets.FirstOrDefault(item => item.Name == selected);
        if (entry is null && preset is null) return false;
        track.MidiProgram = entry?.Program ?? preset!.Program;
        track.InstrumentName = entry?.Name ?? preset!.Name;
        track.Rig.Name = preset?.Rig ?? track.InstrumentName;
        track.Rig.ArticulationMap = preset?.Map ?? entry!.Map;
        var drum = entry?.IsDrumKit ?? preset!.Map == "GM Drums";
        // Another family (drums to pitched or back): the whole track follows, notes re-mapped.
        if (TrackSetup.IsDrumFamily(track) != drum) TrackSetup.ConvertFamily(track, drum, TrackSetup.PitchedKindFor(track.InstrumentName));
        if (drum && track.MidiChannel != 9) track.MidiChannel = 9;
        else if (!drum && track.MidiChannel == 9) track.MidiChannel = NewTrackFactory.FindMidiChannel(project);
        // An empty track takes the kind and default strings of the new instrument (a bass sound gets 4 strings);
        // a track with notes keeps its strings and kind, so no written note ever moves.
        if (!drum) TrackSetup.AdoptInstrumentKind(track, track.InstrumentName, track.MidiChannel);
        TrackColouring.FollowInstrument(track);
        return true;
    }
}
