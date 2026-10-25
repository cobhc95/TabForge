using System.Linq;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Fx;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// A plug-in window the user opened from an FX chain window closes with that window (the engine would otherwise keep it open on its own).
/// Owns: this check. Does not own: the editors (FxEditorController), the engine (FxTestKit.FakeEngine).
/// Tests: this class.
/// </summary>
public class FxFloatingEditorTests
{
    [AvaloniaFact]
    public void ClosingTheFxWindowClosesThePluginWindowsItOpened()
    {
        var track = FxTestKit.Track(FxChainWindow.SampleChain());
        var engine = new FxTestKit.FakeEngine();
        var window = FxTestKit.Window(track, FxTestKit.Host(track, engine));
        window.Show();
        FxTestKit.RaiseActivated(window, 1);   // a double-click floats the plug-in in row 1
        Assert.Contains(engine.Calls, c => c.StartsWith("open:Sample reverb:float", System.StringComparison.Ordinal));
        window.Close();
        DialogTestKit.RunQueuedUiWork();   // the plug-in windows go down in a queued job after the window is gone
        Assert.Contains("close:Sample reverb", engine.Calls);
    }
}
