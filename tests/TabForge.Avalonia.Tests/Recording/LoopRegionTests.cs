using TabForge.Documents;
using TabForge.Models;
using TabForge.Platform.Theme;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the pure loop model rules: the loop region apart from the white selection, the stepped Escape and the undo of a loop clear, the loop frozen
//   while it plays, the ruler drag rules, the bar span every bar command uses (SelectionBars) and the loop and marquee theme tokens.
// Does not own: the views (LoopModelTests, SelectionLoopLinkTests).
// Tests: SelectionModel.Loop, DocumentEdits.Escape/EditLoop, LoopRulerRules, SelectionBars.
public class LoopRegionTests
{
    private static SongDocument Song() => new(() => null) { Project = DemoSongFactory.Create() };

    [Fact]
    public void EscapeClearsWhiteFirstThenTheLoopAsOneUndoStep()
    {
        var song = Song();
        var model = song.Selection;
        model.SetRange(0, 2, 3, SelectionOrigin.Editor);
        model.SetLoop(2, 3);   // ruler-made (a score selection no longer makes a loop)
        model.SetLoopRepeat(true);
        Assert.Equal(((2, 3), true), (model.LoopBars, model.LoopRepeat));
        var steps = song.Undo.UndoCount;
        Assert.Equal(EscapeResult.White, DocumentEdits.Escape(song));
        Assert.Equal(steps, song.Undo.UndoCount);   // a white clear is no undo step
        Assert.True(model.HasLoop);
        Assert.Equal(EscapeResult.Loop, DocumentEdits.Escape(song));
        Assert.Equal((false, true, steps + 1), (model.HasLoop, model.LoopRepeat, song.Undo.UndoCount));   // clearing never switches Loop off
        Assert.Equal(EscapeResult.Nothing, DocumentEdits.Escape(song));
        DocumentEdits.Undo(song);
        Assert.Equal(((2, 3), true), (model.LoopBars, model.LoopRepeat));   // one undo brings the region and Loop back
        DocumentEdits.Redo(song);
        Assert.False(model.HasLoop);
    }

    [Fact]
    public void AScoreSelectionSetsTheLoopRangeButNeverTurnsLoopOn()
    {
        var model = new SelectionModel();
        model.SetRange(0, 1, 2, SelectionOrigin.Editor);
        Assert.True(model.LoopScoreSelection(playing: false));
        Assert.True(model.HasLoop);
        Assert.False(model.LoopRepeat);
        model.SetLoop(5, 6);
        model.SetRange(0, 8, 9, SelectionOrigin.Editor);
        model.LoopScoreSelection(playing: false);
        Assert.Equal((8, 9), model.LoopBars);
        Assert.False(model.LoopRepeat);
    }

    [Fact]
    public void LoopButtonTogglesRepeatAndKeepsTheRegion()
    {
        var model = new SelectionModel();
        model.SetLoop(4, 6);
        model.SetLoopRepeat(true);
        model.SetLoopRepeat(false);
        Assert.Equal(((4, 6), false), (model.LoopBars, model.LoopRepeat));
        model.ClearNext();   // white clears never touch it
        Assert.True(model.HasLoop);
    }

    [Fact]
    public void ARulerAdjustIsOneUndoStepAndBarEditsMoveTheLoop()
    {
        var song = Song();
        Assert.True(DocumentEdits.EditLoop(song, m => m.SetLoop(2, 4), "Set loop"));
        Assert.True(DocumentEdits.EditLoop(song, m => m.SetLoop(3, 5), "Adjust loop"));
        DocumentEdits.Undo(song);
        Assert.Equal((2, 4), song.Selection.LoopBars);
        song.Selection.Remap(SelectionModel.InsertMap(20, 0, 2), 22);
        Assert.Equal((4, 6), song.Selection.LoopBars);
    }

    [Theory]
    [InlineData(LoopGrip.None, 3, 6, 3, 6)]
    [InlineData(LoopGrip.StartEdge, 0, 1, 1, 8)]
    [InlineData(LoopGrip.EndEdge, 0, 12, 5, 12)]
    [InlineData(LoopGrip.EndEdge, 0, 2, 2, 5)]
    [InlineData(LoopGrip.Inside, 6, 8, 7, 10)]
    [InlineData(LoopGrip.Inside, 6, 30, 15, 18)]
    public void RulerDragsMakeResizeAndMoveTheLoopInWholeBars(LoopGrip grip, int press, int bar, int start, int end) =>
        Assert.Equal((start, end), LoopRulerRules.Drag(grip, press, bar, (5, 8), 19));

    [Fact]
    public void RulerPressGrabsAnEdgeTheInsideOrNothing()
    {
        Assert.Equal(LoopGrip.StartEdge, LoopRulerRules.GripAt(102, 100, 200));
        Assert.Equal(LoopGrip.EndEdge, LoopRulerRules.GripAt(197, 100, 200));
        Assert.Equal(LoopGrip.Inside, LoopRulerRules.GripAt(150, 100, 200));
        Assert.Equal(LoopGrip.None, LoopRulerRules.GripAt(300, 100, 200));
        Assert.Equal(LoopGrip.None, LoopRulerRules.GripAt(150, null, null));
        Assert.Equal((5, 12), LoopRulerRules.Drag(LoopGrip.None, 9, 12, (5, 8), 19, extend: true));   // Shift: from the far end of the loop to the pointer
    }

    [Fact]
    public void BarCommandsUseTheLoopElseTheScoreBarsElseTheSelectedClipsOnTheirTracks()
    {
        var song = Song();
        var model = song.Selection;
        Assert.Null(SelectionBars.For(model, song.Project));
        var track = song.Project.Tracks[1];
        var clip = new AudioClip { File = "a.wav", StartSec = 0, SourceLengthSec = 0.01 };
        track.AudioClips.Add(clip);
        model.SetClips(new[] { new TimelineClipSelection(track, clip) });
        var clips = SelectionBars.For(model, song.Project)!;
        Assert.Equal((0, 0, false), (clips.Start, clips.End, clips.FromLoop));
        Assert.Equal(new[] { 1 }, clips.Tracks);
        model.SetRange(0, 2, 3, SelectionOrigin.Editor);
        Assert.Equal((2, 3, null), (SelectionBars.For(model, song.Project)!.Start, SelectionBars.For(model, song.Project)!.End, SelectionBars.For(model, song.Project)!.Tracks));
        model.SetLoop(6, 9);
        var loop = SelectionBars.For(model, song.Project)!;
        Assert.Equal((2, 3, false, null), (loop.Start, loop.End, loop.FromLoop, loop.Tracks));   // 25 Oct: the loop is never a bar-command target
    }

    [Theory]
    [InlineData("LoopEdge")]
    [InlineData("LoopRegion")]
    [InlineData("MarqueeFill")]
    [InlineData("MarqueeBorder")]
    public void LoopAndMarqueeTokensHaveALightAndADarkColour(string token) =>
        Assert.NotEqual(Palette.Get(token, false), Palette.Get(token, true));
}
