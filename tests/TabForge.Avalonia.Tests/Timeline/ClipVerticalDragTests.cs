using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: dragging audio and MIDI clips up and down in the timeline: to another lane of the track, onto another track (empty, an instrument track, a track with
//   clips), a new lane under the last one, the whole selection together, Ctrl copy, Shift axis lock, the not-allowed snap back, the live preview (no song edit
//   while dragging), the overlap prompt after the drop, one undo step, and a sideways pull over a repeat riding along with the vertical one.
// Does not own: the sideways drag alone (TimelineAudioClipTests, ClipDragRepeatTests) or the overlap rules (ClipLaneOverlapTests).
// Tests: this class.
public sealed class ClipVerticalDragTests
{
    private static readonly double Lane = ClipLanes.AudioLaneHeight;

    private sealed class Rig
    {
        public SongDocument Doc = null!;
        public TimelineView View = null!;
        public TrackModel A = null!, B = null!;
        public List<ClipOverlapQuestion> Asked = new();
        public int Refreshes;
        public List<TrackModel> Tracks => Doc.Project.Tracks;
    }

    private static AudioClip Audio(string name, double start, double length, int lane = 0) =>
        new() { Name = name, File = name + ".wav", StartSec = start, SourceLengthSec = length, FileLengthSec = length, Lane = lane };

    private static AudioClip Midi(string name, double start, double length, int lane = 0) =>
        new() { Name = name, StartSec = start, SourceLengthSec = length, FileLengthSec = length, Lane = lane, Notes = new List<ClipNote>() };

    /// <summary>A blank song with two audio tracks at the bottom (A, then B); <paramref name="fill"/> puts the clips on them.</summary>
    private static Rig Make(Action<TrackModel, TrackModel> fill, bool prompt = false, bool moveAnswer = true)
    {
        var project = SongDocument.BlankProject();   // no repeats: seconds map straight onto bars
        var rig = new Rig();
        rig.A = NewTrackFactory.CreateTrack(project, TrackKind.Audio);
        project.Tracks.Add(rig.A);
        rig.B = NewTrackFactory.CreateTrack(project, TrackKind.Audio);
        project.Tracks.Add(rig.B);
        fill(rig.A, rig.B);
        foreach (var track in new[] { rig.A, rig.B })
            if (track.AudioClips.Count > 0) ClipLanes.Ensure(track, track.AudioClips.Max(c => c.Lane) + 1);
        rig.Doc = new SongDocument(() => null) { Project = project };
        rig.View = new TimelineView(project) { Snap = new SnapSettings { Enabled = false }, ClipEdited = _ => rig.Refreshes++ };
        if (prompt)
            rig.View.ClipOverlap = new ClipOverlapPolicy
            {
                Setting = () => ClipOverlapChoice.Ask,
                Remember = _ => { },
                Prompt = question => { rig.Asked.Add(question); return Task.FromResult<ClipOverlapAnswer?>(new ClipOverlapAnswer(moveAnswer, false)); },
            };
        return rig;
    }

    /// <summary>The y of the middle of a lane band; a track with no clips has no lanes laid out, so the middle of its row.</summary>
    private static double CentreOf(TimelineView view, TrackModel track, int lane)
    {
        var row = view.Song.Tracks.IndexOf(track);
        return view.LaidOutLaneCountOf(track) == 0 ? view.RowTop(row) + view.RowHeight / 2 : view.LaneBandTopOf(row, lane) + Lane / 2;
    }

    private sealed record Grab(TimelineClipGestureController Gestures, Point Press, double SourceCentre);

    private static Grab Press(Rig rig, TrackModel from, AudioClip clip)
    {
        var box = TimelineAudioClips.ClipBounds(rig.View, from, clip);
        var press = new Point((box.Left + box.Right) / 2, (box.Top + box.Bottom) / 2);
        var gestures = new TimelineClipGestureController(rig.View);
        gestures.Press(true, press);
        Assert.True(gestures.Begin());
        return new Grab(gestures, press, CentreOf(rig.View, from, clip.Lane));
    }

    /// <summary>The pointer for dragging the grabbed clip so its middle sits on a lane (<paramref name="to"/> null: its own row), <paramref name="dxSec"/> to the right and <paramref name="extraY"/> lower.</summary>
    private static Point Pointer(Rig rig, Grab grab, TrackModel? to, int lane, double dxSec = 0, double extraY = 0) =>
        new(rig.View.XOfSeconds(rig.View.SecondsOfX(grab.Press.X) + dxSec), grab.Press.Y + (to is null ? 0 : CentreOf(rig.View, to, lane) - grab.SourceCentre) + extraY);

    private static bool Drag(Rig rig, TrackModel from, AudioClip clip, TrackModel? to, int lane, double dxSec = 0, KeyModifiers keys = KeyModifiers.None, double extraY = 0)
    {
        var grab = Press(rig, from, clip);
        grab.Gestures.Update(Pointer(rig, grab, to, lane, dxSec, extraY), altHeld: false, keys);
        return grab.Gestures.Commit(rig.Doc);
    }

    private static void Select(Rig rig, params (TrackModel Track, AudioClip Clip)[] clips) =>
        rig.View.SelectClips(clips.Select(c => new TimelineClipSelection(c.Track, c.Clip)));

    // ---- lanes of the same track ----

    [AvaloniaFact]
    public void AnAudioClipDraggedDownLandsOnTheSecondLaneOfItsTrack()
    {
        var x = Audio("X", 0, 1);
        var rig = Make((a, _) => a.AudioClips.AddRange(new[] { Audio("P", 20, 1), x, Audio("Y", 5, 1, lane: 1) }));
        Assert.True(Drag(rig, rig.A, x, rig.A, 1));
        Assert.Equal(1, x.Lane);
        Assert.Equal(0, x.StartSec, 6);   // a vertical drag keeps the time
        Assert.Same(rig.A, rig.Tracks.Single(t => t.AudioClips.Contains(x)));
        Assert.Equal(1, rig.Doc.Undo.UndoCount);
        Assert.True(rig.Refreshes > 0);   // the one post-edit path ran (engine and clips sync)
    }

    [AvaloniaFact]
    public void ADropBelowTheLastLaneOpensANewLane()
    {
        var x = Audio("X", 0, 1);
        var rig = Make((a, _) => a.AudioClips.AddRange(new[] { Audio("P", 20, 1), x }));
        Assert.Single(rig.A.Lanes);
        Assert.True(Drag(rig, rig.A, x, to: null, lane: 0, extraY: 0.45 * Lane));   // the clip middle in the bottom of the only lane
        Assert.Equal(1, x.Lane);
        Assert.Equal(2, rig.A.Lanes.Count);
        Assert.Equal(0, rig.A.AudioClips.Single(c => c.Name == "P").Lane);
    }

    [AvaloniaFact]
    public void ALoneClipDraggedToANewLaneOfItsOwnTrackChangesNothing()
    {
        var x = Audio("X", 0, 1);
        var rig = Make((a, _) => a.AudioClips.Add(x));
        Assert.False(Drag(rig, rig.A, x, to: null, lane: 0, extraY: 0.45 * Lane));   // its lane would close up again
        Assert.Equal(0, x.Lane);
        Assert.Equal(0, rig.Doc.Undo.UndoCount);
    }

    // ---- other tracks ----

    [AvaloniaFact]
    public void AMidiClipDraggedOntoAnotherTrackMovesThereAsOneUndoStep()
    {
        var x = Midi("X", 1, 2);
        var rig = Make((a, _) => a.AudioClips.AddRange(new[] { x, Audio("P", 20, 1) }));
        Assert.True(Drag(rig, rig.A, x, rig.B, 0));   // B has no clips yet: its first lane
        Assert.Contains(x, rig.B.AudioClips);
        Assert.DoesNotContain(x, rig.A.AudioClips);
        Assert.Equal(0, x.Lane);
        Assert.Equal(1, x.StartSec, 6);
        Assert.Equal(1, rig.Doc.Undo.UndoCount);
        Assert.NotNull(DocumentEdits.Undo(rig.Doc));
        var back = rig.Tracks.Single(t => t.AudioClips.Any(c => c.Name == "X"));
        Assert.Equal(rig.A.Id, back.Id);
        Assert.Equal(1, back.AudioClips.Single(c => c.Name == "X").StartSec, 6);
        Assert.Empty(rig.Tracks.Single(t => t.Id == rig.B.Id).AudioClips);
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnyClipCanLandOnAnInstrumentTrack(bool midi)
    {
        var x = midi ? Midi("X", 1, 2) : Audio("X", 1, 2);
        var rig = Make((a, _) => a.AudioClips.Add(x));
        var instrument = rig.Tracks.First(t => !t.IsAudio);
        Assert.True(Drag(rig, rig.A, x, instrument, 0));
        Assert.Contains(x, instrument.AudioClips);
        Assert.Empty(rig.A.AudioClips);
    }

    [AvaloniaFact]
    public void AClipDroppedOnATrackWithClipsTakesThatLane()
    {
        var x = Audio("X", 0, 1);
        var rig = Make((a, b) => { a.AudioClips.Add(x); b.AudioClips.AddRange(new[] { Audio("K", 10, 1), Audio("L", 12, 1, lane: 1) }); });
        Assert.True(Drag(rig, rig.A, x, rig.B, 1));
        Assert.Equal((rig.B.Id, 1), (rig.Tracks.Single(t => t.AudioClips.Any(c => c.Name == "X")).Id, rig.B.AudioClips.Single(c => c.Name == "X").Lane));
    }

    [AvaloniaFact]
    public void ATrackThatCannotHoldClipsShowsNotAllowedAndTheClipSnapsBack()
    {
        var x = Midi("X", 1, 2);
        var rig = Make((a, b) => { a.AudioClips.Add(x); b.BusSlot = 0; });
        var grab = Press(rig, rig.A, x);
        grab.Gestures.Update(Pointer(rig, grab, rig.B, 0, dxSec: 1), altHeld: false);
        Assert.False(rig.View.ClipDragPreview!.Allowed);
        Assert.False(grab.Gestures.Commit(rig.Doc));
        Assert.Contains(x, rig.A.AudioClips);
        Assert.Equal(1, x.StartSec, 6);   // the sideways pull is undone too
        Assert.Equal(0, rig.Doc.Undo.UndoCount);
        Assert.Null(rig.View.ClipDragPreview);
    }

    // ---- several clips, copy, axis lock ----

    [AvaloniaFact]
    public void SelectedClipsMoveDownTogetherKeepingTheirLaneOffsets()
    {
        var (x1, x2) = (Audio("X1", 0, 1), Midi("X2", 3, 1, lane: 1));
        var rig = Make((a, _) => a.AudioClips.AddRange(new[] { Audio("P", 20, 1), x1, x2, Audio("Q", 10, 1, lane: 1) }));
        Select(rig, (rig.A, x1), (rig.A, x2));
        Assert.True(Drag(rig, rig.A, x1, rig.A, 1));
        Assert.Equal((1, 2), (x1.Lane, x2.Lane));   // the offset of one lane between them stays; the lane under the last one opens
        Assert.Equal(3, rig.A.Lanes.Count);
        Assert.Equal(2, rig.View.SelectedClips.Count);
        Assert.Equal(1, rig.Doc.Undo.UndoCount);
        Assert.NotNull(DocumentEdits.Undo(rig.Doc));
        var undone = rig.Tracks.Single(t => t.Id == rig.A.Id).AudioClips;
        Assert.Equal((0, 1), (undone.Single(c => c.Name == "X1").Lane, undone.Single(c => c.Name == "X2").Lane));
    }

    [AvaloniaFact]
    public void TheSelectionStopsAtTheTopLaneAndMovesNothingWhenOneClipCannotGoUp()
    {
        var (w, x) = (Audio("W", 0, 1), Audio("X", 3, 1, lane: 1));
        var rig = Make((a, _) => a.AudioClips.AddRange(new[] { w, x, Audio("P", 20, 1) }));
        Select(rig, (rig.A, w), (rig.A, x));
        Assert.False(Drag(rig, rig.A, x, rig.A, 0));   // W is already on the first lane: the pair stays
        Assert.Equal((0, 1), (w.Lane, x.Lane));
    }

    [AvaloniaFact]
    public void SelectedClipsOnTwoTracksMoveUpOneTrackTogether()
    {
        var (x, z) = (Audio("X", 0, 1), Midi("Z", 4, 1));
        var rig = Make((a, b) => { a.AudioClips.Add(x); b.AudioClips.Add(z); });
        var above = rig.Tracks[rig.Tracks.IndexOf(rig.A) - 1];
        Select(rig, (rig.A, x), (rig.B, z));
        Assert.True(Drag(rig, rig.B, z, rig.A, 0));   // the pointer track is one row up, so every clip goes one row up
        Assert.Contains(x, above.AudioClips);
        Assert.Contains(z, rig.A.AudioClips);
        Assert.Empty(rig.B.AudioClips);
    }

    [AvaloniaFact]
    public void CtrlDragLeavesTheClipAndLandsACopyOnTheOtherTrackInOneUndoStep()
    {
        var x = Midi("X", 1, 2);
        var rig = Make((a, _) => a.AudioClips.Add(x));
        Assert.True(Drag(rig, rig.A, x, rig.B, 0, dxSec: 1, keys: KeyModifiers.Control));
        Assert.Equal(1, x.StartSec, 6);
        Assert.Contains(x, rig.A.AudioClips);
        var copy = Assert.Single(rig.B.AudioClips);
        Assert.NotEqual(x.Id, copy.Id);
        Assert.True(copy.IsMidi);
        Assert.True(copy.StartSec > 1.5);   // the copy took the sideways pull too
        Assert.Same(copy, Assert.Single(rig.View.SelectedClips).Clip);
        Assert.Equal(1, rig.Doc.Undo.UndoCount);
        Assert.NotNull(DocumentEdits.Undo(rig.Doc));
        Assert.Empty(rig.Tracks.Single(t => t.Id == rig.B.Id).AudioClips);
        Assert.Single(rig.Tracks.Single(t => t.Id == rig.A.Id).AudioClips);
    }

    [AvaloniaFact]
    public void CtrlDragShowsTheCopyAsAGhostAndKeepsTheClipWhereItIs()
    {
        var x = Audio("X", 1, 2);
        var rig = Make((a, _) => a.AudioClips.Add(x));
        var grab = Press(rig, rig.A, x);
        grab.Gestures.Update(Pointer(rig, grab, rig.B, 0, dxSec: 1), altHeld: false, KeyModifiers.Control);
        Assert.Equal(1, x.StartSec, 6);
        var ghost = Assert.Single(rig.View.ClipDragPreview!.Ghosts);
        Assert.True(rig.View.ClipDragPreview.Copy);
        Assert.NotSame(ghost.Clip, ghost.Shown);   // the copy is drawn by a stand-in at its own start; the original stays
        Assert.True(ghost.Shown.StartSec > ghost.Clip.StartSec);
        Assert.Same(rig.B, ghost.Track);
        grab.Gestures.Cancel();
        Assert.Null(rig.View.ClipDragPreview);
    }

    [AvaloniaFact]
    public void ShiftLocksTheDragToItsFirstDirection()
    {
        var (x, y) = (Audio("X", 1, 1), Audio("Y", 1, 1));
        var rig = Make((a, b) => { a.AudioClips.Add(x); b.AudioClips.Add(y); });
        // Mostly sideways: the track stays.
        var grab = Press(rig, rig.A, x);
        var sideways = Pointer(rig, grab, rig.B, 0, dxSec: 1);
        grab.Gestures.Update(new Point(grab.Press.X + 120, grab.Press.Y + 6), altHeld: false, KeyModifiers.Shift);
        grab.Gestures.Update(new Point(grab.Press.X + 120, sideways.Y), altHeld: false, KeyModifiers.Shift);
        Assert.True(grab.Gestures.Commit(rig.Doc));
        Assert.Contains(x, rig.A.AudioClips);
        Assert.True(x.StartSec > 1.2);
        // Mostly vertical: the time stays.
        var grabY = Press(rig, rig.A, x);
        var start = x.StartSec;
        grabY.Gestures.Update(new Point(grabY.Press.X + 2, grabY.Press.Y + 8), altHeld: false, KeyModifiers.Shift);
        grabY.Gestures.Update(Pointer(rig, grabY, rig.B, 0, dxSec: 1), altHeld: false, KeyModifiers.Shift);
        Assert.True(grabY.Gestures.Commit(rig.Doc));
        Assert.Contains(x, rig.B.AudioClips);
        Assert.Equal(start, x.StartSec, 6);
    }

    // ---- preview, overlap, undo ----

    [AvaloniaFact]
    public void WhileDraggingNothingInTheSongChangesAndTheGhostFollowsTheRow()
    {
        var x = Audio("X", 0, 1);
        var rig = Make((a, b) => { a.AudioClips.Add(x); b.AudioClips.Add(Audio("K", 10, 1)); });
        var grab = Press(rig, rig.A, x);
        grab.Gestures.Update(Pointer(rig, grab, rig.B, 0), altHeld: false);
        var ghost = Assert.Single(rig.View.ClipDragPreview!.Ghosts);
        Assert.True(rig.View.ClipDragPreview.Allowed);
        Assert.Equal((rig.B, 0), (ghost.Track, ghost.Lane));
        Assert.Contains(x, rig.A.AudioClips);   // no model edit, no rebuild: the song is untouched until the drop
        Assert.Equal(0, rig.Doc.Undo.UndoCount);
        Assert.Equal(0, rig.Refreshes);
        grab.Gestures.Update(Pointer(rig, grab, rig.A, 0), altHeld: false);   // back to its own lane: no ghost
        Assert.Null(rig.View.ClipDragPreview);
        Assert.False(grab.Gestures.Commit(rig.Doc));
        Assert.Equal(0, rig.Doc.Undo.UndoCount);
    }

    [AvaloniaFact]
    public void ThePreviewDrawsTheGhostAndTheLaneWithoutThrowing()
    {
        var x = Audio("X", 0, 1);
        var rig = Make((a, b) => { a.AudioClips.Add(x); b.AudioClips.Add(Audio("K", 10, 1)); });
        var grab = Press(rig, rig.A, x);
        grab.Gestures.Update(Pointer(rig, grab, rig.B, 0, dxSec: 1), altHeld: false, KeyModifiers.Control);
        var draw = new InstrumentFretNumberTests.RecordingDrawContext();
        TimelineAudioClips.DrawClipDragPreview(draw, key => AvaloniaPalette.Get(key, false), rig.View, 2000);
        Assert.True(draw.Rects.Count >= 2, "the lane highlight and the ghost both draw");
        grab.Gestures.Cancel();
    }

    [AvaloniaFact]
    public void ADropOverAClipOnTheTargetLaneAsksAndMoveSendsTheExistingOneToAnotherLane()
    {
        var x = Audio("X", 0.5, 1);
        var rig = Make((a, b) => { a.AudioClips.Add(x); b.AudioClips.Add(Audio("K", 0, 2)); }, prompt: true, moveAnswer: true);
        Assert.True(Drag(rig, rig.A, x, rig.B, 0));
        var asked = Assert.Single(rig.Asked);
        Assert.Equal("X", asked.Edited);
        Assert.Equal(0, x.Lane);   // the dropped clip stays where it was put
        Assert.Equal(1, rig.B.AudioClips.Single(c => c.Name == "K").Lane);
        Assert.Equal(1, rig.Doc.Undo.UndoCount);
    }

    [AvaloniaFact]
    public void WithoutThePromptTheDroppedClipTakesTheNextFreeLane()
    {
        var x = Audio("X", 0.5, 1);
        var rig = Make((a, b) => { a.AudioClips.Add(x); b.AudioClips.Add(Audio("K", 0, 2)); });
        Assert.True(Drag(rig, rig.A, x, rig.B, 0));
        Assert.Equal(1, x.Lane);
        Assert.Equal(0, rig.B.AudioClips.Single(c => c.Name == "K").Lane);
    }

    [AvaloniaFact]
    public void APlainClickWithoutADragChangesNothing()
    {
        var x = Audio("X", 0, 1);
        var rig = Make((a, _) => a.AudioClips.Add(x));
        var box = TimelineAudioClips.ClipBounds(rig.View, rig.A, x);
        var gestures = new TimelineClipGestureController(rig.View);
        gestures.Press(true, new Point((box.Left + box.Right) / 2, (box.Top + box.Bottom) / 2));
        Assert.False(gestures.Commit(rig.Doc));
        Assert.Equal(0, rig.Doc.Undo.UndoCount);
    }

    // ---- sideways and vertical together over a repeat ----

    [AvaloniaFact]
    public void ASidewaysPullOverARepeatKeepsItsPassAwareTimingWhileTheClipChangesTrack()
    {
        // Bars 5-6 (1-based) repeat with three endings; the same clip is pulled six bars left once with and once without a change of track.
        double Run(bool vertical)
        {
            var project = TemplateFactory.Blank();
            foreach (var t in project.Tracks) t.Measures = t.Measures.Take(16).ToList();
            var m = project.Tracks[0].Measures;
            m[4].RepeatStart = true;
            (m[6].AlternateEndingMask, m[6].RepeatEnd) = (1, true);
            (m[7].AlternateEndingMask, m[7].RepeatEnd) = (2, true);
            m[8].AlternateEndingMask = 4;
            var a = NewTrackFactory.CreateTrack(project, TrackKind.Audio);
            var b = NewTrackFactory.CreateTrack(project, TrackKind.Audio);
            project.Tracks.Add(a);
            project.Tracks.Add(b);
            var bar = AudioClipTimelineRules.SecondsAtBar(project, 1, 0);
            var clip = Audio("X", 6 * bar, 14 * bar);
            a.AudioClips.Add(clip);
            var doc = new SongDocument(() => null) { Project = project };
            var view = new TimelineView(project) { Snap = new SnapSettings { Enabled = false } };
            var rig = new Rig { Doc = doc, View = view, A = a, B = b };
            var grab = PressAtBar(rig, a, clip, 11);
            var target = new Point((view.BarX(5) + view.BarX(6)) / 2, grab.Press.Y + (vertical ? CentreOf(view, b, 0) - grab.SourceCentre : 0));
            grab.Gestures.Update(target, altHeld: false);
            Assert.True(grab.Gestures.Commit(doc));
            Assert.Equal(vertical, b.AudioClips.Contains(clip));
            return clip.StartSec - 6 * bar;
        }
        var plain = Run(vertical: false);
        var combined = Run(vertical: true);
        Assert.True(plain < 0);
        Assert.Equal(plain, combined, 9);
    }

    private static Grab PressAtBar(Rig rig, TrackModel track, AudioClip clip, int bar)
    {
        var box = TimelineAudioClips.ClipBounds(rig.View, track, clip);
        var press = new Point((rig.View.BarX(bar) + rig.View.BarX(bar + 1)) / 2, (box.Top + box.Bottom) / 2);
        var gestures = new TimelineClipGestureController(rig.View);
        gestures.Press(true, press);
        Assert.True(gestures.Begin());
        return new Grab(gestures, press, CentreOf(rig.View, track, clip.Lane));
    }
}
