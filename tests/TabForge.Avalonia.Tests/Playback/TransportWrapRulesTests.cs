using TabForge.Avalonia.Platform;
using TabForge.Platform.Theme;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the transport bar fit rules: a wrapped height fits one row when no taller than the tallest group; the snug bar and list widths that hold
/// every item (all buttons, the divider, the master knob, the zoom pair and the master meter with their spacing and the bar's padding and margins)
/// on one row at the density factor; and the button counts the snug widths are built from. The bar never shrinks its buttons (TransportBarView wraps instead).
/// Does not own: the layout of the groups (TransportBarView) or the button drawing.
/// </summary>
public class TransportWrapRulesTests
{
    [Fact]
    public void OneRowFitsAndTwoRowsDoNot()
    {
        Assert.True(TransportWrapRules.FitsOneRow(38, 38));
        Assert.False(TransportWrapRules.FitsOneRow(2 * 36, 36));
    }

    [Fact]
    public void SnugWidthsCountEveryTransportButton()
    {
        Assert.Equal(8, TransportBarView.Items.Count);   // the playback group and the three toggles; live input, tuning, snap and mixer are the other four full-size buttons
        Assert.Equal(2, TransportBarView.ZoomItems.Count);   // stacked in one column after the master knob
    }

    [Theory]
    [InlineData(1.0, 551.0, 557.0)]
    [InlineData(0.85, 549.0, 555.0)]
    public void SnugWidthsFitTheTransportRowExactly(double factor, double expectedBar, double expectedList)
    {
        Assert.Equal(expectedBar, TransportWrapRules.SnugBarWidth(factor), 3);
        Assert.Equal(expectedList, TransportWrapRules.SnugListWidth(factor), 3);
        Assert.Equal(expectedList, TrackListWidthRules.DefaultListWidthFor(factor), 3);
        var comfortable = new AppearanceSettings { Density = "Comfortable" };
        var compact = new AppearanceSettings { Density = "Compact" };
        Assert.Equal(TransportWrapRules.SnugListWidth(factor),
            TransportWrapRules.SnugListWidth(factor < 1.0 ? compact : comfortable), 6);
        var chrome = ChromeMetrics.TransportBarBorder + ChromeMetrics.TransportBarBorder
            + (ChromeMetrics.TransportBarPadding + ChromeMetrics.TransportBarPadding) * factor;
        var slack = TransportWrapRules.SnugBarWidth(factor) - TransportWrapRules.SnugContentWidth(factor) - chrome;
        Assert.InRange(slack, 0, 1);   // the whole-DIP round-up leaves less than one DIP past the meter
        Assert.Equal(ChromeMetrics.TransportBarOuterLeft + ChromeMetrics.TransportBarOuterRight,
            TransportWrapRules.SnugListWidth(factor) - TransportWrapRules.SnugBarWidth(factor), 6);
    }
}
