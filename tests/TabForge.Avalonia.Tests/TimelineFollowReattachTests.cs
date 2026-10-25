using System.Linq;
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

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the timeline keeps following the playhead after the arrangement leaves the visual tree and comes back while the song plays (a dock
///     layout rebuild, a pane hidden and shown, compact mode on and off move it), and after it is attached twice in a row.
/// Does not own: the follow arithmetic (TimelineFollowTests), the dock itself.
/// Tests: this class.
/// </summary>
public class TimelineFollowReattachTests
{
    private const int FarBar = 120;

    [AvaloniaFact]
    public void TheTimelineFollowsThePlayheadAfterTheArrangementIsDetachedAndAttachedWhilePlaying()
    {
        var song = new SongDocument(() => null) { Project = FullDemoSongFactory.Create() };
        var engine = new AvaloniaPlaybackSessionTests.FakeEngine();
        using var session = new AvaloniaPlaybackSession(engine, new AvaloniaUiThread(Dispatcher.UIThread), () => song.Project);
        var view = new ArrangementView(song.Project, song);
        var host = new ContentControl { Content = view };
        var window = new Window { Width = 1200, Height = 500, Content = host };
        window.Show();
        view.BindPlayback(session);
        session.PlayOrPause();
        var timeline = MidiTimelineBuilder.Build(song.Project, new PlaybackOptions());
        engine.RaiseTimelineChanged(timeline);

        // A dock rebuild: the arrangement leaves the tree and returns, twice, while the song plays.
        for (var round = 0; round < 2; round++)
        {
            host.Content = null;
            Dispatcher.UIThread.RunJobs();
            host.Content = view;
            window.UpdateLayout();
        }

        var span = timeline.Bars.Last(b => b.Bar == FarBar);
        engine.Report(new PlaybackPosition { Bar = FarBar, ElapsedMs = (span.StartMs + span.EndMs) / 2 });
        session.PumpFrame();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        var sideways = view.Timeline.GetVisualAncestors().OfType<ScrollViewer>().First();
        Assert.Equal(FarBar, view.Timeline.PlayheadBar);
        Assert.InRange(view.Timeline.PlayheadX(), sideways.Offset.X, sideways.Offset.X + sideways.Viewport.Width);
        window.Close();
    }
}
