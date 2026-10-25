using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the shared UI-scale window helper: the LayoutTransformControl wrapper, following ThemeSettingsBinding.UiScaleChanged,
/// detaching on close, and the Mixer's outer-DIP minimum growing with the scale.
/// Does not own: applying the appearance (ThemeSettingsBinding) or the Mixer layout (MixerWindowTests, MixerColumnLayoutTests).
/// Tests: this class.
/// </summary>
public class WindowUiScaleTests
{
    private static SongDocument DocumentOf(SongProject project) => new(() => null) { Project = project };

    [AvaloniaFact]
    public void AttachAtDefaultScaleWrapsTheContentWithoutATransform()
    {
        var window = new Window();
        var content = new Border();
        var appearance = new AppearanceSettings();
        try
        {
            var root = Assert.IsType<LayoutTransformControl>(WindowUiScale.Attach(window, content, appearance));
            window.Content = root;
            window.Show();

            Assert.Same(content, root.Child);
            Assert.Null(root.LayoutTransform);
            Assert.Equal(1.0, WindowUiScale.CurrentScale(window), 3);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void AScaleChangeTransformsTheWrapperAndGrowsTheMixerMinimum()
    {
        var appearance = new AppearanceSettings { UiScale = 1.0 };
        var window = new MixerWindow(DocumentOf(DemoSongFactory.Create()), sizing: new AppSettings { Appearance = appearance });
        try
        {
            window.Show();
            var root = Assert.IsType<LayoutTransformControl>(window.Content);
            var before = window.MinWidth;
            Assert.Equal(MixerColumnLayout.MinCompactWidth, before, 3);

            appearance.UiScale = 1.25;
            ThemeSettingsBinding.ApplyWindow(new Window(), new LayoutTransformControl(), appearance);

            Assert.Equal(1.25, Assert.IsType<ScaleTransform>(root.LayoutTransform).ScaleX, 3);
            Assert.Equal(1.25, WindowUiScale.CurrentScale(window), 3);
            Assert.Equal(before * 1.25, window.MinWidth, 3);
        }
        finally
        {
            window.Close();
            appearance.UiScale = 1.0;
            ThemeSettingsBinding.ApplyWindow(new Window(), new LayoutTransformControl(), appearance);
        }
    }

    [AvaloniaFact]
    public void AClosedWindowStopsFollowingTheScale()
    {
        var window = new Window();
        var appearance = new AppearanceSettings();
        try
        {
            var root = Assert.IsType<LayoutTransformControl>(WindowUiScale.Attach(window, new Border(), appearance));
            window.Content = root;
            window.Show();
            window.Close();

            appearance.UiScale = 1.5;
            ThemeSettingsBinding.ApplyWindow(new Window(), new LayoutTransformControl(), appearance);

            Assert.Null(root.LayoutTransform);
        }
        finally
        {
            appearance.UiScale = 1.0;
            ThemeSettingsBinding.ApplyWindow(new Window(), new LayoutTransformControl(), appearance);
        }
    }

    [Fact]
    public void AClampedRememberedSizeNeverExceedsTheWorkingArea()
    {
        var fit = WindowUiScale.ClampToWorkingArea(new Size(3000, 2000), new Size(1280, 720));

        Assert.True(fit.Width <= 1280, $"width {fit.Width}");
        Assert.True(fit.Height <= 720, $"height {fit.Height}");
    }

    [Fact]
    public void ARememberedSizeBelowTheComputedMinimumIsRaisedToIt()
    {
        var fit = WindowUiScale.ClampRememberedSize(new Size(400, 300), new Size(560, 360), new Size(1280, 720));

        Assert.Equal(new Size(560, 360), fit);
    }

    [Fact]
    public void TheValidatorAcceptsTheSmallerMixerFloor()
    {
        var accepted = SettingsValidator.Normalize(new AppSettings { MixerWindowWidth = 400, MixerWindowHeight = 300 });
        var rejected = SettingsValidator.Normalize(new AppSettings { MixerWindowWidth = 200, MixerWindowHeight = 200 });

        Assert.Equal(400, accepted.MixerWindowWidth);
        Assert.Equal(300, accepted.MixerWindowHeight);
        Assert.Equal(320, rejected.MixerWindowWidth);
        Assert.Equal(240, rejected.MixerWindowHeight);
    }
}
