using TabForge.Models;

namespace TabForge.Services;

/// <summary>
/// Per-track setup rules shared by the Track properties / Add track window and the track row's Instrument button:
/// which kind an instrument belongs to, the default strings of each kind, and the capo.
/// </summary>
public static class TrackSetup
{
    /// <summary>High to low, like <see cref="TrackModel.StringTunings"/>. Same values as the Track menu's quick add.</summary>
    public static readonly IReadOnlyList<int> GuitarStrings = new[] { 64, 59, 55, 50, 45, 40 };
    public static readonly IReadOnlyList<int> BassStrings = new[] { 43, 38, 33, 28 };
    public static readonly IReadOnlyList<int> KeysStrings = new[] { 96, 91, 86, 81, 76, 71 };

    /// <summary>The default strings of a fretted or keys kind; null for drums (their lines come from the drum notation preset).</summary>
    public static List<int>? DefaultStrings(TrackKind kind) => kind switch
    {
        TrackKind.Guitar => GuitarStrings.ToList(),
        TrackKind.Bass => BassStrings.ToList(),
        TrackKind.Keys => KeysStrings.ToList(),
        _ => null,
    };

    /// <summary>The kind an instrument name belongs to; other sounds (violin, brass, synths...) keep <paramref name="fallback"/>.</summary>
    public static TrackKind KindOf(string instrument, int midiChannel, TrackKind fallback)
    {
        instrument = InstrumentNaming.WithoutStringCount(instrument ?? ""); // "(7 strings)" is not the Strings family
        if (midiChannel == 9 || instrument.Contains("Drum", StringComparison.OrdinalIgnoreCase)) return TrackKind.Drums;
        if (instrument.Contains("Bass", StringComparison.OrdinalIgnoreCase)) return TrackKind.Bass;
        if (instrument.Contains("Piano", StringComparison.OrdinalIgnoreCase) || instrument.Contains("Organ", StringComparison.OrdinalIgnoreCase) ||
            instrument.Contains("Strings", StringComparison.OrdinalIgnoreCase)) return TrackKind.Keys;
        if (instrument.Contains("Guitar", StringComparison.OrdinalIgnoreCase)) return TrackKind.Guitar;
        return fallback;
    }

    /// <summary>True when any bar of the track (either voice) has a note.</summary>
    public static bool HasNotes(TrackModel track) =>
        track.Measures.Any(m => m.Cells.Concat(m.Voice2Cells).Any(c => c.Notes.Count > 0));

    /// <summary>
    /// Choosing an instrument of another kind (a bass or keys sound on a guitar track and so on) gives an EMPTY track that
    /// kind and its default strings. A track with notes is never restrung or re-kinded by an instrument change: its notes
    /// would move. Drum kits are left to the drum notation preset. Returns true when the kind and strings changed.
    /// </summary>
    public static bool AdoptInstrumentKind(TrackModel track, string instrument, int midiChannel)
    {
        if (track.Kind == TrackKind.Drums || HasNotes(track)) return false;
        var kind = KindOf(instrument, midiChannel, track.Kind);
        if (kind == track.Kind || DefaultStrings(kind) is not { } strings) return false;
        track.Kind = kind;
        track.StringTunings = strings;
        track.Capo = 0;
        return true;
    }

    /// <summary>
    /// Sets the capo. Fret numbers are relative to the capo, so the written frets stay and the sounding pitch of every
    /// note moves with it (sounding pitch = open string + capo + fret). Drum tracks have no capo.
    /// </summary>
    public static void SetCapo(TrackModel track, int capo)
    {
        capo = Math.Max(0, capo);
        var delta = capo - Math.Max(0, track.Capo);
        if (delta != 0 && track.Kind != TrackKind.Drums && track.MidiChannel != 9)
        {
            foreach (var measure in track.Measures)
                foreach (var cell in measure.Cells.Concat(measure.Voice2Cells))
                    foreach (var note in cell.Notes)
                    {
                        if (note.StringIndex < 0) continue;
                        note.MidiValue = Math.Clamp(note.MidiValue + delta, 0, 127);
                        if (note.SlideTargetMidi > 0) note.SlideTargetMidi = Math.Clamp(note.SlideTargetMidi + delta, 0, 127);
                        if (note.TrillTargetMidi > 0) note.TrillTargetMidi = Math.Clamp(note.TrillTargetMidi + delta, 0, 127);
                    }
        }
        track.Capo = capo;
    }
}
