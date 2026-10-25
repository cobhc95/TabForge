using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Fx;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the live input's FX chain window: its title, "FX: Live input", shown once; the options a track has are hidden and the app-wide note takes their place;
/// every change reaches the host at once (the chain is saved and the engine synced per change).
/// Does not own: the chain window's contents (FxChainWindowTests) or the live input's state (MainWindowLiveInput).
/// Tests: TabForge.Avalonia.Tests.LiveInputFxTitleTests.
/// </summary>
public class LiveInputFxTitleTests
{
    private static FxChainWindow Open(out TrackModel track, out int changes, Control? note = null)
    {
        track = FxTestKit.Track(FxChainWindow.SampleChain(), MainWindowLiveInput.FxChainName);
        var count = 0;
        var host = new StandaloneFxChainHost(new PluginSettings(), new HotkeySettings(), new TabForge.Plugins.PluginTrust(), new FxTestKit.FakeEngine(), new[] { track }, _ => count++)
        {
            AppWideChainTitle = FxChainPresenter.WindowTitle(MainWindowLiveInput.FxChainName), AppWideChainNote = note,
        };
        var window = new FxChainWindow(host, track);
        window.Show();
        changes = 0;
        _liveCount = () => count;
        return window;
    }

    private static System.Func<int> _liveCount = () => 0;

    [AvaloniaFact]
    public void LiveInputChainWindowIsTitledOnceWithTheFxPrefix()
    {
        var window = Open(out _, out _);
        Assert.Equal("FX: Live input", window.Title);
        window.Close();
    }

    [AvaloniaFact]
    public void TheTrackOptionsAreHiddenAndTheNoteTakesTheirPlace()
    {
        var note = new TextBlock { Text = LiveInputHint.Text, Name = "TheNote" };
        var window = Open(out _, out _, note);
        Assert.False(DialogTestKit.Named<CheckBox>(window, "UseChainCheck").IsVisible);
        Assert.False(DialogTestKit.Named<CheckBox>(window, "AutoLoadCheck").IsVisible);
        Assert.True(DialogTestKit.Named<TextBlock>(window, "TheNote").IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void EachChangeReachesTheHostAtOnce()
    {
        var window = Open(out var track, out _);
        FxTestKit.List(window).Click(1);
        FxTestKit.PressButton(window, "RemoveButton");
        Assert.Equal(1, _liveCount());
        Assert.Equal(2, track.Rig.Plugins.Count);
        FxTestKit.Press(window, DialogTestKit.Named<CheckBox>(window, "BypassCheck"));
        Assert.Equal(2, _liveCount());
        window.Close();
    }

    [AvaloniaFact]
    public void TheHintFollowsTheLiveInputAndTurnsItOn()
    {
        var hint = LiveInputHint.Build(new TabForge.LiveInput.LiveInputSession(new HintHost()));
        var host = new Window { Content = hint };
        host.Show();
        Assert.True(hint.IsVisible);   // live input is off
        Assert.Equal(LiveInputHint.Text, hint.GetLogicalDescendantsOf<TextBlock>().Single(t => t.Name == "LiveHintText").Text);
        Assert.Equal(LiveInputHint.TurnOnText, hint.GetLogicalDescendantsOf<Button>().Single().Content);
        host.Close();
    }

    private sealed class HintHost : TabForge.LiveInput.ILiveInputSessionHost
    {
        public PluginSettings Settings { get; } = new();
        public TabForge.Audio.AudioEngineClient Engine { get; } = new(new TabForge.Plugins.PluginTrust());
        public TabForge.Platform.IUiThread UiThread { get; } = TabForge.Avalonia.Platform.AvaloniaUiThread.ForApplication();
        public void SyncAudioEngine() { }
        public void SaveSettings() { }
        public void SetStatus(string text) { }
        public (bool Restore, bool Remember) AskRestoreChain() => (false, false);
        public void ShowFxChain(System.Action closed) { }
    }
}
