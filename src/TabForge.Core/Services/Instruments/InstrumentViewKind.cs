using TabForge.Models;

namespace TabForge.Services;

/// <summary>What the instrument panel draws for a track.</summary>
public enum InstrumentViewKind { Fretboard, Keyboard, DrumMap }

// Owns: the one rule that maps a track (kind, instrument name, program) to the panel it shows under "Match the instrument".
// Does not own: the panel drawing, the manual view choice (InstrumentViews) or the settings.
// Tests: TabForge.Avalonia.Tests.InstrumentViewKindTests. Table: Docs/Audits/Instrument-view-map.md.
public static class InstrumentViewRules
{
    /// <summary>Drum track -> drum map. A name the track setup files under guitar, bass or keys (TrackSetup.KindOf) follows the track's own kind
    /// (its strings or keys; a track with notes keeps its kind when the sound changes). Any other instrument follows its name, then its GM program.</summary>
    public static InstrumentViewKind For(TrackModel track)
    {
        if (track.IsDrumTrack) return InstrumentViewKind.DrumMap;
        // A piano, synth or pad filed as a guitar or bass (octave-ladder strings, not a bass program) shows the keyboard; its Kind and TAB stay.
        if (track.Kind is TrackKind.Guitar or TrackKind.Bass && TrackInstrumentRules.IsPitchOnlyTuning(track.StringTunings) &&
            track.MidiProgram is not (>= 32 and <= 39) && !(track.Name ?? "").Contains("bass", StringComparison.OrdinalIgnoreCase)) return InstrumentViewKind.Keyboard;
        var name = InstrumentNaming.WithoutStringCount(track.InstrumentName ?? "");
        // Picking a kit converts the track to drums, so a pitched kind with a kit name is stale data: the kind wins. Only an unclassified
        // track (Kind Other, e.g. an import that never set Drums) goes by name: it is the one case where the name is the only drum evidence.
        if (track.Kind == TrackKind.Other && (name.Contains("Drum", StringComparison.OrdinalIgnoreCase) || name.Contains("Percussion", StringComparison.OrdinalIgnoreCase))) return InstrumentViewKind.DrumMap;
        var named = TrackSetup.KindOf(name, track.MidiChannel, TrackKind.Other);
        if (named != TrackKind.Other && !name.Contains("Bassoon", StringComparison.OrdinalIgnoreCase))
        {
            var kind = track.Kind is TrackKind.Guitar or TrackKind.Bass or TrackKind.Keys ? track.Kind : named;
            return kind == TrackKind.Keys ? InstrumentViewKind.Keyboard : InstrumentViewKind.Fretboard;
        }
        if (InstrumentCatalog.Find(name) is { } entry) return ForProgram(entry.Program);
        if (IsFrettedName(name)) return InstrumentViewKind.Fretboard;
        if (name.Length > 0) return track.Kind is TrackKind.Guitar or TrackKind.Bass ? InstrumentViewKind.Fretboard : InstrumentViewKind.Keyboard;
        return track.Kind switch
        {
            TrackKind.Guitar or TrackKind.Bass => InstrumentViewKind.Fretboard,
            TrackKind.Keys => InstrumentViewKind.Keyboard,
            _ => ForProgram(track.MidiProgram),
        };
    }

    /// <summary>The view of a General MIDI program (0-127): guitars, basses, violin family, sitar, banjo, shamisen and fiddle have a fretboard; the rest a keyboard.</summary>
    // Koto (program 107) stays on the keyboard view on purpose: it is a fretless zither, so a fretboard would show frets it does not have.
    public static InstrumentViewKind ForProgram(int program) =>
        program is >= 24 and <= 43 or 104 or 105 or 106 or 110 ? InstrumentViewKind.Fretboard : InstrumentViewKind.Keyboard;

    private static bool IsFrettedName(string name) =>
        Has(name, "Guitar") || Has(name, "Ukulele") || Has(name, "Banjo") || Has(name, "Mandolin") || Has(name, "Sitar") || Has(name, "Shamisen") ||
        Has(name, "Violin") || Has(name, "Viola") || Has(name, "Cello") || Has(name, "Contrabass") || Has(name, "Fiddle") || Has(name, "Lute") || Has(name, "Bouzouki");

    private static bool Has(string name, string word) => name.Contains(word, StringComparison.OrdinalIgnoreCase);
}
