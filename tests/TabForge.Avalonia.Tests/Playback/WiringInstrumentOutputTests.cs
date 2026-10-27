using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using TabForge.Avalonia.Views;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Presenters;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// The Wiring window's Instrument output row: shown for an instrument plug-in only, and its choice is written through the edit callback.
/// Owns: the test of that row. Does not own: the window (WiringWindow.cs) or the choices (WiringPresenter).
/// Tests: this file.
/// </summary>
public sealed class WiringInstrumentOutputTests
{
    [AvaloniaFact]
    public void InstrumentPluginShowsTheInstrumentOutputRowAndWritesItsMode()
    {
        var tracks = WiringWindow.SampleTracks();
        var slot = WiringWindow.SampleSlot();
        slot.Type = PluginSlotType.Instrument;
        var window = new WiringWindow(tracks, tracks[0], slot, change => change());
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var combo = window.GetLogicalDescendants().OfType<ComboBox>().Single(c => c.Name == "InstrumentOutputCombo");
        Assert.Equal(WiringPresenter.InstrumentAudioModes, combo.ItemsSource);
        Assert.Equal(0, combo.SelectedIndex);
        combo.SelectedIndex = 1;
        Assert.Equal(WiringPresenter.InstrumentAudioValue(1), slot.InstrumentAudio);
        window.Close();
    }
}
