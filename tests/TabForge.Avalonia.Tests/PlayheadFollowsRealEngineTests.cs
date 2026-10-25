using System;
using System.Collections.Generic;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TabForge.Audio;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Transport;
using TabForge.Avalonia.Views;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presets;
using TabForge.Views.Score;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the headless proof that a real PlaybackEngine, bound through AvaloniaPlaybackSession, moves the playhead that the score and the
///     arrangement timeline draw: the session position advances over a second of play and both views follow it.
/// Does not own: the audible output (a capture sink takes the MIDI), the frame timer (the test pumps frames itself), the drawing itself.
/// Tests: this class.
/// </summary>
[Collection(TimingCollection.Name)]   // pumps a wall-clock engine and counts the frames it got through: run alone so scheduler contention cannot starve the sample count
public class PlayheadFollowsRealEngineTests
{
    private const double PlaySeconds = 1.0;
    private const double StartSampleSpanMs = 500;   // wall time sampled from the Play press
    private const double SampleDeadlineMs = 4000;   // the longest the sample loop may run when the machine is busy, so the 10-sample assertion still has a fair chance
    private const double StartToleranceMs = 5;      // the frame reads the engine a moment before the sample, so the drawn position trails it
    private const double StartToleranceDip = 0.5;   // sub-pixel rounding of a placed line

    /// <summary>Pressing Play from stopped: both playheads move forward only, and neither draws ahead of the engine's audible position.</summary>
    [AvaloniaTheory]
    [InlineData(0, false, 0, false, 0)]
    [InlineData(150, false, 0, false, 0)]
    [InlineData(0, true, 0, false, 0)]
    [InlineData(0, false, 20, false, 0)]
    [InlineData(0, false, 0, true, 0)]
    [InlineData(150, true, 20, true, 0)]
    [InlineData(0, false, 0, false, 120)]
    [InlineData(0, false, 0, true, 120)]
    [InlineData(0, false, 20, true, 0)]
    public void PressingPlayFromStoppedNeverMovesEitherPlayheadAheadOfTheEngine(int resetMs, bool countIn, int startBar, bool afterAStop, int windowsLatencyMs)
    {
        using var latency = new WindowsMidiLatencyScope(windowsLatencyMs);
        var project = FullDemoSongFactory.Create();
        var document = new ScorePreviewEditor.PreviewDocument(project);
        using var client = new AudioEngineClient(new TabForge.Plugins.PluginTrust());
        using var engine = new PlaybackEngine(new RoutedMidiOutput(new NullSink(resetMs), client));
        using var session = new AvaloniaPlaybackSession(engine, new InlineUi(), () => project);
        var score = new ScorePreviewView();
        var arrangement = new ArrangementView(document.Project, document);
        score.BindPlayback(session);
        arrangement.BindPlayback(session);
        var window = new Window { Width = 1200, Height = 800, Content = new StackPanel { Children = { score, arrangement } } };
        window.Show();
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        session.SetCountIn(countIn);
        session.Seek(startBar, 0);
        if (afterAStop)
        {
            session.PlayOrPause();
            for (var warm = 0; warm < 40; warm++) { session.PumpFrame(); global::Avalonia.Threading.Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); }
            session.Stop();
        }

        var engineFrame = new PlaybackPosition();
        var previousScoreX = double.NegativeInfinity;
        var previousTimelineX = double.NegativeInfinity;
        var previousDrawnMs = double.NegativeInfinity;
        var heldEngineMs = double.NaN;
        var samples = 0;
        session.PlayOrPause();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        // The span gives the samples; a busy machine may not get ten in it, so keep pumping (the same per-frame checks) up to the deadline.
        while (clock.Elapsed.TotalMilliseconds < StartSampleSpanMs || (samples < 10 && clock.Elapsed.TotalMilliseconds < SampleDeadlineMs))
        {
            session.PumpFrame();
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var at = clock.Elapsed.TotalMilliseconds;
            var drawnMs = session.Playhead.ElapsedMs;
            var engineMs = engine.AudiblePlayhead(engineFrame).ElapsedMs;
            var scoreX = score.Probe.Play.PlayheadLine(score.Probe) is { } line ? line.X : double.NaN;
            var timelineX = arrangement.Timeline.PlayheadX();
            samples++;

            Assert.True(drawnMs >= previousDrawnMs - StartToleranceMs, $"the drawn position moved back from {previousDrawnMs:0.0} to {drawnMs:0.0} ms at {at:0} ms");
            Assert.True(drawnMs <= engineMs + StartToleranceMs, $"the drawn position {drawnMs:0.0} ms is ahead of the engine's {engineMs:0.0} ms at {at:0} ms");
            // While the engine still holds its first position, the playhead shows the start bar, not a bar the previous song's timeline maps to.
            if (double.IsNaN(heldEngineMs)) heldEngineMs = engineMs;
            if (engineMs == heldEngineMs) Assert.True(session.Playhead.Bar == startBar, $"the playhead shows bar {session.Playhead.Bar} while the start at bar {startBar} holds, at {at:0} ms");
            if (!double.IsNaN(scoreX))
            {
                Assert.True(scoreX >= previousScoreX - StartToleranceDip, $"the score playhead moved back from {previousScoreX:0.0} to {scoreX:0.0} at {at:0} ms");
                previousScoreX = scoreX;
            }
            if (!double.IsNaN(timelineX))
            {
                Assert.True(timelineX >= previousTimelineX - StartToleranceDip, $"the timeline playhead moved back from {previousTimelineX:0.0} to {timelineX:0.0} at {at:0} ms");
                previousTimelineX = timelineX;
            }
            previousDrawnMs = drawnMs;
            Thread.Sleep(1);
        }
        session.Stop();
        window.Close();
        Assert.True(samples >= 10, $"frames sampled: {samples}");
    }

    [AvaloniaFact]
    public void PlayheadAdvancesWithRealEngineAndBothViewsFollowIt()
    {
        var project = DemoSongFactory.Create();
        var document = new ScorePreviewEditor.PreviewDocument(project);
        using var engine = new PlaybackEngine(new NullSink());
        using var session = new AvaloniaPlaybackSession(engine, new InlineUi(), () => project);
        var score = new ScorePreviewView();
        var arrangement = new ArrangementView(document.Project, document);
        score.BindPlayback(session);
        arrangement.BindPlayback(session);
        // Both views follow playback only while attached, so they are shown in a window.
        var window = new Window { Width = 1200, Height = 800, Content = new StackPanel { Children = { score, arrangement } } };
        window.Show();

        var frames = 0;
        var barChanges = 0;
        session.PlayheadFrame += () => frames++;
        session.PlayheadBarChanged += () => barChanges++;
        var startMs = double.NaN;
        var bars = new HashSet<int>();

        session.PlayOrPause();
        var deadline = DateTime.UtcNow.AddSeconds(PlaySeconds);
        while (DateTime.UtcNow < deadline)
        {
            session.PumpFrame();
            if (double.IsNaN(startMs) && session.Transport.PlayheadBar >= 0) startMs = session.Transport.PlayheadMs;
            if (session.Transport.PlayheadBar >= 0) bars.Add(session.Transport.PlayheadBar);
            // The start compiles on a worker and is handed back through the UI thread's queue, which this loop runs.
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Thread.Sleep(8);
        }
        var endMs = session.Transport.PlayheadMs;
        var scoreActiveWhilePlaying = score.Probe.Play.Active;
        session.Stop();

        Assert.True(frames > 20, $"frames pumped: {frames}");
        Assert.False(double.IsNaN(startMs), "the playhead bar was never set by the real engine");
        Assert.True(endMs - startMs >= 500, $"playhead advanced {endMs - startMs} ms");
        Assert.True(bars.Count >= 1 && barChanges >= 1, $"bars seen {bars.Count}, bar changes {barChanges}");
        Assert.Equal(session.Transport.PlayheadBar, arrangement.Timeline.PlayheadBar);
        Assert.True(scoreActiveWhilePlaying, "the score playhead is active during play");
        window.Close();
    }

    /// <summary>Sets the Windows MIDI latency for one test and restores the previous value when it ends.</summary>
    private sealed class WindowsMidiLatencyScope : IDisposable
    {
        private readonly int _previous = RoutedMidiOutput.WindowsMidiLatencyMs;
        public WindowsMidiLatencyScope(int latencyMs) => RoutedMidiOutput.WindowsMidiLatencyMs = latencyMs;
        public void Dispose() => RoutedMidiOutput.WindowsMidiLatencyMs = _previous;
    }

    /// <summary>A MIDI sink that drops every message; the test checks the playhead, not the sound.</summary>
    private sealed class NullSink : IMidiOutput
    {
        private readonly int _resetMs;
        public NullSink(int resetMs = 0) => _resetMs = resetMs;
        public IReadOnlyList<MidiOutputDeviceInfo> Devices { get; } = Array.Empty<MidiOutputDeviceInfo>();
        public void Send(int deviceId, int status, int data1, int data2) { }
        public void ResetAll() { if (_resetMs > 0) Thread.Sleep(_resetMs); }
        public void Close() { }
        public void Dispose() { }
    }

    /// <summary>A UI thread that runs work inline, as the other headless transport tests do.</summary>
    private sealed class InlineUi : TabForge.Platform.IUiThread
    {
        public bool CheckAccess() => true;
        public bool IsShuttingDown => false;
        public void Post(Action work, TabForge.Platform.UiPriority priority = TabForge.Platform.UiPriority.Normal) => work();
        public System.Threading.Tasks.Task InvokeAsync(Action work, TabForge.Platform.UiPriority priority = TabForge.Platform.UiPriority.Normal) { work(); return System.Threading.Tasks.Task.CompletedTask; }
        public System.Threading.Tasks.Task<T> InvokeAsync<T>(Func<T> work, TabForge.Platform.UiPriority priority = TabForge.Platform.UiPriority.Normal) => System.Threading.Tasks.Task.FromResult(work());
        public System.Threading.Tasks.Task YieldAsync(TabForge.Platform.UiPriority priority) => System.Threading.Tasks.Task.CompletedTask;
        public void Invoke(Action work, TabForge.Platform.UiPriority priority = TabForge.Platform.UiPriority.Send) => work();
        public T Invoke<T>(Func<T> work, TabForge.Platform.UiPriority priority = TabForge.Platform.UiPriority.Send) => work();
        public TabForge.Platform.IUiTimer CreateTimer(TimeSpan interval, TabForge.Platform.UiPriority priority = TabForge.Platform.UiPriority.Background) => new StoppedTimer { Interval = interval };
    }

    private sealed class StoppedTimer : TabForge.Platform.IUiTimer
    {
        public TimeSpan Interval { get; set; }
        public bool IsEnabled => false;
        public event EventHandler? Tick { add { } remove { } }
        public void Start() { }
        public void Stop() { }
    }
}
