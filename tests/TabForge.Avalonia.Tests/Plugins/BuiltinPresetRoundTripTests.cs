using System;
using System.IO;
using System.Linq;
using TabForge.Audio.Contracts.Builtin;
using TabForge.AudioEngine.Builtin;
using TabForge.AudioEngine.Builtin.IrLoader;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: every built-in effect surviving the saved forms (a plug-in preset, an FX chain preset, the app-wide monitor chain in the settings file):
//   all parameters, bypass and the IR Loader's file paths come back equal, and a chain file loaded back gets new plug-in ids so the engine applies its states.
// Does not own: the sound of each effect (Builtin*Tests), the windows (FxChainWindowTests).
// Tests: this class.
[Collection("PluginLibraryRoot")]   // PluginLibrary.RootOverride is process-wide: run alone so no parallel test resets it or reads the scratch folder
public sealed class BuiltinPresetRoundTripTests
{
    private const int Rate = 48000;
    private static readonly string IrPath = Path.Combine(Path.GetTempPath(), "tf-missing-ir", "cab.wav");
    private static readonly string IrLeft = Path.Combine(Path.GetTempPath(), "tf-missing-ir", "left.wav");

    public static TheoryData<string> Ids() => new(BuiltinEffects.All.Select(e => e.Id));

    private static BuiltinEffect Make(string id) => BuiltinEffectFactory.Create(id, Rate, 512);

    /// <summary>A built-in with every editable parameter moved off its default, bypass on, and (IR Loader) three file paths.</summary>
    private static string ConfiguredState(string id)
    {
        using var effect = Make(id);
        var state = new BuiltinState();
        foreach (var p in effect.Parameters.Where(p => !p.ReadOnly))
            state.Values[p.Key] = p.Key == "bypass" ? 1 : p.Clamp(p.Min + (p.Max - p.Min) * 0.37 + (p.Default == p.Min + (p.Max - p.Min) * 0.37 ? 1 : 0));
        if (id == BuiltinEffects.IrLoaderId) { state.Text[IrLoaderEffect.FileKey] = IrPath; state.Text[IrLoaderEffect.LeftKey] = IrLeft; state.Text[IrLoaderEffect.RightKey] = IrPath; state.Values["separate"] = 1; state.Values["autoLevel"] = 0; state.Values["trim"] = 1; }
        effect.SetState(state.ToBytes());
        return Convert.ToBase64String(effect.GetState()!);
    }

    /// <summary>A new effect given the saved state: its own state afterwards must equal the saved one.</summary>
    private static void AssertRestores(string id, string saved)
    {
        using var fresh = Make(id);
        using var defaults = Make(id);
        fresh.SetState(Convert.FromBase64String(saved));
        var want = BuiltinState.Parse(Convert.FromBase64String(saved));
        var got = BuiltinState.Parse(fresh.GetState()!);
        Assert.Equal(want.Values.OrderBy(v => v.Key), got.Values.OrderBy(v => v.Key));
        Assert.Equal(want.Text.OrderBy(v => v.Key), got.Text.OrderBy(v => v.Key));
        Assert.NotEqual(BuiltinState.Parse(defaults.GetState()!).Values.OrderBy(v => v.Key), got.Values.OrderBy(v => v.Key));   // the saved state is not just the defaults
        Assert.Equal(1, got.Values["bypass"]);
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void APluginPresetKeepsEveryParameterAndTheIrPaths(string id)
    {
        using var scratch = FxTestKit.ScratchLibrary();
        Assert.StartsWith(Path.GetTempPath(), PluginLibrary.Root);   // never the user's own presets
        var state = ConfiguredState(id);
        var slot = new PluginSlot { Name = BuiltinEffects.Find(id)!.Name, Path = id, Format = BuiltinEffects.Format, State = state };
        PluginLibrary.SavePreset(slot, "Round trip", state);
        Assert.Contains("Round trip", PluginLibrary.PresetNames(slot));
        Assert.Contains(PresetFolderStore.PluginKey(slot), PresetFolderStore.PluginKeys());
        var loaded = PluginLibrary.LoadPreset(slot, "Round trip");
        Assert.NotNull(loaded);
        Assert.Equal(state, loaded!.Value.State);
        AssertRestores(id, loaded.Value.State);
        if (id == BuiltinEffects.IrLoaderId)
        {
            var text = BuiltinState.Parse(Convert.FromBase64String(loaded.Value.State)).Text;
            Assert.Equal((IrPath, IrLeft, IrPath), (text[IrLoaderEffect.FileKey], text[IrLoaderEffect.LeftKey], text[IrLoaderEffect.RightKey]));
        }
    }

    [Fact]
    public void AChainPresetKeepsEveryBuiltInWithItsStateAndBypass()
    {
        var file = Path.Combine(Path.GetTempPath(), "tf-chain-" + Guid.NewGuid().ToString("N") + ChainFileApply.FileExtension);
        try
        {
            var chain = BuiltinEffects.All.Select(e => new PluginSlot { Name = e.Name, Path = e.Id, Format = BuiltinEffects.Format, State = ConfiguredState(e.Id), Enabled = e.Id != BuiltinEffects.GateId }).ToList();
            PluginLibrary.SaveChain(file, chain);
            var loaded = PluginLibrary.LoadChain(file);
            Assert.NotNull(loaded);
            Assert.Equal(chain.Select(p => p.Path), loaded!.Select(p => p.Path));
            Assert.Equal(chain.Select(p => p.Enabled), loaded.Select(p => p.Enabled));
            Assert.Equal(chain.Select(p => p.State), loaded.Select(p => p.State));
            foreach (var slot in loaded) AssertRestores(slot.Path, slot.State!);
            var text = BuiltinState.Parse(Convert.FromBase64String(loaded.Single(p => p.Path == BuiltinEffects.IrLoaderId).State!)).Text;
            Assert.Equal(IrPath, text[IrLoaderEffect.FileKey]);
        }
        finally { try { File.Delete(file); } catch (IOException) { } }
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void WithoutAnEngineStateThePresetHoldsTheCurrentParameterValues(string id)
    {
        using var scratch = FxTestKit.ScratchLibrary();
        Assert.StartsWith(Path.GetTempPath(), PluginLibrary.Root);
        using var live = Make(id);
        var edited = live.Parameters.First(p => !p.ReadOnly && !p.Stepped && p.Key != "bypass" && p.Clamp(p.Min + (p.Max - p.Min) * 0.8) != p.Default);
        var target = edited.Clamp(edited.Min + (edited.Max - edited.Min) * 0.8);
        Assert.NotEqual(edited.Default, target);
        live.SetParameter(edited.Id, target);
        var slot = new PluginSlot { Name = BuiltinEffects.Find(id)!.Name, Path = id };
        if (id == BuiltinEffects.IrLoaderId) slot.State = Convert.ToBase64String(BuiltinState.OneText(IrLoaderEffect.FileKey, IrPath));   // the editor keeps the file in the slot
        Assert.True(TabForge.Avalonia.Views.Builtin.BuiltinSlotState.FromParameters(slot, live.GetParameters()));
        PluginLibrary.SavePreset(slot, "Edited", slot.State!);
        var state = BuiltinState.Parse(Convert.FromBase64String(PluginLibrary.LoadPreset(slot, "Edited")!.Value.State));
        Assert.Equal(target, state.Values[edited.Key]);
        if (id == BuiltinEffects.IrLoaderId) Assert.Equal(IrPath, state.Text[IrLoaderEffect.FileKey]);
        Assert.False(TabForge.Avalonia.Views.Builtin.BuiltinSlotState.FromParameters(slot, Array.Empty<TabForge.Audio.Contracts.PluginParameterInfo>()));   // nothing read: no state is made up
    }

    [Fact]
    public void ALoadedChainFileGetsNewPluginIdsSoTheEngineAppliesItsStates()
    {
        var chain = new System.Collections.Generic.List<PluginSlot> { new() { Name = "EQ", Path = BuiltinEffects.EqId, State = ConfiguredState(BuiltinEffects.EqId) } };
        var oldId = chain[0].Id;
        var read = ChainFileApply.Check("x.tfchain", chain, 0, replace: true);
        Assert.True(read.Outcome.Applied);
        Assert.NotEqual(oldId, read.Plugins![0].Id);
    }

    [Fact]
    public void TheMonitorChainKeepsItsIrLoaderFileAndSettingsThroughTheSettingsFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "tf-monir-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var settings = new AppSettings();
            settings.Plugins.MonitorFx.On = true;
            settings.Plugins.MonitorFx.Rig.Plugins.Add(new PluginSlot { Name = "IR Loader", Path = BuiltinEffects.IrLoaderId, Format = BuiltinEffects.Format, State = ConfiguredState(BuiltinEffects.IrLoaderId) });
            settings.Plugins.MonitorFx.Rig.Plugins.Add(new PluginSlot { Name = "EQ", Path = BuiltinEffects.EqId, Format = BuiltinEffects.Format, State = ConfiguredState(BuiltinEffects.EqId) });
            SettingsFileService.SaveAtomic(path, settings);
            var plugins = SettingsFileService.Load(path).Plugins.MonitorFx.Rig.Plugins;
            Assert.Equal(2, plugins.Count);
            Assert.Equal(BuiltinEffects.IrLoaderId, plugins[0].Path);
            Assert.Equal(IrPath, BuiltinState.Parse(Convert.FromBase64String(plugins[0].State!)).Text[IrLoaderEffect.FileKey]);
            foreach (var slot in plugins) AssertRestores(slot.Path, slot.State!);
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}

[CollectionDefinition("PluginLibraryRoot", DisableParallelization = true)]
public sealed class PluginLibraryRootCollection { }
