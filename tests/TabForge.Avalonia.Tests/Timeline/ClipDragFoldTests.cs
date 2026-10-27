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

// Owns: a clip drag across a repeat edge previews the fold of the position it is dragged to (the slices the drop will show, not the ones it started with),
//   the preview moves one way only, and the drop lands where, and looks as, the last preview showed.
// Does not own: the fold itself (RepeatUnfoldTests) or the drag gestures (TimelineAudioClipTests).
// Tests: this class.
public sealed class ClipDragFoldTests
{
    [AvaloniaFact]
    public void DraggingAFoldedClipAcrossARepeatEdgeShowsTheFoldOfEachCandidatePosition()
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
        IReadOnlyList<RepeatSlice>? lastPreview = null;
        for (var i = 1; i <= 30; i++)
        {
            window.MouseMove(view.TranslatePoint(press + new Vector(i * 5, 0), window)!.Value);
            // The preview is the fold of the candidate position, exactly what a fresh fold of the clip there gives.
            var performed = SongExtent.Timing(song.Project).Performed;
            Assert.Equal(RepeatFold.FoldedSlices(clip.StartSec, clip.EndSec, performed), TimelineAudioClips.FoldOf(view, clip) ?? RepeatFold.FoldedSlices(clip.StartSec, clip.EndSec, performed));
            lastPreview = TimelineAudioClips.FoldOf(view, clip);
            lefts.Add(TimelineAudioClips.ClipBounds(view, track, clip).X);
        }
        var lastStart = clip.StartSec;
        window.MouseUp(view.TranslatePoint(press + new Vector(150, 0), window)!.Value, MouseButton.Left);
        Assert.Equal(lastPreview, TimelineAudioClips.FoldOf(view, clip));   // the drop looks as the last preview did
        window.Close();
        song.MarkClean();
        for (var i = 1; i < lefts.Count; i++) Assert.True(lefts[i] >= lefts[i - 1] - 1e-9);
        Assert.True(lefts[^1] > lefts[0]);
        Assert.Equal(lastStart, clip.StartSec, 9);
    }

    [AvaloniaFact]
    public void DraggingAnUnfoldedClipIntoARepeatPreviewsTheFoldedPassesTheDropWillShow()
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
        var previewed = false;
        IReadOnlyList<RepeatSlice>? lastPreview = null;
        var box = TimelineAudioClips.ClipBounds(view, track, clip);
        var press = new Point(box.X + 20, (box.Top + box.Bottom) / 2);
        var step = (view.XOfSeconds(1.5 * bar) - view.XOfSeconds(0)) / 30;
        window.MouseDown(view.TranslatePoint(press, window)!.Value, MouseButton.Left);
        for (var i = 1; i <= 30; i++)
        {
            window.MouseMove(view.TranslatePoint(press + new Vector(i * step, 0), window)!.Value);
            lastPreview = TimelineAudioClips.FoldOf(view, clip);
            previewed |= lastPreview?.Any(s => s.IsPart) == true;
        }
        Assert.True(previewed);   // the preview shows the repeat passes while dragging, not one plain box
        window.MouseUp(view.TranslatePoint(press + new Vector(30 * step, 0), window)!.Value, MouseButton.Left);
        var dropped = TimelineAudioClips.FoldOf(view, clip);
        Assert.Equal(lastPreview, dropped);   // preview slices == committed slices at the same position
        window.Close();
        song.MarkClean();
        Assert.True(clip.StartSec > bar);
        Assert.True(dropped?.Any(s => s.IsPart) == true);
    }
}
