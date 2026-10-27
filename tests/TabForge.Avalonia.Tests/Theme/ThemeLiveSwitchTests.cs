using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using TabForge.Avalonia;
using TabForge.Avalonia.Platform;
using TabForge.Platform.Theme;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the live theme switch: a published appearance changes the brush resources and the lookup tables without a restart,
//   and Dark -> Light -> System -> Custom never leaves an older brush behind.
// Does not own: the preset values (ThemePresetTests) or the window variant rule (ThemeSettingsBindingTests).
// Tests: this file.
public class ThemeLiveSwitchTests
{
    [AvaloniaFact]
    public void CustomAccentPublishesToTheBrushResourcesWithoutRestart()
    {
        var app = Application.Current!;
        var window = new Window();
        var appearance = new AppearanceSettings { ThemeMode = "Custom", Accent = "#FF0080" };
        try
        {
            ThemeSettingsBinding.ApplyWindow(window, new LayoutTransformControl(), appearance);
            AssertBrush(app, ThemeVariant.Dark, "AccentBrush", "#FF0080");

            appearance.Accent = "#00FF00";
            ThemeSettingsBinding.ApplyWindow(window, new LayoutTransformControl(), appearance);
            AssertBrush(app, ThemeVariant.Dark, "AccentBrush", "#00FF00");
        }
        finally
        {
            AvaloniaPalette.Publish(new AppearanceSettings());
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ThemeModesLeaveNoStaleBrush()
    {
        var app = Application.Current!;
        var appearance = new AppearanceSettings();
        try
        {
            AvaloniaPalette.Publish(appearance);
            Assert.Equal(Palette.Parse(appearance.Background), AvaloniaPalette.Get("WindowBackground", light: false));

            ThemeColours.ApplyPreset(appearance, light: true);
            appearance.ThemeMode = "Light";
            AvaloniaPalette.Publish(appearance);
            Assert.Equal(Palette.Parse(appearance.Background), AvaloniaPalette.Get("WindowBackground", light: true));
            AssertBrush(app, ThemeVariant.Light, "WindowBackground", appearance.Background);

            // System follows the OS presets and writes nothing, so a custom field must not leak into the published table.
            appearance.ThemeMode = "System";
            appearance.Background = "#123456";
            AvaloniaPalette.Publish(appearance);
            Assert.Equal(Palette.Parse(new AppearanceSettings().Background), AvaloniaPalette.ThemeBrush("WindowBrush", light: false));
            var lightDefault = new AppearanceSettings();
            ThemeColours.ApplyPreset(lightDefault, light: true);
            Assert.Equal(Palette.Parse(lightDefault.Background), AvaloniaPalette.ThemeBrush("WindowBrush", light: true));

            // Custom keeps the live fields.
            appearance.Accent = "#FFA040";
            appearance.ThemeMode = "Custom";
            AvaloniaPalette.Publish(appearance);
            Assert.Equal(Palette.Parse("#123456"), AvaloniaPalette.ThemeBrush("WindowBrush", light: false));
            AssertBrush(app, ThemeVariant.Dark, "WindowBrush", "#123456");
            AssertBrush(app, ThemeVariant.Dark, "AccentBrush", "#FFA040");
        }
        finally { AvaloniaPalette.Publish(new AppearanceSettings()); }
    }

    private static void AssertBrush(Application app, ThemeVariant variant, string name, string expected)
    {
        Assert.True(app.TryGetResource(name, variant, out var value), $"{name} is not a {variant} resource.");
        Assert.Equal(Color.Parse(expected), Assert.IsType<SolidColorBrush>(value).Color);
    }
}
