using TabForge.Models;
using TabForge.Platform;
using TabForge.Platform.Theme;

namespace TabForge.Views.Score;

// ScoreCursorBox: the edit-cursor boxes (a see-through yellow highlight on the TAB lane, grey behind a staff notehead); both are hidden while playing.
// Owns: box colours, the "is this note under the cursor" test and the box drawing.
// Does not own: cursor geometry (ScoreRenderer.Cursor) or digit drawing.
// Tests: no dedicated test class.
internal static class ScoreCursorBox
{
    // A highlight, not a block (35% of the theme yellow): the fret number and the string line stay readable through it in both themes.
    internal static UiColor Highlight(IScoreRenderHost host) => Palette.Get("ScoreCursorTabBox", light: !host.Appearance.DarkPaper) with { A = 89 };

    /// <summary>True when the cursor box is drawn at all: stopped and the cursor not hidden.</summary>
    internal static bool Shown(IScoreRenderHost host) => !host.PlaybackActive && !host.HideCursor;

    /// <summary>True when the fret of this note sits under the caret (the old single-note test).</summary>
    internal static bool OnNote(IScoreRenderHost host, int measure, int cell, int stringIndex) =>
        Shown(host) && measure == host.SelectedMeasure && cell == host.SelectedCell && stringIndex == host.SelectedString;

    /// <summary>True when the fret of this note sits inside the yellow box: the selected range, or the caret when nothing is selected.</summary>
    internal static bool Selected(IScoreRenderHost host, int measure, int cell, int stringIndex)
    {
        if (!Shown(host)) return false;
        if (!host.HasSelection) return OnNote(host, measure, cell, stringIndex);
        var (m1, c1, m2, c2) = host.SelectionRange();
        return ScoreSelectionGeometry.Selected(m1, c1, m2, c2, measure, cell);
    }

    internal static void DrawTab(IDrawContext d, IScoreRenderHost host, UiRect box) => d.Rect(Highlight(host), null, box.X, box.Y, box.Width, box.Height, 2);

    /// <summary>
    /// Grey box behind the notehead on the cursor's own string; the ink stays readable on it in both themes. A cursor on a string with no note in
    /// its beat has no notehead box (the beat outline and the TAB box stay): nothing falls back to another note.
    /// With a selection every note of every selected beat gets the yellow box, on every string.
    /// </summary>
    internal static void DrawStaff(IDrawContext d, IScoreRenderHost host, StaffNotationMeasureLayout layout, int measure, UiColor ink)
    {
        if (!Shown(host)) return;
        var half = host.StaffGap * 0.5 + 2;
        if (host.HasSelection)
        {
            if ((host.ActiveVoiceIndex > 0) != layout.IsSecondVoice) return;   // the range belongs to the active voice
            foreach (var beat in layout.Beats)
            {
                if (!beat.CellIndices.Append(beat.CellIndex).Any(cellIndex => Selected(host, measure, cellIndex, 0))) continue;
                foreach (var note in beat.Notes) d.Rect(Highlight(host), null, note.X - 9, note.Y - half, 18, half * 2, 2);
            }
            return;
        }
        if (measure != host.SelectedMeasure || layout.IsSecondVoice) return;
        var caret = layout.BeatForCell(host.SelectedCell);
        if (caret is null) return;
        var noteAtCaret = caret.Notes.FirstOrDefault(n => n.Source.StringIndex == host.SelectedString);
        if (noteAtCaret is null) return;
        var lightInk = ink.R + ink.G + ink.B > 384;
        d.Rect(Palette.Get("ScoreCursorNoteBox", light: !lightInk), null, noteAtCaret.X - 9, noteAtCaret.Y - half, 18, half * 2, 2);
    }
}
