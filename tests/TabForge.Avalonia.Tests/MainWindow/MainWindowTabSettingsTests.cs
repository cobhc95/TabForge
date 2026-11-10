using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the tab settings the window draws its strip with (the tab style and width mode, refreshed when the settings change) and the
/// last-tab rule (a new empty tab, or the window closes).
/// Tests: MainWindow, MainWindowTabs, TabStripView, MainWindowSettings.
/// </summary>
public sealed class MainWindowTabSettingsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "tf-avalonia-tests", "tabs-" + Guid.NewGuid().ToString("N"));

    public MainWindowTabSettingsTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static Task<PromptButton> NoAnswer(SavePrompt _) => Task.FromResult(PromptButton.Cancel);

    private static Task<bool> NoSave(SongDocument _) => Task.FromResult(false);

    private static void AssertTabsSquareAndShrunk(TabStripView strip, TabSettings tabs)
    {
        var drawn = strip.Tabs.Cast<Border>().ToList();
        Assert.NotEmpty(drawn);
        Assert.All(drawn, t => Assert.Equal(new CornerRadius(0), t.CornerRadius));
        Assert.All(drawn, t => Assert.Equal(0, t.MinWidth));
        Assert.All(drawn, t => Assert.Equal(tabs.MaxTabWidth, t.MaxWidth));
        Assert.All(drawn, t => Assert.Equal(tabs.TabHeight, t.Height));
    }

    [AvaloniaFact]
    public void WindowDrawsItsStripWithTheSavedTabStyleAndWidthMode()
    {
        var settings = MainWindowSettings.Open(Path.Combine(_folder, "settings.json"));
        settings.Settings.Tabs.Style = TabStyles.Square;
        settings.Settings.Tabs.WidthMode = TabWidthModes.Content;
        var main = new MainWindow(settings);

        var strip = main.GetLogicalDescendants().OfType<TabStripView>().Single();
        AssertTabsSquareAndShrunk(strip, settings.Settings.Tabs);
        main.Close();
        settings.Dispose();
    }

    [AvaloniaFact]
    public void StripRedrawsWhenTheSettingsChange()
    {
        var settings = MainWindowSettings.Open(Path.Combine(_folder, "settings.json"));
        var main = new MainWindow(settings);
        var strip = main.GetLogicalDescendants().OfType<TabStripView>().Single();
        Assert.Equal(new CornerRadius(0, 0, 8, 8), strip.Tabs.Cast<Border>().Single().CornerRadius);   // rounded: the bottom corners only, the top stays square at the window edge

        settings.Settings.Tabs.Style = TabStyles.Square;
        settings.Settings.Tabs.WidthMode = TabWidthModes.Content;
        settings.SaveLayout(settings.StartLayout);   // the store raises its change event, the window redraws

        AssertTabsSquareAndShrunk(strip, settings.Settings.Tabs);
        main.Close();
        settings.Dispose();
    }

    [AvaloniaFact]
    public void NewTabButtonRoundsWithTheSavedTabStyle()
    {
        var settings = MainWindowSettings.Open(Path.Combine(_folder, "settings.json"));
        var main = new MainWindow(settings);
        var strip = main.GetLogicalDescendants().OfType<TabStripView>().Single();
        var add = strip.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "NewTab");
        Assert.Equal(new CornerRadius(8), add.CornerRadius);   // rounded with the tabs by default

        settings.Settings.Tabs.Style = TabStyles.Square;
        settings.SaveLayout(settings.StartLayout);   // the store raises its change event, the window redraws

        Assert.Equal(new CornerRadius(0), add.CornerRadius);   // square with the tabs
        main.Close();
        settings.Dispose();
    }

        [Fact]
    public void NextAndPreviousTabWrapAtBothEnds()
    {
        var tabs = new MainWindowTabs(MainWindowTabs.NewSong(), NoAnswer, NoSave, settings: new AppSettings());
        tabs.Open(MainWindowTabs.NewSong());
        tabs.Open(MainWindowTabs.NewSong());   // three tabs, the last one shown
        Assert.Equal(2, tabs.ActiveIndex);

        tabs.SwitchNext();
        Assert.Equal(0, tabs.ActiveIndex);   // wraps from the last to the first
        tabs.SwitchNext();
        Assert.Equal(1, tabs.ActiveIndex);
        tabs.SwitchPrevious();
        Assert.Equal(0, tabs.ActiveIndex);
        tabs.SwitchPrevious();
        Assert.Equal(2, tabs.ActiveIndex);   // wraps from the first to the last
    }

    [Fact]
    public void TabSwitchingWithOneTabStaysPut()
    {
        var tabs = new MainWindowTabs(MainWindowTabs.NewSong(), NoAnswer, NoSave, settings: new AppSettings());
        tabs.SwitchNext();
        tabs.SwitchPrevious();
        Assert.Equal(0, tabs.ActiveIndex);
    }

    [Fact]
    public void NextAndPreviousTabDefaultToCtrlTab()
    {
        Assert.Equal("Ctrl+Tab", HotkeyCatalog.ById("Tab.Next")!.DefaultGesture);
        Assert.Equal("Ctrl+Shift+Tab", HotkeyCatalog.ById("Tab.Previous")!.DefaultGesture);
        var map = HotkeyCatalog.BuildMap(new HotkeySettings());
        Assert.Equal("Tab.Next", map["Ctrl+Tab"]);
        Assert.Equal("Tab.Previous", map["Ctrl+Shift+Tab"]);
    }
[Fact]
    public async Task ClosingTheLastTabOpensAFreshTabByDefault()
    {
        var only = MainWindowTabs.NewSong();
        var tabs = new MainWindowTabs(only, NoAnswer, NoSave);

        Assert.True(await tabs.CloseActive());
        Assert.Single(tabs.Songs);
        Assert.NotSame(only, tabs.Active);
    }

    [Fact]
    public async Task ClosingTheLastTabClosesTheWindowWhenTheSettingSaysSo()
    {
        var settings = new AppSettings();
        settings.Tabs.LastTabClosed = LastTabActions.CloseWindow;
        var only = MainWindowTabs.NewSong();
        var tabs = new MainWindowTabs(only, NoAnswer, NoSave, settings: settings);
        var closeRequests = 0;
        tabs.CloseWindowRequested += () => closeRequests++;

        Assert.True(await tabs.CloseActive());
        Assert.Equal(1, closeRequests);
        Assert.Empty(tabs.Songs);
    }

    [Fact]
    public async Task ClosingAnotherTabNeverClosesTheWindow()
    {
        var settings = new AppSettings();
        settings.Tabs.LastTabClosed = LastTabActions.CloseWindow;
        var first = MainWindowTabs.NewSong();
        var tabs = new MainWindowTabs(first, NoAnswer, NoSave, settings: settings);
        tabs.Open(MainWindowTabs.NewSong());
        var closeRequests = 0;
        tabs.CloseWindowRequested += () => closeRequests++;

        Assert.True(await tabs.CloseActive());
        Assert.Equal(0, closeRequests);
        Assert.Single(tabs.Songs);
    }

    // Releasing a song can run other window code (a window activation arrives during the wait for its output, or when its FX windows close), and that code reads Active.
    [Fact]
    public async Task TheShownSongStaysReadableWhileTheRightmostTabIsReleased()
    {
        var first = MainWindowTabs.NewSong();
        var tabs = new MainWindowTabs(first, NoAnswer, NoSave, settings: new AppSettings());
        tabs.Open(MainWindowTabs.NewSong());
        SongDocument? shownDuringRelease = null;
        tabs.SongClosing += _ => shownDuringRelease = tabs.Active;

        Assert.True(await tabs.CloseActive());
        Assert.Same(first, shownDuringRelease);
        Assert.Same(first, tabs.Active);
    }

    [Theory]
    [InlineData(LastTabActions.NewTab)]
    [InlineData(LastTabActions.CloseWindow)]
    public async Task TheShownSongStaysReadableWhileTheOnlyTabIsReleased(string lastTabAction)
    {
        var settings = new AppSettings();
        settings.Tabs.LastTabClosed = lastTabAction;
        var tabs = new MainWindowTabs(MainWindowTabs.NewSong(), NoAnswer, NoSave, settings: settings);
        SongDocument? shownDuringRelease = null;
        tabs.SongClosing += _ => shownDuringRelease = tabs.Active;

        Assert.True(await tabs.CloseActive());
        Assert.NotNull(shownDuringRelease);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ClosingABackgroundTabLeavesTheShownSongAlone(int closed)
    {
        var first = MainWindowTabs.NewSong();
        var tabs = new MainWindowTabs(first, NoAnswer, NoSave, settings: new AppSettings());
        var shown = MainWindowTabs.NewSong();
        tabs.Open(shown);
        tabs.Open(MainWindowTabs.NewSong());
        tabs.Switch(1);
        var changes = 0;
        tabs.ActiveChanged += () => changes++;

        await tabs.CloseAt(closed);
        Assert.Same(shown, tabs.Active);
        Assert.Equal(2, tabs.Songs.Count);
        Assert.Equal(0, changes);
    }

    [Fact]
    public async Task ClosingSeveralTabsKeepsTheShownSongReadableBetweenSaveQuestions()
    {
        var first = MainWindowTabs.NewSong();
        var tabs = new MainWindowTabs(first, NoAnswer, NoSave, settings: new AppSettings());
        var second = MainWindowTabs.NewSong();
        tabs.Open(second);
        var third = MainWindowTabs.NewSong();
        tabs.Open(third);
        SongDocument? shownDuringRelease = null;
        tabs.SongClosing += _ => shownDuringRelease = tabs.Active;

        await tabs.CloseMany(new[] { third, second });
        Assert.Same(first, shownDuringRelease);
        Assert.Same(first, tabs.Active);
    }
}
