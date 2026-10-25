using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: a dead note at the score cursor is lit as a dead note (the fretboard draws it as a ring with an X) and lights no keyboard key.
/// Does not own: the drawing of the mark (InstrumentPanelView.Labels, checked by eye in the parity captures) or the playback of dead notes.
/// Tests: TabForge.Avalonia.Tests.FretboardDeadNoteTests.
/// </summary>
public sealed class FretboardDeadNoteTests
{
    private const int DeadString = 0;
    private const int DeadFret = 3;

    [AvaloniaFact]
    public void DeadNoteAtTheCursorIsLitAsDeadAndSoundsNoKey()
    {
        var track = DemoSongFactory.Create().Tracks[0];
        var cell = track.Measures[0].Cells[0];
        cell.IsRest = false;
        cell.Notes.Clear();
        cell.Notes.Add(new TabNote { StringIndex = DeadString, Fret = DeadFret, Dead = true });

        var lit = InstrumentLitNotes.At(track, 0, 0);

        var dead = Assert.Single(lit);
        Assert.True(dead.Dead);
        Assert.Equal((DeadString, DeadFret), (dead.StringIndex, dead.Fret));
        var keyboard = new InstrumentPanelView { Kind = InstrumentPanelKind.Keyboard };
        keyboard.Show(track, lit);
        Assert.DoesNotContain(dead.Midi, keyboard.LitMidis());
    }
}
