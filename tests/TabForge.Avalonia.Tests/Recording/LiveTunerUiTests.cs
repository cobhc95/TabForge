using System;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Views.Fx;
using TabForge.LiveInput;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the strip tuner as the user sees it: on by default (a saved off stays off), the cents colour band, the window dropdown wide enough for its longest
/// item, and a steady frame that allocates nothing and asks for no layout.
/// Does not own: the reading rules (LiveTunerReadoutTests) or the strip position (LiveStripSliderTests).
/// Tests: this class.
/// </summary>
public class LiveTunerUiTests
{
    [Fact]
    public void TunerIsOnByDefaultAndASavedOffStaysOff()
    {
        Assert.True(new LiveInputSettings().TunerOn);
        Assert.True(JsonSerializer.Deserialize<LiveInputSettings>("{}")!.TunerOn);
        Assert.False(JsonSerializer.Deserialize<LiveInputSettings>("{\"TunerOn\":false}")!.TunerOn);
    }

    [AvaloniaFact]
    public void StripOpensWithTheTunerOnAndTheWindowSelectorReadsWindowMs()
    {
        var (live, strip, session, client) = LiveStripSliderTests.OpenLive();
        Assert.True(strip.Tuner.IsOn);
        Assert.Equal("100 ms", strip.Tuner.Window.SelectedItem);
        live.Close(); session.Dispose(); client.Dispose();
    }

    [AvaloniaFact]
    public void WindowSelectorIsAsWideAsItsLongestItem()
    {
        var (live, strip, session, client) = LiveStripSliderTests.OpenLive();
        var window = strip.Tuner.Window;
        var widest = 0.0;
        foreach (var item in window.Items.OfType<string>())
        {
            var probe = new TextBlock { Text = item, FontSize = window.FontSize };
            probe.Measure(Size.Infinity);
            widest = Math.Max(widest, probe.DesiredSize.Width);
        }
        Assert.True(window.Bounds.Width >= widest + window.Padding.Left + window.Padding.Right, $"selector {window.Bounds.Width} vs longest item {widest}");
        Assert.True(window.Bounds.Right <= strip.Tuner.Controls.Bounds.Width + 0.5, "the selector stays inside its group");
        live.Close(); session.Dispose(); client.Dispose();
    }

    [Theory]
    [InlineData(0, "Success")]
    [InlineData(3, "Success")]
    [InlineData(-3, "Success")]
    [InlineData(3.2, "Warning")]
    [InlineData(-4, "Warning")]
    [InlineData(-12, "Warning")]
    public void NeedleAndReadoutAreGreenWithinThreeCents(double cents, string token) => Assert.Equal(token, LiveCentsBar.ToneToken(cents));

    [Fact]
    public void CentsTextIsExactAndCached()
    {
        Assert.Equal("-12 \u00a2", LiveStripRules.CentsText(-12));
        Assert.Equal("+7 \u00a2", LiveStripRules.CentsText(7));
        Assert.Equal("0 \u00a2", LiveStripRules.CentsText(0));
        Assert.Same(LiveStripRules.CentsText(-12), LiveStripRules.CentsText(-12));
    }

    [AvaloniaFact]
    public void ASteadyTunerFrameAllocatesNothingAndAsksForNoLayout()
    {
        var (live, strip, session, client) = LiveStripSliderTests.OpenLive();
        strip.PeakSource = () => (0.2f, 0.1f);
        strip.TunerSource = () => (441f, 0.95f, 1);
        var now = DateTime.UtcNow;
        for (var i = 0; i < 20; i++) strip.Tick(i * 7, now);   // warm up: shapes text, fills caches
        live.UpdateLayout();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 20; i < 2020; i++) strip.Tick(i * 7, now);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 2048, $"{allocated} bytes over 2000 steady frames");
        Assert.True(strip.IsMeasureValid && strip.IsArrangeValid);
        live.Close(); session.Dispose(); client.Dispose();
    }
}
