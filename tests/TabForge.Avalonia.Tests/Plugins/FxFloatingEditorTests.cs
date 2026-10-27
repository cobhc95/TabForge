using System.Linq;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Fx;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// A plug-in window the user opened from an FX chain window stays open when that window closes: it belongs to the song and closes with it (FloatingEditorsTests).
/// Owns: this check. Does not own: the editors (FxEditorController), the engine (FxTestKit.FakeEngine).
/// Tests: this class.
/// </summary>
public class FxFloatingEditorTests
{
    [AvaloniaFact]
    public void ClosingTheFxWindowKeepsThePluginWindowsItOpened()
    {
        var track = FxTestKit.Track(FxChainWindow.SampleChain());
        var engine = new FxTestKit.FakeEngine();
        var window = FxTestKit.Window(track, FxTestKit.Host(track, engine));
        window.Show();
        FxTestKit.RaiseActivated(window, 1);   // a double-click floats the plug-in in row 1
        Assert.Contains(engine.Calls, c => c.StartsWith("open:Sample reverb:float", System.StringComparison.Ordinal));
        window.Close();
        DialogTestKit.RunQueuedUiWork();   // any queued close job has run by now
        Assert.DoesNotContain("close:Sample reverb", engine.Calls);   // the floating plug-in windows belong to the song (FloatingEditors), not to the FX window
    }
}
