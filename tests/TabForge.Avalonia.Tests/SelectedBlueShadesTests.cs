using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Presenters;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>Owns: the selected-row blue (palette key AccentSoft) pinned to the WPF pixels measured in the reference shots: dark #2D5181, light #7895BA,
/// and the painted fill of the selected Settings rail row (its content presenter paints nothing over the row fill).
/// Does not own: the row templates (LookStateRulesTests) or the dock tab pill (MainWindowDockParityTests).
/// Tests: AvaloniaPalette.Get("AccentSoft") in the dark and the light theme; the selected rail row in the dark theme.</summary>
public class SelectedBlueShadesTests
{
    [Theory]
    [InlineData(false, "#2D5181")]
    [InlineData(true, "#839BB9")]   // light only: the light-mode selected-tint restyle lightened AccentSoft from #6A82A1; dark #2D5181 is unchanged
    public void SelectedBlueMatchesWpfPixels(bool light, string expectedHex)
    {
        Assert.Equal(Color.Parse(expectedHex), AvaloniaPalette.ColorOf(AvaloniaPalette.Get("AccentSoft", light)));
    }

    [AvaloniaFact]
    public void SelectedRailRowPaintsTheAccentSoftFillOnly()
    {
        var window = new PreferencesWindow(new AvaloniaDialogHost(), new TabForge.Services.AppSettings()) { RequestedThemeVariant = ThemeVariant.Dark };
        window.ShowPage(PreferencesCatalog.Appearance);
        window.Show();
        var rail = window.GetLogicalDescendants().OfType<ListBox>().Single(l => AutomationProperties.GetName(l) == "Settings pages");
        var row = rail.Items.OfType<ListBoxItem>().Single(i => i.Tag as string == PreferencesCatalog.Appearance);
        row.ApplyTemplate();
        Assert.Equal(AvaloniaPalette.ColorOf(AvaloniaPalette.Get("AccentSoft", false)), ((ISolidColorBrush)row.Background!).Color);
        Assert.All(row.GetVisualDescendants().OfType<ContentPresenter>(), presenter => Assert.True(presenter.Background is null or ISolidColorBrush { Color.A: 0 }));
        window.Close();
    }
}
