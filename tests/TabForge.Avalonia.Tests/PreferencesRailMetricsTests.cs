using TabForge.Platform.Theme;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Pins the Settings window metrics to the WPF values they were taken from (PreferencesWindow.xaml and its rail style).
/// Owns: the metric values the rail and the Reset page link use. Does not own: the layout (PreferencesWindow) or the rendering.
/// Tests: this file.
/// </summary>
public class PreferencesRailMetricsTests
{
    [Fact]
    public void RailAndResetPageMetricsMatchTheWpfWindow()
    {
        Assert.Equal(3, ChromeMetrics.SettingsRailAccentBarWidth);
        Assert.Equal(44, ChromeMetrics.SettingsRailRowHeight);
        Assert.Equal(12, ChromeMetrics.SettingsResetPageMarginLeft);
        Assert.Equal(6, ChromeMetrics.SettingsResetPageMarginTop);
        Assert.Equal(12, ChromeMetrics.SettingsResetPageFontSize);
    }
}
