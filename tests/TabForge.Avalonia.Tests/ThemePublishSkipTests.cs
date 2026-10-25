using System.Diagnostics;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using TabForge.Avalonia.Platform;
using TabForge.Services;
using Xunit;
using Xunit.Abstractions;

namespace TabForge.Avalonia.Tests;

// Owns: Publish repaints only on a real appearance change, and updates the published brushes in place.
// Does not own: the live theme switch values (ThemeLiveSwitchTests).
// Tests: this file.
public class ThemePublishSkipTests(ITestOutputHelper output)
{
    [AvaloniaFact]
    public void NonAppearanceChangeLeavesTheBrushesAlone()
    {
        var app = Application.Current!;
        var appearance = new AppearanceSettings();
        try
        {
            AvaloniaPalette.Publish(appearance);
            Assert.True(app.TryGetResource("AccentBrush", ThemeVariant.Dark, out var value));
            var brush = Assert.IsType<SolidColorBrush>(value);
            var marker = Color.Parse("#010203");
            brush.Color = marker;   // a Fill would overwrite this
            appearance.FontSize = 15;
            appearance.UiScale = 1.25;
            AvaloniaPalette.Publish(appearance);
            Assert.Equal(marker, brush.Color);
        }
        finally { AvaloniaPalette.Publish(new AppearanceSettings()); }
    }

    [AvaloniaFact]
    public void AccentChangeKeepsBrushInstancesAndLeavesNoStaleBrush()
    {
        var app = Application.Current!;
        var appearance = new AppearanceSettings { ThemeMode = "Custom", Accent = "#FF0080" };
        try
        {
            AvaloniaPalette.Publish(appearance);
            Assert.True(app.TryGetResource("AccentBrush", ThemeVariant.Dark, out var before));
            Assert.True(app.TryGetResource("FocusRing", ThemeVariant.Dark, out var ringBefore));
            var timer = Stopwatch.StartNew();
            appearance.Accent = "#00FF00";
            AvaloniaPalette.Publish(appearance);
            output.WriteLine($"accent publish ms: {timer.Elapsed.TotalMilliseconds:F1}");
            Assert.True(app.TryGetResource("AccentBrush", ThemeVariant.Dark, out var after));
            Assert.True(app.TryGetResource("FocusRing", ThemeVariant.Dark, out var ringAfter));
            Assert.Same(before, after);
            Assert.Same(ringBefore, ringAfter);
            Assert.Equal(AvaloniaPalette.ColorOf(AvaloniaPalette.Get("Accent", light: false)), Assert.IsType<SolidColorBrush>(after).Color);
            Assert.Equal(AvaloniaPalette.ColorOf(AvaloniaPalette.Get("FocusRing", light: false)), Assert.IsType<SolidColorBrush>(ringAfter).Color);
        }
        finally { AvaloniaPalette.Publish(new AppearanceSettings()); }
    }
}
