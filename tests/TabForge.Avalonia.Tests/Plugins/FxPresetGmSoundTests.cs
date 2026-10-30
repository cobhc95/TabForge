using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
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
/// Owns: the GM sound after an FX preset load: a chain or container preset that holds a VST instrument switches it off as adding the
/// instrument directly does, an effects-only preset leaves it, undo restores it, the auto-switch setting governs the restore, and removing
/// the instrument brings GM back.
/// Does not own: the rule itself (FxChainPresenter.FollowsGmSound), chain files (ChainFileApply, PluginLibrary) or the window.
/// Tests: this class.
/// </summary>
[Collection("PluginLibraryRoot")]   // PluginLibrary.RootOverride is process-wide: run alone so no parallel test resets it
public class FxPresetGmSoundTests
{
    private static PluginSlot Instrument(string name = "Preset piano") => new() { Name = name, Type = PluginSlotType.Instrument };
    private static PluginSlot Effect(string name = "Preset reverb") => new() { Name = name, Type = PluginSlotType.Effect };

    private static string ChainFile(params PluginSlot[] plugins)
    {
        var file = Path.Combine(Path.GetTempPath(), $"tf-fx-gm-{Guid.NewGuid():N}.tfchain");
        PluginLibrary.SaveChain(file, plugins);
        return file;
    }

    private static (FxChainWindow Window, TrackModel Track) Open(PluginSettings? settings = null, Func<string, MessageBoxAnswer>? ask = null)
    {
        var track = new TrackModel { Name = "Preset track" };
        var host = FxTestKit.Host(track, plugins: settings);
        var window = FxTestKit.Window(track, host, ask: ask);
        window.Show();
        return (window, track);
    }

    private static void Load(FxChainWindow window, string file)
    {
        window.Dialogs.Files = new FxTestKit.PickFile(file);
        FxTestKit.Choose(window, "Load FX chain…");
        DialogTestKit.RunQueuedUiWork();
    }

    [AvaloniaFact]
    public void AChainPresetWithAnInstrumentSwitchesTheGmSoundOff()
    {
        var file = ChainFile(Instrument());
        try
        {
            var (window, track) = Open();
            Assert.True(track.MidiSound);
            Load(window, file);
            Assert.Single(track.Rig.Plugins);
            Assert.False(track.MidiSound);
            window.Close();
        }
        finally { File.Delete(file); }
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void AChainPresetWithOnlyEffectsLeavesTheGmSoundAsItWas(bool on)
    {
        var file = ChainFile(Effect());
        try
        {
            var (window, track) = Open();
            track.MidiSound = on;
            Load(window, file);
            Assert.Single(track.Rig.Plugins);
            Assert.Equal(on, track.MidiSound);
            window.Close();
        }
        finally { File.Delete(file); }
    }

    [AvaloniaFact]
    public void LoadingAChainPresetWithAnInstrumentIsOneUndoStepThatRestoresTheGmSound()
    {
        var file = ChainFile(Instrument());
        try
        {
            var (document, host, track) = FxTestKit.Song();
            track.Rig.Plugins.Clear();
            track.MidiSound = true;
            document.MarkClean();
            var window = FxTestKit.Window(track, host, files: new FxTestKit.PickFile(file));
            window.Show();
            Load(window, file);
            Assert.False(track.MidiSound);
            Assert.True(document.Undo.CanUndo);
            DocumentEdits.Undo(document);
            var restored = document.Project.Tracks[0];
            Assert.True(restored.MidiSound);
            Assert.Empty(restored.Rig.Plugins);
            window.Close();
        }
        finally { File.Delete(file); }
    }

    [AvaloniaFact]
    public void RemovingTheInstrumentLoadedFromAPresetBringsTheGmSoundBack()
    {
        var file = ChainFile(Instrument());
        try
        {
            var (window, track) = Open();
            Load(window, file);
            FxTestKit.List(window).Click(0);
            FxTestKit.PressButton(window, "RemoveButton");
            Assert.Empty(track.Rig.Plugins);
            Assert.True(track.MidiSound);
            window.Close();
        }
        finally { File.Delete(file); }
    }

    [AvaloniaTheory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void TheAutoSwitchSettingGovernsTheGmSoundComingBack(bool autoSwitch, bool expected)
    {
        var file = ChainFile(Instrument());
        try
        {
            var (window, track) = Open(new PluginSettings { AutoGmSound = autoSwitch });
            Load(window, file);
            Assert.False(track.MidiSound);
            FxTestKit.List(window).Click(0);
            FxTestKit.PressButton(window, "RemoveButton");
            Assert.Equal(expected, track.MidiSound);
            window.Close();
        }
        finally { File.Delete(file); }
    }

    [AvaloniaFact]
    public void AddingAnInstrumentPresetToAnEffectsChainSwitchesTheGmSoundOff()
    {
        var file = ChainFile(Instrument());
        try
        {
            var (window, track) = Open();
            track.Rig.Plugins.Add(Effect("Existing effect"));
            track.MidiSound = true;
            window.Dialogs.Files = new FxTestKit.PickFile(file);
            FxTestKit.Choose(window, "Add FX chain to the end…");
            DialogTestKit.RunQueuedUiWork();
            Assert.Equal(2, track.Rig.Plugins.Count);
            Assert.False(track.MidiSound);
            window.Close();
        }
        finally { File.Delete(file); }
    }

    [AvaloniaFact]
    public void AContainerPresetWithAnInstrumentSwitchesTheGmSoundOff()
    {
        using var scratch = FxTestKit.ScratchLibrary();
        Directory.CreateDirectory(Path.GetDirectoryName(PresetFolderStore.Containers().PathOf("Synth"))!);
        PluginLibrary.SaveChain(PresetFolderStore.Containers().PathOf("Synth"), new List<PluginSlot> { Instrument("Synth") });
        var (window, track) = Open();
        window.Run(FxChainIds.NewContainer);
        var box = DialogTestKit.Named<ComboBox>(window, "ContainerPresetBox");
        box.SelectedItem = box.Items.OfType<string>().First(i => i == "Synth");
        DialogTestKit.RunQueuedUiWork();
        Assert.Single(track.Rig.Plugins);
        Assert.False(track.MidiSound);
        window.Close();
    }

    [AvaloniaFact]
    public void TheDirectionRuleIsTheSameForEveryGmSoundCase()
    {
        var track = new TrackModel { Name = "Rule" };
        Assert.True(track.MidiSound);
        Assert.False(FxChainPresenter.HasInstrument(track.Rig.Plugins));
        track.Rig.Plugins.Add(Instrument());
        Assert.True(FxChainPresenter.FollowsGmSound(track, hadInstrument: false, autoSwitch: true));
        Assert.False(track.MidiSound);
        track.Rig.Plugins.Clear();
        Assert.True(FxChainPresenter.FollowsGmSound(track, hadInstrument: true, autoSwitch: true));
        Assert.True(track.MidiSound);
    }
}
