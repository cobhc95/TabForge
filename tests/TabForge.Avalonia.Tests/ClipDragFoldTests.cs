using System;
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
using TabForge.Features.Recording.Rules;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: a clip drag across a repeat edge keeps the fold it started with (same slice count and order on every move), the preview moves one way only,
//   and the drop lands where the last preview showed.
// Does not own: the fold itself (RepeatUnfoldTests) or the drag gestures (TimelineAudioClipTests).
// Tests: this class.
public sealed class ClipDragFoldTests
{
    [AvaloniaFact]
    public void DraggingAFoldedClipAcrossARepeatEdgeDoesNotReFoldOrJitter()
    {
        var song = new SongDocument(() => null) { Project = SongDocument.BlankProject() };
        song.MarkClean();
        song.Project.Tracks[0].Measures[1].RepeatStart = true;
        song.Project.Tracks[0].Measures[2].RepeatEnd = true;
        var track = NewTrackFactory.CreateTrack(song.Project, TrackKind.Audio);
        song.Project.Tracks.Add(track);
        var bar = AudioClipTimelineRules.SecondsAtBar(song.Project, 1, 0);
        var clip = new AudioClip { File = "take.wav", Name = "take", StartSec = bar, SourceLengthSec = 5 * bar, FileLengthSec = 5 * bar };
        track.AudioClips.Add(clip);
        var view = new TimelineView(song.Project) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Snap = new SnapSettings { Enabled = false } };
        var input = new TimelineInputController(view, song);
        var window = new Window { Width = 1600, Height = 900, Content = view };
        window.Show();
        window.UpdateLayout();
        view.UpdateLayout();
        var startFold = TimelineAudioClips.FoldOf(view, clip)!;
        Assert.True(startFold.Count > 1);
        var box = TimelineAudioClips.ClipBounds(view, track, clip);
        var press = new Point(box.X + 40, (box.Top + box.Bottom) / 2);
        window.MouseDown(view.TranslatePoint(press, window)!.Value, MouseButton.Left);
        var lefts = new List<double>();
        for (var i = 1; i <= 30; i++)
        {
            window.MouseMove(view.TranslatePoint(press + new Vector(i * 5, 0), window)!.Value);
            var fold = TimelineAudioClips.FoldOf(view, clip)!;
            Assert.Equal(startFold.Count, fold.Count);
            Assert.Equal(startFold.Select(s => s.PartIndex), fold.Select(s => s.PartIndex));
            lefts.Add(TimelineAudioClips.ClipBounds(view, track, clip).X);
        }
        var lastStart = clip.StartSec;
        window.MouseUp(view.TranslatePoint(press + new Vector(150, 0), window)!.Value, MouseButton.Left);
        window.Close();
        song.MarkClean();
        for (var i = 1; i < lefts.Count; i++) Assert.True(lefts[i] >= lefts[i - 1] - 1e-9);
        Assert.True(lefts[^1] > lefts[0]);
        Assert.Equal(lastStart, clip.StartSec, 9);
    }

    [AvaloniaFact]
    public void DraggingAnUnfoldedClipIntoARepeatKeepsItOnePlainBoxUntilTheDrop()
    {
        var song = new SongDocument(() => null) { Project = SongDocument.BlankProject() };
        song.MarkClean();
        song.Project.Tracks[0].Measures[1].RepeatStart = true;
        song.Project.Tracks[0].Measures[2].RepeatEnd = true;
        var track = NewTrackFactory.CreateTrack(song.Project, TrackKind.Audio);
        song.Project.Tracks.Add(track);
        var bar = AudioClipTimelineRules.SecondsAtBar(song.Project, 1, 0);
        var clip = new AudioClip { File = "take.wav", Name = "take", StartSec = 0, SourceLengthSec = 3 * bar, FileLengthSec = 3 * bar };
        track.AudioClips.Add(clip);
        var view = new TimelineView(song.Project) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Snap = new SnapSettings { Enabled = false } };
        var input = new TimelineInputController(view, song);
        var window = new Window { Width = 1600, Height = 900, Content = view };
        window.Show();
        window.UpdateLayout();
        view.UpdateLayout();
        Assert.True(TimelineAudioClips.FoldOf(view, clip)?.Any(s => s.IsPart) != true);
        var box = TimelineAudioClips.ClipBounds(view, track, clip);
        var press = new Point(box.X + 20, (box.Top + box.Bottom) / 2);
        var step = (view.XOfSeconds(1.5 * bar) - view.XOfSeconds(0)) / 30;
        window.MouseDown(view.TranslatePoint(press, window)!.Value, MouseButton.Left);
        for (var i = 1; i <= 30; i++)
        {
            window.MouseMove(view.TranslatePoint(press + new Vector(i * step, 0), window)!.Value);
            Assert.True(TimelineAudioClips.FoldOf(view, clip)?.Any(s => s.IsPart) != true);
        }
        window.MouseUp(view.TranslatePoint(press + new Vector(30 * step, 0), window)!.Value, MouseButton.Left);
        var dropped = TimelineAudioClips.FoldOf(view, clip);
        window.Close();
        song.MarkClean();
        Assert.True(clip.StartSec > bar);
        Assert.True(dropped?.Any(s => s.IsPart) == true);
    }
}
