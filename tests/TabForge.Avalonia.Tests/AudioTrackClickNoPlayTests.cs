using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Transport;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using Xunit;
using static TabForge.Avalonia.Tests.AvaloniaPlaybackSessionTests;

namespace TabForge.Avalonia.Tests;

// Owns: the guard that a click on an audio track (its clip, its empty lane, its header name) in the real window never starts playback,
//   never tells the audio engine's song transport to run, and never toggles the track's solo or mute while the transport is stopped;
//   and a lane click while the song is paused moves the held position without running the engine's audio clips (the paused-seek leak).
// Tests: this class.
public class AudioTrackClickNoPlayTests
{
    private sealed record Harness(MainWindow Window, SongDocument Song, ArrangementView Arrangement, FakeEngine Engine,
        AvaloniaPlaybackSession Session, TrackModel Track, AudioClip Clip, List<bool> EngineRuns) : IDisposable
    {
        public void Dispose()
        {
            Song.MarkClean();
            Window.Close();
        }
    }

    private static Harness Open(AvaloniaPlaybackSessions sessions, FakeEngine engine)
    {
        var song = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        var track = NewTrackFactory.CreateTrack(song.Project, TrackKind.Audio);
        track.Name = "8 - edge of oblivion";
        var clip = new AudioClip { File = "edge.wav", Name = "edge", StartSec = 2, SourceLengthSec = 6, FileLengthSec = 6 };
        track.AudioClips.Add(clip);
        song.Project.Tracks.Insert(0, track);
        var window = new MainWindow(first: song, playback: sessions) { Width = 1800, Height = 1100 };
        window.Show();
        window.UpdateLayout();
        var arrangement = window.GetLogicalDescendants().OfType<ArrangementView>().First();
        arrangement.UpdateLayout();
        var session = sessions.For(song)!;
        session.Stop();
        var runs = new List<bool>();
        var report = session.ReportEnginePosition;
        session.ReportEnginePosition = (playing, sec, speed) => { runs.Add(playing); report?.Invoke(playing, sec, speed); };
        return new Harness(window, song, arrangement, engine, session, track, clip, runs);
    }

    private static void Click(Window window, Control source, Point point)
    {
        var at = source.TranslatePoint(point, window) ?? point;
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    private static void AssertSilent(Harness h, string where)
    {
        Assert.False(h.Engine.Started, where + ": the engine started");
        Assert.False(h.Engine.Resumed, where + ": the engine resumed");
        Assert.False(h.Session.IsPlaying, where + ": the transport plays");
        Assert.DoesNotContain(true, h.EngineRuns);
        Assert.False(h.Track.Solo, where + ": the track was soloed");
        Assert.False(h.Track.Mute, where + ": the track was muted");
        Assert.DoesNotContain(h.Song.Project.Tracks, t => t.Solo);
    }

    private static Harness OpenStopped(out AvaloniaPlaybackSessions sessions)
    {
        var engine = new FakeEngine();
        sessions = new AvaloniaPlaybackSessions(new FakeUiThread(), () => engine);
        return Open(sessions, engine);
    }

    [AvaloniaFact]
    public void ClickOnAudioClipNeitherPlaysNorSolos()
    {
        using var h = OpenStopped(out var sessions);
        using var _ = sessions;
        var timeline = h.Arrangement.Timeline;
        var box = TimelineAudioClips.ClipBounds(timeline, h.Track, h.Clip);
        Assert.True(box.Width > 0 && box.Height > 0);
        Click(h.Window, timeline, box.Center);
        Assert.NotNull(timeline.SelectedClip);
        AssertSilent(h, "clip");
    }

    [AvaloniaFact]
    public void ClickOnEmptyAudioLaneNeitherPlaysNorSolos()
    {
        using var h = OpenStopped(out var sessions);
        using var _ = sessions;
        var timeline = h.Arrangement.Timeline;
        var row = h.Song.Project.Tracks.IndexOf(h.Track);
        var box = TimelineAudioClips.ClipBounds(timeline, h.Track, h.Clip);
        var lane = new Point(box.Right + 40, box.Center.Y);
        Assert.Null(TimelineAudioClips.ClipAt(timeline, lane));
        Assert.Equal(row, timeline.RowAtY(lane.Y));
        Click(h.Window, timeline, lane);
        AssertSilent(h, "lane");
    }

    [AvaloniaFact]
    public void ClickOnAudioTrackHeaderNameNeitherPlaysNorSolos()
    {
        using var h = OpenStopped(out var sessions);
        using var _ = sessions;
        var header = h.Arrangement.Header;
        var timeline = h.Arrangement.Timeline;
        var row = h.Song.Project.Tracks.IndexOf(h.Track);
        var (left, width) = TrackRowCells.Column("name", header.NameColumnWidth);
        var name = new Point(left + width / 2, timeline.RowTop(row) + timeline.BaseBandHeightOf(row) / 2);
        Assert.Equal(row, header.NameRowAt(name));
        Click(h.Window, header, name);
        Assert.Equal(row, h.Song.TrackIndex);
        AssertSilent(h, "header");
    }

    [AvaloniaFact]
    public void ClickOnAudioLaneWhilePausedKeepsTheAudioClipsSilent()
    {
        using var h = OpenStopped(out var sessions);
        using var _ = sessions;
        h.Session.PlayOrPause();
        h.Engine.RaiseTimelineChanged(new TabForge.Playback.ScoreTimeline());
        h.Session.PlayOrPause();
        Assert.True(h.Session.IsPlaying && h.Session.IsPaused);
        h.EngineRuns.Clear();
        var timeline = h.Arrangement.Timeline;
        var box = TimelineAudioClips.ClipBounds(timeline, h.Track, h.Clip);
        var lane = new Point(box.Right + 40, box.Center.Y);
        Assert.Null(TimelineAudioClips.ClipAt(timeline, lane));
        Click(h.Window, timeline, lane);
        Assert.True(h.Session.IsPaused);
        Assert.NotNull(h.Engine.LastSeek);
        Assert.DoesNotContain(true, h.EngineRuns);
        Assert.False(h.Track.Solo);
    }
}
