using System.Collections.Generic;
using System.Linq;
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

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the opt-in playhead trace over the demo song played through its D.S. al Coda: one bar line per performed bar, with the performed
///     position and the source bar apart, and frame lines after the start.
/// Does not own: the trace switch and file (Core Trace) or the views it reads.
/// Tests: this class.
/// </summary>
public class PlayheadTraceTests
{
    private const double StepMs = 200;

    [AvaloniaFact]
    public void TheTraceWritesOneBarLinePerPerformedBarThroughTheDalSegno()
    {
        var engine = new AvaloniaPlaybackSessionTests.FakeEngine();
        using var sessions = new AvaloniaPlaybackSessions(new AvaloniaUiThread(Dispatcher.UIThread), () => engine);
        var document = new SongDocument(() => null) { Project = FullDemoSongFactory.Create() };
        var window = new MainWindow(first: document, playback: sessions) { Width = 1600, Height = 1000 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var lines = new List<string>();
        _ = new PlayheadTrace(window.Songs, sessions, window.GetVisualDescendants().OfType<ScorePreviewView>().Single(),
            () => window.GetVisualDescendants().OfType<ArrangementView>().Single(), lines.Add);
        var session = sessions.For(document)!;

        session.PlayOrPause();
        var timeline = MidiTimelineBuilder.Build(document.Project, engine.LastOptions!);
        engine.RaiseTimelineChanged(timeline);
        for (var ms = timeline.PlayFromMs; ms < timeline.TotalMs - 1; ms += StepMs)
        {
            engine.Report(PlayheadMapper.Map(timeline, ms, 0, 0));
            session.PumpFrame();
            Dispatcher.UIThread.RunJobs();
        }
        window.Close();

        var barLines = lines.Where(l => l.StartsWith("bar ")).ToList();
        Assert.True(lines.Any(l => l.StartsWith("frame jump")), "no frame line after the start");
        Assert.Equal(timeline.Bars.Count, barLines.Count);
        // After the D.S. al Coda the performed bar 124 plays source bar 36 again, and the timeline line says where the view is.
        Assert.Contains(barLines, l => l.Contains($"bar performed 124/{timeline.Bars.Count} source 36 ") && l.Contains("timelineShows") && l.Contains("lateNotesDropped 0"));
    }
}
