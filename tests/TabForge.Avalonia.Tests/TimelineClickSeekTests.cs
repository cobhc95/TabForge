using System.Collections.Generic;
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
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: click vs drag seek/selection on the arrangement timeline: clip click selects without seeking, clip drag moves without seeking,
/// empty-lane click seeks, ruler drag selects without seeking, ruler click seeks.
/// Tests: TimelineInputController.
/// </summary>
public class TimelineClickSeekTests
{
    private static (Window Window, TimelineView View, TimelineInputController Input, SongDocument Doc, TrackModel Track, AudioClip Clip) OpenWithClip()
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
        return (window, view, input, song, track, clip);
    }

    private static Point ToWindow(TimelineView view, Window window, Point point) => view.TranslatePoint(point, window) ?? point;

    private static Point RulerAt(TimelineView view, int bar) => new(view.BarX(bar) + 5, TimelineView.RulerHeight / 2);

    [AvaloniaFact]
    public void ClickOnClipSelectsItAndMovesThePlayheadToItsStart()
    {
        var (w, v, input, _, track, clip) = OpenWithClip();
        var seeks = new List<int>();
        input.BarSeekRequested += seeks.Add;
        var box = TimelineAudioClips.ClipBounds(v, track, clip);
        var press = new Point((box.Left + box.Right) / 2, (box.Top + box.Bottom) / 2);
        var at = ToWindow(v, w, press);
        w.MouseDown(at, MouseButton.Left);
        w.MouseUp(at, MouseButton.Left);
        w.Close();
        Assert.NotNull(v.SelectedClip);
        Assert.Equal(clip, v.SelectedClip!.Clip);
        Assert.Equal(new[] { TabForge.Presenters.AudioClipTimelineRules.BarAtSeconds(v.Song, clip.StartSec).Bar }, seeks);
    }

    [AvaloniaFact]
    public void DragOnClipMovesItWithoutSeeking()
    {
        var (w, v, input, _, track, clip) = OpenWithClip();
        var seeks = new List<int>();
        input.BarSeekRequested += seeks.Add;
        var box = TimelineAudioClips.ClipBounds(v, track, clip);
        var press = new Point((box.Left + box.Right) / 2, (box.Top + box.Bottom) / 2);
        var before = clip.StartSec;
        var at = ToWindow(v, w, press);
        var moved = ToWindow(v, w, press + new Vector(30, 0));
        w.MouseDown(at, MouseButton.Left);
        w.MouseMove(moved);
        w.MouseUp(moved, MouseButton.Left);
        w.Close();
        Assert.NotEqual(before, clip.StartSec);
        Assert.Empty(seeks);
    }

    [AvaloniaFact]
    public void ClickOnEmptyLaneMovesPlayheadWithoutSelectingClip()
    {
        var (w, v, input, _, _, _) = OpenWithClip();
        var seeks = new List<int>();
        input.BarSeekRequested += seeks.Add;
        var bar = -1;
        Point lane = default;
        for (var b = v.BarCount - 1; b >= 0; b--)
        {
            var candidate = new Point(v.BarX(b) + 5, v.LaneBandTopOf(0) + 10);
            if (ToWindow(v, w, candidate).X >= w.ClientSize.Width) continue; // past the window edge: no press can land there
            if (TimelineAudioClips.ClipAt(v, candidate) is null) { bar = b; lane = candidate; break; }
        }
        Assert.True(bar >= 0);
        var at = ToWindow(v, w, lane);
        w.MouseDown(at, MouseButton.Left);
        w.MouseUp(at, MouseButton.Left);
        w.Close();
        Assert.Null(v.SelectedClip);
        Assert.Equal(new[] { bar }, seeks);
    }

    [AvaloniaFact]
    public void DragOnRulerMakesTheLoopWithoutSeeking()
    {
        var (w, v, input, _, _, _) = OpenWithClip();
        var seeks = new List<int>();
        input.BarSeekRequested += seeks.Add;
        var from = ToWindow(v, w, RulerAt(v, 25));
        var to = ToWindow(v, w, RulerAt(v, 29));
        w.MouseDown(from, MouseButton.Left);
        w.MouseMove(to);
        w.MouseUp(to, MouseButton.Left);
        w.Close();
        Assert.Equal((25, 29), input.Selection.LoopBars);
        Assert.Empty(seeks);
    }

    [AvaloniaFact]
    public void ClickOnRulerSeeks()
    {
        var (w, v, input, _, _, _) = OpenWithClip();
        var seeks = new List<int>();
        input.BarSeekRequested += seeks.Add;
        var at = ToWindow(v, w, RulerAt(v, 31));
        w.MouseDown(at, MouseButton.Left);
        w.MouseUp(at, MouseButton.Left);
        w.Close();
        Assert.Equal(new[] { 31 }, seeks);
    }
}
