using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Models;
using TabForge.Platform.Theme;
using TabForge.Presenters;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the Scale finder window checks (structure, presenter output, the Highlight it returns, the preview menu item, dark and light renders
/// and text contrast).
/// Does not own: the window, the presenter or the palette; the expected values come from ScaleFinderPresenter and AvaloniaPalette.
/// Tests: this class.
/// </summary>
public class ScaleFinderWindowTests
{
    private static readonly ScaleFinderPresenter.SearchRange Passage = new(0, 0, 1, 0, "bars 1-2");

    [AvaloniaFact]
    public void ControlsMatchWpfWindowSizeNamesAndTexts()
    {
        var project = DemoSongFactory.Create();
        var window = new ScaleFinderWindow(project, project.Tracks[0], Passage, null);
        window.Show();

        Assert.Equal("Scale finder", window.Title);
        Assert.Equal((560d, 600d, 460d, 420d), (window.Width, window.Height, window.MinWidth, window.MinHeight));
        var selection = DialogTestKit.Named<RadioButton>(window, "InSelection");
        Assert.Equal(ScaleFinderPresenter.SelectionLabel("bars 1-2"), selection.Content as string);
        Assert.True(selection.IsEnabled);
        Assert.Equal("Entire song", DialogTestKit.Named<RadioButton>(window, "InSong").Content as string);
        Assert.Equal(ScaleFinderPresenter.AllTracksLabel(project.Tracks[0].Name), DialogTestKit.Named<CheckBox>(window, "AllTracks").Content as string);
        Assert.Equal(new[] { "Likely scales", "All scales" }, DialogTestKit.Named<TabControl>(window, "Tabs").Items.OfType<TabItem>().Select(t => t.Header as string));
        Assert.Equal(new[] { "Clear highlight", "Show on fretboard", "Close" },
            new[] { "ClearButton", "ShowButton", "CloseButton" }.Select(n => DialogTestKit.Named<Button>(window, n).Content as string));
        window.Close();
    }

    [AvaloniaFact]
    public void NoSelectionDisablesTheSelectionScopeAndLikelyRowsFollowThePresenter()
    {
        var project = DemoSongFactory.Create();
        var window = new ScaleFinderWindow(project, project.Tracks[0], null, null);
        window.Show();

        var selection = DialogTestKit.Named<RadioButton>(window, "InSelection");
        Assert.False(selection.IsEnabled);
        Assert.Equal(ScaleFinderPresenter.SelectionLabel(null), selection.Content as string);
        Assert.True(DialogTestKit.Named<RadioButton>(window, "InSong").IsChecked);

        var expected = ScaleFinderPresenter.Search(project, project.Tracks[0], DialogTestKit.Named<CheckBox>(window, "AllTracks").IsChecked == true, false, null);
        Assert.Equal(expected.Status, DialogTestKit.Named<TextBlock>(window, "StatusText").Text);
        var rows = DialogTestKit.Named<ListBox>(window, "LikelyList").Items.OfType<ListBoxItem>().Select(i => i.Tag as string).ToList();
        Assert.Equal(expected.Rows.Select(r => r.Highlight), rows);
        window.Close();
    }

    [AvaloniaFact]
    public void ShowOnFretboardReturnsTheLikelyRowWhileThatTabIsOpen()
    {
        var project = DemoSongFactory.Create();
        var window = new ScaleFinderWindow(project, project.Tracks[0], null, null);
        window.Show();
        var expected = ScaleFinderPresenter.Search(project, project.Tracks[0], DialogTestKit.Named<CheckBox>(window, "AllTracks").IsChecked == true, false, null);

        DialogTestKit.Click(DialogTestKit.Named<Button>(window, "ShowButton"));
        Assert.Equal(expected.Rows[0].Highlight, window.Highlight);
    }

    [AvaloniaFact]
    public void ShowOnFretboardReturnsRootAndScaleOfTheAllScalesTab()
    {
        var project = DemoSongFactory.Create();
        var scale = ScaleFinderPresenter.ScaleChoices[1];
        var window = new ScaleFinderWindow(project, project.Tracks[0], null, $"D {scale}");
        window.Show();
        DialogTestKit.Named<TabControl>(window, "Tabs").SelectedIndex = 1;

        DialogTestKit.Click(DialogTestKit.Named<Button>(window, "ShowButton"));
        Assert.Equal($"D {scale}", window.Highlight);
    }

    [AvaloniaFact]
    public void ClearHighlightReturnsOff()
    {
        var project = DemoSongFactory.Create();
        var window = new ScaleFinderWindow(project, project.Tracks[0], null, null);
        window.Show();

        DialogTestKit.Click(DialogTestKit.Named<Button>(window, "ClearButton"));
        Assert.Equal(ScaleFinderPresenter.OffHighlight, window.Highlight);
    }

    [AvaloniaFact]
    public void PreviewToolsMenuOpensScaleFinder()
    {
        var main = new MainWindow();
        main.Show();
        var item = main.GetLogicalDescendants().OfType<MenuItem>().Single(m => m.Header as string == "Scale finder…");
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        var opened = main.OwnedWindows.OfType<ScaleFinderWindow>().ToList();
        Assert.Single(opened);
        foreach (var window in opened) window.Close();
        main.Close();
    }

    [AvaloniaTheory]
    [InlineData(false, "dark")]
    [InlineData(true, "light")]
    public void RendersPngInEachTheme(bool light, string themeName)
    {
        using var theme = DialogTestKit.UseTheme(light);
        var project = DemoSongFactory.Create();
        var window = new ScaleFinderWindow(project, project.Tracks[0], Passage, "C Major");
        var path = DialogTestKit.SavePng(window, nameof(ScaleFinderWindowTests), $"scale-finder-{themeName}-{Guid.NewGuid():N}.png");
        Assert.True(new FileInfo(path).Length > 0);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void TextAndBackgroundColoursResolveToCorePaletteValues(bool light)
    {
        using var theme = DialogTestKit.UseTheme(light);
        var project = DemoSongFactory.Create();
        var window = new ScaleFinderWindow(project, project.Tracks[0], null, null);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(AvaloniaPalette.ColorOf(AvaloniaPalette.Get("WindowBackground", light)), Assert.IsType<SolidColorBrush>(window.Background).Color);
        Assert.Equal(AvaloniaPalette.ColorOf(AvaloniaPalette.Get("TextPrimary", light)),
            Assert.IsType<SolidColorBrush>(DialogTestKit.Named<TextBlock>(window, "StatusText").Foreground).Color);
        // The default button is the accent fill, so its label is the ink that reads on the accent (not the dialog text colour).
        Assert.Equal(AvaloniaPalette.ColorOf(TabForge.Platform.Theme.ThemeColours.InkOn(AvaloniaPalette.Get("Accent", light))),
            Assert.IsType<SolidColorBrush>(DialogTestKit.Named<Button>(window, "ShowButton").Foreground).Color);
        window.Close();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TextReadsAtLeastFourAndAHalfToOneOnTheWindowAndItsLists(bool light)
    {
        foreach (var surface in new[] { "WindowBackground", "PanelBackground" })
            foreach (var text in new[] { "TextPrimary", "TextMuted" })
                Assert.True(ThemeColours.Contrast(AvaloniaPalette.Get(text, light), AvaloniaPalette.Get(surface, light)) >= 4.5,
                    $"{text} on {surface} (light: {light})");
    }
}
