using TabForge.Models;

namespace TabForge.Services;

// Owns: which tracks get a TAB staff (fretted instruments) and the repair of a non-fretted instrument stored as a guitar or bass (octave-spaced "strings").
// Does not own: the instrument panel choice (InstrumentViewRules) or the drawing of the staff.
// Tests: TabForge.Avalonia.Tests.TrackInstrumentRulesTests.
public static class TrackInstrumentRules
{
    /// <summary>True when the tuning is the octave-spaced (or all-fourths) pitch ladder a piano, synth or pad is given so every pitch has a TAB number: not a real fretboard.</summary>
    public static bool IsPitchOnlyTuning(IReadOnlyList<int> tuning)
    {
        if (tuning.Count < 4) return false;
        if (tuning.SequenceEqual(TrackSetup.KeysStrings)) return true;
        var step = tuning[0] - tuning[1];
        return step == 12 && Enumerable.Range(1, tuning.Count - 1).All(i => tuning[i - 1] - tuning[i] == 12);
    }

    /// <summary>True when the score draws a TAB staff for the track: fretted kinds, and the drum lines. Keys and other keyboard-like tracks (piano, synth, pad) get the standard staff only.</summary>
    public static bool HasTab(TrackModel track) =>
        track.Kind is TrackKind.Guitar or TrackKind.Bass or TrackKind.Drums ||
        (track.Kind == TrackKind.Other && InstrumentViewRules.For(track) == InstrumentViewKind.Fretboard);

    /// <summary>The notation a track really shows: a track without a TAB staff shows the standard staff whatever the song's mode says.</summary>
    public static NotationMode Effective(NotationMode mode, TrackModel? track) =>
        track is not null && !track.IsAudio && !HasTab(track) ? NotationMode.StaffOnly : mode;

    /// <summary>Load migration: a guitar or bass track whose strings are the pitch-only ladder (a piano or synth that an import or an old build filed as fretted) becomes a keys track. Returns the count changed.</summary>
    public static int RepairNonFrettedKinds(SongProject project)
    {
        var changed = 0;
        foreach (var track in project.Tracks)
        {
            if (track.Kind is not (TrackKind.Guitar or TrackKind.Bass) || !IsPitchOnlyTuning(track.StringTunings)) continue;
            if (track.MidiProgram is >= 32 and <= 39 || track.Name.Contains("bass", StringComparison.OrdinalIgnoreCase)) continue;
            track.Kind = TrackKind.Keys;
            track.InstrumentName = InstrumentNaming.WithoutStringCount(track.InstrumentName ?? "");
            track.Capo = 0;
            changed++;
        }
        return changed;
    }
}
