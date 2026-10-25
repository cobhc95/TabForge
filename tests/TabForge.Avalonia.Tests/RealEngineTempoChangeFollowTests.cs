using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using TabForge.Audio;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Transport;
using TabForge.Avalonia.Views;
using TabForge.Playback;
using TabForge.Presets;
using TabForge.Views.Score;
using Xunit;
using Xunit.Abstractions;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the demo song played at 1x through a real PlaybackEngine (its own clock, audible position and holds) across the first tempo change
///     (bar 91, 150 to 112 bpm) and the ramp back (bar 102): on every frame the drawn bar matches the engine's, the score lights the notes
///     sounding at the drawn position and the timeline keeps the drawn bar in view.
/// Does not own: the sound (a sink drops the MIDI), the fake-engine pass over the whole song (DemoSongEndPlaybackTests).
/// Tests: this class.
/// </summary>
public class RealEngineTempoChangeFollowTests
{
    private const int FromBar = 84;          // 0-based: bar 85
    private const int ThroughBar = 105;      // 0-based: bar 106
    private const double TimeLimitMs = 60_000;
    private const int SettleFrames = 6;      // frames the timeline follow may take after the bar changes
    private const double MaxDrawnBehindEngineMs = 40;   // the drawn position may trail the engine reading by a frame or two, never by a beat

    private readonly ITestOutputHelper _out;
    public RealEngineTempoChangeFollowTests(ITestOutputHelper output) => _out = output;

    [AvaloniaFact]
    public void PlayingAcrossTheTempoChangesKeepsTheScoreLightsAndTheTimelineOnTheEngineBar()
    {
        var project = FullDemoSongFactory.Create();
        var document = new ScorePreviewEditor.PreviewDocument(project);
        using var client = new AudioEngineClient(new TabForge.Plugins.PluginTrust());
        using var engine = new PlaybackEngine(new RoutedMidiOutput(new DroppingSink(), client));
        using var session = new AvaloniaPlaybackSession(engine, new AvaloniaPlaybackSessionTests.FakeUiThread(), () => project);
        var score = new ScorePreviewView();
        var arrangement = new ArrangementView(document.Project, document);
        score.BindPlayback(session);
        arrangement.BindPlayback(session);
        var window = new Window { Width = 1600, Height = 1000, Content = new StackPanel { Children = { score, arrangement } } };
        window.Show();
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var sideways = arrangement.Timeline.FindAncestorOfType<ScrollViewer>()!;
        var track = project.Tracks[score.Probe.TrackIndex];

        session.Seek(FromBar, 0);
        session.PlayOrPause();
        var log = new StringBuilder();
        var failures = 0;
        var lastBar = -1;
        var settle = 0;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.Elapsed.TotalMilliseconds < TimeLimitMs)
        {
            session.PumpFrame();
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Thread.Sleep(7);
            var timeline = session.Transport.Timeline;
            if (timeline is null || session.Transport.PlayheadBar < 0) continue;
            var drawn = session.Playhead;
            if (drawn.Bar > ThroughBar) break;
            if (drawn.Bar != lastBar) { settle = SettleFrames; lastBar = drawn.Bar; log.AppendLine($"bar {drawn.Bar + 1} drawnMs {drawn.ElapsedMs:0} engineMs {session.Transport.PlayheadMs:0} engineBar {session.Transport.PlayheadBar + 1} late {session.LateNotesDropped}"); }
            else if (settle > 0) settle--;
            var notes = timeline.NotesFor(score.Probe.TrackIndex);
            var expected = NoteTimeline.SoundingAt(notes, drawn.ElapsedMs).Select(n => (n.Bar, n.Cell, n.StringIndex)).ToHashSet();
            var lit = score.Probe.Play.Sounding;
            var x = arrangement.Timeline.PlayheadX();
            var inView = x >= sideways.Offset.X - 1 && x <= sideways.Offset.X + sideways.Viewport.Width + 1;
            var problems = new List<string>();
            if (Math.Abs(drawn.ElapsedMs - session.Transport.PlayheadMs) > MaxDrawnBehindEngineMs) problems.Add($"drawn {drawn.ElapsedMs:0} engine {session.Transport.PlayheadMs:0}");
            if (score.Probe.Play.Measure != drawn.Bar) problems.Add($"score bar {score.Probe.Play.Measure + 1}");
            if (!expected.SetEquals(lit)) problems.Add($"lit {lit.Count} expected {expected.Count}");
            if (arrangement.Timeline.PlayheadBar != drawn.Bar) problems.Add($"timeline bar {arrangement.Timeline.PlayheadBar + 1}");
            if (settle == 0 && !inView) problems.Add($"timeline x {x:0} outside {sideways.Offset.X:0}..{sideways.Offset.X + sideways.Viewport.Width:0}");
            if (problems.Count == 0) continue;
            failures++;
            if (failures <= 40) log.AppendLine($"  at {clock.Elapsed.TotalMilliseconds:0} bar {drawn.Bar + 1} ms {drawn.ElapsedMs:0}: {string.Join("; ", problems)}");
        }
        var reached = session.Playhead.Bar;
        session.Stop();
        window.Close();
        _out.WriteLine(log.ToString());
        Assert.True(reached > ThroughBar, $"playback reached bar {reached + 1} only\n{log}");
        Assert.True(failures == 0, $"{failures} bad frames:\n{log}");
        Assert.True(track.Measures.Count > ThroughBar);
    }

    /// <summary>A MIDI sink that drops every message; the test checks the playhead, not the sound.</summary>
    private sealed class DroppingSink : IMidiOutput
    {
        public IReadOnlyList<MidiOutputDeviceInfo> Devices { get; } = Array.Empty<MidiOutputDeviceInfo>();
        public void Send(int deviceId, int status, int data1, int data2) { }
        public void ResetAll() { }
        public void Close() { }
        public void Dispose() { }
    }
}
