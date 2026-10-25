using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the global window hook: wrapping a plain window once at its content, scaling its explicit inner sizes from the inner
/// base, leaving SizeToContent axes automatic, never double-wrapping the Mixer, and detaching when it closes.
/// Does not own: the wrapper itself or the scale maths (WindowUiScaleTests).
/// Tests: this class.
/// </summary>
public class WindowUiScaleHookTests
{
    // One root across calls, like the main window's own: ApplyWindow sees the previous scale and raises UiScaleChanged on every real change.
    private static LayoutTransformControl? _scaleRoot;

    private static SongDocument DocumentOf(SongProject project) => new(() => null) { Project = project };

    private static void SetScale(double scale)
    {
        WindowUiScale.CurrentAppearance.UiScale = scale;
        _scaleRoot ??= new LayoutTransformControl();
        ThemeSettingsBinding.ApplyWindow(new Window(), _scaleRoot, WindowUiScale.CurrentAppearance);
    }

    private static double WorkingWidth(Window window)
    {
        var screens = window.Screens;
        var screen = screens?.ScreenFromWindow(window) ?? screens?.Primary;
        if (screen is null) return double.PositiveInfinity;
        var scaling = screen.Scaling > 0 ? screen.Scaling : window.RenderScaling;
        if (!(scaling > 0)) scaling = 1.0;
        return screen.WorkingArea.Width / scaling;
    }

    private static double WorkingHeight(Window window)
    {
        var screens = window.Screens;
        var screen = screens?.ScreenFromWindow(window) ?? screens?.Primary;
        if (screen is null) return double.PositiveInfinity;
        var scaling = screen.Scaling > 0 ? screen.Scaling : window.RenderScaling;
        if (!(scaling > 0)) scaling = 1.0;
        return screen.WorkingArea.Height / scaling;
    }

    [AvaloniaFact]
    public void APlainWindowIsWrappedOnceAndItsExplicitSizeIsScaled()
    {
        Window? window = null;
        try
        {
            SetScale(1.5);
            var content = new TextBlock();
            window = new Window { Width = 400, Height = 300, Content = content };
            window.Show();

            var root = Assert.IsType<LayoutTransformControl>(window.Content);
            Assert.Same(content, root.Child);
            Assert.Equal(Math.Min(600, WorkingWidth(window)), window.Width, 3);
            Assert.Equal(Math.Min(450, WorkingHeight(window)), window.Height, 3);
        }
        finally
        {
            window?.Close();
            SetScale(1.0);
        }
    }

    [AvaloniaFact]
    public void AnAutomaticSizeKeepsSizeToContentAndScalesTheMinimum()
    {
        Window? window = null;
        try
        {
            SetScale(1.5);
            window = new Window { SizeToContent = SizeToContent.WidthAndHeight, MinWidth = 200 };
            window.Content = new TextBlock();

            Assert.Equal(SizeToContent.WidthAndHeight, window.SizeToContent);
            Assert.Equal(300, window.MinWidth, 3);
            Assert.True(double.IsNaN(window.Width));
            window.Show();
        }
        finally
        {
            window?.Close();
            SetScale(1.0);
        }
    }

    [AvaloniaFact]
    public void TheMixerWindowIsNotWrappedTwice()
    {
        MixerWindow? window = null;
        try
        {
            SetScale(1.5);
            window = new MixerWindow(DocumentOf(DemoSongFactory.Create()));
            window.Show();

            var root = Assert.IsType<LayoutTransformControl>(window.Content);
            Assert.IsNotType<LayoutTransformControl>(root.Child);
        }
        finally
        {
            window?.Close();
            SetScale(1.0);
        }
    }

    [AvaloniaFact]
    public void AScaleChangeUsesTheInnerSizeAndAClosedWindowIsNoLongerTouched()
    {
        Window? window = null;
        var closed = false;
        try
        {
            SetScale(1.0);
            window = new Window { Width = 400, Height = 300 };
            window.Content = new TextBlock();

            Assert.Equal(400, window.Width, 3);

            SetScale(1.5);
            Assert.Equal(600, window.Width, 3);
            Assert.Equal(450, window.Height, 3);

            SetScale(1.0);
            Assert.Equal(400, window.Width, 3);
            Assert.Equal(300, window.Height, 3);

            window.Show();
            window.Close();
            closed = true;

            SetScale(1.5);
            Assert.Equal(400, window.Width, 3);
            Assert.Equal(300, window.Height, 3);
        }
        finally
        {
            if (!closed) window?.Close();
            SetScale(1.0);
        }
    }
}
