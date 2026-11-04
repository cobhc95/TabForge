using System;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Platform.Theme;
using TabForge.Services;
using Xunit;
using static TabForge.Avalonia.Tests.DialogTestKit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the Settings window wrapping to its content as the UI scale changes: the default and the remembered size follow the scale
/// down and up, the minimum follows, and the size is remembered in inner DIPs so it reopens at the same proportions.
/// Does not own: the helper's maths (WindowUiScaleTests, WindowUiScaleHookTests).
/// Tests: this class.
/// </summary>
public class SettingsWindowScaleTests
{
    // One root across calls, like the main window's own: ApplyWindow sees the previous scale and raises UiScaleChanged on a real change.
    private static readonly LayoutTransformControl ScaleRoot = new();

    private static void SetScale(AppSettings saved, double scale)
    {
        saved.Appearance.UiScale = scale;
        ThemeSettingsBinding.ApplyWindow(new Window(), ScaleRoot, saved.Appearance);
        RunQueuedUiWork();
    }

    private static double WorkingWidth(Window window) => WindowUiScale.WorkingArea(window)?.Width ?? double.PositiveInfinity;

    [AvaloniaFact]
    public void TheDefaultSizeAndMinimumShrinkAndGrowWithTheScale()
    {
        var saved = new AppSettings();
        PreferencesWindow? window = null;
        try
        {
            SetScale(saved, 1.0);
            window = new PreferencesWindow(new AvaloniaDialogHost(), saved);
            window.Show();
            RunQueuedUiWork();
            Assert.Equal(1040, window.Width, 3);

            SetScale(saved, 0.7);
            Assert.Equal(Math.Min(1040 * 0.7, WorkingWidth(window)), window.Width, 3);
            Assert.Equal(690 * 0.7, window.Height, 3);
            Assert.Equal(ChromeMetrics.SettingsWindowMinWidth * 0.7, window.MinWidth, 3);
            Assert.Equal(ChromeMetrics.SettingsWindowMinHeight * 0.7, window.MinHeight, 3);

            SetScale(saved, 1.5);
            Assert.Equal(Math.Min(1040 * 1.5, WorkingWidth(window)), window.Width, 3);
            Assert.Equal(ChromeMetrics.SettingsWindowMinWidth * 1.5, window.MinWidth, 3);

            SetScale(saved, 1.0);
            Assert.Equal(1040, window.Width, 3);
            Assert.Equal(690, window.Height, 3);
        }
        finally
        {
            window?.Close();
            SetScale(saved, 1.0);
        }
    }

    [AvaloniaFact]
    public void ARememberedSizeIsKeptInInnerDipsAndFollowsTheScale()
    {
        var saved = new AppSettings { SettingsWindowWidth = 900, SettingsWindowHeight = 650 };
        PreferencesWindow? window = null;
        try
        {
            SetScale(saved, 0.7);
            window = new PreferencesWindow(new AvaloniaDialogHost(), saved);
            window.Show();
            RunQueuedUiWork();
            Assert.Equal(900 * 0.7, window.Width, 3);
            Assert.Equal(650 * 0.7, window.Height, 3);

            // The helper's own size change is not the user's: the remembered inner size stays what it was.
            Assert.Equal(900, saved.SettingsWindowWidth);
            Assert.Equal(650, saved.SettingsWindowHeight);

            SetScale(saved, 1.0);
            Assert.Equal(900, window.Width, 3);
            Assert.Equal(650, window.Height, 3);
        }
        finally
        {
            window?.Close();
            SetScale(saved, 1.0);
        }
    }

    [AvaloniaFact]
    public void ASizeTheUserSetIsRescaledNotFrozen()
    {
        var saved = new AppSettings();
        PreferencesWindow? window = null;
        try
        {
            SetScale(saved, 1.0);
            window = new PreferencesWindow(new AvaloniaDialogHost(), saved);
            window.Show();
            RunQueuedUiWork();

            window.Width = 1200;   // the user drags the edge
            RunQueuedUiWork();
            Assert.Equal(1200, saved.SettingsWindowWidth, 3);

            SetScale(saved, 0.7);
            Assert.Equal(Math.Min(1200 * 0.7, WorkingWidth(window)), window.Width, 3);
            Assert.Equal(1200, saved.SettingsWindowWidth, 3);
        }
        finally
        {
            window?.Close();
            SetScale(saved, 1.0);
        }
    }
}
