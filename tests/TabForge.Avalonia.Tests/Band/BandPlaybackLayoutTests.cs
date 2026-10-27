using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Transport;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the Band view's playback and layout behaviour: a card staff click seeks (like the main score), a paused
/// seek slides the card windows, the fretboards light the playing notes, a playing second line stays in place
/// (new lines only at its end), the cards resize by their bottom edge, and Reset view restores the defaults.
/// Tests: BandView, BandTrackPanel, BandSongCards, MainWindowBandView (the row-height hotkey).
/// </summary>
public sealed class BandPlaybackLayoutTests
{
    private static SongDocument DemoDocument() => new(() => null) { Project = DemoSongFactory.Create() };

    private static void Settle(Window window)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
    }

    [AvaloniaFact]
    public void CardStaffClickSeeksPlaybackToThatCell()
    {
        var song = DemoSongFactory.Create();
        var document = new SongDocument(() => null) { Project = song };
        using var session = new AvaloniaPlaybackSession(new MusicalBarEngine(), new AvaloniaPlaybackSessionTests.FakeUiThread(), () => song);
        var window = new BandWindow(document);
        window.BindPlayback(session);
        window.Show();
        var panel = window.View.PanelFor(window.View.ShownTrackIndices[0])!;
        panel.Staff.RaiseCursorPlaced(4, 0);
        Assert.Equal(4, session.Transport.PlayheadBar);
        window.Close();
    }

    [AvaloniaFact]
    public void PausedSeekSlidesEveryCardWindow()
    {
        var song = DemoSongFactory.Create();
        var document = new SongDocument(() => null) { Project = song };
        using var session = new AvaloniaPlaybackSession(new MusicalBarEngine(), new AvaloniaPlaybackSessionTests.FakeUiThread(), () => song);
        var window = new BandWindow(document);
        window.BindPlayback(session);
        window.Show();
        Settle(window);
        session.PlayOrPause();
        session.Seek(10, 0);
        session.PumpFrame();
        Settle(window);
        session.PlayOrPause();
        Assert.True(session.IsPaused);
        session.Seek(0, 0);
        Settle(window);
        var view = window.View;
        foreach (var index in view.ShownTrackIndices)
        {
            var panel = view.PanelFor(index)!;
            Assert.Equal(BandTrackPanel.SystemWindow(panel.Staff, 0, panel.SystemCount)!.Value.Top, panel.WindowTop, 1);
        }
        window.Close();
    }

    [AvaloniaFact]
    public void FretboardsShowThePlayedNotes()
    {
        var song = DemoSongFactory.Create();
        var document = new SongDocument(() => null) { Project = song };
        using var session = new AvaloniaPlaybackSession(new MusicalBarEngine(), new AvaloniaPlaybackSessionTests.FakeUiThread(), () => song);
        var window = new BandWindow(document);
        window.BindPlayback(session);
        window.Show();
        Settle(window);
        var first = window.View.PanelFor(window.View.ShownTrackIndices[0])!;
        session.PlayOrPause();
        session.Seek(0, 0);
        for (var i = 0; i < 5; i++) session.PumpFrame();
        Settle(window);
        Assert.NotEmpty(first.Instrument.LitMidis());
        window.Close();
    }

    [AvaloniaFact]
    public void PlayingSecondLineStaysInPlaceUntilItsEnd()
    {
        var window = new BandWindow(DemoDocument());
        window.Show();
        Settle(window);
        var view = window.View;
        var panel = view.PanelFor(view.ShownTrackIndices[0])!;
        panel.SystemCount = 2;
        var systems = panel.Staff.Probe.Layout.GetLayout(panel.Staff.Probe.Track!).Systems;
        Assert.True(systems.Count >= 3, $"need three systems to tell paging from sliding, have {systems.Count}");
        var secondBar = systems[1].FirstMeasure;
        panel.ShowSystemsAround(secondBar);
        // The second line plays in place: the window still starts at the first system (it slid there before the fix).
        Assert.Equal(BandTrackPanel.SystemWindowFromFirst(panel.Staff, 0, 2)!.Value.Top, panel.WindowTop, 1);
        var thirdBar = systems[2].FirstMeasure;
        panel.ShowSystemsAround(thirdBar);
        // Past the end of the second line two new lines render, with the playing system at the top.
        Assert.Equal(BandTrackPanel.SystemWindowFromFirst(panel.Staff, 2, 2)!.Value.Top, panel.WindowTop, 1);
        window.Close();
    }

    [AvaloniaFact]
    public void CardBottomEdgeResizeAndResetViewRestoreDefaults()
    {
        var window = new BandWindow(DemoDocument());
        window.Show();
        Settle(window);
        var view = window.View;
        var panel = view.PanelFor(view.ShownTrackIndices[0])!;
        var defaults = panel.SystemCount;
        var grip = panel.GetVisualDescendants().OfType<BandFretboardGrip>().Single(g => g.Name == BandTrackPanel.CardGripName);
        var from = grip.TranslatePoint(new Point(grip.Bounds.Width / 2, grip.Bounds.Height / 2), window)!.Value;
        var to = new Point(from.X, from.Y + 300);
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(to, RawInputModifiers.LeftMouseButton);
        window.MouseUp(to, MouseButton.Left);
        Settle(window);
        Assert.True(panel.SystemCount > defaults, $"systems {panel.SystemCount} vs default {defaults}");
        view.ResetView();
        Settle(window);
        Assert.Equal(defaults, panel.SystemCount);
        // The row-height hotkey path clears a resized height too.
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(to, RawInputModifiers.LeftMouseButton);
        window.MouseUp(to, MouseButton.Left);
        Settle(window);
        Assert.True(panel.SystemCount > defaults);
        view.ResetRowHeights();
        Settle(window);
        Assert.Equal(defaults, panel.SystemCount);
        window.Close();
    }

    [AvaloniaFact]
    public void TimelineInBandLayoutShowsPlayheadAndFollows()
    {
        var song = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        using var playback = new AvaloniaPlaybackSessions(new AvaloniaPlaybackSessionTests.FakeUiThread(), () => new MusicalBarEngine());
        var main = new MainWindow(first: song, playback: playback);
        main.Width = 2000;
        main.Height = 1100;
        main.Show();
        Settle(main);
        main.GetLogicalDescendants().OfType<MenuItem>().Single(m => m.Tag as string == "View.BandView")
            .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Settle(main);
        var arrangement = main.GetVisualDescendants().OfType<ArrangementView>().Single();
        var session = playback.For(song)!;
        session.PlayOrPause();
        session.Seek(3, 0);
        session.PumpFrame();
        Settle(main);
        Assert.Equal(3, arrangement.Timeline.PlayheadBar);
        Assert.False(double.IsNaN(arrangement.Timeline.PlayheadX()));
        var performed = session.Transport.Timeline!.Bars.Count - 1;
        session.Seek(performed, 0);
        session.PumpFrame();
        Settle(main);
        var x = arrangement.Timeline.PlayheadX();
        var sideways = arrangement.GetVisualDescendants().OfType<ScrollViewer>().First(v => v.HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled);
        Assert.True(x >= sideways.Offset.X && x <= sideways.Offset.X + sideways.Viewport.Width,
            $"playhead {x} outside view [{sideways.Offset.X}, {sideways.Offset.X + sideways.Viewport.Width}]");
        main.Close();
        playback.Dispose();
    }

    /// <summary>A playback engine that seeks and reports musical bars (the song's own bar numbers).</summary>
    private sealed class MusicalBarEngine : IPlaybackEngine
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
