using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the main window opens at its saved position, size and maximised state, falls back to the OS placement when the
/// saved position is off the screens, and saves the position it is moved to. Every run uses its own temp folder; no user settings are read or written.
/// Tests: MainWindowPlacement.
/// </summary>
public sealed class MainWindowPlacementTests
{
    [AvaloniaFact]
    public void SavedSizeAndMaximisedStateAreRestoredAtStart()
    {
        WithSettingsFile(path =>
        {
            SettingsFileService.SaveAtomic(path, new AppSettings { WindowWidth = 1300, WindowHeight = 820, Maximised = true });
            var settings = MainWindowSettings.Open(path);
            var main = new MainWindow(settings);
            Assert.Equal(WindowState.Maximized, main.WindowState);
            Assert.Equal(1300d, main.Width);
            Assert.Equal(820d, main.Height);
            settings.Dispose();
        });
    }

    [AvaloniaFact]
    public void SavedPositionAndSizeAreRestoredWhenOnScreen()
    {
        WithSettingsFile(path =>
        {
            var area = new Window().Screens.Primary!.WorkingArea;
            SettingsFileService.SaveAtomic(path, new AppSettings
            {
                WindowWidth = 1300, WindowHeight = 820, Maximised = false,
                WindowLeft = area.X + 100, WindowTop = area.Y + 80
            });
            var settings = MainWindowSettings.Open(path);
            var main = new MainWindow(settings);
            Assert.Equal(WindowStartupLocation.Manual, main.WindowStartupLocation);
            Assert.Equal(new PixelPoint(area.X + 100, area.Y + 80), main.Position);
            Assert.Equal(1300d, main.Width);
            Assert.Equal(820d, main.Height);
            settings.Dispose();
        });
    }

    [AvaloniaFact]
    public void SavedPositionOffEveryScreenFallsBackToTheOsPlacement()
    {
        WithSettingsFile(path =>
        {
            SettingsFileService.SaveAtomic(path, new AppSettings
            {
                WindowWidth = 1300, WindowHeight = 820, Maximised = false,
                WindowLeft = -15_000, WindowTop = -15_000
            });
            var settings = MainWindowSettings.Open(path);
            var main = new MainWindow(settings);
            Assert.NotEqual(new PixelPoint(-15_000, -15_000), main.Position);
            Assert.Equal(1300d, main.Width);
            settings.Dispose();
        });
    }

    [AvaloniaFact]
    public void MaximisedStateWinsOverSavedPosition()
    {
        WithSettingsFile(path =>
        {
            var area = new Window().Screens.Primary!.WorkingArea;
            SettingsFileService.SaveAtomic(path, new AppSettings
            {
                WindowWidth = 1300, WindowHeight = 820, Maximised = true,
                WindowLeft = area.X + 100, WindowTop = area.Y + 80
            });
            var settings = MainWindowSettings.Open(path);
            var main = new MainWindow(settings);
            Assert.Equal(WindowState.Maximized, main.WindowState);
            settings.Dispose();
        });
    }

    [AvaloniaFact]
    public void MovedWindowsPositionIsSavedOnFlush()
    {
        WithSettingsFile(path =>
        {
            var area = new Window().Screens.Primary!.WorkingArea;
            SettingsFileService.SaveAtomic(path, new AppSettings { Maximised = false });
            var settings = MainWindowSettings.Open(path);
            var main = new MainWindow(settings);
            main.Show();
            main.Position = new PixelPoint(area.X + 240, area.Y + 60);
            settings.Dispose();   // the window's close writes the pending save the same way

            var reopened = MainWindowSettings.Open(path);
            Assert.Equal(area.X + 240, reopened.Settings.WindowLeft);
            Assert.Equal(area.Y + 60, reopened.Settings.WindowTop);
            reopened.Dispose();
            main.Close();
        });
    }

    [Fact]
    public void UnusableSavedPositionsAreDroppedAndNegativeScreenCoordinatesKept()
    {
        var dropped = SettingsValidator.Normalize(new AppSettings { WindowLeft = double.NaN, WindowTop = 1e9 });
        Assert.Null(dropped.WindowLeft);
        Assert.Null(dropped.WindowTop);
        var kept = SettingsValidator.Normalize(new AppSettings { WindowLeft = -1920, WindowTop = 240 });
        Assert.Equal(-1920d, kept.WindowLeft);
        Assert.Equal(240d, kept.WindowTop);
    }

    [Fact]
    public void WindowEntirelyInsideAScreenIsMostlyWithinIt()
    {
        var area = new PixelRect(0, 0, 1920, 1040);
        Assert.True(MainWindowPlacement.IsMostlyWithin(new PixelRect(100, 100, 960, 1000), area));
    }

    [Fact]
    public void WindowHalfOutsideAScreenIsAtTheLimit()
    {
        var area = new PixelRect(0, 0, 1920, 1040);
        // 1000 wide at x = 1420: 500 of 1000 pixels are on screen, exactly the minimum share.
        Assert.True(MainWindowPlacement.IsMostlyWithin(new PixelRect(1420, 0, 1000, 1000), area));
        Assert.False(MainWindowPlacement.IsMostlyWithin(new PixelRect(1421, 0, 1000, 1000), area));
    }

    [Fact]
    public void WindowOnAnotherMonitorToTheLeftIsWithinThatMonitor()
    {
        var left = new PixelRect(-1920, 0, 1920, 1040);
        Assert.True(MainWindowPlacement.IsMostlyWithin(new PixelRect(-1800, 100, 1200, 800), left));
    }

    private static void WithSettingsFile(Action<string> test)
    {
        var folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tf-avalonia-tests", "placement-" + Guid.NewGuid().ToString("N"))).FullName;
        try { test(Path.Combine(folder, "settings.json")); }
        finally { Directory.Delete(folder, recursive: true); }
    }
}
