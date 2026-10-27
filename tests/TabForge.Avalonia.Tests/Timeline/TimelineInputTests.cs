using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Styling;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using TabForge.Views.Score;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the timeline gestures driven by headless pointer input: hover, click and shift-click selection, the section drag and the row drag with
/// their undoable edits. Renders (hover, selection band, drags) go to %TEMP%\tf-avalonia-tests\TimelineInputTests for a look by eye.
/// Tests: TimelineInputController, TimelineView.
/// </summary>
public class TimelineInputTests
{
    private static (Window Window, TimelineView View, TimelineInputController Input, ScorePreviewEditor.PreviewDocument Doc) Open(ThemeVariant? theme = null)
    {
        var song = DemoSongFactory.Create();
        var doc = new ScorePreviewEditor.PreviewDocument(song);
        var view = new TimelineView(song) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var input = new TimelineInputController(view, doc);
        var window = new Window { Width = 1300, Height = 400, Content = view, RequestedThemeVariant = theme ?? ThemeVariant.Dark };
        window.Show();
        return (window, view, input, doc);
    }

    private static Point Cell(TimelineView v, int bar, int row) => new(v.BarX(bar) + 5, v.RowTop(row) + 10);

    private static void Shot(Window w, string name)
    {
        var dir = DialogTestKit.RenderDirectory(nameof(TimelineInputTests));
        using var frame = w.CaptureRenderedFrame();
        frame!.Save(Path.Combine(dir, name + ".png"));
    }

    [AvaloniaFact]
    public void HoverMarksBarAndRow()
    {
        var (w, v, _, _) = Open();
        w.MouseMove(Cell(v, 3, 1));
        Assert.Equal((3, 1), (v.HoverBar, v.HoverRow));
        Shot(w, "hover-dark");
        w.MouseMove(new Point(10, 5));
        Assert.Equal((-1, -1), (v.HoverBar, v.HoverRow));
        w.Close();
    }

    [AvaloniaFact]
    public void HoverOverAnAudioLaneDoesNotShadeTheTabAboveIt()
    {
        var song = DemoSongFactory.Create();
        song.Tracks[0].AudioClips.Add(new AudioClip { Name = "Take", StartSec = 0, SourceLengthSec = 2, FileLengthSec = 2 });
        var view = new TimelineView(song) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var window = new Window { Width = 1300, Height = 400, Content = view, RequestedThemeVariant = ThemeVariant.Dark };
        window.Show();
        Assert.True(ClipLanes.LanesHeight(song.Tracks[0]) > 0);

        window.MouseMove(new Point(view.BarX(3) + 5, view.LaneBandTopOf(0) + 10));
        Assert.Equal((-1, 0), (view.HoverBar, view.HoverRow));
        window.Close();
    }

    [AvaloniaFact]
    public void PressOnLaneMovesCursorHighlightsTheCellAndShiftClickOnAnotherTrackExtendsTheBlock()
    {
        var (w, v, input, doc) = Open(ThemeVariant.Light);
        var raised = -1;
        input.SelectedBarChanged += b => raised = b;
        w.MouseDown(Cell(v, 2, 0), MouseButton.Left);
        w.MouseUp(Cell(v, 2, 0), MouseButton.Left);
        // Spec barhighlight: the clicked cell is highlighted (grey), never selected; Shift+click on another tab track makes the block between the anchor and the click.
        Assert.Equal((2, 2, false, false), (input.SelectedBar, raised, input.Selection.HasRange, input.Selection.HasLoop));
        Assert.Equal((0, 0, 2, 2), (doc.Highlight.FirstTrack, doc.Highlight.LastTrack, doc.Highlight.FirstBar, doc.Highlight.LastBar));
        w.MouseDown(Cell(v, 5, 1), MouseButton.Left, RawInputModifiers.Shift);
        w.MouseUp(Cell(v, 5, 1), MouseButton.Left, RawInputModifiers.Shift);
        Assert.False(input.Selection.HasLoop);
        Assert.Equal((0, 1, 2, 5), (doc.Highlight.FirstTrack, doc.Highlight.LastTrack, doc.Highlight.FirstBar, doc.Highlight.LastBar));
        w.MouseMove(Cell(v, 7, 2));
        Shot(w, "selection-hover-light");
        w.Close();
    }

    [AvaloniaFact]
    public void DragOnLaneMakesNoRangeAndNoLoop()
    {
        var (w, v, input, _) = Open();
        w.MouseDown(Cell(v, 2, 1), MouseButton.Left);
        w.MouseMove(Cell(v, 6, 1));
        w.MouseUp(Cell(v, 6, 1), MouseButton.Left);
        Assert.Equal((false, false, null), (input.Selection.HasRange, input.Selection.HasLoop, v.Marquee));   // the marquee is gone after the release
        w.Close();
    }

    [AvaloniaFact]
    public void DraggingASectionReordersSectionsAndAShortDragOnlyClicks()
    {
        var (w, v, _, doc) = Open();
        var slots = v.SectionSlots();
        Assert.True(slots.Length >= 2);
        var title = SectionLayout.Sorted(doc.Project)[0].Title;
        var from = new Point(slots[0].Left + 24, slots[0].Top + 4);
        w.MouseDown(from, MouseButton.Left);
        w.MouseMove(from + new Vector(2, 0));
        Assert.Equal(-1, v.DragSection);
        var to = new Point(slots[1].Left + slots[1].Width - 2, from.Y);
        w.MouseMove(to);
        Assert.Equal(0, v.DragSection);
        Shot(w, "section-drag-dark");
        w.MouseUp(to, MouseButton.Left);
        Assert.Equal(title, SectionLayout.Sorted(doc.Project)[1].Title);
        w.Close();
    }

    private static (Window Window, TimelineView View, SongDocument Doc, TrackModel Track, AudioClip Clip) OpenWithClip()
    {
        var song = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        var track = NewTrackFactory.CreateTrack(song.Project, TrackKind.Guitar);
        song.Project.Tracks.Insert(0, track);
        var clip = new AudioClip
        {
            File = "gesture.mid", Name = "gesture", StartSec = 2, SourceLengthSec = 4, FileLengthSec = 4,
            Notes = new List<ClipNote> { new(0, 1, 60, 100) },
        };
        track.AudioClips.Add(clip);
        var view = new TimelineView(song.Project) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var input = new TimelineInputController(view, song);
        var window = new Window { Width = 1600, Height = 1100, Content = view };
        window.Show();
        window.UpdateLayout();
        view.UpdateLayout();
        return (window, view, song, track, clip);
    }

    private static Point ToWindow(TimelineView view, Window window, Point point) => view.TranslatePoint(point, window) ?? point;

    [AvaloniaFact]
    public void ADraggedClipLandsWhereItIsDroppedAndUndoRestoresIt()
    {
        var (w, v, doc, track, clip) = OpenWithClip();
        var box = TimelineAudioClips.ClipBounds(v, track, clip);
        var press = new Point((box.Left + box.Right) / 2, (box.Top + box.Bottom) / 2);
        var before = clip.StartSec;
        w.MouseDown(ToWindow(v, w, press), MouseButton.Left);
        var deltaX = v.XOfSeconds(before + 2) - v.XOfSeconds(before);
        var moved = ToWindow(v, w, press + new Vector(deltaX, 0));
        w.MouseMove(moved);
        w.MouseMove(moved);
        w.MouseUp(moved, MouseButton.Left);

        var after = clip.StartSec;
        w.Close();
        Assert.True(after > before + 1, $"clip did not move: {before} -> {after}");
        Assert.NotNull(DocumentEdits.Undo(doc));
        // Undo replaces the project: the restored clip is a different object at the press position.
        Assert.Equal(before, doc.Project.Tracks[0].AudioClips.Single().StartSec, 6);
        doc.MarkClean();
    }

    [AvaloniaFact]
    public void ADraggedFadeHandleLandsAndUndoRestoresIt()
    {
        var (w, v, doc, track, clip) = OpenWithClip();
        clip.Notes = null;   // an audio clip: MIDI takes have no fades
        v.SelectedClip = new TimelineClipSelection(track, clip);
        var box = TimelineAudioClips.ClipBounds(v, track, clip);
        var (inX, _) = TimelineAudioClips.FadeHandleXs(v, clip);
        var press = new Point(inX + 1, box.Y + 1);   // the fade handle sits over the trim grip; the fade wins
        w.MouseDown(ToWindow(v, w, press), MouseButton.Left);
        var moved = ToWindow(v, w, new Point(v.XOfSeconds(clip.StartSec + 0.4), press.Y));
        w.MouseMove(moved);
        w.MouseUp(moved, MouseButton.Left);

        w.Close();
        Assert.Equal(0.4, clip.FadeInSec, 4);
        Assert.Equal(2.0, clip.StartSec, 6);   // a fade does not trim
        Assert.NotNull(DocumentEdits.Undo(doc));
        Assert.Equal(0, doc.Project.Tracks[0].AudioClips.Single().FadeInSec, 6);
        doc.MarkClean();
    }

    [AvaloniaFact]
    public void ALanePressClearsTheTrackRowFocusAHeaderPressSetsIt()
    {
        var song = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        song.TrackIndex = 2;
        var view = new TimelineView(song.Project) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var header = new TrackHeaderColumn(view, song.Project);
        var input = new TimelineInputController(view, song, header);
        var clickedRows = new List<int>();
        input.TrackRowClicked += clickedRows.Add;   // the arrangement selects the clicked row from this, as ArrangementView does
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(header);
        panel.Children.Add(view);
        var window = new Window { Width = 1600, Height = 1100, Content = panel };
        window.Show();
        window.UpdateLayout();

        // A lane press is a timeline press, not a track-row press: the TrackRow.* keys stay off it.
        var lane = new Point(view.BarX(2) + 5, view.RowTop(1) + 10);
        var at = view.TranslatePoint(lane, window) ?? lane;
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Assert.False(view.TrackRowFocused);
        Assert.Equal(new[] { 1 }, clickedRows);

        // A press on the track rows themselves (the header name) focuses that row for the TrackRow.* keys and reports it.
        var name = TrackRowCells.Column("name", header.NameColumnWidth);
        var onName = new Point(name.Left + name.Width / 2, view.RowTop(0) + 10);
        var onHeader = header.TranslatePoint(onName, window) ?? onName;
        window.MouseDown(onHeader, MouseButton.Left);
        window.MouseUp(onHeader, MouseButton.Left);
        window.Close();
        Assert.True(view.TrackRowFocused);
        Assert.Equal(new[] { 1, 0 }, clickedRows);
        song.MarkClean();
    }
}
