using TabForge.Avalonia.Shell;
using System;
using System.IO;
using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Services;
using TabForge.Platform.Theme;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the checks of the preview's window bars (View and tempo bar, status notice row, document tab strip): structure, texts, their
/// place in the preview main window, dark and light renders and text contrast.
/// Does not own: the bars or the presenters; the expected texts are the presenters' formats.
/// Tests: this class.
/// </summary>
public class PanelBarsTests
{
    [AvaloniaFact]
    public void ViewTempoBarShowsCaptionsNamesAndTooltips()
    {
        var bar = new ViewTempoBarView();
        bar.Show("Fit width", 85, 96, "3/4", "Am");
        Assert.Equal(new[] { "Fit", "85%", "96", "3/4 Am" }, Enumerable.Range(0, 4).Select(bar.Caption));
        Assert.Equal("Playback speed", AutomationProperties.GetName(bar.Buttons[1]));
        Assert.StartsWith("Time signature and key: 3/4, A minor", ToolTip.GetTip(bar.Buttons[3]) as string);
    }

    [AvaloniaFact]
    public void StatusNoticeShowsTextTooltipAndHides()
    {
        var clicks = 0;
        var notice = new StatusNoticeBarView("Notice", "Fix", () => clicks++);
        Assert.False(notice.IsVisible);
        var window = new Window { Content = notice };
        window.Show();
        notice.Show("Plug-in blocked");
        var text = notice.GetLogicalDescendants().OfType<TextBlock>().Single(t => t.Name == "NoticeText");
        Assert.True(notice.IsVisible);
        Assert.Equal("Plug-in blocked", text.Text);
        Assert.Equal("Plug-in blocked", ToolTip.GetTip(text));
        notice.Show("Second", "Details");
        Assert.Equal("Details", ToolTip.GetTip(text));
        DialogTestKit.Click(notice.GetLogicalDescendants().OfType<Button>().Single());
        Assert.Equal(1, clicks);
        notice.Hide();
        Assert.False(notice.IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void TabStripShowsDocumentsInOrderWithLabelsAndCloseRule()
    {
        var strip = new TabStripView(new TabSettings());
        strip.Show(new[] { new TabStripDocument(@"C:\Songs\Riff.tforge", "x"), new TabStripDocument(null, "My Song"), new TabStripDocument(null, " ") }, 1, 800);
        var tabs = strip.Tabs.Cast<Border>().ToList();
        Assert.Equal(new[] { "Riff.tforge", "My Song", "Untitled" }, tabs.Select(t => t.GetLogicalDescendants().OfType<TextBlock>().Single(b => b.Name == "TabTitle").Text));
        Assert.Equal(@"C:\Songs\Riff.tforge" + "\n" + "x", ToolTip.GetTip(tabs[0]));
        Assert.Equal("My Song (unsaved)", ToolTip.GetTip(tabs[1]));
        var crosses = tabs.Select(t => t.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "TabClose").IsVisible).ToList();
        Assert.Equal(new[] { false, true, false }, crosses);
        Assert.All(tabs, t => Assert.Equal(TabForge.Presenters.TabStripPresenter.TabWidths(new TabSettings(), 800, 36, 3).Min, t.MinWidth));
    }

    [AvaloniaFact]
    public void PreviewBarsKeepNaturalSizeAndPresenterTabWidth()
    {
        var main = new MainWindow();
        main.Show();
        Dispatcher.UIThread.RunJobs();
        var dock = (DockPanel)((LayoutTransformControl)main.Content!).Child!;
        var strip = (TabStripView)((DockPanel)((TitleBarView)dock.Children[0]).Child!).Children[1];
        var expected = TabForge.Presenters.TabStripPresenter.TabWidths(new TabSettings(), main.Width - CaptionButtons.ReservedWidth, 32, 2).Min;
        Assert.All(strip.Tabs, t => Assert.Equal(expected, t.Bounds.Width, 1));
        var bar = main.GetVisualDescendants().OfType<ViewTempoBarView>().Single();
        Assert.True(bar.Buttons.Sum(b => b.Bounds.Width) < main.Width / 2);
        main.Close();
    }

    [AvaloniaFact]
    public void PreviewMainWindowPlacesTheBarsAsTheAppDoes()
    {
        var main = new MainWindow();
        main.Show();
        Dispatcher.UIThread.RunJobs();
        var dock = (DockPanel)((LayoutTransformControl)main.Content!).Child!;
        // The compact strip is a top-docked child between the menu row and the notice rows: the "Video saved" row
        // sits at the top above the workspace, the general notice row under the status bar.
        var order = dock.Children.Select(c => (c.Name ?? c.GetType().Name, DockPanel.GetDock(c))).ToList();
        Assert.Equal(("TitleBar", Dock.Top), order[0]);
        Assert.Equal(("MainMenuRow", Dock.Top), order[1]);
        Assert.Equal(("CompactTrackStrip", Dock.Top), order[2]);
        Assert.Equal(("StatusNoticeBar", Dock.Top), order[3]);
        Assert.Equal(("StatusNoticeBar", Dock.Bottom), order[4]);
        Assert.False(dock.Children[3].IsVisible);
        main.Close();
    }

    [AvaloniaTheory]
    [InlineData(false, "dark")]
    [InlineData(true, "light")]
    public void RendersPngInEachTheme(bool light, string themeName)
    {
        using var theme = DialogTestKit.UseTheme(light);
        var window = new MainWindow();
        var path = DialogTestKit.SavePng(window, nameof(PanelBarsTests), $"panel-bars-{themeName}-{Guid.NewGuid():N}.png");
        Assert.True(new FileInfo(path).Length > 0);
        window.Close();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TextReadsAtLeastFourAndAHalfToOneOnTheBars(bool light)
    {
        foreach (var (text, surface) in new[] { ("TextPrimary", "WindowBackground"), ("TextMuted", "WindowBackground"), ("TextPrimary", "PanelBackground"), ("TextMuted", "PanelBackground") })
            Assert.True(ThemeColours.Contrast(AvaloniaPalette.Get(text, light), AvaloniaPalette.Get(surface, light)) >= 4.5,
                $"{text} on {surface} (light: {light})");
    }
}
