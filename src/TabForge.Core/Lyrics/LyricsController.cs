using TabForge.Documents;
using TabForge.Models;

namespace TabForge.Lyrics;

/// <summary>One bar of the lyric panel: its zero-based index, its section name and its syllables as one line of text.</summary>
public readonly record struct LyricBarRow(int BarIndex, string SectionName, string Text);

// Owns: the lyric rows of a song (one per bar of its first notation track: the bar's section name and its syllables, the beat lyrics
//       joined by spaces) and the one edit that replaces a bar's syllables, run through DocumentEdits.Run so one commit is one undo step.
// Does not own: the panel (LyricsPaneView), the playhead (PlaybackTransport), the lyric storage (TabCell.Lyrics) or hyphen handling, which
//       the model does not do: a hyphen is kept as typed inside its syllable.
// Tests: TabForge.Avalonia.Tests.LyricsPaneTests.
public static class LyricsController
{
    /// <summary>The lyric rows of the song's first notation track; empty when the song has no notation track.</summary>
    public static IReadOnlyList<LyricBarRow> RowsFor(SongDocument document)
    {
        var track = document.Project.FirstNotationTrack;
        if (track is null) return Array.Empty<LyricBarRow>();
        var rows = new LyricBarRow[track.Measures.Count];
        for (var bar = 0; bar < rows.Length; bar++)
        {
            var measure = track.Measures[bar];
            var syllables = measure.Cells.Select(cell => cell.Lyrics).Where(text => !string.IsNullOrWhiteSpace(text));
            rows[bar] = new LyricBarRow(bar, measure.SectionName, string.Join(" ", syllables));
        }
        return rows;
    }

    /// <summary>
    /// Replaces the syllables of one bar. The text splits on spaces; syllable i goes to the i-th beat that carries notes (or the i-th beat when the
    /// bar has none), and any syllables past the last such beat join it. Returns false, with no undo step, when the text changes nothing.
    /// </summary>
    public static bool SetBarText(SongDocument document, int bar, string text)
    {
        var syllables = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return DocumentEdits.Run(document, project =>
        {
            var track = project.FirstNotationTrack ?? throw new InvalidOperationException("The song has no notation track to hold lyrics.");
            if (bar < 0 || bar >= track.Measures.Count) throw new ArgumentOutOfRangeException(nameof(bar), bar, "The bar is not in the song.");
            var cells = track.Measures[bar].Cells;
            var slots = BeatSlots(cells);
            if (slots.Count == 0) return false;   // a bar with no beats cannot hold syllables
            var lyrics = new string?[cells.Count];
            for (var i = 0; i < syllables.Length; i++)
            {
                var slot = slots[Math.Min(i, slots.Count - 1)];
                lyrics[slot] = lyrics[slot] is null ? syllables[i] : lyrics[slot] + " " + syllables[i];
            }
            var changed = false;
            for (var beat = 0; beat < cells.Count; beat++)
            {
                var next = lyrics[beat] ?? "";
                if (cells[beat].Lyrics == next) continue;
                cells[beat].Lyrics = next;
                changed = true;
            }
            return changed;
        }, label: "Edit lyrics").Changed;
    }

    /// <summary>The indexes of the beats that carry notes; every beat index when none does.</summary>
    private static List<int> BeatSlots(IReadOnlyList<TabCell> cells)
    {
        var slots = new List<int>();
        for (var beat = 0; beat < cells.Count; beat++)
            if (!cells[beat].IsRest && cells[beat].Notes.Count > 0) slots.Add(beat);
        if (slots.Count == 0) for (var beat = 0; beat < cells.Count; beat++) slots.Add(beat);
        return slots;
    }
}
