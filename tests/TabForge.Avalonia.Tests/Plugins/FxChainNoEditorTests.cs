using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Headless.XUnit;
using TabForge.Audio;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine;
using TabForge.Services;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Fx;
using TabForge.AudioEngine.Isolation;
using TabForge.Models;
using TabForge.Plugins;
using Xunit;
using EH = TabForge.AudioEngine.EngineHost.Headless;

namespace TabForge.Avalonia.Tests;

// Owns: a bridged 32-bit plug-in without an editor in the FX chain window: the parameter panel is the first and only view (no blank editor area,
//   no failed-editor notice, no Parameters button), also on every later visit; an editor-bearing one still docks (see FxChainBridgedEditorTests).
// Does not own: the helper (native\tfvst32) or the editor state machine (FxEditorController).
[Collection("Vst32Native")]
public sealed class FxChainNoEditorTests
{
    [AvaloniaFact]
    public void ABridgedPluginWithoutAnEditorShowsTheParameterPanelAtOnce()
    {
        if (!OperatingSystem.IsWindows() || Vst32TestAssets.HelperPath is null || !System.IO.Directory.Exists(Vst32TestAssets.FixturesFolder)) return;
        var path = FxChainBridgedEditorTests.Fixture("ADClip932");
        using var shared = SharedBlock.Create($"tf-vst32-fx-noeditor-{Guid.NewGuid():N}");
        using var owner = new FxChainBridgedEditorTests.TestOwner();
        var pids = new List<int>();
        FxChainWindow? window = null;
        try
        {
            FxChainBridgedEditorTests.Attach(shared);
            EH.LoadChain(0, "No editor", useMidiSynth: false, new List<PluginSpec> { Vst32TestAssets.Spec(path, instrument: false) with { Id = "ad-a", Name = "ADClip9 A" } });
            var chain = EH.ChainAt(0);
            Assert.NotNull(chain);
            var plugin = Assert.IsType<RemotePlugin>(Assert.Single(chain!.Plugins));
            pids.Add(plugin.HostProcessId);
            Assert.False(plugin.HasEditor, "the fixture is expected to have no editor");

            var slot = new PluginSlot { Id = "ad-a", Name = "ADClip9 A", Path = path, Format = PluginFormats.Bridged32, Type = PluginSlotType.Effect };
            var track = FxTestKit.Track(new[] { slot });
            track.SoundSource = SoundSources.Plugins;
            var settings = new PluginSettings();
            var engine = new FxChainBridgedEditorTests.RealBridgeEngine(track);
            var host = FxTestKit.Host(track, engine, settings);
            host.Trust.Approve(settings, path, host.PluginFiles);
            window = FxTestKit.Window(track, host);
            window.Show();
            var area = DialogTestKit.Named<FxEditorArea>(window, "EditorArea");
            area.Surface.SetAreaForTest(owner.Handle);
            FxChainBridgedEditorTests.Pump();
            FxTestKit.List(window).Click(0);
            FxChainBridgedEditorTests.Pump();

            Assert.True(window.ParameterPanel.IsVisible, "the parameter panel must be the first view");
            Assert.False(area.IsVisible, "the blank editor area must not show");
            Assert.False(area.EditorVisible);
            Assert.DoesNotContain("didn't open", area.NoticeText);
            Assert.False(DialogTestKit.Named<global::Avalonia.Controls.Button>(window, "ParametersButton").IsVisible, "the panel is the only view: no switch button");

            // A later visit (another row and back) goes straight to the panel without a new attempt.
            FxTestKit.List(window).Click(0);
            FxChainBridgedEditorTests.Pump();
            Assert.True(window.ParameterPanel.IsVisible);
            Assert.False(area.IsVisible);
        }
        finally
        {
            try { window?.Close(); } catch (Exception) { /* the assertion already says why */ }
            EH.Detach();
            FxChainBridgedEditorTests.AssertHelpersGone(pids);
        }
    }
}
