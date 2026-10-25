using TabForge.Models;

namespace TabForge.Presenters;

// Owns: which notes an instrument panel lights for a score cursor (the sounding notes of one beat cell), as string, fret and MIDI note.
// Does not own: the sounding-note timeline during playback (InstrumentVisualizer), drawing or the cursor itself.
// Tests: TabForge.Avalonia.Tests.InstrumentPanelViewTests.
public static class InstrumentLitNotes
{
    /// <summary>A lit note: its string, fret and MIDI note; <paramref name="Dead"/> marks a dead note, drawn as an X and sounding no pitch.</summary>
    public readonly record struct Lit(int StringIndex, int Fret, int Midi, bool Dead = false);

    /// <summary>The notes of the beat cell at the cursor, dead notes included (marked <see cref="Lit.Dead"/>); none for a missing bar or cell, or a rest.</summary>
    public static IReadOnlyList<Lit> At(TrackModel track, int bar, int cell)
    {
        if (bar < 0 || bar >= track.Measures.Count) return Array.Empty<Lit>();
        var cells = track.Measures[bar].Cells;
        if (cell < 0 || cell >= cells.Count || cells[cell].IsRest) return Array.Empty<Lit>();
        var lit = new List<Lit>();
        foreach (var note in cells[cell].Notes)
            lit.Add(new Lit(note.StringIndex, note.Fret, note.MidiValue > 0 ? note.MidiValue : track.NoteMidi(note.StringIndex, note.Fret), note.Dead));
        return lit;
    }
}
