using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using TabForge.Avalonia.Views.Midi;
using TabForge.Avalonia.Views.NoteMaps;
using TabForge.Avalonia.Views.Presets;
using TabForge.Plugins;
using TabForge.Presenters;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the note map processor's rows: the saved map becomes the To value, and both map drop-downs end with the Manage row that
///   opens the drum-map manager and is never stored as a map.
/// Does not own: the editor window or the Edit map... button (MidiProcessingWindow).
/// Tests: this class.
/// </summary>
public class NoteMapEditEntryTests
{
    [Fact]
    public void SavedMapBecomesTheToValueAndTheOtherValuesStay()
    {
        var text = NoteMapEditEntry.ParamsWithMap("{\"From\":\"General MIDI\",\"Overrides\":\"36>38\"}", "My kit");
        var values = JsonNode.Parse(text)!.AsObject();
        Assert.Equal("My kit", values["To"]!.GetValue<string>());
        Assert.Equal("General MIDI", values["From"]!.GetValue<string>());
        Assert.Equal("36>38", values["Overrides"]!.GetValue<string>());
    }

    [AvaloniaFact]
    public void BothMapDropDownsEndWithManageAndNeverStoreItAsAMap()
    {
        using var scratch = FxTestKit.ScratchLibrary();
        DrumMapLibrary.Reload();
        var owner = new Window();
        var values = JsonNode.Parse("{\"From\":\"General MIDI\",\"To\":\"Tabs to SD3\",\"Overrides\":\"\"}")!.AsObject();
        var stored = new List<(string Key, string Value)>();
        var rows = new MidiNoteMapRows(owner, values, (key, value) => stored.Add((key, value)));
        var info = MidiProcessorCatalog.Find("noteMap")!;
        var panel = new StackPanel();
        foreach (var parameter in info.Params.Where(p => p.Key is "From" or "To")) panel.Children.Add(rows.Row(parameter));
        owner.Content = panel;
        owner.Show();
        var combos = owner.GetLogicalDescendants().OfType<ComboBox>().Where(combo => combo.Name?.StartsWith("NoteMap_") == true).ToList();

        Assert.Equal(2, combos.Count);
        Assert.All(combos, combo =>
        {
            var last = combo.Items.Cast<object>().Last();
            Assert.True(PresetRowText.IsManage(last));
            Assert.Equal(PresetRowText.Manage, ((ComboBoxItem)last).Content);
        });

        var calls = new List<(PresetCatalogKind Kind, string? Key)>();
        var previous = PresetManager.Opener;
        PresetManager.Opener = (_, kind, key) => calls.Add((kind, key));
        try
        {
            var to = combos.Single(combo => combo.Name == "NoteMap_To");
            to.SelectedItem = to.Items.Cast<object>().Last();
            DialogTestKit.RunQueuedUiWork();
        }
        finally { PresetManager.Opener = previous; }

        Assert.Equal((PresetCatalogKind.DrumMap, (string?)null), Assert.Single(calls));
        Assert.Empty(stored);   // the manage row is never stored as a map
        Assert.Equal("Tabs to SD3", values["To"]!.GetValue<string>());
        owner.Close();
    }
}
