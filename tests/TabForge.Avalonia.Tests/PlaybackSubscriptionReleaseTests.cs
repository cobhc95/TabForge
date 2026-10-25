using System;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Transport;
using TabForge.Avalonia.Views;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Views.Score;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the playback subscriptions of views that leave the visual tree. A detached arrangement or score releases its PlayheadFrame,
/// PlayheadBarChanged and StateChanged handlers, so the session's subscriber count returns to its baseline after each detach.
/// Tests: ArrangementView (Attach/Detach), ScorePreviewView (ScorePlayheadBinding).
/// </summary>
public class PlaybackSubscriptionReleaseTests
{
    // The session's events are field-like, so the delegate behind each event is the field of the same name.
    private static int Subscribers(AvaloniaPlaybackSession session, string eventName)
    {
        var field = typeof(AvaloniaPlaybackSession).GetField(eventName, BindingFlags.Instance | BindingFlags.NonPublic);
        return (field?.GetValue(session) as Delegate)?.GetInvocationList().Length ?? 0;
    }

    [AvaloniaFact]
    public void ArrangementReleasesPlayheadSubscriptionsWhileDetachedAndRestoresThemOnAttach()
    {
        using var session = new AvaloniaPlaybackSession(new AvaloniaPlaybackSessionTests.FakeEngine(), new AvaloniaPlaybackSessionTests.FakeUiThread(), () => new SongProject());
        var doc = new ScorePreviewEditor.PreviewDocument(DemoSongFactory.Create());
        var view = new ArrangementView(doc.Project, doc);
        var host = new Window { Width = 1300, Height = 400 };
        var baselineFrame = Subscribers(session, "PlayheadFrame");
        var baselineBar = Subscribers(session, "PlayheadBarChanged");

        view.BindPlayback(session);
        Assert.Equal(baselineFrame, Subscribers(session, "PlayheadFrame"));
        for (var cycle = 0; cycle < 10; cycle++)
        {
            host.Content = view;
            host.Show();
            Assert.Equal(baselineFrame + 1, Subscribers(session, "PlayheadFrame"));
            Assert.Equal(baselineBar + 1, Subscribers(session, "PlayheadBarChanged"));

            host.Content = null;
            Assert.Equal(baselineFrame, Subscribers(session, "PlayheadFrame"));
            Assert.Equal(baselineBar, Subscribers(session, "PlayheadBarChanged"));
        }
        host.Close();
    }

    [AvaloniaFact]
    public void ScorePreviewReleasesPlayheadSubscriptionsWhileDetachedAndRestoresThemOnAttach()
    {
        using var session = new AvaloniaPlaybackSession(new AvaloniaPlaybackSessionTests.FakeEngine(), new AvaloniaPlaybackSessionTests.FakeUiThread(), () => new SongProject());
        var view = new ScorePreviewView();
        var host = new Window { Width = 900, Height = 400 };
        var baselineFrame = Subscribers(session, "PlayheadFrame");
        var baselineState = Subscribers(session, "StateChanged");

        view.BindPlayback(session);
        Assert.Equal(baselineFrame, Subscribers(session, "PlayheadFrame"));
        for (var cycle = 0; cycle < 10; cycle++)
        {
            host.Content = view;
            host.Show();
            Assert.Equal(baselineFrame + 1, Subscribers(session, "PlayheadFrame"));
            Assert.Equal(baselineState + 1, Subscribers(session, "StateChanged"));

            host.Content = null;
            Assert.Equal(baselineFrame, Subscribers(session, "PlayheadFrame"));
            Assert.Equal(baselineState, Subscribers(session, "StateChanged"));
        }
        host.Close();
    }
}
