using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Fx;
using TabForge.Models;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// The FX window as the app opens it (ShowOver, as the live input chain and the track FX do): the plug-in editor area joins its native window, the engine
/// is asked to dock the focused plug-in's editor there, and the editor's reported size gives the area its size.
/// Owns: this check of that path. Does not own: the window (FxChainWindow), the area (FxEditorArea), the engine (FxTestKit.FakeEngine).
/// Tests: this class (the native window itself is a headless stand-in: SetAreaForTest).
/// </summary>
public class FxLiveEditorDockTests
{
    [AvaloniaFact]
    public void AnEditorOpenedWithShowOverDocksAndTakesTheSizeTheEngineReports()
    {
        var owner = new Window();
        owner.Show();
        var track = FxTestKit.Track(FxChainWindow.SampleChain());
        track.SoundSource = SoundSources.Plugins;
        var engine = new FxTestKit.FakeEngine();
        var window = FxTestKit.Window(track, FxTestKit.Host(track, engine, new PluginSettings()));
        window.ShowOver(owner);
        var area = DialogTestKit.Named<FxEditorArea>(window, "EditorArea");
        Assert.True(area.Surface.IsAttachedToVisualTree());

        area.Surface.SetAreaForTest((System.IntPtr)1234);
        DialogTestKit.RunQueuedUiWork();
        Assert.Contains(engine.Calls, c => c.StartsWith("open:", System.StringComparison.Ordinal) && c.Contains(":docked:"));

        engine.RaiseEditorSized(0, 0, 600, 400);
        Assert.True(area.FrameSize.Width > 0 && area.FrameSize.Height > 0, $"frame {area.FrameSize}");
        window.Close();
        owner.Close();
    }
}
