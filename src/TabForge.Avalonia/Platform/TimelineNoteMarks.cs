using System;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Platform;

// Owns: the drawing of the two arrangement lane modes: the note marks of "Show the notes in each track lane" (one mark per pitched note or
//   drum hit, sized as the WPF miniature) and the one continuous line of "Show one continuous line per track lane".
// Does not own: the mode state and the lane cells (TimelineView), the menu rows (MainWindowArrangementModes), what a bar holds
//   (TimelineActivityRules in Core).
// Tests: TabForge.Avalonia.Tests.MainWindowMenuTests (the rows); the lane drawing is checked in the running app.
/// <summary>The note marks and the continuous line of the arrangement lanes, drawn through <see cref="IDrawContext"/>.</summary>
internal static class TimelineNoteMarks
{
    /// <summary>Opacity of a bar's cell in the notes mode, as a share of the track colour.</summary>
    public const double NotesCellOpacity = 0.30;
    /// <summary>Opacity of a note mark or drum hit in the notes mode.</summary>
    public const double NoteMarkOpacity = 0.95;
    /// <summary>Opacity of the continuous line in the continuous mode.</summary>
    public const double ContinuousLineOpacity = 0.96;
    /// <summary>Thickness of the continuous line; its ends are round.</summary>
    public const double ContinuousLineThickness = 9;

    private const int DrumHitMinHeight = 6;
    private const int DrumHitMaxExtraHeight = 8;
    private const int DrumVelocityPerExtraPixel = 14;
    private const double DrumHitMaxWidth = 4;
    private const double DrumHitMinWidth = 1.5;
    private const double DrumHitBottomInset = 4;
    private const int PitchedNoteBaseHeight = 5;
    private const int PitchedNoteHeightPerNote = 3;
    private const double PitchedNoteVerticalInset = 8;
    private const double PitchedNoteMinWidth = 1.5;
    private const double PitchedNoteGap = 1.5;
    private const double PitchedNoteRadius = 1.5;

    /// <summary>Draws the note marks of one bar's cell (cellX, cellY, cellWidth by cellHeight) in <paramref name="markColour"/>.</summary>
    public static void DrawNotes(IDrawContext d, TimelineActivityRules.Activity activity, bool drums, double cellX, double cellY, double cellWidth,
        double cellHeight, UiColor markColour)
    {
        var slotWidth = cellWidth / Math.Max(1, activity.CellCount);
        foreach (var item in activity.MiniatureEvents)
        {
            var x = cellX + item.CellIndex * slotWidth;
            if (drums)
            {
                foreach (var velocity in item.DrumVelocities)
                {
                    var height = DrumHitMinHeight + Math.Min(DrumHitMaxExtraHeight, velocity / DrumVelocityPerExtraPixel);
                    d.Rect(markColour, null, x + 1, cellY + cellHeight - DrumHitBottomInset - height,
                        Math.Max(DrumHitMinWidth, Math.Min(DrumHitMaxWidth, slotWidth - 2)), height);
                }
            }
            else
            {
                var width = Math.Max(PitchedNoteMinWidth, item.ConsumedSlots * slotWidth - PitchedNoteGap);
                var height = Math.Min(cellHeight - PitchedNoteVerticalInset, PitchedNoteBaseHeight + item.NoteCount * PitchedNoteHeightPerNote);
                d.Rect(markColour, null, x + 1, cellY + (cellHeight - height) / 2, width, height, PitchedNoteRadius);
            }
        }
    }

    /// <summary>Draws one continuous line from <paramref name="left"/> to <paramref name="right"/>, centred in the row at <paramref name="rowTop"/>.</summary>
    public static void DrawContinuousRun(IDrawContext d, double left, double right, double rowTop, double rowHeight, UiColor colour)
    {
        var y = rowTop + (rowHeight - ContinuousLineThickness) / 2;
        d.Rect(colour, null, left, y, Math.Max(1, right - left), ContinuousLineThickness, ContinuousLineThickness / 2);
    }
}
