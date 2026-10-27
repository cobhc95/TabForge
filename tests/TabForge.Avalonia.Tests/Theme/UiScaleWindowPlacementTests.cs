using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the proof that UI scale changes never move the main window: maximized and normal windows keep
/// Width/Height/Position/WindowState across 1.0 -&gt; 0.9 -&gt; 1.25 via the real settings path, while the
/// restore-window toggle still restores. Every run uses its own temp folder; no user settings are touched.
/// Tests: MainWindowPlacement (FollowSettings/Restore), MainWindow MinWidth clamp.
/// </summary>
public sealed class UiScaleWindowPlacementTests
{
    [AvaloniaFact]
    public void MaximizedWindowKeepsPlacementAcrossUiScaleChanges()
    {
        WithSettingsFile(path =>
        {
            SettingsFileService.SaveAtomic(path, new AppSettings { WindowWidth = 1300, WindowHeight = 820, Maximised = true });
            using var settings = MainWindowSettings.Open(path);
            var main = new MainWindow(settings);
            main.Show();
            Assert.Equal(WindowState.Maximized, main.WindowState);
            var width = main.Width;
            var height = main.Height;
            var position = main.Position;
            var state = main.WindowState;

            ApplyUiScale(settings, 0.9);
            Assert.Equal(width, main.Width);
            Assert.Equal(height, main.Height);
            Assert.Equal(position, main.Position);
            Assert.Equal(state, main.WindowState);

            ApplyUiScale(settings, 1.25);
            Assert.Equal(width, main.Width);
            Assert.Equal(height, main.Height);
            Assert.Equal(position, main.Position);
            Assert.Equal(state, main.WindowState);

            ApplyUiScale(settings, 1.0);
            Assert.Equal(width, main.Width);
            Assert.Equal(height, main.Height);
            Assert.Equal(position, main.Position);
            Assert.Equal(state, main.WindowState);

            AssertMinWidthFitsWorkingArea(main);
            main.Close();
        });
    }

    [AvaloniaFact]
    public void NormalWindowKeepsPlacementAcrossUiScaleChanges()
    {
        WithSettingsFile(path =>
        {
            var area = new Window().Screens.Primary!.WorkingArea;
            SettingsFileService.SaveAtomic(path, new AppSettings
            {
                WindowWidth = 1300, WindowHeight = 820, Maximised = false,
                WindowLeft = area.X + 100, WindowTop = area.Y + 80
            });
            using var settings = MainWindowSettings.Open(path);
            var main = new MainWindow(settings);
            main.Show();
            Assert.Equal(WindowState.Normal, main.WindowState);
            var width = main.Width;
            var height = main.Height;
            var position = main.Position;

            ApplyUiScale(settings, 0.9);
            Assert.Equal(width, main.Width);
            Assert.Equal(height, main.Height);
            Assert.Equal(position, main.Position);
            Assert.Equal(WindowState.Normal, main.WindowState);

            ApplyUiScale(settings, 1.25);
            Assert.Equal(width, main.Width);
            Assert.Equal(height, main.Height);
            Assert.Equal(position, main.Position);
            Assert.Equal(WindowState.Normal, main.WindowState);

            ApplyUiScale(settings, 1.0);
            Assert.Equal(width, main.Width);
            Assert.Equal(height, main.Height);
            Assert.Equal(position, main.Position);
            Assert.Equal(WindowState.Normal, main.WindowState);

            AssertMinWidthFitsWorkingArea(main);
            main.Close();
        });
    }

    [AvaloniaFact]
    public void RestoreWindowToggleStillRestores()
    {
        WithSettingsFile(path =>
        {
            SettingsFileService.SaveAtomic(path, new AppSettings
            {
                WindowWidth = 1300, WindowHeight = 820, Maximised = false,
                General = new GeneralSettings { RestoreWindow = true }
            });
            using var settings = MainWindowSettings.Open(path);
            var main = new MainWindow(settings);
            main.Show();
            Assert.Equal(1300d, main.Width);

            // UI scale via the real settings path must not restore or move the window.
            ApplyUiScale(settings, 0.9);
            Assert.Equal(1300d, main.Width);
            Assert.Equal(820d, main.Height);

            // Switching restore off is a no-op for placement.
            settings.Settings.General.RestoreWindow = false;
            settings.SaveSettings();
            Assert.Equal(1300d, main.Width);

            // A new saved size while restore is off stays quiet: the window does not follow it.
            settings.Settings.WindowWidth = 1400;
            settings.Settings.WindowHeight = 900;
            settings.SaveWindowPlacement();
            Assert.Equal(1300d, main.Width);

            // Switching restore back on restores the saved size at once.
            settings.Settings.General.RestoreWindow = true;
            settings.SaveSettings();
            Assert.Equal(1400d, main.Width);
            Assert.Equal(900d, main.Height);

            main.Close();
        });
    }

    private static void ApplyUiScale(MainWindowSettings settings, double scale)
    {
        settings.Settings.Appearance.UiScale = scale;
        settings.SaveSettings();
    }

    private static void AssertMinWidthFitsWorkingArea(MainWindow main)
    {
        var screen = main.Screens.ScreenFromWindow(main) ?? main.Screens.Primary;
        if (screen is null) return;
        var scaling = screen.Scaling > 0 ? screen.Scaling : 1.0;
        if (!(scaling > 0)) scaling = 1.0;
        var areaWidth = screen.WorkingArea.Width / scaling;
        if (areaWidth > 0) Assert.True(main.MinWidth <= areaWidth + 1, $"MinWidth {main.MinWidth} exceeds working area {areaWidth}");
    }

    private static void WithSettingsFile(Action<string> test)
    {
        var folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tf-avalonia-tests", "uiscale-placement-" + Guid.NewGuid().ToString("N"))).FullName;
        try { test(Path.Combine(folder, "settings.json")); }
        finally { Directory.Delete(folder, recursive: true); }
    }
}
