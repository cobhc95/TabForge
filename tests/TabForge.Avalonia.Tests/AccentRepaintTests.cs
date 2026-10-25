using System.Diagnostics;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Models;
using TabForge.Services;
using Xunit;
using Xunit.Abstractions;

namespace TabForge.Avalonia.Tests;

// Owns: a published accent change reaches the views that set brushes from code, with no pointer event, in one coalesced pass.
// Does not own: the brush values themselves (ThemePublishSkipTests, AccentDerivationTests).
// Tests: this file.
public class AccentRepaintTests(ITestOutputHelper output)
{
    private static void Flush() => Dispatcher.UIThread.RunJobs();

    [AvaloniaFact]
    public void LitMenuRowToggleTakesTheNewAccentWithoutAPointerEvent()
    {
        var appearance = new AppearanceSettings { ThemeMode = "Custom", Accent = "#FF0080" };
        try
        {
            AvaloniaPalette.Publish(appearance);
            var window = new Window { RequestedThemeVariant = global::Avalonia.Styling.ThemeVariant.Dark };
            var strip = MenuRowToggles.Build(new MainWindowCommands(window, () => { }));
            window.Content = strip;
            window.Show();
            var toggle = strip.Children.OfType<ToggleButton>().First();
            toggle.IsChecked = true;
            var before = ((ISolidColorBrush)toggle.BorderBrush!).Color;
            appearance.Accent = "#00FF00";
            AvaloniaPalette.Publish(appearance);
            Flush();
            var after = ((ISolidColorBrush)toggle.BorderBrush!).Color;
            Assert.NotEqual(before, after);
            Assert.Equal(AvaloniaPalette.ColorOf(AvaloniaPalette.Get("Accent", light: false)), after);
            window.Close();
        }
        finally { AvaloniaPalette.Publish(new AppearanceSettings()); }
    }

    [AvaloniaFact]
    public void ThemeColourCacheReadsAgainAfterAPublish()
    {
        var appearance = new AppearanceSettings { ThemeMode = "Custom", Accent = "#FF0080" };
        try
        {
            AvaloniaPalette.Publish(appearance);
            var window = new Window { RequestedThemeVariant = global::Avalonia.Styling.ThemeVariant.Dark };
            window.Show();
            var cache = new ThemeColourCache(window);
            var before = cache.Get("AccentBrush");
            appearance.Accent = "#00FF00";
            AvaloniaPalette.Publish(appearance);
            Assert.NotEqual(before, cache.Get("AccentBrush"));
            window.Close();
        }
        finally { AvaloniaPalette.Publish(new AppearanceSettings()); }
    }

    [AvaloniaFact]
    public void AccentChangeRepaintsOnceAndWithinTheBudgetOnALargeTree()
    {
        var appearance = new AppearanceSettings { ThemeMode = "Custom", Accent = "#FF0080" };
        try
        {
            AvaloniaPalette.Publish(appearance);
            var window = new Window { RequestedThemeVariant = global::Avalonia.Styling.ThemeVariant.Dark };
            var panel = new StackPanel();
            for (var i = 0; i < 10; i++) panel.Children.Add(MenuRowToggles.Build(new MainWindowCommands(window, () => { })));
            window.Content = panel;
            window.Show();
            Flush();   // the first layout and render are not the accent change
            var timer = Stopwatch.StartNew();
            appearance.Accent = "#00FF00";
            AvaloniaPalette.Publish(appearance);
            appearance.Accent = "#0000FF";
            AvaloniaPalette.Publish(appearance);
            Flush();
            var elapsed = timer.Elapsed.TotalMilliseconds;
            output.WriteLine($"accent change ms: {elapsed:F1}");
            Assert.False(AvaloniaPalette.InvalidateQueued);
            Assert.True(elapsed < 50);
            window.Close();
        }
        finally { AvaloniaPalette.Publish(new AppearanceSettings()); }
    }

    [Fact]
    public void SelectionFollowsTheAccentUntilThePickedOne()
    {
        var appearance = new AppearanceSettings { Accent = "#00FF00" };
        Assert.Equal("#00FF00", appearance.SelectionColourInUse());
        appearance.SelectionColour = appearance.Own("SelectionColour", "#112233");
        Assert.Equal("#112233", appearance.SelectionColourInUse());
    }

    [Fact]
    public void FretboardScaleHighlightFollowsTheAccentByDefault()
    {
        Assert.Equal("Accent", new EditingSettings().ScaleHighlightColour);
        Assert.Contains("Accent", ScaleHighlightStyles.Colours);
        Assert.Equal("Accent", FretboardSettingsBinding.ColourKey("Accent"));
        Assert.Equal("InstrumentAccent", FretboardSettingsBinding.ColourKey("Blue"));
    }
}
