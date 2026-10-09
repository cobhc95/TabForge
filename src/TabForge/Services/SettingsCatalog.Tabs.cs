using TabForge.Documents;
using TabForge.Models;

namespace TabForge.Services;

// Owns: the Tabs part of the settings catalogue (rows in Preferences order).
// Does not own: page layout (SettingsCatalog.Layout) or the stored values (AppSettings).
// Tests: TestPreferencesCatalog, TestSettingsFileSplitSnapshots.
public static partial class SettingsCatalog
{
    private static List<SettingDescriptor> TabsRows(AppSettings s)
    {
        var tabs = s.Tabs!;
        var g = s.General!;
        var a = s.Appearance!;
        return new List<SettingDescriptor>
        {
            // Tabs & windows
            Bool(Tabs, "Window", "general.restorewindow", "Remember the window size", v => g.RestoreWindow = v, () => g.RestoreWindow,
                "Restore window geometry and maximised state between runs.", "window restore geometry size"),
            Bool(Tabs, "Tabs", "appearance.tabstrip", "Show the tab strip in the title bar", v => a.ShowTabStrip = v, () => a.ShowTabStrip,
                "Show document tabs in the title bar.", "document title bar hide"),
            Choice(Tabs, "Tabs", "tabs.style", "Tab shape", v => tabs.Style = v, () => tabs.Style,
                new[] { TabStyles.Rounded, TabStyles.Square }, "Rounded browser-like tabs or flat rectangles.", "tab shape corner"),
            Choice(Tabs, "Tabs", "tabs.width", "Tab width", v => tabs.WidthMode = v, () => tabs.WidthMode,
                new[] { TabWidthModes.Fit, TabWidthModes.Content }, "Share the strip width or size tabs to their titles.", "tab width fit content"),
            Number(Tabs, "Tabs", "tabs.maxwidth", "Maximum tab width", v => tabs.MaxTabWidth = v, () => tabs.MaxTabWidth, 80, 600,
                "Upper bound on a tab's width.", "tab width maximum size", "px"),
            Number(Tabs, "Tabs", "tabs.minwidth", "Minimum tab width", v => tabs.MinTabWidth = v, () => tabs.MinTabWidth, 46, 400,
                "Tabs never shrink below this; extra tabs scroll with the strip's arrows instead.", "tab width minimum size shrink", "px"),
            Number(Tabs, "Tabs", "tabs.height", "Tab height", v => tabs.TabHeight = v, () => tabs.TabHeight, 26, 44,
                "Height of each tab in the title bar.", "tab height size tall", "px"),
            Number(Tabs, "Tabs", "tabs.fontsize", "Tab title size", v => tabs.TabFontSize = v, () => tabs.TabFontSize, 10, 16,
                "Font size of tab titles.", "tab font text size title", "pt"),
            Choice(Tabs, "Tabs", "tabs.closebutton", "Close button", v => tabs.CloseButton = v, () => tabs.CloseButton,
                new[] { CloseButtonModes.Always, CloseButtonModes.ActiveAndHover, CloseButtonModes.ActiveOnly, CloseButtonModes.Never },
                "When each tab's close button is visible.", "tab close cross"),
            Bool(Tabs, "Tabs", "tabs.doubleclick", "Double-click closes a tab", v => tabs.CloseOnDoubleClick = v, () => tabs.CloseOnDoubleClick,
                "Close a tab by double-clicking it.", "double click close"),
            Choice(Tabs, "Tabs", "tabs.middleclick", "Middle-click a tab", v => tabs.MiddleClick = v, () => tabs.MiddleClick,
                new[] { MiddleClickActions.Close, MiddleClickActions.Duplicate, MiddleClickActions.NewTab, MiddleClickActions.Nothing },
                "Action performed when a tab is middle-clicked.", "middle click mouse"),
            Bool(Tabs, "Tabs", "tabs.middlebar", "Middle-click title bar opens a tab", v => tabs.MiddleClickTitleBarNewTab = v, () => tabs.MiddleClickTitleBarNewTab,
                "Open a new tab from empty title-bar space.", "middle click title bar"),
            Choice(Tabs, "Tabs", "tabs.lastclosed", "When the last tab is closed", v => tabs.LastTabClosed = v, () => tabs.LastTabClosed,
                new[] { LastTabActions.NewTab, LastTabActions.CloseWindow }, "Open a fresh tab or close the window.", "last tab close window"),
            Choice(Tabs, "Tabs", "tabs.newposition", "New tab position", v => tabs.NewTabPosition = v, () => tabs.NewTabPosition,
                new[] { NewTabPositions.AfterCurrent, NewTabPositions.AtEnd }, "Open next to the active tab or at the end.", "new tab order"),
            Bool(Tabs, "Opening", "tabs.openincurrent", "Open projects in the current tab", v => tabs.OpenInCurrentTab = v, () => tabs.OpenInCurrentTab,
                "Replace the active tab when opening a project.", "open project replace ctrl o"),
            Choice(Tabs, "Playback", "tabs.playbackonswitch", "When opening/switching to another tab while music is playing", v => tabs.PlaybackOnTabSwitch = v, () => tabs.PlaybackOnTabSwitch,
                new[] { TabPlaybackActions.ContinuePlayingPrevious, TabPlaybackActions.PausePrevious, TabPlaybackActions.StopPrevious },
                "Choose whether playback in other tabs continues, pauses or stops.", "audio playback switch"),
            Bool(Tabs, "Window", "tabs.detach", "Drag a tab out to a new window", v => tabs.DetachToNewWindow = v, () => tabs.DetachToNewWindow,
                "Tear a tab into its own window by dragging it away.", "detach tear out"),
            Bool(Tabs, "Window", "tabs.merge", "Allow dropping tabs onto other windows", v => tabs.MergeAcrossWindows = v, () => tabs.MergeAcrossWindows,
                "Move tabs between TabForge windows by dragging.", "merge drop"),
            Bool(Tabs, "Tabs", "tabs.playing", "Show playing badge", v => tabs.ShowPlayingIndicator = v, () => tabs.ShowPlayingIndicator,
                "Mark tabs whose scores are playing.", "playing audio badge"),

        };
    }
}
