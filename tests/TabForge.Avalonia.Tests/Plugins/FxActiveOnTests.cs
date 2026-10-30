using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Fx;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Presenters;
using TabForge.Services;
using TabForge.Services.Features;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the "Active on" rule model and matcher: driver kind, device by name, the AND of both, a container's rule on everything
/// inside, the engine's enabled flags, and the words the menu, popup and rows show.
/// Does not own: the engine sync (AudioEngineClient) or the popup and rows (Fx* UI tests in this class).
/// Tests: this class.
/// </summary>
[Collection("PluginLibraryRoot")]   // PluginLibrary.RootOverride is process-wide: run alone so no parallel test resets it or reads the scratch folder
public class FxActiveOnTests
{
    private static readonly ActiveOnOutput Asio = new("ASIO", "");
    private static readonly ActiveOnOutput Wasapi = new("WASAPI (shared)", "");

    private static PluginSlot Effect(string name, ActiveOnRule? rule = null) =>
        new() { Name = name, Type = PluginSlotType.Effect, ActiveOn = rule };

    [Fact]
    public void FxActiveOnDefaultRuleAllowsEveryOutput()
    {
        Assert.True(ActiveOnRules.Matches(null, Asio));
        Assert.True(ActiveOnRules.Matches(new ActiveOnRule(), Wasapi));
        Assert.Null(ActiveOnRule.Normalize(new ActiveOnRule { DeviceName = "   " }));
        Assert.True(ActiveOnRules.Matches(new ActiveOnRule { DeviceName = "   " }, default));
    }

    [Fact]
    public void FxActiveOnDriverRuleMatchesOnlyThatDriverKind()
    {
        var asioOnly = new ActiveOnRule { DriverKind = ActiveOnDriverKind.Asio };
        Assert.True(ActiveOnRules.Matches(asioOnly, Asio));
        Assert.False(ActiveOnRules.Matches(asioOnly, Wasapi));
        Assert.False(ActiveOnRules.Matches(asioOnly, new ActiveOnOutput("DirectSound", "")));
        Assert.True(ActiveOnRules.Matches(new ActiveOnRule { DriverKind = ActiveOnDriverKind.Wasapi }, new ActiveOnOutput("WASAPI (exclusive)", "")));
    }

    [Fact]
    public void FxActiveOnDeviceMatchesByNameIgnoringCaseAndSpaces()
    {
        var headphones = new ActiveOnRule { DeviceName = "  Headphones " };
        Assert.True(ActiveOnRules.Matches(headphones, new ActiveOnOutput("WASAPI (shared)", "headphones")));
        Assert.False(ActiveOnRules.Matches(headphones, new ActiveOnOutput("WASAPI (shared)", "Speakers")));
        Assert.False(ActiveOnRules.Matches(headphones, new ActiveOnOutput("WASAPI (shared)", "")));
    }

    [Fact]
    public void FxActiveOnBothRuleArgumentsMustMatch()
    {
        var rule = new ActiveOnRule { DriverKind = ActiveOnDriverKind.Asio, DeviceName = "Headphones" };
        Assert.True(ActiveOnRules.Matches(rule, new ActiveOnOutput("ASIO", "Headphones")));
        Assert.False(ActiveOnRules.Matches(rule, new ActiveOnOutput("ASIO", "Speakers")));
        Assert.False(ActiveOnRules.Matches(rule, new ActiveOnOutput("WASAPI (shared)", "Headphones")));
    }

    [Fact]
    public void FxActiveOnContainerRuleAppliesToEverythingInsideAndAChildCanBeStricter()
    {
        var rig = new RigPreset
        {
            Plugins = new List<PluginSlot>
            {
                Effect("IR Loader"),
                Effect("Reverb", new ActiveOnRule { DriverKind = ActiveOnDriverKind.Asio }),
                Effect("Delay"),
            },
            Containers = new List<ChainContainer>
            {
                new() { Start = 0, Count = 3, ActiveOn = new ActiveOnRule { DeviceName = "Headphones" } },
            },
        };
        var speakers = new ActiveOnOutput("WASAPI (shared)", "Speakers");
        var headphonesWasapi = new ActiveOnOutput("WASAPI (shared)", "Headphones");
        Assert.All(Enumerable.Range(0, 3), i => Assert.True(ActiveOnRules.Inactive(rig, i, speakers)));
        Assert.False(ActiveOnRules.Inactive(rig, 0, headphonesWasapi));
        Assert.True(ActiveOnRules.Inactive(rig, 1, headphonesWasapi));    // the child's own ASIO-only rule is stricter
        Assert.False(ActiveOnRules.Inactive(rig, 2, headphonesWasapi));
    }

    [Fact]
    public void FxActiveOnEnabledKeepsManualBypassAndContainerBypass()
    {
        var rig = new RigPreset
        {
            Plugins = new List<PluginSlot>
            {
                Effect("Manual off", new ActiveOnRule { DriverKind = ActiveOnDriverKind.Asio }),
                Effect("Rule off", new ActiveOnRule { DriverKind = ActiveOnDriverKind.Asio }),
                Effect("On"),
            },
        };
        rig.Plugins[0].Enabled = false;
        rig.Plugins[0].GroupBypassed = false;
        var enabled = ActiveOnRules.Enabled(rig, true, Wasapi);
        Assert.False(enabled[0]);   // manual bypass wins even if its rule would not
        Assert.False(enabled[1]);   // the rule bypasses
        Assert.True(enabled[2]);

        var asio = ActiveOnRules.Enabled(rig, true, Asio);
        Assert.False(asio[0]);      // manual bypass still wins when the rule matches
        Assert.True(asio[1]);
        Assert.False(ActiveOnRules.Enabled(rig, false, Asio)[2]);   // chain off bypasses every plug-in
    }

    [Fact]
    public void FxActiveOnSwitchingTheOutputChangesOnlyTheEngineBypassFlags()
    {
        var rig = new RigPreset
        {
            Plugins = new List<PluginSlot>
            {
                Effect("IR Loader", new ActiveOnRule { DriverKind = ActiveOnDriverKind.Asio }),
                Effect("Reverb"),
            },
        };
        Assert.Equal(new[] { false, true }, ActiveOnRules.Enabled(rig, true, Wasapi));
        Assert.Equal(new[] { true, true }, ActiveOnRules.Enabled(rig, true, Asio));
        Assert.All(rig.Plugins, p => Assert.True(p.Enabled));   // the rule never edits the user's own switch
    }

    [Fact]
    public void FxActiveOnOutputUsesTheOpenedDevicesFriendlyName()
    {
        Assert.Equal("Speakers", ActiveOnOutput.DeviceFromDescription("WASAPI (shared): Speakers"));
        Assert.Equal("ASIO driver", ActiveOnOutput.DeviceFromDescription("ASIO: ASIO driver"));
        Assert.Null(ActiveOnOutput.DeviceFromDescription(""));
        Assert.Equal(ActiveOnDriverKind.Asio, ActiveOnRules.DriverKindOf("ASIO"));
        Assert.Equal(ActiveOnDriverKind.Wasapi, ActiveOnRules.DriverKindOf("WASAPI (shared)"));
        Assert.Null(ActiveOnRules.DriverKindOf("DirectSound"));
    }

    [Fact]
    public void FxActiveOnWordsDescribeTheRuleAndTheHint()
    {
        Assert.Equal("every output", ActiveOnPresenter.Describe(null));
        Assert.Equal("ASIO only", ActiveOnPresenter.Describe(new ActiveOnRule { DriverKind = ActiveOnDriverKind.Asio }));
        Assert.Equal("Headphones", ActiveOnPresenter.Describe(new ActiveOnRule { DeviceName = "Headphones" }));
        Assert.Equal("ASIO only · Headphones", ActiveOnPresenter.Describe(new ActiveOnRule { DriverKind = ActiveOnDriverKind.Asio, DeviceName = "Headphones" }));
        Assert.Contains(ActiveOnPresenter.InactiveHint, ActiveOnPresenter.BadgeToolTip(new ActiveOnRule { DriverKind = ActiveOnDriverKind.Asio }, inactive: true));
        Assert.DoesNotContain(ActiveOnPresenter.InactiveHint, ActiveOnPresenter.BadgeToolTip(null, inactive: false));
    }

    [Fact]
    public void FxActiveOnProjectRoundTripKeepsTheRuleOnEffectsAndContainers()
    {
        var project = SongDocument.BlankProject();
        var track = project.Tracks[0];
        track.Rig.Plugins = new List<PluginSlot> { Effect("IR Loader", new ActiveOnRule { DriverKind = ActiveOnDriverKind.Asio, DeviceName = "Headphones" }), Effect("Delay") };
        track.Rig.Containers = new List<ChainContainer> { new() { Start = 0, Count = 2, ActiveOn = new ActiveOnRule { DeviceName = "Headphones" } } };

        var bytes = ProjectService.PersistBytes(project);
        _ = ProjectService.RestoreBytes(bytes, out var raw, out _);
        Assert.Contains("\"DriverKind\":\"Asio\"", System.Text.Encoding.UTF8.GetString(raw));
        var restored = ProjectService.RestorePersistedBytes(bytes);
        var rig = restored.Tracks[0].Rig;
        Assert.Equal(ActiveOnDriverKind.Asio, rig.Plugins[0].ActiveOn!.DriverKind);
        Assert.Equal("Headphones", rig.Plugins[0].ActiveOn!.DeviceName);
        Assert.Null(rig.Plugins[1].ActiveOn);
        Assert.Equal("Headphones", rig.Containers![0].ActiveOn!.DeviceName);
    }

    [Fact]
    public void FxActiveOnOldFileWithoutTheFieldLoadsAsAlwaysActive()
    {
        var project = SongDocument.BlankProject();
        project.Tracks[0].Rig.Plugins = new List<PluginSlot> { Effect("IR Loader") };
        var bytes = ProjectService.PersistBytes(project);
        _ = ProjectService.RestoreBytes(bytes, out var raw, out _);
        var json = System.Text.Encoding.UTF8.GetString(raw);
        Assert.DoesNotContain("ActiveOn", json);   // a chain without a rule writes no field
        var restored = ProjectService.RestorePersistedBytes(bytes);
        Assert.Null(restored.Tracks[0].Rig.Plugins[0].ActiveOn);
        Assert.True(ActiveOnRules.Matches(restored.Tracks[0].Rig.Plugins[0].ActiveOn, new ActiveOnOutput("ASIO", "Speakers")));
    }

    [Fact]
    public void FxActiveOnChainFileKeepsTheRuleAndAPluginPresetDoesNot()
    {
        using var scratch = FxTestKit.ScratchLibrary();
        var chain = new List<PluginSlot> { Effect("IR Loader", new ActiveOnRule { DriverKind = ActiveOnDriverKind.Asio }) };
        var containers = new List<ChainContainer> { new() { Start = 0, Count = 1, ActiveOn = new ActiveOnRule { DeviceName = "Headphones" } } };
        var path = Path.Combine(PluginLibrary.Root, "chain.tfchain");

        PluginLibrary.SaveChain(path, chain, containers);
        var loaded = PluginLibrary.LoadChain(path)!;
        Assert.Equal(ActiveOnDriverKind.Asio, loaded[0].ActiveOn!.DriverKind);
        Assert.Equal("Headphones", loaded[0].ChainContainers![0].ActiveOn!.DeviceName);

        var rig = new RigPreset { Plugins = chain, Containers = containers };
        var containerPreset = ChainTree.PackCopy(rig, containers[0]);
        Assert.Equal("Headphones", containerPreset[0].ChainContainers![0].ActiveOn!.DeviceName);

        PluginLibrary.SavePreset(chain[0], "rule preset", "state");
        var preset = File.ReadAllText(PresetFolderStore.Plugin(chain[0]).PathOf("rule preset"));
        Assert.DoesNotContain("ActiveOn", preset);
        Assert.DoesNotContain("Headphones", preset);
    }

    internal static void Done(FxActiveOnPopup popup) =>
        popup.DoneButton.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

    [AvaloniaFact]
    public void FxActiveOnMenuOpensThePopupForAPluginAndForAContainer()
    {
        var track = FxTestKit.Track(FxChainWindow.SampleChain());
        var engine = new FxTestKit.FakeEngine { ActiveOutput = new ActiveOnOutput("WASAPI (shared)", "Speakers") };
        var window = FxTestKit.Window(track, FxTestKit.Host(track, engine));
        window.Show();
        var list = FxTestKit.List(window);
        var menu = list.ContextMenu!;

        list.Click(0);
        list.RaiseEvent(new ContextRequestedEventArgs());
        var pluginItem = menu.Items.OfType<MenuItem>().Single(item => item.Header as string == ActiveOnPresenter.MenuText);
        Assert.True(pluginItem.IsEnabled);
        pluginItem.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        Assert.NotNull(window.ActiveOnPopup);
        Assert.Equal(3, window.ActiveOnPopup!.DriverBox.ItemCount);   // All, ASIO only, WASAPI only
        menu.Close();

        list.Click(0);
        list.Click(1, shift: true);
        Assert.True(window.Run(FxChainIds.GroupSelected));
        list.Click(0);
        list.RaiseEvent(new ContextRequestedEventArgs());
        var containerItem = menu.Items.OfType<MenuItem>().Single(item => item.Header as string == ActiveOnPresenter.MenuText);
        Assert.True(containerItem.IsEnabled);
        menu.Close();
        window.Close();
    }

    [AvaloniaFact]
    public void FxActiveOnChoicesBypassTheRowsAndFollowTheOutputChangeWithoutClosingTheWindow()
    {
        var track = FxTestKit.Track(FxChainWindow.SampleChain());
        var engine = new FxTestKit.FakeEngine { ActiveOutput = new ActiveOnOutput("WASAPI (shared)", "Speakers") };
        var window = FxTestKit.Window(track, FxTestKit.Host(track, engine));
        window.Show();
        var list = FxTestKit.List(window);
        list.Click(0);
        window.Run(FxChainIds.ActiveOn);

        var popup = Assert.IsType<FxActiveOnPopup>(window.ActiveOnPopup);
        popup.ChooseDriver(ActiveOnDriverKind.Asio);
        Assert.Null(track.Rig.Plugins[0].ActiveOn);   // nothing is written until Done
        Done(popup);
        Assert.Equal(ActiveOnDriverKind.Asio, track.Rig.Plugins[0].ActiveOn?.DriverKind);
        Assert.True(track.Rig.Plugins[0].Enabled);   // the user's own switch is not changed
        Assert.Contains(list.RowAt(0).GetLogicalDescendants().OfType<TextBlock>(), text => text.Name == "ActiveOnHint");

        engine.ActiveOutput = new ActiveOnOutput("ASIO", "Speakers");
        engine.RaiseOutputChanged();
        Assert.DoesNotContain(list.RowAt(0).GetLogicalDescendants().OfType<TextBlock>(), text => text.Name == "ActiveOnHint");
        Assert.Contains(list.RowAt(0).GetLogicalDescendants().OfType<global::Avalonia.Controls.Shapes.Path>(), path => path.Name == "ActiveOnBadge");
        window.Close();
    }

    [AvaloniaFact]
    public void FxActiveOnContainerRuleBypassesItsChildrenAndAnUnpluggedDeviceStaysInThePopupGreyed()
    {
        var track = FxTestKit.Track(FxChainWindow.SampleChain());
        var engine = new FxTestKit.FakeEngine { ActiveOutput = new ActiveOnOutput("WASAPI (shared)", "Speakers") };
        var settings = new PluginSettings();
        settings.OutputProfiles[PluginSettings.OutputKey("WASAPI (shared)", "Headphones")] = new OutputProfile();
        var window = FxTestKit.Window(track, FxTestKit.Host(track, engine, settings));
        window.Show();
        var list = FxTestKit.List(window);
        list.Click(0);
        list.Click(1, shift: true);
        window.Run(FxChainIds.GroupSelected);
        list.Click(0);
        window.Run(FxChainIds.ActiveOn);

        var popup = Assert.IsType<FxActiveOnPopup>(window.ActiveOnPopup);
        Assert.Contains(popup.Model.DeviceChoices, device => device.Name == "Headphones");
        popup.ChooseDevice("Headphones");
        Done(popup);
        Assert.Equal("Headphones", track.Rig.Containers![0].ActiveOn?.DeviceName);
        var inactive = ActiveOnRules.Enabled(track.Rig, true, engine.ActiveOutput);
        Assert.All(inactive.Take(2), Assert.False);
        Assert.Contains(list.RowAt(0).GetLogicalDescendants().OfType<TextBlock>(), text => text.Name == "ActiveOnHint");
        Assert.Contains(list.RowAt(1).GetLogicalDescendants().OfType<TextBlock>(), text => text.Name == "ActiveOnHint");
        window.Close();
    }

    [AvaloniaFact]
    public void FxActiveOnUnpluggedDeviceRowSaysNotConnectedAndIsStillSelectable()
    {
        var track = FxTestKit.Track(FxChainWindow.SampleChain());
        var window = FxTestKit.Window(track);
        window.Show();
        ActiveOnRule? applied = null;
        var list = FxTestKit.List(window);
        var popup = new FxActiveOnPopup(list, null, "IR Loader",
            _ => new[] { new FxActiveOnDevice("Speakers", true), new FxActiveOnDevice("Headphones", false) },
            () => default, null, rule => applied = rule);
        popup.Open();
        Assert.Contains(popup.Model.DeviceChoices, row => row.Label == "Headphones (not connected)");
        popup.ChooseDevice("Headphones");
        Done(popup);
        Assert.Equal("Headphones", applied?.DeviceName);
        popup.Close();
        window.Close();
    }

    [AvaloniaFact]
    public void FxActiveOnChangeIsOneUndoStepForTheSelectedRows()
    {
        var (document, host, track) = FxTestKit.Song();
        host.PluginSettings.OutputProfiles[PluginSettings.OutputKey("WASAPI (shared)", "Headphones")] = new OutputProfile();
        var window = new FxChainWindow(host, track);
        window.Show();
        var list = FxTestKit.List(window);
        list.Click(0);
        window.Run(FxChainIds.ActiveOn);
        var popup = Assert.IsType<FxActiveOnPopup>(window.ActiveOnPopup);
        popup.ChooseDevice("Headphones");
        Done(popup);
        Assert.Equal("Headphones", track.Rig.Plugins[0].ActiveOn?.DeviceName);
        Assert.True(document.Undo.CanUndo);
        DocumentEdits.Undo(document);
        Assert.Null(document.Project.Tracks[0].Rig.Plugins[0].ActiveOn);
        window.Close();
    }
}
