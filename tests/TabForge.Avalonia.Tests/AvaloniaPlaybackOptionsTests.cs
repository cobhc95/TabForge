using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Transport;
using TabForge.Playback;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the playback options an Avalonia session starts with: the app's click, count-in and let-ring settings reach every start, so the events a
// start compiles match the transport's options (source bar 121 of the demo is compared event for event).
// Does not own: the engine (PlaybackEngine), the transport (a read-only reference; its options are written out here).
// Tests: this file.
public class AvaloniaPlaybackOptionsTests
{
    private const int CompareBar = 121;

    [AvaloniaFact]
    public void ASessionStartCompilesTheSameEventsAsTheWpfOptions()
    {
        var project = FullDemoSongFactory.Create();
        var audio = new AudioSettings { LetRingCapMs = 300, CountInBars = 2, MetronomeSubdivision = 2 };
        var engine = new AvaloniaPlaybackSessionTests.FakeEngine();
        var session = new AvaloniaPlaybackSession(engine, new AvaloniaUiThread(Dispatcher.UIThread), () => project);
        session.ApplyClickOptions(audio);
        session.PlayOrPause();

        // The transport's options for the same settings (TransportControlsController.BuildOptions).
        var wpf = new PlaybackOptions
        {
            LetRingCapMs = audio.LetRingCapMs,
            CountInBars = audio.CountInBars,
            MetronomeSubdivision = audio.MetronomeSubdivision,
            MetronomeAccentNote = audio.MetronomeAccent,
            MetronomeClickNote = audio.MetronomeClick,
            MetronomeVolume = audio.MetronomeVolume,
            MetronomeAccentVolume = audio.MetronomeAccentVolume,
            MetronomeClickVolume = audio.MetronomeClickVolume,
        };
        var avaloniaEvents = BarEvents(MidiTimelineBuilder.Build(project, engine.LastOptions!));
        var wpfEvents = BarEvents(MidiTimelineBuilder.Build(project, wpf));

        Assert.NotEmpty(wpfEvents);
        Assert.Equal(wpfEvents, avaloniaEvents);
    }

    /// <summary>The events of source bar <see cref="CompareBar"/> of a compiled timeline, in timeline order.</summary>
    private static (double TimeMs, int Status, int Data1, int Data2, int Track)[] BarEvents(TabForge.Playback.ScoreTimeline timeline)
    {
        var bar = timeline.Bars.First(candidate => candidate.Bar == CompareBar);
        return timeline.Events.Where(e => e.TimeMs >= bar.StartMs && e.TimeMs < bar.EndMs)
            .Select(e => (e.TimeMs, e.Status, e.Data1, e.Data2, e.TrackIndex)).ToArray();
    }
}
