using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using TabForge.Avalonia.Views;
using TabForge.Models;
using TabForge.Plugins;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the MIDI chain multi-select checks: Ctrl/Shift selection, Ctrl+A, the rubber band, Remove/Up/Down on the whole
/// selection, the pane showing the focused row (like the FX window) and the single-click behaviour staying as today.
/// Does not own: the window, the selection rules (ChainSelection, MidiProcessingListPresenter) or the catalog.
/// Tests: this class.
/// </summary>
[Collection("PluginLibraryRoot")]
public class MidiProcessingChainMultiSelectTests
{
    private sealed record Made(MidiProcessingWindow Window, PluginSlot Slot, List<string> Edits);

    private static Made Open(bool show = true)
    {
        var slot = new PluginSlot
        {
            Name = "Sample synth",
            Type = PluginSlotType.Instrument,
            MidiProcessors =
            {
                new PluginMidiProcessor { Type = "transpose" },
                new PluginMidiProcessor { Type = "legacyFilter" },
                new PluginMidiProcessor { Type = "log" },
                new PluginMidiProcessor { Type = "extraUnknown" },
            },
        };
        var track = FxTestKit.Track(new[] { slot });
        var engine = new FxTestKit.FakeEngine();
        var edits = new List<string>();
        var window = new MidiProcessingWindow(FxTestKit.Host(track, engine), track, slot, apply => { edits.Add("edit"); apply(); });
        if (show) window.Show();
        return new Made(window, slot, edits);
    }

    private static ListBox Rows(MidiProcessingWindow window) => DialogTestKit.Named<ListBox>(window, "ProcessorList");

    [AvaloniaFact]
    public void CtrlClickTogglesAndShiftExtendsRangeAndFocusFollowsLastClicked()
    {
        var made = Open();
        made.Window.ClickChainRow(0);
        Assert.Equal(new[] { 0 }, made.Window.SelectedChainIndices);
        made.Window.ClickChainRow(2, shift: true);
        Assert.Equal(new[] { 0, 1, 2 }, made.Window.SelectedChainIndices);
        Assert.Equal(2, made.Window.FocusedChainIndex);
        made.Window.ClickChainRow(1, ctrl: true);
        Assert.Equal(new[] { 0, 2 }, made.Window.SelectedChainIndices);
        Assert.Equal(2, made.Window.FocusedChainIndex);
        made.Window.ClickChainRow(3, ctrl: true);
        Assert.Equal(new[] { 0, 2, 3 }, made.Window.SelectedChainIndices);
        Assert.Equal(3, made.Window.FocusedChainIndex);
        made.Window.Close();
    }

    [AvaloniaFact]
    public void CtrlASelectsAllAndSingleClickCollapses()
    {
        var made = Open();
        made.Window.ClickChainRow(1);
        made.Window.SelectAllChain();
        Assert.Equal(4, made.Window.SelectedChainIndices.Count);
        made.Window.ClickChainRow(2);
        Assert.Equal(new[] { 2 }, made.Window.SelectedChainIndices);
        Assert.Equal(2, made.Window.FocusedChainIndex);
        made.Window.Close();
    }

    [AvaloniaFact]
    public void RubberBandSelectsTouchedRowsAndCtrlAdds()
    {
        var made = Open();
        DialogTestKit.RunQueuedUiWork();
        var list = Rows(made.Window);
        double CentreY(int index)
        {
            var container = list.ContainerFromIndex(index) as Control
                ?? throw new Xunit.Sdk.XunitException($"Row {index} has no container.");
            var origin = container.TranslatePoint(new Point(0, 0), list);
            Assert.NotNull(origin);
            return origin!.Value.Y + container.Bounds.Height / 2;
        }
        made.Window.SimulateChainBand(CentreY(1), CentreY(2));
        Assert.Equal(new[] { 1, 2 }, made.Window.SelectedChainIndices);
        made.Window.SimulateChainBand(CentreY(0), CentreY(0), add: true);
        Assert.Equal(new[] { 0, 1, 2 }, made.Window.SelectedChainIndices);
        made.Window.Close();
    }

    [AvaloniaFact]
    public void RemoveActsOnTheWholeSelectionAsOneUndoStep()
    {
        var made = Open();
        made.Window.ClickChainRow(1);
        made.Window.ClickChainRow(2, shift: true);
        Assert.Equal(new[] { 1, 2 }, made.Window.SelectedChainIndices);
        FxTestKit.PressButton(made.Window, "RemoveButton");
        Assert.Equal(new[] { "transpose", "extraUnknown" }, made.Slot.MidiProcessors.Select(p => p.Type));
        Assert.Single(made.Window.SelectedChainIndices);
        Assert.Single(made.Edits);
        made.Window.Close();
    }

    [AvaloniaFact]
    public void MoveUpAndDownActOnTheWholeSelection()
    {
        var made = Open();
        made.Window.ClickChainRow(1);
        made.Window.ClickChainRow(3, ctrl: true);
        Assert.Equal(new[] { 1, 3 }, made.Window.SelectedChainIndices);
        FxTestKit.PressButton(made.Window, "UpButton");
        Assert.Equal(new[] { "legacyFilter", "transpose", "extraUnknown", "log" }, made.Slot.MidiProcessors.Select(p => p.Type));
        Assert.Equal(new[] { 0, 2 }, made.Window.SelectedChainIndices);
        FxTestKit.PressButton(made.Window, "DownButton");
        Assert.Equal(new[] { "transpose", "legacyFilter", "log", "extraUnknown" }, made.Slot.MidiProcessors.Select(p => p.Type));
        Assert.Equal(new[] { 1, 3 }, made.Window.SelectedChainIndices);
        Assert.Equal(2, made.Edits.Count);
        made.Window.Close();
    }

    [AvaloniaFact]
    public void DetailPaneShowsTheFocusedRowForAMultiSelection()
    {
        var made = Open();
        made.Window.ClickChainRow(2);
        made.Window.ClickChainRow(0, ctrl: true);
        Assert.Equal(0, made.Window.FocusedChainIndex);
        var title = DialogTestKit.Named<TextBlock>(made.Window, "TitleText").Text;
        var transpose = MidiProcessorCatalog.Find("transpose")!;
        Assert.Equal(transpose.Name, title);
        made.Window.Close();
    }

    [AvaloniaFact]
    public void SingleClickStillSelectsOneRowAndCheckboxTogglesOnlyItsRow()
    {
        var made = Open();
        var list = Rows(made.Window);
        list.SelectedIndex = 1;
        Assert.Equal(new[] { 1 }, made.Window.SelectedChainIndices);
        Assert.Equal("legacyFilter", DialogTestKit.Named<TextBlock>(made.Window, "TitleText").Text);
        var before = made.Slot.MidiProcessors.Select(p => p.Enabled).ToList();
        var row = list.ContainerFromIndex(1) as Control
            ?? throw new Xunit.Sdk.XunitException("Row 1 has no container.");
        var check = row.GetLogicalDescendants().OfType<CheckBox>().Single(c => c.Name == "ProcessorTick");
        check.IsChecked = !check.IsChecked;
        DialogTestKit.RunQueuedUiWork();
        var after = made.Slot.MidiProcessors.Select(p => p.Enabled).ToList();
        Assert.Equal(!before[1], after[1]);
        Assert.Equal(before[0], after[0]);
        Assert.Equal(before[2], after[2]);
        Assert.Equal(before[3], after[3]);
        made.Window.Close();
    }
}
