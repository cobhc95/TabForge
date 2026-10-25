using TabForge.Platform.Theme;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>Owns: the pinned Settings rail sizes (width 222, row 44, glyph 25, label 15, gap 9, row inset 16, accent bar 3), the caption metrics and the compact default window size (1040 x 690, minimum 834 x 597). Tests: ChromeMetrics as PreferencesWindow uses it. Does not own: the rail layout (PreferencesWindowTests).</summary>
public class SettingsRailMetricsTests
{
    [Fact]
    public void RailSizesMatchTheWpfSettingsWindow()
    {
        Assert.Equal(222, ChromeMetrics.SettingsRailWidth);
        Assert.Equal(44, ChromeMetrics.SettingsRailRowHeight);
        Assert.Equal(25, ChromeMetrics.SettingsRailIconSize);
        Assert.Equal(15, ChromeMetrics.SettingsRailLabelFontSize);
        Assert.Equal(9, ChromeMetrics.SettingsRailLabelGap);
        Assert.Equal(16, ChromeMetrics.SettingsRailRowInset);
        Assert.Equal(3, ChromeMetrics.SettingsRailAccentBarWidth);
    }

    [Fact]
    public void WindowAndCaptionSizes()
    {
        Assert.Equal(1040, ChromeMetrics.SettingsWindowWidth);
        Assert.Equal(690, ChromeMetrics.SettingsWindowHeight);
        Assert.Equal(834, ChromeMetrics.SettingsWindowMinWidth);
        Assert.Equal(597, ChromeMetrics.SettingsWindowMinHeight);
        Assert.Equal(11, ChromeMetrics.SettingsRailCaptionFontSize);
        Assert.Equal(18, ChromeMetrics.SettingsRailCaptionIndent);
        Assert.Equal(12, ChromeMetrics.SettingsRailCaptionTop);
        Assert.Equal(4, ChromeMetrics.SettingsRailCaptionBottom);
    }
}
