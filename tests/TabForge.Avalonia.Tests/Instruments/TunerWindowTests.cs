using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Models;
using TabForge.Platform.Theme;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the Tuner window checks (structure, the sample reading's note, cents and lit string, a reading that is on no string, the preview
/// menu item, dark and light renders, text contrast).
/// Does not own: the window, the presenter or the palette; the expected texts are the presenter's formats for the sample pitch.
/// Tests: this class.
/// </summary>
public class TunerWindowTests
{
    [AvaloniaFact]
    public void ShowsTheSampleNoteDetailNeedleAndTheLitString()
    {
        var project = DemoSongFactory.Create();
        var window = new TunerWindow(project.Tracks[0], TunerWindow.SampleHz);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("A2", DialogTestKit.Named<TextBlock>(window, "NoteText").Text);
        Assert.Equal("110.0 Hz   0 cents", DialogTestKit.Named<TextBlock>(window, "DetailText").Text);
        Assert.Equal(0, DialogTestKit.Named<TunerNeedle>(window, "Needle").Cents);
        var boxes = DialogTestKit.Named<StackPanel>(window, "Strings").Children.OfType<Border>().ToList();
        Assert.Equal(6, boxes.Count);
        var lit = LitBoxes(boxes);
        Assert.Single(lit);
        Assert.Equal("A2", ((TextBlock)lit[0].Child!).Text);
        window.Close();
    }

    [AvaloniaFact]
    public void AReadingOffEveryStringShowsItsNoteAndLightsNone()
    {
        var project = DemoSongFactory.Create();
        var window = new TunerWindow(project.Tracks[0], 442.5);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("A4", DialogTestKit.Named<TextBlock>(window, "NoteText").Text);
        Assert.Equal("442.5 Hz   +10 cents", DialogTestKit.Named<TextBlock>(window, "DetailText").Text);
        var boxes = DialogTestKit.Named<StackPanel>(window, "Strings").Children.OfType<Border>().ToList();
        Assert.Empty(LitBoxes(boxes));
        window.Close();
    }

    /// <summary>The string boxes outlined in the lit colour (SeaGreen; the same in both themes).</summary>
    private static List<Border> LitBoxes(IEnumerable<Border> boxes)
    {
        var lit = AvaloniaPalette.ColorOf(AvaloniaPalette.Get("SeaGreen", light: false));
        return boxes.Where(box => box.BorderBrush is SolidColorBrush brush && brush.Color == lit).ToList();
    }

    [AvaloniaFact]
    public void ToolsMenuOpensTheTuner()
    {
        var main = new MainWindow();
        main.Show();
        var item = main.GetLogicalDescendants().OfType<MenuItem>().Single(m => m.Header as string == "Tuner…");
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        var opened = main.OwnedWindows.OfType<TunerWindow>().ToList();
        Assert.Single(opened);
        foreach (var window in opened) window.Close();
        main.Close();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThemeSwitchRecoloursTheIdleOutlinesAndKeepsTheLitOne(bool light)
    {
        using var theme = DialogTestKit.UseTheme(light);
        var window = new TunerWindow(DemoSongFactory.Create().Tracks[0], TunerWindow.SampleHz);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var boxes = DialogTestKit.Named<StackPanel>(window, "Strings").Children.OfType<Border>().ToList();
        var lit = Assert.Single(LitBoxes(boxes));
        var idle = boxes.First(box => box != lit);
        Assert.Equal(ThemeColour("SeaGreen", light), BrushColour(lit));
        Assert.Equal(ThemeColour("ChromeBorder", light), BrushColour(idle));

        // The window reads the brushes again on a live theme switch: the lit outline stays green, the idle ones take the new chrome border.
        Application.Current!.RequestedThemeVariant = light ? ThemeVariant.Dark : ThemeVariant.Light;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(ThemeColour("SeaGreen", !light), BrushColour(lit));
        Assert.Equal(ThemeColour("ChromeBorder", !light), BrushColour(idle));
        window.Close();
    }

    private static Color ThemeColour(string key, bool light) => AvaloniaPalette.ColorOf(AvaloniaPalette.Get(key, light));

    private static Color BrushColour(Border box) => Assert.IsType<SolidColorBrush>(box.BorderBrush).Color;

    [AvaloniaTheory]
    [InlineData(false, "dark")]
    [InlineData(true, "light")]
    public void RendersPngInEachTheme(bool light, string themeName)
    {
        using var theme = DialogTestKit.UseTheme(light);
        var window = new TunerWindow(DemoSongFactory.Create().Tracks[0], TunerWindow.SampleHz);
        var path = DialogTestKit.SavePng(window, nameof(TunerWindowTests), $"tuner-{themeName}-{Guid.NewGuid():N}.png");
        Assert.True(new FileInfo(path).Length > 0);
        window.Close();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TextReadsAtLeastFourAndAHalfToOneOnTheWindow(bool light)
    {
        foreach (var (text, surface) in new[] { ("TextPrimary", "WindowBackground"), ("TextMuted", "WindowBackground"), ("TextPrimary", "PanelBackground") })
            Assert.True(ThemeColours.Contrast(AvaloniaPalette.Get(text, light), AvaloniaPalette.Get(surface, light)) >= 4.5,
                $"{text} on {surface} (light: {light})");
    }
}
