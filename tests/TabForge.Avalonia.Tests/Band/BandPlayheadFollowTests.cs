using System;
using System.Linq;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Transport;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the Band view's playhead while a song plays: after a seek to a bar, every shown card's playhead is on that bar.
/// Does not own: the vertical follow maths (BandPlayheadFollow's callers and ScorePlayheadFollower), the card contents (BandCardTests) or the transport.
/// Tests: this class.
/// </summary>
public class BandPlayheadFollowTests
{
    private const int SeekedBar = 3;

    [AvaloniaFact]
    public void AfterASeekToABarEveryCardShowsThatBar()
    {
        var song = DemoSongFactory.Create();
        var document = new SongDocument(() => null) { Project = song };
        using var session = new AvaloniaPlaybackSession(new SeekableMomentEngine(), new AvaloniaPlaybackSessionTests.FakeUiThread(), () => song);
        var window = new BandWindow(document);
        window.BindPlayback(session);
        window.Show();
        session.PlayOrPause();
        session.Seek(SeekedBar, 0);
        session.PumpFrame();
        var view = window.View;
        Assert.True(view.ShownTrackIndices.Count >= 2);
        foreach (var index in view.ShownTrackIndices)
            Assert.Equal(SeekedBar, view.PanelFor(index)!.Staff.Probe.Play.Measure);
        window.Close();
    }

    [AvaloniaFact]
    public void EveryCardShowsAWindowOfSystemsThatSlidesToThePlayingSystemTogether()
    {
        var song = DemoSongFactory.Create();
        var document = new SongDocument(() => null) { Project = song };
        using var session = new AvaloniaPlaybackSession(new SeekableMomentEngine(), new AvaloniaPlaybackSessionTests.FakeUiThread(), () => song);
        var window = new BandWindow(document);
        window.BindPlayback(session);
        window.Show();
        var view = window.View;
        var lateBar = song.Tracks[0].Measures.Count - 1;   // the last bar, in a later system than the first window
        var cards = view.ShownTrackIndices.Select(index => view.PanelFor(index)!).ToList();
        // Each card is a window of systems, not the whole song.
        foreach (var card in cards)
        {
            // The first window starts at the first system, less the unused room above its tab.
            Assert.Equal(BandTrackPanel.SystemWindow(card.Staff, 0, card.SystemCount)!.Value.Top, card.WindowTop, 1);
            Assert.True(card.WindowHeight < card.Staff.Bounds.Height, $"window {card.WindowHeight} of staff {card.Staff.Bounds.Height}");
        }

        session.PlayOrPause();
        session.Seek(lateBar, 0);
        session.PumpFrame();
        window.UpdateLayout();
        // Every card slid to the playing bar's system: the cards share their bars, so it is the same system in each.
        foreach (var card in cards)
        {
            Assert.True(card.WindowTop > 0, $"top {card.WindowTop}, wanted {BandTrackPanel.SystemWindow(card.Staff, lateBar, card.SystemCount)}, playing {card.Staff.Probe.Play.Active} bar {card.Staff.Probe.Play.Measure} of {lateBar}, extent {card.Staff.Bounds.Height}");
            Assert.Equal(BandTrackPanel.SystemWindow(card.Staff, session.Playhead.Bar, card.SystemCount)!.Value.Top, card.WindowTop, 1);
        }
        window.Close();
    }

    /// <summary>A playback engine that compiles the timeline at a start and holds the bar it was last seeked to as its audible position.</summary>
    private sealed class SeekableMomentEngine : IPlaybackEngine
    {
        private ScoreTimeline? _timeline;
        private PlaybackPosition _moment = new();

        public bool IsPlaying { get; private set; }
        public bool IsPaused { get; private set; }

        public event Action<ScoreTimeline>? TimelineChanged;
        public event Action<ScoreTimeline>? TimelineRevised { add { } remove { } }

        public void Start(SongProject project, PlaybackOptions options, Action<PlaybackPosition> onPosition, Action onFinished, bool startPaused = false)
        {
            _timeline = MidiTimelineBuilder.Build(project, options);
            IsPlaying = true;
            IsPaused = startPaused;
            MoveTo(0);
            TimelineChanged?.Invoke(_timeline);
        }

        public void Stop() { IsPlaying = false; IsPaused = false; }
        public void Pause() => IsPaused = true;
        public void Resume() => IsPaused = false;
        public void Seek(SongProject project, int bar, int cell) => MoveTo(bar);
        public void SetLoop(bool loop) { }
        public PlaybackPosition Playhead() => _moment;

        public PlaybackPosition AudiblePlayhead(PlaybackPosition frame)
        {
            frame.Bar = _moment.Bar;
            frame.Cell = _moment.Cell;
            frame.BarFraction = _moment.BarFraction;
            frame.ElapsedMs = _moment.ElapsedMs;
            return frame;
        }

        public void Dispose() { }

        private void MoveTo(int bar)
        {
            var bars = _timeline!.Bars;
            var span = bars[Math.Clamp(bar, 0, bars.Count - 1)];
            _moment = new PlaybackPosition { Bar = span.Bar, Cell = 0, BarFraction = 0, ElapsedMs = span.StartMs };
        }
    }
}
