// Owns: the density-scaled chrome size resources (control paddings, min heights, tab and menu bar heights) set once in the application resources.
// Does not own: the factors (UiDensity), the styles that read the keys (AvaloniaFormControlStyles, AvaloniaMenuTemplates), or notation and timeline sizes.
// Tests: UiMetricsTests.
using Avalonia;
using Avalonia.Controls;
using TabForge.Platform.Theme;
using TabForge.Services;

namespace TabForge.Avalonia;

/// <summary>Publishes the chrome padding and height resources for the appearance density; styles bind them with DynamicResource so Apply updates every open window at once.</summary>
public static class UiDensityResources
{
    public const string ButtonPad = "DensButtonPad", TextBoxPad = "DensTextBoxPad", ComboPad = "DensComboPad", ListPad = "DensListPad",
        MenuItemPad = "DensMenuItemPad", ControlMinHeight = "DensControlMinHeight", TabHeight = "DensTabHeight", TabPad = "DensTabPad",
        MenuBarHeight = "DensMenuBarHeight", MenuInnerHeight = "DensMenuInnerHeight", Factor = "DensFactor",
        PaletteButton = "DensPaletteButton", PaletteGap = "DensPaletteGap", PaneMargin = "DensPaneMargin", PanePad = "DensPanePad", GroupGap = "DensGroupGap";

    /// <summary>Sets every key in <paramref name="resources"/> from the density of <paramref name="appearance"/>; Comfortable gives the base values exactly.</summary>
    public static void Publish(IResourceDictionary resources, AppearanceSettings appearance)
    {
        var pad = UiDensity.Of(appearance).Pad;
        resources[ButtonPad] = Scaled(8, 3, pad);
        resources[TextBoxPad] = Scaled(5, 3, pad);
        resources[ComboPad] = Scaled(6, 2, pad);
        resources[ListPad] = Scaled(5, 3, pad);
        resources[MenuItemPad] = Scaled(8, 5, pad, 10);
        resources[TabPad] = Scaled(12, 0, pad);
        resources[ControlMinHeight] = UiMetrics.Row(24, appearance);
        resources[TabHeight] = UiMetrics.Bar(34, appearance);
        resources[MenuBarHeight] = UiMetrics.Bar(MainWindowMenu.BarHeight, appearance);
        resources[MenuInnerHeight] = UiMetrics.Bar(MainWindowMenu.BarHeight - 1, appearance);
        resources[Factor] = pad;
        resources[PaletteButton] = UiMetrics.Bar(36, appearance);
        resources[PaletteGap] = UiMetrics.Sides(1, 1, 1, 1, pad);
        resources[PaneMargin] = UiMetrics.Sides(8, 8, 8, 8, pad);
        resources[PanePad] = UiMetrics.Sides(7, 7, 7, 7, pad);
        resources[GroupGap] = UiMetrics.Sides(0, 0, 0, 6, pad);
        resources["TextControlThemeMinHeight"] = UiMetrics.Row(24, appearance);
        resources["TextControlThemePadding"] = Scaled(5, 3, pad);
    }

    private static Thickness Scaled(double x, double y, double factor, double right = double.NaN) =>
        new(x * factor, y * factor, (double.IsNaN(right) ? x : right) * factor, y * factor);
}
