using System.Linq;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: dragging a clip past the song end through the real window input (press, moves, release on the MainWindow with default preferences and the full demo song):
//   a body move or a right-edge drag grows the song (the demo's Fine on its last bar moves to the new end so the bars play) in one undo step,
//   and the white top-right handle is the fade-out.
// Does not own: the growth rules (ClipAutoBarsTests), the snap targets (TimelineClipSnapPastEndTests).
// Tests: this class.
public sealed class ClipDragPastEndInputTests
{
    private sealed record Setup(MainWindow Window, SongDocument Song, TimelineView Timeline, TrackModel Track, AudioClip Clip, int Bars, double BarSec);

    private static Setup Open(bool midi = false)
    {
        var project = FullDemoSongFactory.Create();
        var track = NewTrackFactory.CreateTrack(project, midi ? TrackKind.Keys : TrackKind.Audio);
        project.Tracks.Add(track);
        var (endSec, barSec) = SongExtent.Measure(project);
        // A long imported file that ends exactly at the song end (the user's "edge of oblivion" clip).
        var length = 40 * barSec;
        var clip = midi
            ? new AudioClip { File = "", Name = "midi", StartSec = endSec - length, SourceLengthSec = length, FileLengthSec = length, Notes = new() }
            : new AudioClip { File = "edge of oblivion.wav", Name = "edge of oblivion", StartSec = endSec - length, SourceLengthSec = length, FileLengthSec = length };
        track.AudioClips.Add(clip);
        var song = new SongDocument(() => null) { Project = project };
        song.MarkClean();
        var window = new MainWindow(first: song);
        window.Show();
        window.UpdateLayout();
        var timeline = window.GetLogicalDescendants().OfType<ArrangementView>().First().Timeline;
        return new Setup(window, song, timeline, track, clip, BarRangeEditor.MaxMeasures(project), barSec);
    }

    private static double BarWidth(Setup s) => s.Timeline.BarX(s.Timeline.BarCount) - s.Timeline.BarX(s.Timeline.BarCount - 1);

    private static Point ToWindow(Setup s, double x, double y) => s.Timeline.TranslatePoint(new Point(x, y), s.Window)!.Value;

    private static void Drag(Setup s, Point from, double dx)
    {
        s.Window.MouseMove(ToWindow(s, from.X, from.Y));
        s.Window.MouseDown(ToWindow(s, from.X, from.Y), MouseButton.Left);
        for (var step = 1; step <= 10; step++) s.Window.MouseMove(ToWindow(s, from.X + dx * step / 10, from.Y), RawInputModifiers.LeftMouseButton);
        s.Window.MouseUp(ToWindow(s, from.X + dx, from.Y), MouseButton.Left);
    }

    private static Rect Box(Setup s)
    {
        var row = s.Song.Project.Tracks.IndexOf(s.Track);
        s.Timeline.EnsureRowVisible(row);
        var arrangement = s.Window.GetLogicalDescendants().OfType<ArrangementView>().First();
        arrangement.RevealSection(s.Timeline.BarCount - 3, s.Timeline.BarCount - 1, select: false);   // the clip's end and the empty grid after it in view
        s.Window.UpdateLayout();
        return TimelineAudioClips.ClipBounds(s.Timeline, s.Track, s.Clip);
    }

    private static void Close(Setup s)
    {
        s.Song.MarkClean();
        s.Window.Close();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void DraggingTheBodyTenBarsPastTheEndMovesTheClipAndGrowsTheSong(bool midi)
    {
        var s = Open(midi);
        var box = Box(s);
        var start = s.Clip.StartSec;
        Drag(s, new Point(box.Right - 1.5 * BarWidth(s), box.Y + box.Height * 0.6), 12 * BarWidth(s));
        Assert.True(s.Clip.StartSec > start + 10 * s.BarSec, $"clip start {s.Clip.StartSec} vs {start}");
        Assert.True(BarRangeEditor.MaxMeasures(s.Song.Project) >= s.Bars + 10, $"bars {BarRangeEditor.MaxMeasures(s.Song.Project)} vs {s.Bars}");
        Assert.True(SongExtent.Measure(s.Song.Project).EndSec >= s.Clip.EndSec - 1e-6, "the song plays to the clip's end");
        DocumentEdits.Undo(s.Song);   // the move and the growth are one undo step
        Assert.Equal(start, s.Song.Project.Tracks[^1].AudioClips.Single().StartSec, 6);
        Assert.Equal(s.Bars, BarRangeEditor.MaxMeasures(s.Song.Project));
        Close(s);
    }

    [AvaloniaFact]
    public void DraggingAnAudioClipsRightEdgePastTheEndLoopsItAndGrowsTheSong()
    {
        // The end edge has no upper limit: past the media the clip loops (ClipTrim, ClipLoop), so the song grows to hold it.
        var s = Open();
        var box = Box(s);
        var end = s.Clip.EndSec;
        Drag(s, new Point(box.Right - 2, box.Y + box.Height * 0.6), 12 * BarWidth(s));
        Assert.True(s.Clip.EndSec > end + 10 * s.BarSec, $"clip end {s.Clip.EndSec} vs {end}");
        Assert.True(BarRangeEditor.MaxMeasures(s.Song.Project) >= s.Bars + 10);
        Assert.True(SongExtent.Measure(s.Song.Project).EndSec >= s.Clip.EndSec - 1e-6, "the song plays to the clip's end");
        Close(s);
    }

    [AvaloniaFact]
    public void DraggingAMidiClipsRightEdgePastTheEndGrowsTheSong()
    {
        var s = Open(midi: true);
        var box = Box(s);
        var end = s.Clip.EndSec;
        Drag(s, new Point(box.Right - 2, box.Y + box.Height * 0.6), 12 * BarWidth(s));
        Assert.True(s.Clip.EndSec > end + 10 * s.BarSec, $"clip end {s.Clip.EndSec} vs {end}");
        Assert.True(BarRangeEditor.MaxMeasures(s.Song.Project) >= s.Bars + 10);
        Assert.True(SongExtent.Measure(s.Song.Project).EndSec >= s.Clip.EndSec - 1e-6, "the song plays to the clip's end");
        Close(s);
    }

    [AvaloniaFact]
    public void TheTopRightHandleIsTheFadeOutAndNeverMovesTheClip()
    {
        var s = Open();
        var box = Box(s);
        s.Window.MouseMove(ToWindow(s, box.Right - 3, box.Y + 3));   // hover shows the handles
        var (start, end) = (s.Clip.StartSec, s.Clip.EndSec);
        Drag(s, new Point(box.Right - 3, box.Y + 3), -4 * BarWidth(s));
        Assert.True(s.Clip.FadeOutSec > 0, "the top-right handle sets the fade-out");
        Assert.Equal(start, s.Clip.StartSec, 6);
        Assert.Equal(end, s.Clip.EndSec, 6);
        Close(s);
    }
}
