using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using TabForge.Avalonia.Views;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// The Wiring window offers no side-chain while plug-ins run in their own process, as the settings say; otherwise it does.
/// Owns: this check. Does not own: the rule (WiringPresenter.SidechainDisabledReason) or the window (WiringWindow).
/// Tests: this class.
/// </summary>
public class FxWiringIsolationTests
{
    [AvaloniaFact]
    public void SeparateProcessPlugInsOfferNoSidechain()
    {
        var tracks = WiringWindow.SampleTracks();
        var shared = new WiringWindow(tracks, tracks[0], WiringWindow.SampleSlot(), change => change());
        var isolated = new WiringWindow(tracks, tracks[0], WiringWindow.SampleSlot(), change => change(), separateProcessPerPlugin: true);
        shared.Show();
        isolated.Show();
        Assert.True(shared.GetLogicalDescendants().OfType<ComboBox>().Single(c => c.Name == "SidechainCombo").IsEnabled);
        Assert.False(isolated.GetLogicalDescendants().OfType<ComboBox>().Single(c => c.Name == "SidechainCombo").IsEnabled);
        shared.Close();
        isolated.Close();
    }
}
