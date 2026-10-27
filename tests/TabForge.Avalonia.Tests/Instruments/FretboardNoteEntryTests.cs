using System.Linq;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the fretboard click that writes a note at the score cursor and a second click on the same note removes it, and the stop while the song plays.
/// Does not own: the note rules (Core ScoreEditCommands) or the hover ghost (InstrumentPanelView).
/// Tests: FretboardNoteEntry.
/// </summary>
public class FretboardNoteEntryTests
{
    // String 3 is index 2; fret 5 is the note the test writes and removes.
    private const int TestString = 2;
    private const int TestFret = 5;

    [AvaloniaFact]
    public void ClickWritesThenRemovesTheNoteAtTheCursor()
    {
        var score = new ScorePreviewView();
        score.Editor.SetPosition(0, 0, TestString);
        int CountAtTest() => score.Editor.Effects.CurrentCell()?.Notes.Count(n => n.StringIndex == TestString && n.Fret == TestFret) ?? 0;
        Assert.Equal(0, CountAtTest());

        Assert.True(FretboardNoteEntry.Toggle(score.Editor, TestString, TestFret));
        Assert.Equal(1, CountAtTest());

        Assert.True(FretboardNoteEntry.Toggle(score.Editor, TestString, TestFret));
        Assert.Equal(0, CountAtTest());
    }
}
