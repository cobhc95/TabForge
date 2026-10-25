using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// The Settings rail rows paint their own fill: the selected row shows the AccentSoft fill across its width (a pixel away from the accent bar,
/// icon and label), a hovered row shows the hover fill, and the selected label reads at 4.5:1 or better in both themes.
/// Owns: the rail row fill, hover and label checks. Does not own: the row template (PrefsNavRowTemplate) or the row styles (AvaloniaFormControlStyles).
/// Tests: this class.
/// </summary>
public class PrefsNavRowTemplateTests
{
    // 180 DIP from the row's left edge: right of the icon and the longest label, and left of the rail's scrollbar overlay (from about 205 DIP).
    private const double ProbeX = 180;

    [AvaloniaFact]
    public void SelectedRailRowPaintsAccentSoftAcrossItsWidthInDark()
    {
        using var theme = DialogTestKit.UseTheme(light: false);
        var window = ShownWindow(SettingsCatalog.General);
        using var frame = window.CaptureRenderedFrame()!;
        var accentSoft = FrameValue(ResourceColour(window, "AccentSoftBrush"));
        Assert.Equal(accentSoft, ProbeRow(frame, window, RailRow(window, SettingsCatalog.General)));
        Assert.NotEqual(accentSoft, ProbeRow(frame, window, RailRow(window, SettingsCatalog.Appearance)));
    }

    [AvaloniaFact]
    public void HoveredRailRowPaintsHoverFillInDark()
    {
        using var theme = DialogTestKit.UseTheme(light: false);
        var window = ShownWindow(SettingsCatalog.General);
        var row = RailRow(window, SettingsCatalog.Appearance);
        window.MouseMove(RowPoint(window, row));
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame()!;
        Assert.Equal(FrameValue(ResourceColour(window, "HoverBrush")), ProbeRow(frame, window, row));
    }

    [AvaloniaFact]
    public void SelectedRailLabelReadsAtLeast4Point5InDark() => AssertSelectedLabelContrast(light: false);

    [AvaloniaFact]
    public void SelectedRailLabelReadsAtLeast4Point5InLight() => AssertSelectedLabelContrast(light: true);

    private static void AssertSelectedLabelContrast(bool light)
    {
        using var theme = DialogTestKit.UseTheme(light);
        var window = ShownWindow(SettingsCatalog.General);
        var row = RailRow(window, SettingsCatalog.General);
        var label = row.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == SettingsCatalog.General);
        Assert.Equal(FontWeight.SemiBold, label.FontWeight);
        Assert.True(DialogTestKit.Contrast(label.Foreground, row.Background, "Selected rail row") >= 4.5);
    }

    private static PreferencesWindow ShownWindow(string page)
    {
        var window = new PreferencesWindow(new AvaloniaDialogHost(), new TabForge.Services.AppSettings());
        window.Show();
        window.ShowPage(page);
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static ListBoxItem RailRow(Window window, string page) =>
        window.GetVisualDescendants().OfType<ListBoxItem>().Single(item => (string?)item.Tag == page);

    // The point ProbeX into the row, at its vertical middle, in window DIP.
    private static Point RowPoint(Window window, ListBoxItem row)
    {
        var origin = row.TranslatePoint(new Point(0, 0), window)!.Value;
        return new Point(origin.X + ProbeX, origin.Y + row.Bounds.Height / 2);
    }

    // The pixel the row paints at RowPoint. The frame is in device pixels, the points in DIP.
    private static int ProbeRow(WriteableBitmap frame, Window window, ListBoxItem row)
    {
        var point = RowPoint(window, row);
        return ButtonLabelFillTests.PixelAt(frame, point.X * window.RenderScaling, point.Y * window.RenderScaling);
    }

    private static Color ResourceColour(Window window, string key)
    {
        Assert.True(Application.Current!.TryGetResource(key, window.ActualThemeVariant, out var value));
        return ((ISolidColorBrush)value!).Color;
    }

    // The 32-bit value the frame reads for a colour: the headless frame is Rgba8888, so the red byte is the lowest.
    private static int FrameValue(Color colour) => colour.R | colour.G << 8 | colour.B << 16 | colour.A << 24;
}
