using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Midi;
using TabForge.Avalonia.Views.Presets;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Presets;
using TabForge.Presenters;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the preset memory of every converted selector: the remembered name on reopening, the edited " *", the missing " (missing)", Save
//   clearing the mark, the record's comparison and the architecture guard against a selector's own placeholder.
// Does not own: the window layouts or the stores.
// Tests: this class.
[Collection("PluginLibraryRoot")]
public class PresetMemoryTests
{
    private sealed record Made(MidiProcessingWindow Window, PluginSlot Slot);

    private static Made Open(PluginSlot slot)
    {
        var track = FxTestKit.Track(new[] { slot });
        var window = new MidiProcessingWindow(FxTestKit.Host(track, new FxTestKit.FakeEngine()), track, slot, apply => apply());
        window.Dialogs.SavePresetName = (_, _) => System.Threading.Tasks.Task.FromResult<string?>("Mine");
        window.Dialogs.Ask = (_, _, _, _, _) => System.Threading.Tasks.Task.FromResult(MessageBoxAnswer.Yes);
        window.Show();
        return new Made(window, slot);
    }

    private static ComboBox ListBox(Window w) => DialogTestKit.Named<ComboBox>(w, "PresetBox");

    private static ComboBox ProcessorBox(Window w) => DialogTestKit.Named<ComboBox>(w, "ProcessorPresetBox");

    [Fact]
    public void CanonicalComparisonIgnoresKeyOrderAndWholeNumberForm()
    {
        var applied = AppliedPreset.Create("Drums", "MidiChain", "{\"b\":2,\"a\":1.0}");
        Assert.NotNull(applied);
        Assert.False(applied!.IsEdited("{\"a\":1,\"b\":2}"));
        Assert.True(applied.IsEdited("{\"a\":1,\"b\":3}"));
        Assert.Equal(AppliedPreset.Canonical(" { \"b\" : 2 , \"a\" : 1 } "), AppliedPreset.Canonical("{\"a\":1,\"b\":2}"));
    }

    [AvaloniaFact]
    public void ASelectedListPresetIsShownWhenTheWindowIsReopened()
    {
        using var scratch = FxTestKit.ScratchLibrary();
        var slot = MidiProcessingWindow.SampleSlot();
        var first = Open(slot);
        FxTestKit.PressButton(first.Window, "SavePresetButton");
        DialogTestKit.RunQueuedUiWork();
        Assert.Equal("★ Mine", ListBox(first.Window).SelectedItem?.ToString());
        first.Window.Close();

        var again = Open(slot);
        Assert.Equal("★ Mine", ListBox(again.Window).SelectedItem?.ToString());
        again.Window.Close();
    }

    [AvaloniaFact]
    public void AnEditedPresetShowsTheTrailingStarAndRevertingClearsIt()
    {
        using var scratch = FxTestKit.ScratchLibrary();
        var slot = new PluginSlot { Name = "P", Type = PluginSlotType.Instrument, MidiProcessors = { MidiProcessorCatalog.Create(MidiProcessorCatalog.Find("transpose")!) } };
        var made = Open(slot);
        var info = MidiProcessorCatalog.Find("transpose")!;
        var number = info.Params.First(p => p.Kind == MidiParamKind.Number);
        var knob = made.Window.GetLogicalDescendants().OfType<TabForge.Avalonia.Platform.Controls.KnobControl>().First(k => k.Name == "Param_" + number.Key);
        var scale = Math.Max(1, number.Scale);
        knob.Value = Math.Clamp(number.Default + 1, number.Min, number.Max) * scale;
        FxTestKit.PressButton(made.Window, "SaveProcessorPresetButton");
        DialogTestKit.RunQueuedUiWork();
        Assert.Equal("Mine", ProcessorBox(made.Window).SelectedItem?.ToString());
        made.Window.Close();

        var again = Open(slot);
        Assert.Equal("Mine", ProcessorBox(again.Window).SelectedItem?.ToString());
        var knob2 = again.Window.GetLogicalDescendants().OfType<TabForge.Avalonia.Platform.Controls.KnobControl>().First(k => k.Name == "Param_" + number.Key);
        knob2.Value = Math.Clamp(number.Default + 2, number.Min, number.Max) * scale;
        again.Window.Close();

        var edited = Open(slot);
        Assert.Equal("Mine" + PresetCombo.Edited, ProcessorBox(edited.Window).SelectedItem?.ToString());
        var knob3 = edited.Window.GetLogicalDescendants().OfType<TabForge.Avalonia.Platform.Controls.KnobControl>().First(k => k.Name == "Param_" + number.Key);
        knob3.Value = Math.Clamp(number.Default + 1, number.Min, number.Max) * scale;   // back to the values the preset gave it
        edited.Window.Close();

        var clean = Open(slot);
        Assert.Equal("Mine", ProcessorBox(clean.Window).SelectedItem?.ToString());
        clean.Window.Close();
    }

    [AvaloniaFact]
    public void ADeletedPresetKeepsItsNameAndSettingsAndShowsMissing()
    {
        using var scratch = FxTestKit.ScratchLibrary();
        var slot = MidiProcessingWindow.SampleSlot();
        var made = Open(slot);
        FxTestKit.PressButton(made.Window, "SavePresetButton");
        DialogTestKit.RunQueuedUiWork();
        FxTestKit.PressButton(made.Window, "DeletePresetButton");
        DialogTestKit.RunQueuedUiWork();

        Assert.Equal("★ Mine" + PresetCombo.Missing, ListBox(made.Window).SelectedItem?.ToString());
        Assert.Equal(2, slot.MidiProcessors.Count);   // the settings stay applied
        made.Window.Close();
    }

    [AvaloniaFact]
    public void SavingAPresetClearsTheStar()
    {
        using var scratch = FxTestKit.ScratchLibrary();
        var slot = new PluginSlot { Name = "P", Type = PluginSlotType.Instrument, MidiProcessors = { MidiProcessorCatalog.Create(MidiProcessorCatalog.Find("transpose")!) } };
        var made = Open(slot);
        var info = MidiProcessorCatalog.Find("transpose")!;
        var number = info.Params.First(p => p.Kind == MidiParamKind.Number);
        var knob = made.Window.GetLogicalDescendants().OfType<TabForge.Avalonia.Platform.Controls.KnobControl>().First(k => k.Name == "Param_" + number.Key);
        var scale = Math.Max(1, number.Scale);
        knob.Value = Math.Clamp(number.Default + 1, number.Min, number.Max) * scale;
        FxTestKit.PressButton(made.Window, "SaveProcessorPresetButton");
        DialogTestKit.RunQueuedUiWork();
        var knob2 = made.Window.GetLogicalDescendants().OfType<TabForge.Avalonia.Platform.Controls.KnobControl>().First(k => k.Name == "Param_" + number.Key);
        knob2.Value = Math.Clamp(number.Default + 2, number.Min, number.Max) * scale;

        FxTestKit.PressButton(made.Window, "SaveProcessorPresetButton");
        DialogTestKit.RunQueuedUiWork();
        Assert.Equal("Mine", ProcessorBox(made.Window).SelectedItem?.ToString());
        made.Window.Close();
    }

    [Fact]
    public void RememberedPresetsSurviveTheSongJson()
    {
        var slot = new PluginSlot { Name = "P" };
        slot.AppliedMidiPreset = AppliedPreset.Create("Chain", "MidiChain", "{\"a\":1}");
        slot.AppliedPreset = AppliedPreset.Create("Tone", "PluginPreset", "state");
        slot.MidiProcessors.Add(new PluginMidiProcessor { Type = "transpose", AppliedPreset = AppliedPreset.Create("Up", "MidiProcessor:transpose", "{\"semitones\":2}") });
        var rig = new RigPreset { Plugins = { slot }, AppliedArticulationPreset = AppliedPreset.Create("Ample", "Articulation", "{\"lead\":10}") };
        var json = JsonSerializer.Serialize(rig);
        var back = JsonSerializer.Deserialize<RigPreset>(json)!;
        Assert.Equal("Chain", back.Plugins[0].AppliedMidiPreset?.Name);
        Assert.Equal("Tone", back.Plugins[0].AppliedPreset?.Name);
        Assert.Equal("Up", back.Plugins[0].MidiProcessors[0].AppliedPreset?.Name);
        Assert.Equal("Ample", back.AppliedArticulationPreset?.Name);
        Assert.DoesNotContain("AppliedPreset", JsonSerializer.Serialize(new RigPreset { Plugins = { new PluginSlot() } }));

        var container = new ChainContainer { AppliedPreset = AppliedPreset.Create("Box", "Container", "x") };
        Assert.Equal("Box", JsonSerializer.Deserialize<ChainContainer>(JsonSerializer.Serialize(container))!.AppliedPreset?.Name);
    }

    [Theory]
    [InlineData("Drums", "1", true, "Drums")]
    [InlineData("Drums", "2", true, "Drums *")]
    [InlineData("Drums", "1", false, "Drums (missing)")]
    public void EveryConvertedSelectorUsesTheSameLabelRule(string name, string current, bool exists, string expected)
    {
        var applied = AppliedPreset.Create(name, "Any", "{ \"x\": 1 }");
        Assert.Equal(expected, PresetCombo.Decorate(name, name, applied, "{ \"x\": " + current + " }", exists));
    }

    [Fact]
    public void NoSelectorAddsItsOwnLoadPresetPlaceholder()
    {
        var repo = ArchitectureScan.RepoRoot();
        var offenders = new List<string>();
        foreach (var file in ArchitectureScan.SourceFiles(repo).Where(f => f.Contains("TabForge.Avalonia", StringComparison.OrdinalIgnoreCase)))
        {
            var text = ArchitectureScan.ReadText(file);
            if (text.Contains("PlaceholderText = \"Load preset", StringComparison.Ordinal)
                || text.Contains("PlaceholderText = \"Load preset…\"", StringComparison.Ordinal))
                offenders.Add(ArchitectureScan.Relative(repo, file));
        }
        Assert.Empty(offenders);
    }
}
