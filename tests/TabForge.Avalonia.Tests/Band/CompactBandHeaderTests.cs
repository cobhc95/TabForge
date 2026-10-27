using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Layout;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Avalonia.Views;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the checks of the header modes that share the strip: the caption names the mode entered last, the tick rows of the compact and band menus
//   turn their modes on and off (both can be on, and each shows its own tick) and the toolbar toggles light with their mode, and the band menu's options.
// Does not own: the strip's pills and menu (CompactTrackStripTests), the Band view's own layout (BandViewTests).
// Tests: itself.
public class CompactBandHeaderTests
{
    private const string CompactRowHeader = "Compact mode";
    private const string BandRowHeader = "Band view";

    [Fact]
    public void CaptionNamesTheModeEnteredLastAndFallsBackWhenItIsLeft()
    {
        var caption = new HeaderModeCaption();
        Assert.Equal("", caption.Caption);
        caption.Enter(HeaderMode.Compact);
        Assert.Equal(CompactModeStripPresenter.Caption, caption.Caption);
        caption.Enter(HeaderMode.Band);
        Assert.Equal(HeaderModeCaption.BandCaption, caption.Caption);
        caption.Leave(HeaderMode.Band);
        Assert.Equal(CompactModeStripPresenter.Caption, caption.Caption);
        caption.Leave(HeaderMode.Compact);
        Assert.Equal("", caption.Caption);
    }

    [AvaloniaFact]
    public void TickRowsTurnEachModeOnAndOffAndShowBothWhenBothAreOn()
    {
        var window = new Window();
        var compactRow = new MenuItem { Header = "Compact mode", Tag = ModeCommandIds.CompactMode };
        var bandRow = new MenuItem { Header = "Band view", Tag = ModeCommandIds.BandView };
        var view = new MenuItem { Header = "View" };
        view.Items.Add(compactRow);
        view.Items.Add(bandRow);
        var row = new Menu();
        row.Items.Add(view);
        var commands = new MainWindowCommands(window, () => { });
        var compactOn = false;
        var bandOn = false;
        // The real mode handlers set the menu row's check and the toolbar toggle's lit state through SetRowChecks; these stand-ins do the same.
        commands.Add(ModeCommandIds.CompactMode, () => { compactOn = !compactOn; MainWindowViewMenu.SetRowChecks(window, ModeCommandIds.CompactMode, compactOn); });
        commands.Add(ModeCommandIds.BandView, () => { bandOn = !bandOn; MainWindowViewMenu.SetRowChecks(window, ModeCommandIds.BandView, bandOn); });
        var strip = MenuRowToggles.Build(commands);
        window.Content = new DockPanel { Children = { strip, row } };

        Assert.False(Ticked(window, CompactRowHeader));
        Assert.False(ToolbarToggle(strip, ModeCommandIds.CompactMode).IsChecked);
        ClickTick(window, CompactRowHeader);
        Assert.True(compactOn);
        Assert.True(Ticked(window, CompactRowHeader));
        Assert.True(ToolbarToggle(strip, ModeCommandIds.CompactMode).IsChecked);

        ClickTick(window, BandRowHeader);
        Assert.True(bandOn);
        Assert.True(Ticked(window, CompactRowHeader));
        Assert.True(Ticked(window, BandRowHeader));
        Assert.True(ToolbarToggle(strip, ModeCommandIds.CompactMode).IsChecked);
        Assert.True(ToolbarToggle(strip, ModeCommandIds.BandView).IsChecked);

        ClickTick(window, CompactRowHeader);
        Assert.False(compactOn);
        Assert.False(Ticked(window, CompactRowHeader));
        Assert.False(ToolbarToggle(strip, ModeCommandIds.CompactMode).IsChecked);
        Assert.True(ToolbarToggle(strip, ModeCommandIds.BandView).IsChecked);
    }

    [AvaloniaFact]
    public void ToolbarTogglesLightWithTheModeTheyRun()
    {
        var window = new Window();
        var commands = new MainWindowCommands(window, () => { });
        var bandOn = false;
        commands.Add(ModeCommandIds.BandView, () => { bandOn = !bandOn; MainWindowViewMenu.SetRowChecks(window, ModeCommandIds.BandView, bandOn); });
        var strip = MenuRowToggles.Build(commands);
        window.Content = new DockPanel { Children = { strip } };
        var toggle = ToolbarToggle(strip, ModeCommandIds.BandView);

        Assert.False(toggle.IsChecked);
        Click(toggle);                                  // the toolbar button itself, as a user clicks it
        Assert.True(bandOn);
        Assert.True(toggle.IsChecked);
        Click(toggle);
        Assert.False(bandOn);
        Assert.False(toggle.IsChecked);
    }

    [AvaloniaFact]
    public void BandMenuListsItsOptionsThenTheTwoModeTicks()
    {
        var window = new Window();
        var commands = new MainWindowCommands(window, () => { });
        var bandToggled = false;
        commands.Add(ModeCommandIds.BandView, () => bandToggled = true);
        window.Content = new DockPanel { Children = { MenuRowToggles.Build(commands), new Menu() } };
        var picked = new List<string>();
        var menu = BandViewMenu.Build(new BandLayoutChoices(FretboardAbove: true, ColourFrame: false), picked.Add, window, new HotkeySettings());

        var items = menu.Items.OfType<MenuItem>().ToList();
        Assert.Equal(new[] { "Fretboard beside tab", "Fretboard above tab", "Track colour frame", "Reset view", BandRowHeader, CompactRowHeader }, items.Select(item => item.Header as string));
        Assert.False(items[0].IsChecked);
        Assert.True(items[1].IsChecked);
        Assert.False(items[2].IsChecked);
        Assert.DoesNotContain(items, item => item.Items.Count > 0);

        items[2].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Equal(new[] { BandViewMenu.ColourFrameOption }, picked);
        Click(TickOf(menu, BandRowHeader));
        Assert.True(bandToggled);
    }

    /// <summary>Whether the tick row of a mode is ticked in a freshly built compact menu; the menu is handed its lent items back.</summary>
    private static bool Ticked(Window window, string header)
    {
        var compact = new CompactModeMenu(window);
        var checkedNow = TickOf(compact.Build(), header).IsChecked;
        compact.Restore();
        return checkedNow;
    }

    /// <summary>Clicks the tick row of a mode in a freshly built compact menu, as a user picks it.</summary>
    private static void ClickTick(Window window, string header) => Click(TickOf(new CompactModeMenu(window).Build(), header));

    /// <summary>The icon-row tick of a mode in a menu (the rows that carry an icon; the lent main-menu items carry none).</summary>
    private static MenuItem TickOf(ContextMenu menu, string header) =>
        menu.Items.OfType<MenuItem>().Single(item => item.Icon is not null && Equals(item.Header, header));

    /// <summary>The toolbar toggle that runs a command: its lit state (IsChecked) is what MenuRowToggles shows for the mode behind it.</summary>
    private static ToggleButton ToolbarToggle(StackPanel strip, string commandId) =>
        strip.Children.OfType<ToggleButton>().Single(toggle => Equals(toggle.Tag, commandId));

    private static void Click(MenuItem item) => item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
}
