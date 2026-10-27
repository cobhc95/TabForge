using System;
using System.Linq;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Views.Score;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the staff notehead box of the edit cursor: it sits on the notehead of the cursor's own string, and a cursor on a string with no note in its
//   beat draws no notehead box (the beat outline and the TAB box stay).
// Does not own: the cursor geometry (ScoreRenderer.Cursor), the TAB box (ScoreCursorBox.DrawTab), the zoom and hover (ScoreZoomAndHoverTests).
// Tests: this file.
public class ScoreCursorNoteboxTests
{
    /// <summary>The width of the staff notehead box ScoreCursorBox.DrawStaff draws (18 DIPs).</summary>
    private const double NoteboxWidth = 18;

    [AvaloniaFact]
    public void CursorOnAnEmptyStringOfANotedBeatDrawsNoNoteheadBox()
    {
        var song = FullDemoSongFactory.Create();
        var probe = new ScoreGeometryProbe(song, new AvaloniaDrawContext());
        probe.Notation = NotationMode.TabAndStaff;
        var track = song.Tracks[0];
        var strings = track.StringTunings.Count;
        var (bar, cell, occupied, empty) = FindBeatWithEmptyString(track, strings);

        probe.Cursor = (bar, cell, empty);
        var withEmptyString = NoteboxCount(probe);
        probe.Cursor = (bar, cell, occupied);
        var withOccupiedString = NoteboxCount(probe);

        Assert.Equal(withEmptyString + 1, withOccupiedString);
    }

    /// <summary>A beat with notes on some strings and none on at least one other: its bar, cell, a noted string and an empty string.</summary>
    private static (int Bar, int Cell, int Occupied, int Empty) FindBeatWithEmptyString(TrackModel track, int strings)
    {
        for (var bar = 0; bar < track.Measures.Count; bar++)
        {
            var cells = track.Measures[bar].Cells;
            for (var cell = 0; cell < cells.Count; cell++)
            {
                var sounding = cells[cell].Notes.Select(note => note.StringIndex).ToArray();
                if (sounding.Length == 0 || sounding.Length >= strings) continue;
                var empty = Enumerable.Range(0, strings).First(index => !sounding.Contains(index));
                return (bar, cell, sounding[0], empty);
            }
        }
        throw new InvalidOperationException("The demo song has no beat with notes on some strings and none on another.");
    }

    /// <summary>How many rectangles of notehead width the page draws (the staff notehead box is one of them).</summary>
    private static int NoteboxCount(ScoreGeometryProbe probe)
    {
        var recorder = new TrackRowSelectionTests.RectRecorder();
        probe.DrawPage(recorder, int.MaxValue, withPlayhead: false);
        return recorder.Rects.Count(rect => rect.Width == NoteboxWidth);
    }
}
