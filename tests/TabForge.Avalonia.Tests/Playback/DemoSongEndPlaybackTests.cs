using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Transport;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Playback;
using TabForge.Presets;
using Xunit;
using Xunit.Abstractions;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the demo song played continuously from bar 1 to its end in the real main window (the D.S. al Coda, the 2/4 bar, the late tempo changes):
///     on every frame the score lights exactly the notes sounding at the drawn position, and the timeline keeps the playing bar in view.
/// Does not own: the engine's own clock (a fake engine reports the song time) or the sound.
/// Tests: this class.
/// </summary>
public class DemoSongEndPlaybackTests
{
    private const double StepMs = 60;     // song time per frame
    private const int SettleFrames = 6;   // frames the timeline follow may take after a jump

    private readonly ITestOutputHelper _out;
    public DemoSongEndPlaybackTests(ITestOutputHelper output) => _out = output;

    [AvaloniaTheory]
    [InlineData("Lead Gtr")]
    [InlineData("Rhythm Gtr L")]
    public void PlayingTheDemoSongToItsEndKeepsTheScoreLightsAndTheTimelineOnTheSoundingBar(string trackPrefix)
    {
        var engine = new AvaloniaPlaybackSessionTests.FakeEngine();
        using var sessions = new AvaloniaPlaybackSessions(new AvaloniaUiThread(Dispatcher.UIThread), () => engine);
        var document = new SongDocument(() => null) { Project = FullDemoSongFactory.Create() };
        var trackIndex = document.Project.Tracks.FindIndex(t => t.Name.StartsWith(trackPrefix, System.StringComparison.OrdinalIgnoreCase));
        Assert.True(trackIndex >= 0);
        document.TrackIndex = trackIndex;
        var window = new MainWindow(first: document, playback: sessions) { Width = 1600, Height = 1000 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var score = window.GetVisualDescendants().OfType<ScorePreviewView>().Single();
        var arrangement = window.GetVisualDescendants().OfType<ArrangementView>().Single();
        var sideways = arrangement.Timeline.FindAncestorOfType<ScrollViewer>()!;
        var session = sessions.For(document)!;
        Assert.Equal(trackIndex, score.Probe.TrackIndex);

        session.PlayOrPause();
        var timeline = MidiTimelineBuilder.Build(document.Project, engine.LastOptions!);
        engine.RaiseTimelineChanged(timeline);
        var notes = timeline.NotesFor(trackIndex);
        var track = document.Project.Tracks[trackIndex];

        var log = new StringBuilder();
        var failures = 0;
        var lastBar = -1;
        var settle = 0;
        for (var ms = timeline.PlayFromMs; ms < timeline.TotalMs - 1; ms += StepMs)
        {
            engine.Report(PlayheadMapper.Map(timeline, ms, 0, 0));
            session.PumpFrame();
            Dispatcher.UIThread.RunJobs();
            var drawn = session.Playhead;
            var performed = timeline.Bars.FindLastIndex(b => b.StartMs <= drawn.ElapsedMs + 1e-6);
            if (drawn.Bar != lastBar) { settle = SettleFrames; lastBar = drawn.Bar; }
            else if (settle > 0) settle--;

            var expected = NoteTimeline.SoundingAt(notes, drawn.ElapsedMs).Select(n => (n.Bar, n.Cell, n.StringIndex)).ToHashSet();
            NoteTimeline.ForEachContinuationAt(notes, drawn.ElapsedMs, 0, (c, _) => expected.Add((c.Bar, c.Cell, c.StringIndex)));   // tie and legato slide beats light too
            var lit = score.Probe.Play.Sounding;
            var drawable = lit.Count(k => Drawn(track, k));
            var x = arrangement.Timeline.PlayheadX();
            var inView = x >= sideways.Offset.X - 1 && x <= sideways.Offset.X + sideways.Viewport.Width + 1;
            var problems = new List<string>();
            if (score.Probe.Play.Measure != drawn.Bar) problems.Add($"score bar {score.Probe.Play.Measure}");
            if (!expected.SetEquals(lit)) problems.Add($"lit {lit.Count} expected {expected.Count}");
            if (drawable != lit.Count) problems.Add($"undrawn lit keys {lit.Count - drawable}");
            if (arrangement.Timeline.PlayheadBar != drawn.Bar) problems.Add($"timeline bar {arrangement.Timeline.PlayheadBar}");
            if (settle == 0 && !inView) problems.Add($"timeline x {x:0} outside {sideways.Offset.X:0}..{sideways.Offset.X + sideways.Viewport.Width:0} (extent {sideways.Extent.Width:0})");
            if (problems.Count == 0) continue;
            failures++;
            if (failures <= 40) log.AppendLine($"ms {drawn.ElapsedMs:0} performed {performed} source bar {drawn.Bar + 1} cell {drawn.Cell}: {string.Join("; ", problems)}");
        }
        _out.WriteLine(log.ToString());
        window.Close();
        Assert.True(failures == 0, $"{failures} bad frames:\n{log}");
    }

    /// <summary>A note the sounding query already counts at a frame (its onset within the tolerance past it) is a boundary for that frame, so the score lights it then.</summary>
    [Fact]
    public void ANoteCountedAsSoundingAtAFrameIsABoundaryForThatFrame()
    {
        var notes = new[] { new NoteEvent { OnsetMs = 1000.3, DurationMs = 200 } };
        Assert.Single(NoteTimeline.SoundingAt(notes, 1000));
        Assert.True(NoteTimeline.AnyBoundaryBetween(notes, 900, 1000));
        Assert.False(NoteTimeline.AnyBoundaryBetween(notes, 1000, 1100));
    }

    // A lit key the score can draw: a note on that string in that cell of the bar, in either voice.
    private static bool Drawn(TabForge.Models.TrackModel track, (int Bar, int Cell, int String) key)
    {
        if (key.Bar < 0 || key.Bar >= track.Measures.Count) return false;
        var measure = track.Measures[key.Bar];
        if (measure.SimileOneBar || measure.SimileTwoBar) return true;   // a simile bar draws its mark and plays the copied bar
        bool In(List<TabForge.Models.TabCell> cells) => key.Cell < cells.Count && cells[key.Cell].Notes.Any(n => n.StringIndex == key.String);
        return In(measure.Cells) || In(measure.Voice2Cells);
    }
}
