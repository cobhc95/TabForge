using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TabForge.Avalonia.Views.Docking;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using TabForge.Avalonia;
using TabForge.Avalonia.Platform;
using TabForge.Models;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the check that each Appearance setting ThemeSettingsBinding applies changes what the preview shows: theme variant, UI scale, font, density padding,
/// track row tint and muted track dimming. Each row is applied from the defaults and from the changed value; only the changed value shows the effect.
/// Does not own: the settings storage (the catalogue tests) or the arrangement drawing (TimelineViewTests).
/// Tests: this class.
/// </summary>
public class ThemeSettingsBindingTests
{
    private sealed record Row(string Setting, Action<AppearanceSettings> Change, Func<AppearanceSettings, bool> Shows);

    private static readonly Row[] Rows =
    {
        new("ThemeMode", a => a.ThemeMode = "Light", a => ShownWindow(a).RequestedThemeVariant == ThemeVariant.Light),
        new("ThemeMode", a => a.ThemeMode = "System", a => ShownWindow(a).RequestedThemeVariant == ThemeVariant.Default),
        new("UiScale", a => a.UiScale = 1.5, a => ShownRoot(a).LayoutTransform is ScaleTransform { ScaleX: 1.5 }),
        new("FontSize", a => a.FontSize = 15, a => ShownWindow(a).FontSize == 15),
        new("FontFamily", a => a.FontFamily = "Arial", a => ShownWindow(a).FontFamily.Name == "Arial"),
        new("Density", a => a.Density = "Compact", a => Math.Abs((double)ShownWindow(a).Resources["TextControlThemeMinHeight"]! - 20.4) < 1e-6),
        new("TrackTintPercent", a => a.TrackTintPercent = 0, a => TrackRowTint.Of(Track(), false, a.TrackTintPercent) is null),
        new("MutedTrackDimPercent", a => a.MutedTrackDimPercent = 0, a => TrackRowTint.MutedAlpha(a.MutedTrackDimPercent) == 255),
    };

    [AvaloniaFact]
    public void EachAppearanceSettingChangesItsEffect()
    {
        foreach (var row in Rows)
        {
            var defaults = new AppearanceSettings();
            var changed = new AppearanceSettings();
            row.Change(changed);
            Assert.True(row.Shows(changed), $"{row.Setting} changed: the preview does not show it");
            Assert.False(row.Shows(defaults), $"{row.Setting} default: the preview already shows the changed value");
        }
    }

    [AvaloniaTheory]
    [InlineData(1.0, false)]
    [InlineData(1.2, true)]
    [InlineData(150, true)]
    public void ScaleIsClampedAndTheDefaultSetsNoTransform(double uiScale, bool transformed)
    {
        var root = ShownRoot(new AppearanceSettings { UiScale = uiScale });
        Assert.Equal(transformed, root.LayoutTransform is not null);
        if (root.LayoutTransform is ScaleTransform t) Assert.InRange(t.ScaleX, 0.8, 1.5);
    }

    [AvaloniaTheory]
    [InlineData("{}")]
    [InlineData("{\"Appearance\":{\"UiScale\":1.2,\"FontSize\":12,\"Density\":\"Comfortable\"}}")]
    [InlineData("{\"Appearance\":{\"UiScale\":1.5,\"FontSize\":16,\"Density\":\"Compact\"}}")]
    public void MainWindowFitsItsWindowAtAnyScale(string settingsJson)
    {
        var folder = Path.Combine(Path.GetTempPath(), "tf-theme-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "settings.json");
        File.WriteAllText(path, settingsJson);
        try
        {
            using var settings = MainWindowSettings.Open(path);
            var main = new MainWindow(settings) { Width = 1200, Height = 800 };
            main.Show();
            Dispatcher.UIThread.RunJobs();
            foreach (var surface in main.GetVisualDescendants().OfType<DockTabSurface>())
            {
                var box = new Rect(surface.Bounds.Size).TransformToAABB(surface.TransformToVisual(main)!.Value);
                Assert.True(box.Right <= main.Width + 1, $"{surface.Selected} right edge {box.Right} beyond window {main.Width}");
                Assert.True(box.Bottom <= main.Height + 1, $"{surface.Selected} bottom {box.Bottom} beyond window {main.Height}");
            }
            main.Close();
        }
        finally { Directory.Delete(folder, true); }
    }

    private static Window ShownWindow(AppearanceSettings appearance)
    {
        var window = new Window();
        ThemeSettingsBinding.ApplyWindow(window, new LayoutTransformControl(), appearance);
        return window;
    }

    private static LayoutTransformControl ShownRoot(AppearanceSettings appearance)
    {
        var root = new LayoutTransformControl();
        ThemeSettingsBinding.ApplyWindow(new Window(), root, appearance);
        return root;
    }

    private static TrackModel Track() => new() { Name = "Lead", ColorHex = "#C04040" };
}
