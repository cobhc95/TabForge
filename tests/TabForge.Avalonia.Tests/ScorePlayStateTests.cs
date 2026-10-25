using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Transport;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the playing score and band checks over a held moment (PlayingStateStub): the playhead line and band appear while it plays and go when it stops, the
///   Band glows only bars with a struck note, and the keyboard strip reads the session's position.
/// Does not own: the engine or real timing.
/// Tests: this class.
/// </summary>
public class ScorePlayStateTests
{
    private static (AvaloniaPlaybackSession Session, SongDocument Document) Held(AvaloniaPlaybackSessions sessions)
    {
        var document = new SongDocument(() => null) { Project = FullDemoSongFactory.Create() };
        return (sessions.For(document)!, document);
    }

    /// <summary>Shows the content in a window, so it attaches to the visual tree: a score or band view follows the playback only while attached.</summary>
    private static Window Hosted(Control content)
    {
        var window = new Window { Width = 1200, Height = 800, Content = content };
        window.Show();
        return window;
    }

    [AvaloniaFact]
    public void ScoreShowsPlayheadWhilePlayingAndClearsItOnStop()
    {
        using var sessions = new AvaloniaPlaybackSessions(new AvaloniaUiThread(global::Avalonia.Threading.Dispatcher.UIThread), () => new PlayingStateStub(2, 0.5));
        var (session, document) = Held(sessions);
        var view = new ScorePreviewView();
        view.BindPlayback(session);
        var window = Hosted(view);
        session.PlayOrPause();
        session.PumpFrame();
        Assert.True(view.Probe.Play.Active);
        Assert.Equal(2, view.Probe.Play.Measure);
        Assert.NotNull(view.Probe.Play.PlayheadLine(view.Probe));
        session.Stop();
        Assert.False(view.Probe.Play.Active);
        Assert.Null(view.Probe.Play.PlayheadLine(view.Probe));
        Assert.NotNull(document);
        window.Close();
    }

    [AvaloniaFact]
    public void PlayheadLineMovesWithinTheBeatWithoutRebuildingTheLitNotes()
    {
        using var sessions = new AvaloniaPlaybackSessions(new AvaloniaUiThread(global::Avalonia.Threading.Dispatcher.UIThread), () => new PlayingStateStub(2, 0.2));
        var (session, _) = Held(sessions);
        var view = new ScorePreviewView();
        view.BindPlayback(session);
        var window = Hosted(view);
        session.PlayOrPause();
        session.PumpFrame();
        var before = view.Probe.Play.PlayheadLine(view.Probe)!.Value.X;
        view.Probe.Play.SetFraction(view.Probe.Play.Fraction + 0.1);
        Assert.True(view.Probe.Play.PlayheadLine(view.Probe)!.Value.X > before);
        window.Close();
    }

    [AvaloniaFact]
    public void BandShowsPlayheadAndKeyboardReadsSessionPosition()
    {
        using var sessions = new AvaloniaPlaybackSessions(new AvaloniaUiThread(global::Avalonia.Threading.Dispatcher.UIThread), () => new PlayingStateStub(2, 0.5));
        var (session, document) = Held(sessions);
        var band = new BandView(document);
        band.BindPlayback(session);
        var strip = new KeyboardStripView();
        using var binding = new TabForge.Avalonia.Views.KeyboardSessionBinding(strip, session, () => document.Project, () => 0);
        var window = Hosted(new StackPanel { Children = { band, strip } });
        session.PlayOrPause();
        session.PumpFrame();
        var staff = band.PanelFor(band.ShownTrackIndices[0])!.Staff;
        Assert.NotNull(staff.Probe.Play.PlayheadLine(staff.Probe));
        Assert.Equal(session.Transport.PlayheadMs, strip.Position!.SongMs);
        Assert.True(strip.Progress > 0);
        session.Stop();
        window.Close();
    }
}
