using System;
using Avalonia.Controls;
using TabForge.Services;

namespace TabForge.Avalonia.Platform.Controls;

/// <summary>The Band view's layout choices as the layout menu shows them.</summary>
/// <param name="FretboardAbove">The fretboard sits above the tab (false: beside it).</param>
/// <param name="ColourFrame">Each row has its track colour frame.</param>
public sealed record BandLayoutChoices(bool FretboardAbove, bool ColourFrame);

// Owns: the Band view's layout menu, the roll-down after the "BAND VIEW" caption, in the menu style of the compact menu: the fretboard placement
// (beside or above the tab, one choice), the track colour frame and Reset view; then the Band view and
//   Compact mode tick rows (ToolbarMenuRows), so either mode can be turned off from it. It has no File to Help submenus.
// Does not own: the band settings and what an option changes (the band host passes the current choices and runs the chosen option), the mode
//   commands behind the tick rows (MainWindowCommands), the caption and when the menu opens.
// Tests: TabForge.Avalonia.Tests.CompactBandHeaderTests.
public static class BandViewMenu
{
    /// <summary>The option ids the menu passes to the band host.</summary>
    public const string FretboardBesideOption = "beside", FretboardAboveOption = "above", ColourFrameOption = "frame", ResetOption = "reset";

    /// <summary>The hotkey command ids of the fretboard placement and the colour frame, whose live keys the rows show (the band layout menu).</summary>
    public const string ToggleFretboardAboveCommandId = "Band.ToggleFretboardAbove", ToggleColourFrameCommandId = "Band.ToggleColourFrame";

    private const string ResetTooltip = "Back to the Band view's defaults in every tab: fretboard above the tab, the first row's fretboard only, the colour frame, and rows as tall as their lines of music.";

    /// <summary>The menu for the current choices; <paramref name="runOption"/> gets the id of the option the user picked.</summary>
    /// <param name="window">The window whose toolbar supplies the tick rows.</param>
    public static ContextMenu Build(BandLayoutChoices choices, Action<string> runOption, Window window, HotkeySettings hotkeys)
    {
        var menu = MenuStability.CreateContextMenu();
        menu.Items.Add(Option("Fretboard beside tab", MenuItemToggleType.Radio, !choices.FretboardAbove, FretboardBesideOption, runOption, ToggleFretboardAboveCommandId, hotkeys));
        menu.Items.Add(Option("Fretboard above tab", MenuItemToggleType.Radio, choices.FretboardAbove, FretboardAboveOption, runOption, ToggleFretboardAboveCommandId, hotkeys));
        ToolbarMenuRows.AddSeparator(menu);
        menu.Items.Add(Option("Track colour frame", MenuItemToggleType.CheckBox, choices.ColourFrame, ColourFrameOption, runOption, ToggleColourFrameCommandId, hotkeys));
        ToolbarMenuRows.AddSeparator(menu);
        var reset = Option("Reset view", MenuItemToggleType.None, false, ResetOption, runOption, null, hotkeys);
        ToolTip.SetTip(reset, ResetTooltip);
        menu.Items.Add(reset);
        ToolbarMenuRows.Append(menu, window, id => id is ModeCommandIds.BandView or ModeCommandIds.CompactMode, hotkeys, () => { });
        return menu;
    }

    private static MenuItem Option(string header, MenuItemToggleType toggle, bool isChecked, string option, Action<string> runOption, string? commandId, HotkeySettings hotkeys)
    {
        var item = new MenuItem { Header = header, ToggleType = toggle, IsChecked = isChecked };
        if (commandId is not null) ToolbarMenuRows.SetShortcut(item, commandId, hotkeys);
        item.Click += (_, _) => runOption(option);
        return item;
    }
}
