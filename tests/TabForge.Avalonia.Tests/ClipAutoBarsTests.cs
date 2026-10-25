using System.Linq;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: a clip that ends after the song grows it by empty bars in the clip edit's undo step, and the bars it added go again when nothing reaches them (E1 to E6),
//   and the drag of a clip past the end (ghost bars while it drags, the drop grows the song, the drag back shrinks it).
// Does not own: the bar rules (BarGrid, TrailingBars, SongExtent) or the drag fold (ClipDragFoldTests).
// Tests: this class.
public sealed class ClipAutoBarsTests
{
    private const int Bars = 100;

    private static (SongDocument Song, TrackModel Track, AudioClip Clip, double BarSec) Make()
    {
        var song = new SongDocument(() => null) { Project = SongDocument.BlankProject() };
        var track = NewTrackFactory.CreateTrack(song.Project, TrackKind.Audio);
        song.Project.Tracks.Add(track);
        BarGrid.AppendBars(song.Project, Bars - BarRangeEditor.MaxMeasures(song.Project));
        var barSec = SongExtent.Measure(song.Project).BarSec;
        var clip = new AudioClip { File = "take.wav", Name = "take", StartSec = 0, SourceLengthSec = barSec, FileLengthSec = barSec };
        track.AudioClips.Add(clip);
        song.MarkClean();
        return (song, track, clip, barSec);
    }

    private static int BarCount(SongDocument song) => BarRangeEditor.MaxMeasures(song.Project);

    private static AutoFitResult Move(SongDocument song, AudioClip clip, double endSec)
    {
        var fit = default(AutoFitResult);
        DocumentEdits.Run(song, p => { clip.StartSec = endSec - clip.SourceLengthSec; fit = ClipAutoBars.Fit(p); return true; });
        return fit;
    }

    [Fact]
    public void AClipEndingPastTheSongGrowsItToFitAndOneUndoRestoresBoth()
    {
        var (song, _, clip, barSec) = Make();
        Assert.Equal(Bars, BarCount(song));
        var fit = Move(song, clip, 104.5 * barSec);
        Assert.Equal(5, fit.Added);
        Assert.Equal(Bars + 5, BarCount(song));
        DocumentEdits.Undo(song);
        Assert.Equal(Bars, BarCount(song));
        Assert.Equal(0, song.Project.Tracks.SelectMany(t => t.AudioClips).Single().StartSec, 6);
    }

    [Fact]
    public void DraggingItBackGivesTheAddedBarsBack()
    {
        var (song, _, clip, barSec) = Make();
        Move(song, clip, 104.5 * barSec);
        var fit = Move(song, clip, 98 * barSec);
        Assert.Equal(5, fit.Removed);
        Assert.Equal(Bars, BarCount(song));
    }

    [Fact]
    public void ABarWithNotationStaysWhenTheClipLeaves()
    {
        var (song, track, clip, barSec) = Make();
        Move(song, clip, 104.5 * barSec);
        track.Measures[102].Cells[0].Notes.Add(new TabNote { StringIndex = 0, Fret = 5 });
        Move(song, clip, 98 * barSec);
        Assert.Equal(103, BarCount(song));
    }

    [Fact]
    public void BarsTheUserAddedAreNeverRemoved()
    {
        var (song, _, clip, barSec) = Make();
        BarGrid.AppendBars(song.Project, 3);
        Move(song, clip, 50 * barSec);
        Assert.Equal(Bars + 3, BarCount(song));
    }

    [Fact]
    public void ASongThatEndsInAJumpIsNotExtended()
    {
        var (song, track, clip, barSec) = Make();
        song.Project.Tracks[0].Measures[49].Directions = "Fine";
        song.Project.Tracks[0].Measures[Bars - 1].Directions = "DaCapo";
        song.Project.MarkTimelineChanged();
        var fit = Move(song, clip, 170 * barSec);
        Assert.True(fit.EndsInJump);
        Assert.Equal(0, fit.Added);
        Assert.Equal(Bars, BarCount(song));
    }

    [Fact]
    public void AFineOnTheLastBarMovesToTheNewEndSoTheAddedBarsPlayAndComesBackWithThem()
    {
        var (song, _, clip, barSec) = Make();
        var track = song.Project.Tracks[0];   // the bar form is read from the first track
        track.Measures[10].Directions = "Segno";
        track.Measures[50].Directions = "DalSegno";
        track.Measures[Bars - 1].Directions = "Fine";   // the D.S. pass stops on the last bar, as in the full demo song
        song.Project.MarkTimelineChanged();
        var fit = Move(song, clip, SongExtent.Measure(song.Project).EndSec + 4.5 * barSec);
        Assert.False(fit.EndsInJump);
        Assert.Equal(5, fit.Added);
        Assert.Equal("", track.Measures[Bars - 1].Directions);
        Assert.Equal("Fine", track.Measures[Bars + 4].Directions);
        Assert.True(SongExtent.Measure(song.Project).EndSec >= clip.EndSec - 1e-6);
        Move(song, clip, 98 * barSec);
        Assert.Equal(Bars, BarCount(song));
        Assert.Equal("Fine", track.Measures[Bars - 1].Directions);
    }

    [Fact]
    public void AFineAlreadyOnTheBarTheGrowthGivesBackIsNotDoubled()
    {
        var (song, _, clip, barSec) = Make();
        var track = song.Project.Tracks[0];
        track.Measures[10].Directions = "Segno";
        track.Measures[50].Directions = "DalSegno";
        track.Measures[Bars - 1].Directions = "Fine";
        song.Project.MarkTimelineChanged();
        Move(song, clip, SongExtent.Measure(song.Project).EndSec + 4.5 * barSec);
        track.Measures[Bars - 1].Directions = "Fine";   // the user wrote one there while the bars were out
        Move(song, clip, 98 * barSec);
        Assert.Equal("Fine", track.Measures[Bars - 1].Directions);
    }

    [Fact]
    public void ADaCapoAlCodaSongKeepsItsMarkersAndTheGrowthAddsPlayingBars()
    {
        var (song, _, clip, barSec) = Make();
        var track = song.Project.Tracks[0];
        (track.Measures[10].Directions, track.Measures[30].Directions, track.Measures[50].Directions, track.Measures[60].Directions) = ("Segno", "ToCoda", "DalSegnoAlCoda", "Coda");
        song.Project.MarkTimelineChanged();
        var fit = Move(song, clip, SongExtent.Measure(song.Project).EndSec + 4.5 * barSec);
        Assert.Equal(("Segno", "ToCoda", "DalSegnoAlCoda", "Coda"), (track.Measures[10].Directions, track.Measures[30].Directions, track.Measures[50].Directions, track.Measures[60].Directions));
        Assert.Equal(5, fit.Added);
        Assert.Equal(Bars + 4, SongExtent.Timing(song.Project).Performed[^1].Bar);   // the added bars play
    }

    [Fact]
    public void AFineTheUserAddedStaysWhenTheGrowthIsGivenBack()
    {
        var (song, _, clip, barSec) = Make();
        var track = song.Project.Tracks[0];
        track.Measures[Bars - 1].Directions = "Fine";   // the user's own, with no D.S. pass that stops there: the growth moves nothing
        song.Project.MarkTimelineChanged();
        Move(song, clip, SongExtent.Measure(song.Project).EndSec + 4.5 * barSec);
        Assert.Equal(Bars + 5, BarCount(song));
        Move(song, clip, 98 * barSec);
        Assert.Equal(Bars, BarCount(song));
        Assert.Equal("Fine", track.Measures[Bars - 1].Directions);
    }

    [Fact]
    public void ARightTrimGrowsTheSongToTheTrimmedEnd()
    {
        var (song, _, clip, barSec) = Make();
        clip.StartSec = 90 * barSec;
        DocumentEdits.Run(song, p => { clip.SourceLengthSec = 20 * barSec; return ClipAutoBars.Fit(p).Added == 10; });
        Assert.Equal(110, BarCount(song));
    }

    [Fact]
    public void GrowToBarsIsReusableAndCountsAsAutoAdded()
    {
        var (song, _, clip, _) = Make();
        Assert.Equal(7, ClipAutoBars.GrowToBars(song.Project, Bars + 7).Added);
        Assert.Equal(7, ClipAutoBars.Shrink(song.Project));
        Assert.Equal(Bars, BarCount(song));
    }

    [AvaloniaFact]
    public void DraggingAClipPastTheEndShowsGhostBarsThenTheDropGrowsAndTheDragBackShrinks()
    {
        var (song, track, clip, barSec) = Make();
        var view = new TimelineView(song.Project) { Snap = new SnapSettings { Enabled = false } };
        var gestures = new TimelineClipGestureController(view);
        var box = TimelineAudioClips.ClipBounds(view, track, clip);
        var press = new global::Avalonia.Point(box.X + 10, (box.Top + box.Bottom) / 2);
        var barPx = view.BarsWidth / Bars;
        gestures.Press(true, press);
        Assert.True(gestures.Begin());
        gestures.Update(new global::Avalonia.Point(press.X + 103.5 * barPx, press.Y), altHeld: false);
        Assert.True(view.GhostBars > 0);
        Assert.Equal(Bars, BarCount(song));   // no model change while it drags
        Assert.True(gestures.Commit(song));
        view.Reload(song.Project);
        Assert.Equal(Bars + 5, BarCount(song));

        box = TimelineAudioClips.ClipBounds(view, track, clip);
        press = new global::Avalonia.Point(box.X + 10, (box.Top + box.Bottom) / 2);
        gestures.Press(true, press);
        Assert.True(gestures.Begin());
        gestures.Update(new global::Avalonia.Point(press.X - 103.5 * barPx, press.Y), altHeld: false);
        Assert.True(gestures.Commit(song));
        Assert.Equal(Bars, BarCount(song));
    }

    private static (TimelineView View, TimelineClipGestureController Gestures, global::Avalonia.Point Press) Grab(SongDocument song, TrackModel track, AudioClip clip, double fromRight = 0)
    {
        var view = new TimelineView(song.Project) { Snap = new SnapSettings() };   // the app's default snapping: grid at any distance
        var gestures = new TimelineClipGestureController(view);
        var box = TimelineAudioClips.ClipBounds(view, track, clip);
        var press = new global::Avalonia.Point(fromRight > 0 ? box.Right - fromRight : box.X + 10, (box.Top + box.Bottom) / 2);
        gestures.Press(true, press);
        Assert.True(gestures.Begin());
        return (view, gestures, press);
    }

    private static void AssertPreviewCleared(TimelineView view)
    {
        Assert.Equal(0, view.GhostBars);
        Assert.False(view.PastEnd);
        Assert.Null(view.DragFold);
        Assert.Null(view.ClipDropLane);
    }

    [AvaloniaFact]
    public void ASnappedDragPastTheEndKeepsTheClipWhereItWasDropped()
    {
        var (song, track, clip, barSec) = Make();
        var (view, gestures, press) = Grab(song, track, clip);
        gestures.Update(new global::Avalonia.Point(press.X + 103.4 * view.BarsWidth / Bars, press.Y), altHeld: false);
        Assert.True(clip.EndSec > Bars * barSec);   // the grid does not pull it back to the old end
        var dropped = clip.StartSec;
        Assert.True(gestures.Commit(song));
        AssertPreviewCleared(view);
        Assert.Equal(dropped, clip.StartSec, 6);
        Assert.True(BarCount(song) > Bars);
        Assert.True(SongExtent.Measure(song.Project).EndSec >= clip.EndSec - 1e-6);
    }

    [AvaloniaFact]
    public void AMidiClipDraggedPastTheEndGrowsTheSongInOneUndoStep()
    {
        var (song, track, clip, barSec) = Make();
        clip.Notes = new System.Collections.Generic.List<ClipNote> { new(0, barSec / 2, 60, 100) };
        var (view, gestures, press) = Grab(song, track, clip);
        gestures.Update(new global::Avalonia.Point(press.X + 103.4 * view.BarsWidth / Bars, press.Y), altHeld: false);
        Assert.True(gestures.Commit(song));
        Assert.True(BarCount(song) > Bars);
        DocumentEdits.Undo(song);
        Assert.Equal(Bars, BarCount(song));
        Assert.Equal(0, song.Project.Tracks.SelectMany(t => t.AudioClips).Single().StartSec, 6);
    }

    [AvaloniaFact]
    public void ARightTrimDraggedPastTheEndGrowsTheSongAndClearsThePreview()
    {
        var (song, track, clip, barSec) = Make();
        clip.StartSec = 90 * barSec;
        clip.SourceLengthSec = 9 * barSec;
        var (view, gestures, press) = Grab(song, track, clip, fromRight: 2);
        gestures.Update(new global::Avalonia.Point(press.X + 20 * view.BarsWidth / Bars, press.Y), altHeld: false);
        Assert.True(view.GhostBars > 0);
        Assert.True(gestures.Commit(song));
        AssertPreviewCleared(view);
        Assert.True(BarCount(song) >= 110);
        Assert.True(clip.EndSec > Bars * barSec);
    }

    [AvaloniaFact]
    public void ACancelledDragLeavesNoPreviewAndPutsTheClipBack()
    {
        var (song, track, clip, _) = Make();
        var (view, gestures, press) = Grab(song, track, clip);
        gestures.Update(new global::Avalonia.Point(press.X + 103.4 * view.BarsWidth / Bars, press.Y), altHeld: false);
        Assert.True(view.GhostBars > 0);
        gestures.Cancel();
        AssertPreviewCleared(view);
        Assert.Equal(0, clip.StartSec, 6);
        Assert.Equal(Bars, BarCount(song));
    }
}
