using System.Collections.Generic;
using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views.Docking;
using TabForge.Docking;

namespace TabForge.Avalonia;

// Owns: the handlers of the View menu rows that show or hide a part of the window: Arrangement overview (the timeline panel), Full screen
//   (the window maximised, and back to its earlier state), Highlight playing bar (the score's band, saved in the Follow settings),
//   Instrument view (the fretboard / keyboard panel, with its row's check) and Side panel (every side panel hidden or brought back,
//   as the side-panel button does).
// Does not own: the menu rows (MainMenuTable in Core), the commands table (MainWindowCommands), the dock layout (DockWorkspaceController),
//   the score drawing (ScoreAppearance in Core), the settings file (MainWindowSettings).
// Tests: TabForge.Avalonia.Tests.MainWindowViewMenuTests.
/// <summary>The View menu's show and hide commands, bound to the window, its dock and its score.</summary>
public static class MainWindowViewMenu
{
    /// <summary>The View menu command ids this class handles.</summary>
    public static readonly IReadOnlyList<string> Commands = new[] { GlobalCommandId, FullscreenCommandId, FullscreenExitCommandId, PlayingBarCommandId, InstrumentPanelCommandId, SidePanelCommandId };

    /// <summary>The View menu row that shows and hides the Arrangement (timeline) panel.</summary>
    private const string GlobalCommandId = "View.Global";

    /// <summary>The View menu row and hotkey that toggle full screen, and the fixed Escape command that leaves it (only while in full screen).</summary>
    public const string FullscreenCommandId = "View.Fullscreen";
    public const string FullscreenExitCommandId = "View.FullscreenExit";

    /// <summary>The View menu row that shows and hides the score's playing band.</summary>
    private const string PlayingBarCommandId = "View.PlayingBar";

    /// <summary>The View menu row and toolbar button that show and hide the instrument panel.</summary>
    private const string InstrumentPanelCommandId = "View.InstrumentPanel";

    /// <summary>The toolbar button that hides the side panel or brings it back.</summary>
    private const string SidePanelCommandId = "View.SidePanel";

    /// <summary>The status notice hiding the side panels shows, and the one bringing them back shows.</summary>
    private const string SidePanelHiddenText = "Side panel hidden";
    private const string SidePanelShownText = "Side panel shown";

    /// <summary>The dock panel the Arrangement overview shows and hides.</summary>
    private const string ArrangementPanelId = "timeline";

    /// <summary>The dock panel holding the fretboard / keyboard strip.</summary>
    private const string InstrumentPanelId = "instrument";

    /// <param name="window">The window that goes full screen.</param>
    /// <param name="dock">The dock whose Arrangement panel shows and hides.</param>
    /// <param name="score">The score view whose playing bar shows and hides.</param>
    /// <param name="settings">The shared settings the playing bar choice is read from and saved to; null keeps the choice for this session.</param>
    /// <param name="status">The status notice the side-panel toggle shows; null shows nothing (the Compose call leaves it unset).</param>
    public static void Register(MainWindowCommands commands, Window window, DockWorkspaceController dock, ScorePreviewView score, MainWindowSettings? settings, Action<string>? status = null)
    {
        score.Probe.Appearance.PlayingBarEnabled = settings?.Settings.Follow.PlayingBarEnabled ?? true;
        commands.Add(GlobalCommandId, () => ToggleGlobal(window, dock));
        RegisterFullscreen(commands, window);
        commands.Add(PlayingBarCommandId, () => TogglePlayingBar(window, score, settings));
        commands.Add(InstrumentPanelCommandId, () => ToggleInstrumentPanel(window, dock));
        commands.Add(SidePanelCommandId, () => ToggleSidePanel(dock, settings, status));
        // The menu rows are built after Register, so their checks take the panels' state when the window opens.
        window.Opened += (_, _) => SyncRowChecks(window, dock, score);
        // Every path that shows or hides a panel (its own close button, a layout, the piano roll) rebuilds the dock: the checks follow the real visibility.
        dock.LayoutRebuilt += () => { SetRowChecks(window, GlobalCommandId, dock.IsPanelVisible(ArrangementPanelId)); SetRowChecks(window, InstrumentPanelCommandId, dock.IsPanelVisible(InstrumentPanelId)); };
    }

    /// <summary>Sets the check of every show and hide row in the window from the panels and the score.</summary>
    internal static void SyncRowChecks(Window window, DockWorkspaceController dock, ScorePreviewView score)
    {
        SetRowChecks(window, GlobalCommandId, dock.IsPanelVisible(ArrangementPanelId));
        SetRowChecks(window, PlayingBarCommandId, score.Probe.Appearance.PlayingBarEnabled);
        SetRowChecks(window, InstrumentPanelCommandId, dock.IsPanelVisible(InstrumentPanelId));
    }

    /// <summary>View > Arrangement overview: shows or hides the timeline panel and sets the check of its menu row.</summary>
    private static void ToggleGlobal(Window window, DockWorkspaceController dock)
    {
        var show = !dock.IsPanelVisible(ArrangementPanelId);
        dock.SetPanelVisible(ArrangementPanelId, show);
        SetRowChecks(window, GlobalCommandId, show);
    }

    /// <summary>View > Instrument view: shows or hides the fretboard / keyboard panel and sets the check of its menu row.</summary>
    private static void ToggleInstrumentPanel(Window window, DockWorkspaceController dock)
    {
        // Standing rule: never a fretboard in keyboard mode; the row stays unchecked and the dock refuses anyway.
        if (dock.SuppressInstrumentPanel)
        {
            SetRowChecks(window, InstrumentPanelCommandId, false);
            return;
        }
        var show = !dock.IsPanelVisible(InstrumentPanelId);
        dock.SetPanelVisible(InstrumentPanelId, show);
        SetRowChecks(window, InstrumentPanelCommandId, show);
    }

    /// <summary>Sets the check of every menu row, and the lit state of every toolbar toggle, in the window that runs the command.</summary>
    internal static void SetRowChecks(Window window, string commandId, bool shown)
    {
        foreach (var item in window.GetLogicalDescendants().OfType<MenuItem>().Where(item => Equals(item.Tag, commandId)))
            item.IsChecked = shown;
        foreach (var toggle in window.GetLogicalDescendants().OfType<ToggleButton>().Where(b => Equals(b.Tag, commandId) && b.Classes.Contains(Views.MenuRowToggles.ToggleClass)))
            toggle.IsChecked = shown;
    }

    /// <summary>View > Highlight playing bar: flips the score's band and saves the choice in the Follow settings.</summary>
    public static void TogglePlayingBar(ScorePreviewView score, MainWindowSettings? settings)
    {
        var on = !score.Probe.Appearance.PlayingBarEnabled;
        score.Probe.Appearance.PlayingBarEnabled = on;
        if (settings is not null)
        {
            settings.Settings.Follow.PlayingBarEnabled = on;
            settings.SaveSettingsDeferred();
        }
        score.Refresh();
    }

    /// <summary>View > Highlight playing bar from its menu row: flips the band and sets the check of its menu row.</summary>
    private static void TogglePlayingBar(Window window, ScorePreviewView score, MainWindowSettings? settings)
    {
        TogglePlayingBar(score, settings);
        SetRowChecks(window, PlayingBarCommandId, score.Probe.Appearance.PlayingBarEnabled);
    }

    /// <summary>Toolbar side-panel button: hides every shown side panel, or brings back exactly the ones that were shown.</summary>
    private static void ToggleSidePanel(DockWorkspaceController dock, MainWindowSettings? settings, Action<string>? status)
    {
        var shown = DockPaneTable.SideIds.Where(dock.IsPanelVisible).ToList();
        if (shown.Count > 0)
        {
            if (settings is not null) settings.Settings.WorkspaceBeforeSideHide = dock.SnapshotLayout();
            foreach (var id in shown) dock.SetPanelVisible(id, false);
            settings?.SaveSettings();
            status?.Invoke(SidePanelHiddenText);
        }
        else if (settings?.Settings.WorkspaceBeforeSideHide is { } saved && DockLayoutCatalog.OnlySidePanelsDiffer(saved, dock.SnapshotLayout()))
        {
            dock.ApplyLayout(saved);
            settings.Settings.WorkspaceBeforeSideHide = null;
            settings.SaveSettings();
            status?.Invoke(SidePanelShownText);
        }
        else
        {
            // Other panels were opened or closed meanwhile: the hidden ones cannot be placed back exactly, so each returns to its default place.
            foreach (var id in DockPaneTable.SideIds) dock.SetPanelVisible(id, true);
            if (settings is not null)
            {
                settings.Settings.WorkspaceBeforeSideHide = null;
                settings.SaveSettings();
            }
            status?.Invoke(SidePanelShownText);
        }
    }

    /// <summary>View > Full screen (F11): the window goes to true full screen on its own monitor and back to the state it had before; Escape leaves it.
    /// The row's check follows the window state, however it changed.</summary>
    internal static void RegisterFullscreen(MainWindowCommands commands, Window window)
    {
        var restoreState = WindowState.Normal;
        commands.Add(FullscreenCommandId, () => window.WindowState = window.WindowState == WindowState.FullScreen ? restoreState : WindowState.FullScreen);
        commands.AddWhen(FullscreenExitCommandId, () => window.WindowState = restoreState, () => window.WindowState == WindowState.FullScreen);
        window.PropertyChanged += (_, e) =>
        {
            if (e.Property != Window.WindowStateProperty) return;
            var now = window.WindowState;
            if (now == WindowState.FullScreen && e.OldValue is WindowState before and not WindowState.FullScreen) restoreState = before == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
            SetRowChecks(window, FullscreenCommandId, now == WindowState.FullScreen);
        };
    }
}
