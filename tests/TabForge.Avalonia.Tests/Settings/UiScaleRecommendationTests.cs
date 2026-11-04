using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Platform.Theme;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the monitor-aware interface size: the 0.7–1.0 recommendation for common monitors, the manual 1.5 maximum, the user-override mark (set by a
/// stepper change, kept by same-value writes, inferred for files from before the mark) and the automatic default never replacing a chosen size.
/// Does not own: applying a scale to a window (WindowUiScaleTests) or the Settings button's layout (the screenshot sweep).
/// Tests: UiScaleRecommendation, MonitorUiScale, the appearance.uiscale catalogue row, SettingsMigration, SettingsValidator.
/// </summary>
public sealed class UiScaleRecommendationTests
{
    [AvaloniaTheory]
    [InlineData(0.0)]
    [InlineData(0.0000000001)]
    public void RepeatedRecommendationDoesNotChangeSettingsOrEnableApply(double epsilon)
    {
        var settings = new AppSettings();
        settings.Appearance.UiScale = MonitorUiScale.Recommend(new Window())!.Value.Scale + epsilon;
        settings.Appearance.UiScaleUserOverridden = true;
        var window = new PreferencesWindow(new AvaloniaDialogHost(), settings);
        window.ShowPage(SettingsCatalog.Appearance);
        var previews = 0;
        window.Previewed += () => previews++;
        var changesBefore = window.PendingChanges(); // The presenter always supplies fallback text, even with no differing rows.
        var button = Assert.IsType<Button>(window.EditorOf(UiScaleRecommendation.RowKey));
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(changesBefore, window.PendingChanges());
        Assert.True(window.Working.Appearance.UiScaleUserOverridden);
        Assert.Equal(settings.Appearance.UiScale, window.Working.Appearance.UiScale);
        Assert.Equal(0, previews);
        Assert.False(window.GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.Content, "Apply")).IsEnabled);
    }

    [AvaloniaFact]
    public void ADifferentRecommendationIsStillAnEditAndARepeatDoesNotClearIt()
    {
        var settings = new AppSettings();
        settings.Appearance.UiScale = 1.5;
        settings.Appearance.UiScaleUserOverridden = true;
        var window = new PreferencesWindow(new AvaloniaDialogHost(), settings);
        window.ShowPage(SettingsCatalog.Appearance);
        var button = Assert.IsType<Button>(window.EditorOf(UiScaleRecommendation.RowKey));
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(MonitorUiScale.Recommend(window)!.Value.Scale, window.Working.Appearance.UiScale);
        Assert.False(window.Working.Appearance.UiScaleUserOverridden);
        Assert.NotEmpty(window.PendingChanges());
        Assert.True(window.GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.Content, "Apply")).IsEnabled);
        button = Assert.IsType<Button>(window.EditorOf(UiScaleRecommendation.RowKey));
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.NotEmpty(window.PendingChanges());
    }

    [Theory]
    [InlineData(1920, 1040, 1.0, 0.95)]   // 1080p at 100%: half-growth, then uniform 0.95 multiplier
    [InlineData(1536, 824, 1.25, 0.8)]    // 1080p at 125% with the taskbar: the height decides
    [InlineData(1536, 864, 1.25, 0.85)]   // 1080p at 125%, auto-hidden taskbar
    [InlineData(1536, 920, 1.25, 0.9)]    // 1920 x 1200 at 125%: leave room instead of recommending 0.95
    [InlineData(1536, 960, 1.25, 0.9)]    // 1920 x 1200 at 125%, auto-hidden taskbar
    [InlineData(1280, 693, 1.5, 0.7)]     // 1080p at 150%: below the minimum, held at 0.7
    [InlineData(1280, 760, 1.5, 0.75)]    // 1920 x 1200 at 150%: uniform multiplier also applies here
    [InlineData(1366, 728, 1.0, 0.7)]     // 1366 x 768 laptop
    [InlineData(1440, 860, 1.0, 0.85)]    // 1440 x 900
    [InlineData(2560, 1400, 1.0, 1.0)]    // 1440p at 100%: capped at the recommendation maximum
    [InlineData(2048, 1120, 1.25, 1.0)]   // 1440p at 125%
    [InlineData(2560, 1400, 1.5, 0.95)]   // 4K at 150%: the OS already enlarged the interface
    [InlineData(3840, 2120, 1.0, 1.0)]    // 4K at 100%: capped at the recommendation maximum
    [InlineData(1920, 1040, 2.0, 0.95)]   // 4K at 200%
    [InlineData(3440, 1400, 1.0, 1.0)]    // 3440 x 1440 ultrawide: capped at the recommendation maximum
    public void RecommendsASizeThatFitsTheMonitor(double width, double height, double scaling, double expected)
    {
        var advice = UiScaleRecommendation.For(width, height, scaling);
        Assert.Equal(expected, advice.Scale, 9);
        Assert.InRange(advice.Scale, UiDensity.MinScale, UiScaleRecommendation.MaxRecommendedScale);
    }

    [Fact]
    public void AMissingScreenRecommendsTheDefault()
    {
        Assert.Equal(UiDensity.DefaultScale, UiScaleRecommendation.For(0, 0, 1).Scale);
        Assert.Equal(UiDensity.DefaultScale, UiScaleRecommendation.For(double.NaN, 900, double.NaN).Scale);
    }

    [Theory]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void OsEnlargedScreensDoNotGrowWithExtraRoom(double scaling)
    {
        Assert.Equal(UiScaleRecommendation.For(1600, 950, scaling).Scale,
            UiScaleRecommendation.For(3840, 2120, scaling).Scale);
    }

    [Fact]
    public void RecommendationRemainsMonotonicAndOnTheStepperGrid()
    {
        var previous = UiDensity.MinScale;
        for (var percent = 50; percent <= 250; percent++)
        {
            var fit = percent / 100.0;
            var scale = UiScaleRecommendation.For(1600 * fit, 950 * fit, 1.25).Scale;
            Assert.True(scale >= previous);
            Assert.InRange(scale, UiDensity.MinScale, 1.0);
            Assert.Equal(Math.Round(scale / UiScaleRecommendation.Step), scale / UiScaleRecommendation.Step, 9);
            previous = scale;
        }
    }

    [Fact]
    public void TheAdviceExplainsWhatItSaw()
    {
        var advice = UiScaleRecommendation.For(1536, 824, 1.25);
        Assert.Equal("0.80×", advice.ScaleText);
        Assert.Equal("1536 × 824 working area at 125% display scaling", advice.Detail);
    }

    [Fact]
    public void TheMinimumIs07Everywhere()
    {
        Assert.Equal(0.7, UiDensity.MinScale);
        Assert.Equal(0.7, UiDensity.ClampScale(0.5));
        var row = SettingsCatalog.Build(new AppSettings()).Single(r => r.Key == "appearance.uiscale");
        Assert.Equal(0.7, row.Min);
        Assert.Equal(1.5, row.Max);
        var settings = new AppSettings();
        settings.Appearance.UiScale = 0.7;
        Assert.Equal(0.7, SettingsValidator.Normalize(settings).Appearance.UiScale);
        settings.Appearance.UiScale = 0.6;
        Assert.Equal(0.7, SettingsValidator.Normalize(settings).Appearance.UiScale);
    }

    [Fact]
    public void TheRecommendationCapDoesNotLimitManualSizes()
    {
        Assert.Equal(1.0, UiScaleRecommendation.MaxRecommendedScale);
        Assert.Equal(1.5, UiDensity.MaxScale);
        Assert.Equal(1.5, UiDensity.ClampScale(1.5));
        var settings = new AppSettings();
        var row = SettingsCatalog.Build(settings).Single(r => r.Key == "appearance.uiscale");
        row.Set(1.5);
        Assert.Equal(1.5, settings.Appearance.UiScale);
        Assert.True(settings.Appearance.UiScaleUserOverridden);
        Assert.Equal(1.5, SettingsValidator.Normalize(settings).Appearance.UiScale);
    }

    [Fact]
    public void AStepperChangeMarksTheSizeAsTheUsersOwnButASameValueWriteDoesNot()
    {
        var settings = new AppSettings();
        var row = SettingsCatalog.Build(settings).Single(r => r.Key == "appearance.uiscale");
        Assert.False(settings.Appearance.UiScaleUserOverridden);
        row.Set(1.0);   // a copy or a clamp of the same value
        Assert.False(settings.Appearance.UiScaleUserOverridden);
        row.Set(0.85);
        Assert.True(settings.Appearance.UiScaleUserOverridden);
        Assert.Equal(0.85, settings.Appearance.UiScale);
    }

    [Fact]
    public void TheRecommendedButtonRowSitsUnderInterfaceSize()
    {
        var rows = SettingsCatalog.Build(new AppSettings()).Where(r => r.Category == SettingsCatalog.Appearance).Select(r => r.Key).ToList();
        Assert.Equal(rows.IndexOf("appearance.uiscale") + 1, rows.IndexOf(UiScaleRecommendation.RowKey));
    }

    [Theory]
    [InlineData("{\"Appearance\":{\"UiScale\":0.85}}", true)]    // a size chosen before the mark existed
    [InlineData("{\"Appearance\":{\"UiScale\":1.0}}", false)]    // the old default: follows the monitor
    [InlineData("{}", false)]                                    // first start
    [InlineData("{\"Appearance\":{\"UiScale\":0.85,\"UiScaleUserOverridden\":false}}", false)]   // an automatic size saved by this version
    [InlineData("{\"Appearance\":{\"UiScale\":1.0,\"UiScaleUserOverridden\":true}}", true)]      // the user chose 1 explicitly
    public void LoadingInfersTheMarkOnlyForOlderFiles(string json, bool overridden)
    {
        WithSettingsFile(path =>
        {
            File.WriteAllText(path, json);
            Assert.Equal(overridden, SettingsFileService.Load(path).Appearance.UiScaleUserOverridden);
        });
    }

    [Fact]
    public void TheMarkSurvivesASaveAndLoad()
    {
        WithSettingsFile(path =>
        {
            var settings = new AppSettings();
            settings.Appearance.UiScale = 1.0;
            settings.Appearance.UiScaleUserOverridden = true;
            SettingsFileService.SaveAtomic(path, settings);
            Assert.True(SettingsFileService.Load(path).Appearance.UiScaleUserOverridden);
        });
    }

    [AvaloniaFact]
    public void TheAutomaticDefaultTakesTheRecommendationAndNeverReplacesAChosenSize()
    {
        var window = new Window();
        var advice = MonitorUiScale.Recommend(window);
        Assert.NotNull(advice);

        var automatic = new AppearanceSettings { UiScale = 1.3 };
        Assert.Equal(Math.Abs(advice!.Value.Scale - 1.3) > 1e-9, MonitorUiScale.UseRecommended(window, automatic));
        Assert.Equal(advice.Value.Scale, automatic.UiScale);
        Assert.False(automatic.UiScaleUserOverridden);

        WithSettingsFile(path =>
        {
            var file = new AppSettings();
            file.Appearance.UiScale = 1.3;
            file.Appearance.UiScaleUserOverridden = true;
            SettingsFileService.SaveAtomic(path, file);
            using var settings = MainWindowSettings.Open(path);
            var main = new MainWindow(settings);
            main.Show();
            Assert.Equal(1.3, settings.Settings.Appearance.UiScale);   // chosen: kept, nothing asked
            Assert.True(settings.Settings.Appearance.UiScaleUserOverridden);
            main.Close();
        });

        WithSettingsFile(path =>
        {
            var file = new AppSettings();
            file.Appearance.UiScale = 1.3;   // an automatic size from another monitor
            SettingsFileService.SaveAtomic(path, file);
            using var settings = MainWindowSettings.Open(path);
            var main = new MainWindow(settings);
            main.Show();
            Assert.Equal(advice.Value.Scale, settings.Settings.Appearance.UiScale);   // follows the monitor it opened on
            Assert.False(settings.Settings.Appearance.UiScaleUserOverridden);
            main.Close();
        });
    }

    private static void WithSettingsFile(Action<string> test)
    {
        var folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tf-avalonia-tests", "uiscale-recommend-" + Guid.NewGuid().ToString("N"))).FullName;
        try { test(Path.Combine(folder, "settings.json")); }
        finally { Directory.Delete(folder, recursive: true); }
    }
}
