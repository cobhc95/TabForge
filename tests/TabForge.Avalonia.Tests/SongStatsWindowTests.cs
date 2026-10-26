using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Views;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the Song stats window checks (structure, presenter texts, dark and light PNGs, text contrast, the preview's menu item).
/// Does not own: the window, the presenter or the palette. The expected texts come from SongStatsPresenter, the sample song from DemoSongFactory.
/// Tests: this class.
/// </summary>
public class SongStatsWindowTests
{
    private static SongProject Sample() => DemoSongFactory.Create();

    [AvaloniaFact]
    public void ControlsMatchWpfWindowRolesAndTextsInOrder()
    {
        var project = Sample();
        var window = new SongStatsWindow(project, null);
        window.Show();

        // The window's controls in tree order, written from the app's SongStatsWindow.cs: a Close button, then the heading and the counts.
        var expected = new[]
        {
            ("Button", "Close"),
            ("TextBlock", SongStatsPresenter.Heading(project, null)),
            ("TextBlock", SongStatsPresenter.CountsLine(project)),
        };
        var found = DialogTestKit.LayoutControls(window)
            .Where(c => c is Button or TextBlock)
            .Select(c => (Role: c.GetType().Name, Text: c is Button b ? b.Content as string ?? "" : ((TextBlock)c).Text ?? ""))
            .ToList();
        Assert.Equal(expected, found);
        Assert.Equal("Song stats", window.Title);
        Assert.Equal((400d, 190d, 320d, 170d), (window.Width, window.Height, window.MinWidth, window.MinHeight));
        Assert.True(window.FindControl<Button>("CloseButton")!.IsCancel);
        Assert.Equal(FontWeight.SemiBold, window.FindControl<TextBlock>("HeadingText")!.FontWeight);
        window.Close();
    }

    [AvaloniaFact]
    public void HeadingNamesTheFileWhenThereIsOne()
    {
        var project = Sample();
        var window = new SongStatsWindow(project, "C:/Songs/Demo.tforge");
        window.Show();
        Assert.Equal(SongStatsPresenter.Heading(project, "C:/Songs/Demo.tforge"), window.FindControl<TextBlock>("HeadingText")!.Text);
        Assert.Equal(SongStatsPresenter.CountsLine(project), window.FindControl<TextBlock>("CountsText")!.Text);
        window.Close();
    }

    [AvaloniaFact]
    public void CloseButtonClosesTheWindow()
    {
        var window = new SongStatsWindow(Sample(), null);
        window.Show();
        DialogTestKit.Click(window.FindControl<Button>("CloseButton")!);
        Assert.False(window.IsVisible);
    }

    [AvaloniaTheory]
    [InlineData(false, "song-stats-dark.png")]
    [InlineData(true, "song-stats-light.png")]
    public void RendersPngInEachTheme(bool light, string fileName)
    {
        using var theme = DialogTestKit.UseTheme(light);
        var window = new SongStatsWindow(Sample(), null);
        var path = DialogTestKit.SavePng(window, nameof(SongStatsWindowTests), fileName);
        Assert.True(new System.IO.FileInfo(path).Length > 0);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void TextReadsAtLeastFourAndAHalfToOneOnTheWindow(bool light)
    {
        using var theme = DialogTestKit.UseTheme(light);
        var window = new SongStatsWindow(Sample(), null);
        window.Show();
        foreach (var text in new IBrush?[] { window.FindControl<TextBlock>("HeadingText")!.Foreground, window.FindControl<TextBlock>("CountsText")!.Foreground, window.FindControl<Button>("CloseButton")!.Foreground })
            Assert.True(DialogTestKit.Contrast(text, window.Background, "Song stats") >= 4.5);
        window.Close();
    }

    [AvaloniaFact]
    public void PreviewToolsMenuOpensSongStats()
    {
        var main = new MainWindow();
        main.Show();
        var item = main.GetLogicalDescendants().OfType<MenuItem>().Single(m => m.Header as string == "Song stats…");
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        var opened = main.OwnedWindows.OfType<SongStatsWindow>().ToList();
        Assert.Single(opened);
        foreach (var window in opened) window.Close();
        main.Close();
    }
}
