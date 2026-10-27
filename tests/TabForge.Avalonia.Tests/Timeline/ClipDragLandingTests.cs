using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: where a dragged clip is drawn while it moves: a move lifts the clip off its own spot and draws it itself on the landing lane (the lane only lightly highlighted), a Ctrl copy leaves
//   the original and draws the copy; and the drop below the last track (the Add track strip): the strip shade and words, the slot only while the pointer is there, Ctrl copy, one undo step,
//   the new track named after the clip, Escape putting everything back.
// Does not own: the other drag landings (ClipVerticalDragTests) or the stress run (ClipVerticalDragStressTests).
// Tests: this class.
public sealed class ClipDragLandingTests
{
    private sealed class Rig
    {
        public SongDocument Doc = null!;
        public ArrangementView Arrangement = null!;
        public TimelineView View = null!;
        public Window Window = null!;
        public TrackModel A = null!, B = null!;
        public AudioClip X = null!;
        public int Refreshes;
        public SongProject Project => Doc.Project;
    }

    private static Rig Open()
    {
        var project = TemplateFactory.Blank();
        foreach (var t in project.Tracks) t.Measures = t.Measures.Take(16).ToList();
        var rig = new Rig { A = NewTrackFactory.CreateTrack(project, TrackKind.Audio), B = NewTrackFactory.CreateTrack(project, TrackKind.Audio) };
        project.Tracks.Add(rig.A);
        project.Tracks.Add(rig.B);
        rig.X = new AudioClip { Name = "Vocal take", File = "vocal.wav", StartSec = 1, SourceLengthSec = 2, FileLengthSec = 2 };
        rig.A.AudioClips.AddRange(new[] { rig.X, new AudioClip { Name = "P", File = "p.wav", StartSec = 20, SourceLengthSec = 1, FileLengthSec = 1 } });
        rig.B.AudioClips.Add(new AudioClip { Name = "K", File = "k.wav", StartSec = 10, SourceLengthSec = 1, FileLengthSec = 1 });
        foreach (var t in new[] { rig.A, rig.B }) ClipLanes.Ensure(t, 1);
        rig.Doc = new SongDocument(() => null) { Project = project };
        rig.Arrangement = new ArrangementView(project, rig.Doc);
        rig.View = rig.Arrangement.Timeline;
        rig.View.Snap = new SnapSettings { Enabled = false };
        rig.View.ClipEdited = _ => rig.Refreshes++;
        rig.Window = new Window { Width = 1800, Height = 900, Content = rig.Arrangement };
        rig.Window.Show();
        rig.Window.UpdateLayout();
        rig.View.UpdateLayout();
        return rig;
    }

    private static (TimelineClipGestureController Gestures, Point Press) Grab(Rig rig)
    {
        var box = TimelineAudioClips.ClipBounds(rig.View, rig.A, rig.X);
        var press = new Point((box.Left + box.Right) / 2, (box.Top + box.Bottom) / 2);
        var gestures = new TimelineClipGestureController(rig.View);
        gestures.Press(true, press);
        Assert.True(gestures.Begin());
        return (gestures, press);
    }

    private static Point BelowTheLastTrack(Rig rig, Point press, double dx = 0) => new(press.X + dx, rig.View.NewTrackSlotTop + 6);

    private static Point OnLaneOf(Rig rig, Point press, TrackModel track) =>
        new(press.X, rig.View.LaneBandTopOf(rig.Project.Tracks.IndexOf(track), 0) + ClipLanes.AudioLaneHeight / 2);

    // ---- a move draws the clip itself on the landing lane ----

    [AvaloniaFact]
    public void AMovedClipLeavesItsOwnSpotAndIsDrawnOnTheLandingLane()
    {
        var rig = Open();
        var (gestures, press) = Grab(rig);
        gestures.Update(OnLaneOf(rig, press, rig.B), altHeld: false);
        Assert.True(TimelineAudioClips.IsLiftedByDrag(rig.View, rig.X), "the clip is not drawn on its own spot while it hovers over another lane");
        Assert.Same(rig.X, Assert.Single(rig.View.ClipDragPreview!.Ghosts).Shown);
        var draw = new InstrumentFretNumberTests.RecordingDrawContext();
        TimelineAudioClips.DrawClipDragPreview(draw, key => AvaloniaPalette.Get(key, false), rig.View, 2000);
        var laneTop = rig.View.LaneBandTopOf(rig.Project.Tracks.IndexOf(rig.B), 0);
        Assert.Contains(draw.Rects, r => r.Fill is not null && r.Y >= laneTop && r.Y < laneTop + ClipLanes.AudioLaneHeight && r.Width > 10 && r.Width < 1000);   // the clip itself, on the lane of B
        Assert.Contains(draw.Rects, r => r.Width >= 1000 && r.Height == ClipLanes.AudioLaneHeight && r.Y == laneTop);   // the light lane highlight
        gestures.Update(press, altHeld: false);   // back over its own lane
        Assert.False(TimelineAudioClips.IsLiftedByDrag(rig.View, rig.X));
        gestures.Cancel();
        rig.Window.Close();
    }

    [AvaloniaFact]
    public void ACopyKeepsTheOriginalDrawnAndShowsTheCopyOnTheLandingLane()
    {
        var rig = Open();
        var (gestures, press) = Grab(rig);
        gestures.Update(OnLaneOf(rig, press, rig.B), altHeld: false, KeyModifiers.Control);
        Assert.False(TimelineAudioClips.IsLiftedByDrag(rig.View, rig.X), "a copy leaves the original in place");
        Assert.True(rig.View.ClipDragPreview!.Copy);
        gestures.Cancel();
        Assert.False(TimelineAudioClips.IsLiftedByDrag(rig.View, rig.X));
        rig.Window.Close();
    }

    // ---- the Add track strip ----

    [AvaloniaFact]
    public void DraggingBelowTheLastTrackShowsTheNewTrackLandingOnTheStripOnlyWhileThere()
    {
        var rig = Open();
        var (gestures, press) = Grab(rig);
        Assert.False(rig.Arrangement.AddTrackBar.DropActive);
        gestures.Update(BelowTheLastTrack(rig, press), altHeld: false);
        Assert.True(rig.View.ClipDragPreview!.NewTrack);
        Assert.Null(Assert.Single(rig.View.ClipDragPreview.Ghosts).Track);
        Assert.True(rig.Arrangement.AddTrackBar.DropActive);
        Assert.Equal(AddTrackLane.ClipMoveLabel, rig.Arrangement.AddTrackBar.DropWords);
        Assert.True(TimelineAudioClips.IsLiftedByDrag(rig.View, rig.X));
        gestures.Update(BelowTheLastTrack(rig, press), altHeld: false, KeyModifiers.Control);
        Assert.Equal(AddTrackLane.ClipCopyLabel, rig.Arrangement.AddTrackBar.DropWords);
        gestures.Update(OnLaneOf(rig, press, rig.B), altHeld: false);   // back up onto a track: the slot goes away
        Assert.False(rig.View.ClipDragPreview!.NewTrack);
        Assert.False(rig.Arrangement.AddTrackBar.DropActive);
        gestures.Update(BelowTheLastTrack(rig, press), altHeld: false);
        Assert.True(rig.Arrangement.AddTrackBar.DropActive);
        gestures.Cancel();   // Escape (or a lost pointer) ends the drag
        Assert.Null(rig.View.ClipDragPreview);
        Assert.False(rig.Arrangement.AddTrackBar.DropActive);
        Assert.Null(rig.Arrangement.AddTrackBar.DropWords);
        Assert.Equal(1, rig.X.StartSec, 6);
        Assert.Equal(0, rig.Doc.Undo.UndoCount);
        rig.Window.Close();
    }

    [AvaloniaFact]
    public void DroppingBelowTheLastTrackMovesTheClipToANewTrackNamedAfterItInOneUndoStep()
    {
        var rig = Open();
        var tracksBefore = rig.Project.Tracks.Count;
        var (gestures, press) = Grab(rig);
        gestures.Update(BelowTheLastTrack(rig, press, dx: 60), altHeld: false);
        Assert.True(gestures.Commit(rig.Doc));
        Assert.Equal(tracksBefore + 1, rig.Project.Tracks.Count);
        var created = rig.Project.Tracks[^1];
        Assert.Equal("Vocal take", created.Name);
        Assert.True(created.IsAudio);
        Assert.Same(rig.X, Assert.Single(created.AudioClips));
        Assert.Equal(0, rig.X.Lane);
        Assert.True(rig.X.StartSec > 1, "the sideways pull came along");
        Assert.DoesNotContain(rig.X, rig.A.AudioClips);
        Assert.Single(rig.A.AudioClips);   // the original track keeps only its other clip
        Assert.Equal(1, rig.Doc.Undo.UndoCount);
        Assert.True(rig.Refreshes > 0);
        Assert.Null(rig.View.ClipDragPreview);
        Assert.False(rig.Arrangement.AddTrackBar.DropActive);
        Assert.NotNull(DocumentEdits.Undo(rig.Doc));
        Assert.Equal(tracksBefore, rig.Doc.Project.Tracks.Count);
        Assert.Contains(rig.Doc.Project.Tracks.Single(t => t.Id == rig.A.Id).AudioClips, c => c.Name == "Vocal take" && c.StartSec == 1);
        rig.Window.Close();
    }

    [AvaloniaFact]
    public void CtrlDroppingBelowTheLastTrackCopiesTheClipToANewTrack()
    {
        var rig = Open();
        var tracksBefore = rig.Project.Tracks.Count;
        var (gestures, press) = Grab(rig);
        gestures.Update(BelowTheLastTrack(rig, press), altHeld: false, KeyModifiers.Control);
        Assert.True(gestures.Commit(rig.Doc));
        Assert.Equal(tracksBefore + 1, rig.Project.Tracks.Count);
        var copy = Assert.Single(rig.Project.Tracks[^1].AudioClips);
        Assert.NotEqual(rig.X.Id, copy.Id);
        Assert.Contains(rig.X, rig.A.AudioClips);   // the original stays
        Assert.Equal(1, rig.Doc.Undo.UndoCount);
        rig.Window.Close();
    }

    [AvaloniaFact]
    public void WithoutTheStripNothingBelowTheLastTrackMakesATrack()
    {
        var rig = Open();
        rig.View.NewTrackDropAvailable = false;   // the strip is hidden (compact mode or the View setting)
        var tracksBefore = rig.Project.Tracks.Count;
        var (gestures, press) = Grab(rig);
        gestures.Update(BelowTheLastTrack(rig, press), altHeld: false);
        Assert.True(rig.View.ClipDragPreview is null or { NewTrack: false });
        gestures.Commit(rig.Doc);
        Assert.Equal(tracksBefore, rig.Project.Tracks.Count);
        rig.Window.Close();
    }

    [AvaloniaFact]
    public void TheEscapeKeyDuringAClipDragPutsTheClipBackAndDropsNothing()
    {
        var rig = Open();
        var box = TimelineAudioClips.ClipBounds(rig.View, rig.A, rig.X);
        var press = new Point((box.Left + box.Right) / 2, (box.Top + box.Bottom) / 2);
        Point Win(Point p) => rig.View.TranslatePoint(p, rig.Window)!.Value;
        rig.Window.MouseDown(Win(press), MouseButton.Left);
        rig.Window.MouseMove(Win(new Point(press.X + 80, rig.View.NewTrackSlotTop + 6)), RawInputModifiers.LeftMouseButton);
        Assert.NotNull(rig.View.ClipDragPreview);
        rig.Window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Assert.Null(rig.View.ClipDragPreview);
        Assert.Equal(1, rig.X.StartSec, 6);
        rig.Window.MouseUp(Win(new Point(press.X + 80, rig.View.NewTrackSlotTop + 6)), MouseButton.Left);
        Assert.Contains(rig.X, rig.A.AudioClips);
        Assert.Equal(0, rig.Doc.Undo.UndoCount);
        rig.Window.Close();
    }
}
