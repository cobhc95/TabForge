using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Transport;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Platform;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the playback session checks against a fake engine and a fake UI thread: play, stop, pause, seek, loop, the frame pump, the finish
///     flag, dispose on tab close, and the transport bar wiring.
/// Does not own: the real engine (the WPF PlaybackEngine is not reachable from Avalonia yet) or the MIDI output.
/// Tests: this class.
/// </summary>
public class AvaloniaPlaybackSessionTests
{
    [Fact]
    public void PlayReportsPositionStopSilencesAndSeekMovesTheEngine()
    {
        var engine = new FakeEngine();
        var ui = new FakeUiThread();
        using var session = new AvaloniaPlaybackSession(engine, ui, () => new SongProject());

        session.PlayOrPause();
        Assert.True(engine.Started);
        Assert.True(ui.FrameTimer.IsEnabled);

        engine.Report(new PlaybackPosition { Bar = 2, Cell = 3, BarFraction = 0.4, ElapsedMs = 1000 });
        ui.FrameTimer.Fire();
        Assert.Equal(2, session.Transport.PlayheadBar);
        Assert.Equal(3, session.Transport.PlayheadCell);
        Assert.Equal(1000, session.Transport.PlayheadMs);

        session.Seek(5, 1);
        Assert.Equal((5, 1), engine.LastSeek);

        session.Stop();
        Assert.True(engine.Stopped);
        Assert.False(ui.FrameTimer.IsEnabled);
        Assert.Equal(-1, session.Transport.PlayheadBar);
    }

    [Fact]
    public void StateAndPlayheadEventsFireOnlyForRealChanges()
    {
        var engine = new FakeEngine();
        var ui = new FakeUiThread();
        using var session = new AvaloniaPlaybackSession(engine, ui, () => new SongProject());
        var stateChanges = 0;
        var playheadChanges = 0;
        session.StateChanged += () => stateChanges++;
        session.PlayheadBarChanged += () => playheadChanges++;

        session.PlayOrPause();
        Assert.Equal(1, stateChanges);

        engine.Report(new PlaybackPosition { Bar = 2, Cell = 0, BarFraction = 0, ElapsedMs = 1000 });
        ui.FrameTimer.Fire();
        Assert.Equal(1, playheadChanges);

        // A frame with no new report draws nothing, so no event.
        ui.FrameTimer.Fire();
        Assert.Equal(1, playheadChanges);
        Assert.Equal(1, stateChanges);

        session.SetLoop(true);
        Assert.Equal(2, stateChanges);
        Assert.True(session.Loop);

        session.Stop();
        Assert.Equal(3, stateChanges);
        Assert.Equal(2, playheadChanges);
    }

    [Fact]
    public void PlayOrPauseTogglesPauseAndStopsTheFrameTimerWhilePaused()
    {
        var engine = new FakeEngine();
        var ui = new FakeUiThread();
        using var session = new AvaloniaPlaybackSession(engine, ui, () => new SongProject());

        session.PlayOrPause();
        session.PlayOrPause();
        Assert.True(engine.Paused);
        Assert.False(ui.FrameTimer.IsEnabled);

        session.PlayOrPause();
        Assert.True(engine.Resumed);
        Assert.True(ui.FrameTimer.IsEnabled);
    }

    [Fact]
    public void CompiledTimelineMovesThePlayheadToItsStart()
    {
        var engine = new FakeEngine();
        using var session = new AvaloniaPlaybackSession(engine, new FakeUiThread(), () => new SongProject());

        engine.RaiseTimelineChanged(new ScoreTimeline { PlayFromMs = 250 });
        engine.RaiseTimelineRevised(new ScoreTimeline { PlayFromMs = 250 });

        Assert.Equal(250, session.Transport.PlayheadMs);
        Assert.Equal(-1, session.Transport.PlayheadBar);
        Assert.NotNull(session.Transport.Timeline);
    }

    [Fact]
    public void LoopIsPassedToTheEngineAndTheNextStart()
    {
        var engine = new FakeEngine();
        using var session = new AvaloniaPlaybackSession(engine, new FakeUiThread(), () => new SongProject());

        session.SetLoop(true);
        session.PlayOrPause();

        Assert.True(engine.LoopSet);
        Assert.True(engine.LastOptions!.Loop);
        Assert.True(session.Loop);
    }

    [Fact]
    public void FinishedSongStopsTheFrameTimerAndClearsThePlayhead()
    {
        var engine = new FakeEngine();
        var ui = new FakeUiThread();
        using var session = new AvaloniaPlaybackSession(engine, ui, () => new SongProject());
        session.PlayOrPause();
        engine.Report(new PlaybackPosition { Bar = 1, ElapsedMs = 500 });
        ui.FrameTimer.Fire();

        engine.FinishFromScheduler();
        ui.FrameTimer.Fire();

        Assert.False(session.IsPlaying);
        Assert.False(ui.FrameTimer.IsEnabled);
        Assert.Equal(-1, session.Transport.PlayheadBar);
    }

    [Fact]
    public void FrameTickAllocatesNothingOnceWarm()
    {
        var engine = new FakeEngine();
        var ui = new FakeUiThread();
        using var session = new AvaloniaPlaybackSession(engine, ui, () => new SongProject());
        session.PlayOrPause();
        var position = new PlaybackPosition();
        for (var i = 0; i < 10; i++) { engine.Report(position); ui.FrameTimer.Fire(); }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            position.Bar = i % 8;
            engine.Report(position);
            ui.FrameTimer.Fire();
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void SessionsKeepOneSessionPerDocumentAndDisposeItOnTabClose()
    {
        var engines = new List<FakeEngine>();
        var ui = new FakeUiThread();
        using var sessions = new AvaloniaPlaybackSessions(ui, () => { var e = new FakeEngine(); engines.Add(e); return e; });
        var document = new SongDocument(() => null);

        var first = sessions.For(document);
        Assert.Same(first, sessions.For(document));
        first!.PlayOrPause();

        sessions.Close(document);

        Assert.True(engines[0].Disposed);
        Assert.True(engines[0].Stopped);
        Assert.False(ui.FrameTimer.IsEnabled);
        Assert.NotSame(first, sessions.For(document));
    }

    [Fact]
    public void TabCloseAnnouncesTheSessionBeforeDisposingIt()
    {
        var engine = new FakeEngine();
        using var sessions = new AvaloniaPlaybackSessions(new FakeUiThread(), () => engine);
        var document = new SongDocument(() => null);
        var session = sessions.For(document);
        AvaloniaPlaybackSession? announced = null; var disposedThen = true;
        sessions.SessionClosing += s => { announced = s; disposedThen = engine.Disposed; };

        sessions.Close(document);

        Assert.Same(session, announced);
        Assert.False(disposedThen);   // a recording of this song can still stop its playback
    }

    [Fact]
    public void WithoutAnEngineEveryDocumentHasNoSession()
    {
        using var sessions = new AvaloniaPlaybackSessions(new FakeUiThread(), null);
        Assert.Null(sessions.For(new SongDocument(() => null)));
    }

    [AvaloniaFact]
    public void TransportBarWiresOnlyTheBoundPlaybackButtons()
    {
        var bar = new TransportBarView();
        var buttons = BarButtons(bar);
        Assert.All(System.Linq.Enumerable.Where(buttons, b => !b.Local), b => Assert.False(b.IsEnabled)); // local buttons work without a session

        var engine = new FakeEngine();
        using var session = new AvaloniaPlaybackSession(engine, new FakeUiThread(), () => new SongProject());
        bar.Bind(session);

        Assert.True(Button(buttons, "Play or pause").IsEnabled);
        Assert.True(Button(buttons, "Stop playback").IsEnabled);
        Assert.True(Button(buttons, "Rewind to beginning").IsEnabled);
        Assert.False(Button(buttons, "Loop enable").IsEnabled);   // a window command (Transport.Loop, the loop link), wired by BindCommands
        Assert.False(Button(buttons, "Record").IsEnabled);
        Assert.False(Button(buttons, "Next section").IsEnabled);

        Button(buttons, "Play or pause").Pressed!.Invoke();
        Assert.True(engine.Started);
        Button(buttons, "Stop playback").Pressed!.Invoke();
        Assert.True(engine.Stopped);

        bar.Bind(null);
        Assert.All(System.Linq.Enumerable.Where(buttons, b => !b.Local), b => Assert.False(b.IsEnabled));
    }

    [AvaloniaFact]
    public void MainWindowOpenedWithASessionEnablesRewindPlayStopAndLoop()
    {
        var engine = new FakeEngine();
        using var sessions = new AvaloniaPlaybackSessions(new FakeUiThread(), () => engine);
        var window = new MainWindow(first: new SongDocument(() => null) { Project = TabForge.Presets.DemoSongFactory.Create() }, playback: sessions);
        window.Show();
        var buttons = BarButtons(window.GetLogicalDescendants().OfType<TransportBarView>().Single());

        Assert.True(Button(buttons, "Play or pause").IsEnabled);
        Assert.True(Button(buttons, "Stop playback").IsEnabled);
        Assert.True(Button(buttons, "Rewind to beginning").IsEnabled);
        Assert.True(Button(buttons, "Loop enable").IsEnabled);
    }

    private static List<TransportBarView.TransportButton> BarButtons(TransportBarView bar)
    {
        var row = (global::Avalonia.Controls.WrapPanel)bar.Child!;
        var list = new List<TransportBarView.TransportButton>();
        foreach (var group in row.Children.OfType<global::Avalonia.Controls.StackPanel>())
            foreach (var child in group.Children)
                if (child is TransportBarView.TransportButton button) list.Add(button);
        return list;
    }

    private static TransportBarView.TransportButton Button(List<TransportBarView.TransportButton> buttons, string name) =>
        buttons.Find(b => b.Item.Name == name) ?? throw new InvalidOperationException("No button " + name);

    /// <summary>An engine that records the calls and lets a test play the scheduler's side (reports and finish).</summary>
    [Fact]
    public void KeyboardSongPositionFollowsTheSessionPlayhead()
    {
        var engine = new FakeEngine();
        var ui = new FakeUiThread();
        using var session = new AvaloniaPlaybackSession(engine, ui, () => new SongProject());
        var position = new SessionKeyboardSongPosition(session);
        session.PlayOrPause();

        engine.Report(new PlaybackPosition { Bar = 2, Cell = 0, BarFraction = 0.5, ElapsedMs = 4321 });
        ui.FrameTimer.Fire();

        Assert.Equal(session.Transport.PlayheadMs, position.SongMs);
        Assert.Equal(4321, position.SongMs);
    }

    internal sealed class FakeEngine : IPlaybackEngine
    {
        private Action<PlaybackPosition>? _onPosition;
        private Action? _onFinished;
        private PlaybackPosition _lastReport = new();

        public bool Started { get; private set; }
        public int Starts { get; private set; }
        public bool Stopped { get; private set; }
        public bool Paused { get; private set; }
        public bool Resumed { get; private set; }
        public bool LoopSet { get; private set; }
        public bool Disposed { get; private set; }
        public int Rearms { get; private set; }
        public void RearmChannelSetup() => Rearms++;
        public (int Bar, int Cell)? LastSeek { get; private set; }
        public PlaybackOptions? LastOptions { get; private set; }
        public bool IsPlaying { get; private set; }
        public bool IsPaused { get; private set; }

        public event Action<ScoreTimeline>? TimelineChanged;
        public event Action<ScoreTimeline>? TimelineRevised;

        public void Start(SongProject project, PlaybackOptions options, Action<PlaybackPosition> onPosition, Action onFinished, bool startPaused = false)
        {
            Started = true;
            Starts++;
            IsPlaying = true;
            LastOptions = options;
            _onPosition = onPosition;
            _onFinished = onFinished;
        }

        public void Stop()
        {
            Stopped = true;
            IsPlaying = false;
            IsPaused = false;
        }

        public void Pause() { Paused = true; IsPaused = true; }
        public void Resume() { Resumed = true; IsPaused = false; }
        public void Seek(SongProject project, int bar, int cell) => LastSeek = (bar, cell);
        public void SetLoop(bool loop) => LoopSet = true;

        /// <summary>The loop bounds the transport last handed to the running playback (the engine wraps at their end).</summary>
        public (int StartBar, int EndBar, int StartCell, int EndCell)? LiveLoopRange { get; private set; }
        public void SetLoopRange(int startBar, int endBar, int startCell, int endCell) => LiveLoopRange = (startBar, endBar, startCell, endCell);

        public PlaybackPosition Playhead() => new()
        {
            Bar = _lastReport.Bar,
            Cell = _lastReport.Cell,
            BarFraction = _lastReport.BarFraction,
            ElapsedMs = _lastReport.ElapsedMs,
        };
        // The real engine fills the caller's frame with the audible position; this fake shows the last report.
        public PlaybackPosition AudiblePlayhead(PlaybackPosition frame)
        {
            frame.Bar = _lastReport.Bar;
            frame.Cell = _lastReport.Cell;
            frame.BarFraction = _lastReport.BarFraction;
            frame.ElapsedMs = _lastReport.ElapsedMs;
            return frame;
        }

        public void Dispose() => Disposed = true;

        public void RaiseTimelineChanged(ScoreTimeline timeline) => TimelineChanged?.Invoke(timeline);
        public void RaiseTimelineRevised(ScoreTimeline timeline) => TimelineRevised?.Invoke(timeline);

        /// <summary>Plays the scheduler's side: a position report, from the scheduler's thread in the real engine.</summary>
        public void Report(PlaybackPosition position)
        {
            _lastReport = position;
            _onPosition?.Invoke(position);
        }

        public void FinishFromScheduler()
        {
            IsPlaying = false;
            _onFinished?.Invoke();
        }
    }

    internal sealed class FakeUiThread : IUiThread
    {
        public FakeTimer FrameTimer { get; } = new();

        public bool CheckAccess() => true;
        public bool IsShuttingDown => false;
        public void Post(Action work, UiPriority priority = UiPriority.Normal) => work();
        public Task InvokeAsync(Action work, UiPriority priority = UiPriority.Normal) { work(); return Task.CompletedTask; }
        public Task<T> InvokeAsync<T>(Func<T> work, UiPriority priority = UiPriority.Normal) => Task.FromResult(work());
        public Task YieldAsync(UiPriority priority) => Task.CompletedTask;
        public void Invoke(Action work, UiPriority priority = UiPriority.Send) => work();
        public T Invoke<T>(Func<T> work, UiPriority priority = UiPriority.Send) => work();
        public IUiTimer CreateTimer(TimeSpan interval, UiPriority priority = UiPriority.Background)
        {
            FrameTimer.Interval = interval;
            return FrameTimer;
        }
    }

    /// <summary>A timer the test fires by hand; <see cref="Fire"/> is one display frame.</summary>
    internal sealed class FakeTimer : IUiTimer
    {
        public TimeSpan Interval { get; set; }
        public bool IsEnabled { get; private set; }
        public event EventHandler? Tick;

        public void Start() => IsEnabled = true;
        public void Stop() => IsEnabled = false;

        public void Fire()
        {
            if (IsEnabled) Tick?.Invoke(this, EventArgs.Empty);
        }
    }
}
