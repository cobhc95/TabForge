using System.Windows;
using System.Windows.Media;
using TabForge.Models;
using RenderDraw = TabForge.Visualization.Draw;

namespace TabForge.Views.Score;

// ScoreCursorBox: the edit-cursor boxes (a see-through yellow highlight on the TAB lane, grey behind a staff notehead); both are hidden while playing.
// Owns: box colours, the "is this note under the cursor" test and the box drawing.
// Does not own: cursor geometry (ScoreRenderer.Cursor) or digit drawing.
// Tests: TestScoreCursorBox.
internal static class ScoreCursorBox
{
    internal static readonly Color Yellow = Color.FromRgb(0xFF, 0xD8, 0x3A);
    // A highlight, not a block: the fret number and the string line stay readable through it in both themes.
    internal static readonly Brush Highlight = RenderDraw.Solid(Yellow, 0.35);
    private static readonly Color GreyOnDark = Color.FromRgb(0x55, 0x60, 0x6E);
    private static readonly Color GreyOnLight = Color.FromRgb(0xC4, 0xCA, 0xD3);

    /// <summary>True when the cursor box is drawn at all: stopped and the cursor not hidden.</summary>
    internal static bool Shown(IScoreRenderHost host) => !host.PlaybackActive && !host.HideCursor;

    /// <summary>True when the fret of this note sits inside the yellow box.</summary>
    internal static bool OnNote(IScoreRenderHost host, int measure, int cell, int stringIndex) =>
        Shown(host) && measure == host.SelectedMeasure && cell == host.SelectedCell && stringIndex == host.SelectedString;

    internal static void DrawTab(DrawingContext dc, Rect box) =>
        dc.DrawRoundedRectangle(Highlight, null, box, 2, 2);

    /// <summary>Grey box behind the selected notehead; the ink stays readable on it in both themes.</summary>
    internal static void DrawStaff(DrawingContext dc, IScoreRenderHost host, StaffNotationMeasureLayout layout, int measure, Color ink)
    {
        if (!Shown(host) || measure != host.SelectedMeasure || layout.IsSecondVoice) return;
        var beat = layout.BeatForCell(host.SelectedCell);
        if (beat is null || beat.Notes.Count == 0) return;
        var note = beat.Notes.FirstOrDefault(n => n.Source.StringIndex == host.SelectedString) ?? beat.Notes[0];
        var lightInk = ink.R + ink.G + ink.B > 384;
        var half = host.StaffGap * 0.5 + 2;
        dc.DrawRoundedRectangle(RenderDraw.Solid(lightInk ? GreyOnDark : GreyOnLight, 1), null,
            new Rect(note.X - 9, note.Y - half, 18, half * 2), 2, 2);
    }
}
