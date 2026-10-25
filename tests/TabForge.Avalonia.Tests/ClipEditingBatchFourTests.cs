using System;
using System.Linq;
using Avalonia;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Platform.Testing;
using TabForge.Presets;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the adjusted clip editing of the arrangement: the WPF fade shading, fade handles shown on hover, the fade-handle hit winning over the
/// trim grip in the top band while the lower edges still trim, the fade drag as one undo step, and Split using the lane edit cursor when it lies
/// inside the selected clip and the playhead otherwise.
/// Does not own: the lane/clip rules (ClipLanes, ClipSplitGlue), the menu rows (ContextMenuTests) or the engine sync (RecordingLaneLifecycleTests).
/// Tests: this class.
/// </summary>
public class ClipEditingBatchFourTests
{
    private static readonly string ClipName = "fade-clip";

    private static (SongDocument Song, TrackModel Track, AudioClip Clip, TimelineView View, int Row, Rect Box) SetUp(double fadeIn = 0)
    {
        var song = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        var track = NewTrackFactory.CreateTrack(song.Project, TrackKind.Audio);
        song.Project.Tracks.Add(track);
        var clip = new AudioClip { File = "fade.wav", Name = ClipName, StartSec = 1, SourceLengthSec = 2, FileLengthSec = 2, FadeInSec = fadeIn };
        track.AudioClips.Add(clip);
        var view = new TimelineView(song.Project);
        var row = song.Project.Tracks.IndexOf(track);
        var box = TimelineAudioClips.ClipBounds(view, track, clip);
        return (song, track, clip, view, row, box);
    }

    private static AudioClip CurrentClip(SongDocument song) =>
        song.Project.Tracks.SelectMany(track => track.AudioClips).Single(clip => clip.Name == ClipName);

    [AvaloniaFact]
    public void FadeHandleWinsOverTrimInTheTopBandAndTheDragIsOneUndoStep()
    {
        var (song, track, clip, view, _, box) = SetUp();
        view.SelectedClip = new TimelineClipSelection(track, clip);
        var gestures = new TimelineClipGestureController(view);

        gestures.Press(true, new Point(box.X + 1, box.Y + 1));   // the top-left handle sits over the 6 px trim grip; the fade wins
        Assert.True(gestures.Begin());
        gestures.Update(new Point(view.XOfSeconds(clip.StartSec + 0.4), box.Y + 1), altHeld: false);
        Assert.True(gestures.Commit(song));

        Assert.Equal(0.4, clip.FadeInSec, 4);
        Assert.Equal(1.0, clip.StartSec, 6);                      // not trimmed
        Assert.NotNull(DocumentEdits.Undo(song));                 // one undo step
        Assert.Equal(0, CurrentClip(song).FadeInSec, 6);
    }

    [AvaloniaFact]
    public void TrimStillWinsAtTheLeftEdgeBelowTheFadeBand()
    {
        var (song, track, clip, view, _, box) = SetUp(fadeIn: 0.4);
        view.SelectedClip = new TimelineClipSelection(track, clip);
        var gestures = new TimelineClipGestureController(view);

        gestures.Press(true, new Point(box.X + 1, box.Bottom - 2));   // below the top band: the edge grip trims
        Assert.True(gestures.Begin());
        gestures.Update(new Point(view.XOfSeconds(clip.StartSec + 0.25), box.Bottom - 2), altHeld: false);
        Assert.True(gestures.Commit(song));

        Assert.InRange(clip.StartSec, 1.01, 1.4);   // the edge grip trimmed it (the exact snap target does not matter here)
        Assert.Equal(0.4, clip.FadeInSec, 4);
    }

    [AvaloniaFact]
    public void AHoveredClipShowsAndStartsAFadeWithoutBeingSelected()
    {
        var (song, track, clip, view, row, box) = SetUp();
        view.SelectedClip = null;
        view.HoverClip = (row, 0);
        Assert.True(TimelineAudioClips.ShowsFadeHandles(view, clip, row, 0));
        var gestures = new TimelineClipGestureController(view);

        gestures.Press(true, new Point(box.X + 1, box.Y + 1));
        Assert.True(gestures.Begin());
        gestures.Update(new Point(view.XOfSeconds(clip.StartSec + 0.3), box.Y + 1), altHeld: false);
        Assert.True(gestures.Commit(song));

        Assert.Equal(0.3, clip.FadeInSec, 4);
    }

    [AvaloniaFact]
    public void FadeShadingIsDrawnForAFadedClip()
    {
        var (song, track, clip, view, row, _) = SetUp(fadeIn: 0.5);
        clip.FadeOutSec = 0.25;
        var recorder = new PathCountingDrawContext();

        TimelineAudioClips.DrawRow(recorder, key => AvaloniaPalette.Get(key, false), view, track, row,
            view.LaneBandTopOf(row), AvaloniaPalette.Get("InstrumentAccent", false));

        Assert.Equal(2, recorder.PathsDrawn);   // the fade-in wedge and the fade-out wedge
    }

    [AvaloniaFact]
    public void SplitUsesTheLaneCursorWhenItLiesInsideTheSelectedClip()
    {
        var (song, track, clip, view, _, _) = SetUp();
        view.SelectedClip = new TimelineClipSelection(track, clip);
        view.LaneCursor = new TimelineLaneCursor(track, clip.Lane, 2.0, null);
        var target = new MainWindowClipKeys.Target(song, view, view.SelectedClip!);

        MainWindowClipProperties.Split(target, _ => throw new InvalidOperationException("the split should be inside the clip"));

        var parts = track.AudioClips.OrderBy(c => c.StartSec).ToList();
        Assert.Equal(2, parts.Count);
        Assert.Equal(2.0, parts[0].EndSec, 3);
        Assert.Equal(2.0, parts[1].StartSec, 3);
        Assert.Equal(2.0, view.LaneCursor!.Value.Sec, 3);   // the split point becomes the new lane cursor
    }

    [AvaloniaFact]
    public void SplitFallsBackToThePlayheadWithoutAnInsideLaneCursor()
    {
        var (song, track, clip, view, _, _) = SetUp();
        var bar = view.BarCount > 1 ? 1 : 0;
        var playheadSec = AudioClipTimelineRules.SecondsAtBar(song.Project, bar, 0);
        if (playheadSec <= clip.StartSec || playheadSec >= clip.EndSec)
        {
            clip.StartSec = Math.Max(0, playheadSec - 0.5);
            clip.SourceLengthSec = 2;
        }
        view.SelectedClip = new TimelineClipSelection(track, clip);
        view.SetPlayhead(bar, 0);
        var target = new MainWindowClipKeys.Target(song, view, view.SelectedClip!);
        var splitSec = AudioClipTimelineRules.SecondsAtBar(song.Project, view.PlayheadBar, view.PlayheadFraction);

        MainWindowClipProperties.Split(target, _ => throw new InvalidOperationException("the playhead is inside the clip"));

        Assert.Contains(track.AudioClips, part => Math.Abs(part.StartSec - splitSec) < 0.001);
    }

    /// <summary>A draw context that records only how many filled paths (the fade wedges) were drawn; text and shapes are ignored.</summary>
    private sealed class PathCountingDrawContext : IDrawContext
    {
        private readonly TrackRowSelectionTests.RectRecorder _inner = new();

        public int PathsDrawn { get; private set; }
        public double PixelsPerDip => 1;
        public IPathFactory Paths => _inner.Paths;

        public UiText Text(string text, string fontFamily, double size, UiColor colour, double pixelsPerDip, double maxWidth = 0, UiTextAlign align = UiTextAlign.Left,
            int weight = 400, bool italic = false, UiStroke? outline = null, bool idealLayout = false) => new(text.Length * 6, 12, 10, new object());
        public void DrawText(in UiText text, double x, double y) { }
        public void Rect(UiColor? fill, UiStroke? stroke, double x, double y, double width, double height, double radius = 0) { }
        public void Rect(IUiPaint? fill, IUiPen? pen, double x, double y, double width, double height, double radiusX = 0, double radiusY = 0) { }
        public void Line(UiStroke stroke, double x1, double y1, double x2, double y2) { }
        public void Line(IUiPen pen, double x1, double y1, double x2, double y2) { }
        public void Ellipse(UiColor? fill, UiStroke? stroke, double cx, double cy, double rx, double ry) { }
        public void Ellipse(IUiPaint? fill, IUiPen? pen, double cx, double cy, double rx, double ry) { }
        public void Path(UiColor? fill, UiStroke? stroke, IUiPath path) => PathsDrawn++;
        public void Path(IUiPaint? fill, IUiPen? pen, IUiPath path) => PathsDrawn++;
        public void Image(IUiImage image, double x, double y, double width, double height) { }
        public void PushClip(double x, double y, double width, double height) { }
        public void PushClip(IUiPath clip) { }
        public void PushTranslate(double dx, double dy) { }
        public void PushScale(double sx, double sy, double centreX = 0, double centreY = 0) { }
        public void PushRotate(double degrees, double centreX, double centreY) { }
        public void PushTransform(IUiTransform transform) { }
        public void PushOpacity(double opacity) { }
        public void Drawing(IUiDrawing drawing) { }
        public void Pop() { }
    }
}
