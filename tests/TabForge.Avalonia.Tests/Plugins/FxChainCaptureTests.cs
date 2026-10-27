using System;
using System.IO;
using System.Linq;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Fx;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Services.Features;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the FX chain window renders for the visual audit, in dark and light: an empty chain, two plug-ins and a container with the instrument focused, and
/// the docked editor area with a bypassed plug-in (the BYPASSED stamp over the area). The PNGs go under the temp folder (DialogTestKit.RenderDirectory).
/// Does not own: the window; the audit looks at the PNGs.
/// Tests: this class.
/// </summary>
public class FxChainCaptureTests
{
    private static FxChainWindow Build(string scene)
    {
        var track = FxTestKit.Track(scene == "empty" ? null : new[]
        {
            new PluginSlot { Name = "Example instrument", Type = PluginSlotType.Instrument, Vendor = "Example Labs", Format = "VST3" },
            new PluginSlot { Name = "Example reverb", Type = PluginSlotType.Effect, Vendor = "Sample Audio", Format = "VST3" },
            new PluginSlot { Name = "Example delay", Type = PluginSlotType.Effect, Vendor = "Sample Audio", Format = "VST3", Enabled = scene != "docked" },
        });
        track.SoundSource = SoundSources.Plugins;
        var window = FxTestKit.Window(track, FxTestKit.Host(track, new FxTestKit.FakeEngine()));
        window.Width = 980; window.Height = 620;
        window.Show();
        var list = FxTestKit.List(window);
        if (scene == "container")
        {
            list.Click(1);
            list.Click(2, shift: true);
            window.Run(FxChainIds.GroupSelected);
            list.Click(0);
        }
        if (scene == "docked")
        {
            DialogTestKit.Named<FxEditorArea>(window, "EditorArea").Surface.SetAreaForTest((IntPtr)1234);
            DialogTestKit.RunQueuedUiWork();
            list.Click(2);
        }
        DialogTestKit.RunQueuedUiWork();
        return window;
    }

    [AvaloniaTheory]
    [InlineData("empty", false)]
    [InlineData("empty", true)]
    [InlineData("container", false)]
    [InlineData("container", true)]
    [InlineData("docked", false)]
    [InlineData("docked", true)]
    public void RendersEachSceneInEachTheme(string scene, bool light)
    {
        using var theme = DialogTestKit.UseTheme(light);
        var window = Build(scene);
        window.CaptureRenderedFrame()?.Dispose();   // the first frame after a change can be the one before it
        DialogTestKit.RunQueuedUiWork();
        var path = DialogTestKit.SavePng(window, nameof(FxChainCaptureTests), $"fx-{scene}-{(light ? "light" : "dark")}.png");
        Assert.True(new FileInfo(path).Length > 1000);
        window.Close();
    }
}
