using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the timeline selection rules with real pointer and key events: a box drag on an empty lane selects only the clips it touches (white) and never makes
//   a range or loop, a click on a clip selects it, Ctrl+click toggles clips, a click on empty space (or Esc) clears the white and leaves the loop, and a press on a clip still moves it.
// Does not own: the bar highlight (TimelineBarHighlightInputTests), the model's Esc order (SelectionModelTests) or the drawing (SelectionDrawingTests).
// Tests: this class.
public sealed class TimelineBoxSelectionTests
{
    // Row 0 a guitar, row 1 an audio track with clips over bars 2-3 and 6-7.
    private static (Window Window, TimelineView View, SongDocument Doc, AudioClip First, AudioClip Second) Open()
    {
        var project = new SongProject();
        project.Tracks.Add(new TrackModel { Name = "Guitar", Measures = Enumerable.Range(0, 12).Select(bar => new MeasureModel { Number = bar + 1 }).ToList() });
        var audio = NewTrackFactory.CreateTrack(project, TrackKind.Audio);
        AudioClip Clip(int from, int to)
        {
            var span = BarRangeGaps.Span(project, from, to).GetValueOrDefault();
            return new AudioClip { File = "take.wav", Name = "take", StartSec = span.From, SourceLengthSec = span.To - span.From, FileLengthSec = span.To - span.From };
        }
        var first = Clip(2, 3);
        var second = Clip(6, 7);
        audio.AudioClips.Add(first);
        audio.AudioClips.Add(second);
        project.Tracks.Add(audio);
        var doc = new SongDocument(() => null) { Project = project };
        doc.MarkClean();
        var view = new TimelineView(project) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        _ = new TimelineInputController(view, doc);
        var window = new Window { Width = 1600, Height = 1100, Content = view };
        window.Show();
        window.UpdateLayout();
        view.UpdateLayout();
        return (window, view, doc, first, second);
    }

    // A point at bar's left edge + 5 on the audio lane's height (a clip's centre height).
    private static Point At(Window w, TimelineView v, int bar, double dx = 5)
    {
        var track = v.Song.Tracks[1];
        var box = TimelineAudioClips.ClipBounds(v, track, track.AudioClips[0]);
        return v.TranslatePoint(new Point(v.BarX(bar) + dx, (box.Top + box.Bottom) / 2), w) ?? default;
    }

    private static void Drag(Window w, Point from, Point to)
    {
        w.MouseDown(from, MouseButton.Left);
        w.MouseMove(new Point((from.X + to.X) / 2, to.Y));
        w.MouseMove(to);
        w.MouseUp(to, MouseButton.Left);
    }

    private static void Click(Window w, Point at, RawInputModifiers keys = RawInputModifiers.None)
    {
        w.MouseDown(at, MouseButton.Left, keys);
        w.MouseUp(at, MouseButton.Left, keys);
    }

    [AvaloniaFact]
    public void ABoxDragOnEmptySpaceSelectsOnlyTheClipsItTouches()
    {
        var (w, v, doc, first, _) = Open();
        Drag(w, At(w, v, 9), At(w, v, 3));
        Assert.Equal((false, false), (doc.Selection.HasRange, doc.Selection.HasLoop));
        Assert.Equal(new[] { first }, doc.Selection.Clips.Select(c => c.Clip).Where(c => c == first).ToArray());
        Assert.Equal(2, doc.Selection.ClipCount);   // the box from bar 9 back to bar 3 also reaches the clip over bars 6-7
        Drag(w, At(w, v, 4, 40), At(w, v, 5));
        Assert.Equal(0, doc.Selection.ClipCount);   // between the clips: nothing white
        w.Close();
    }

    [AvaloniaFact]
    public void TheClipSpanActsOnlyOnTheSelectedClipsTracks()
    {
        var (w, v, doc, _, _) = Open();
        Click(w, At(w, v, 2, 20));
        var span = Assert.IsType<BarSpan>(SelectionBars.For(doc.Selection, doc.Project));
        Assert.Equal((2, 3, 1), (span.Start, span.End, span.FirstTrack));
        Assert.Equal(new[] { 1 }, span.Tracks);
        var snapshot = RangeCapture.Capture(doc.Project, span.Start, span.End, span.Tracks![0], span.Tracks[^1]);
        Assert.Single(snapshot.Tracks);   // the copy (and so delete/paste) reaches the audio track only
        w.Close();
    }

    [AvaloniaFact]
    public void ADragOverEmptySpaceSelectsNothingAndMakesNoLoop()
    {
        var (w, v, doc, _, _) = Open();
        Drag(w, At(w, v, 9), At(w, v, 11));
        Assert.Equal((false, false, 0), (doc.Selection.HasRange, doc.Selection.HasLoop, doc.Selection.ClipCount));
        w.Close();
    }

    [AvaloniaFact]
    public void AClickOnAClipSelectsItWithoutMakingARangeAndKeepsAnExistingOne()
    {
        var (w, v, doc, first, _) = Open();
        Click(w, At(w, v, 2, 20));
        Assert.Equal(new[] { first }, doc.Selection.Clips.Select(c => c.Clip));
        Assert.False(doc.Selection.HasRange);
        doc.Selection.SetLoop(9, 10);
        Click(w, At(w, v, 6, 20));
        Assert.Equal((9, 10), doc.Selection.LoopBars);
        Assert.Equal(1, doc.Selection.ClipCount);
        w.Close();
    }

    [AvaloniaFact]
    public void CtrlClickTogglesClipsInAndOutOfTheWhiteSelectionAndLeavesTheRange()
    {
        var (w, v, doc, first, second) = Open();
        doc.Selection.SetLoop(9, 10);
        Click(w, At(w, v, 2, 20), RawInputModifiers.Control);
        Click(w, At(w, v, 6, 20), RawInputModifiers.Control);
        Assert.Equal(new[] { first, second }, doc.Selection.Clips.Select(c => c.Clip));
        Click(w, At(w, v, 2, 20), RawInputModifiers.Control);
        Assert.Equal(new[] { second }, doc.Selection.Clips.Select(c => c.Clip));
        Assert.Equal((9, 10), doc.Selection.LoopBars);
        w.Close();
    }

    [AvaloniaFact]
    public void AClickOnEmptySpaceClearsTheWhiteAndNeverTheLoop()
    {
        var (w, v, doc, _, _) = Open();
        doc.Selection.SetLoop(3, 9);
        Drag(w, At(w, v, 9), At(w, v, 3));
        Click(w, At(w, v, 11));
        Assert.Equal((0, (3, 9)), (doc.Selection.ClipCount, doc.Selection.LoopBars.GetValueOrDefault()));
        Click(w, At(w, v, 11));
        Assert.Equal((3, 9), doc.Selection.LoopBars);
        w.Close();
    }

    [AvaloniaFact]
    public void EscClearsTheWhiteFirstAndThenTheLoop()
    {
        var (w, v, doc, _, _) = Open();
        doc.Selection.SetLoop(3, 9);
        Drag(w, At(w, v, 9), At(w, v, 3));
        v.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape, Source = v });
        Assert.Equal((0, (3, 9)), (doc.Selection.ClipCount, doc.Selection.LoopBars.GetValueOrDefault()));
        Assert.Equal(EscapeResult.Loop, DocumentEdits.Escape(doc));   // the window Esc: the next thing is the loop
        Assert.False(doc.Selection.HasLoop);
        w.Close();
    }

    [AvaloniaFact]
    public void APressOnAClipStillMovesTheClipAndMakesNoRange()
    {
        var (w, v, doc, first, _) = Open();
        var start = first.StartSec;
        Drag(w, At(w, v, 2, 20), At(w, v, 4, 20));
        Assert.True(first.StartSec > start);
        Assert.False(doc.Selection.HasRange);
        w.Close();
    }
}
