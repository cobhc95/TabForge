using System;
using Avalonia.Controls;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Menus;
using TabForge.Models;
using TabForge.Views.Score;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: the handlers of the score's right-click rows on the shown score: Show standard notation, Show note length outline, Zoom in, Zoom out,
//   Fit width, the page layout choices (page or continuous, vertical or horizontal) and Score settings... (the Settings window on the Score page).
//   Each runs what the WPF row runs (MainWindow.ContextMenus), with the zoom arithmetic from Core (ScoreZoomRules).
// Does not own: the rows (ScoreMenus), the score drawing and layout (Core ScoreGeometryProbe and ScoreLayoutEngine), the zoom and page width
//   of the view (ScorePreviewView), the Settings window (MainWindowSettings).
// Tests: TabForge.Avalonia.Tests.MainWindowRowCommandsTests.
public static class MainWindowScoreRows
{
    /// <param name="score">The score view the rows change.</param>
    /// <param name="settings">The shared settings the Score page shows; null opens the defaults.</param>
    /// <param name="owner">The window the Settings page opens over.</param>
    /// <param name="bandMenu">The Band view's score menu while the Band view shows, else null: the zoom hotkeys zoom its cards then, not the hidden main score.</param>
    public static void Register(MainWindowCommands commands, ScorePreviewView score, MainWindowSettings? settings, Window owner, Func<BandScoreMenu?>? bandMenu = null)
    {
        commands.Add(ScoreMenus.NotationId, () => ToggleStandardNotation(score));
        commands.Add(ScoreMenus.OutlineId, () => ToggleNoteOutline(score));
        // The zoom rows and the View zoom hotkeys (Ctrl+plus and Ctrl+minus, numpad too) share one step about the viewport's middle.
        commands.Add(ScoreMenus.ZoomInId, () => score.ZoomStep(1));
        commands.Add(ScoreMenus.ZoomOutId, () => score.ZoomStep(-1));
        commands.Add("View.ZoomIn", () => ZoomHotkey(score, bandMenu, 1));
        commands.Add("View.ZoomOut", () => ZoomHotkey(score, bandMenu, -1));
        commands.Add(ScoreMenus.FitWidthId, score.FitWidth);
        commands.Add(ContextMenuRows.ScoreLayoutIdPrefix + "page", () => SetContinuous(score, settings, false));
        commands.Add(ContextMenuRows.ScoreLayoutIdPrefix + "continuous", () => SetContinuous(score, settings, true));
        commands.Add(ContextMenuRows.ScoreLayoutIdPrefix + "vertical", () => SetHorizontal(score, settings, false));
        commands.Add(ContextMenuRows.ScoreLayoutIdPrefix + "horizontal", () => SetHorizontal(score, settings, true));
        commands.Add("View.HorizontalScroll", () => SetHorizontal(score, settings, !score.Probe.HorizontalScroll));
        commands.Add(ScoreMenus.SettingsId, () => MainWindowSettings.ShowPreferencesPage(settings, owner, SettingsCatalog.Score, ScoreMenus.SettingsRow));
    }

    // Ctrl+plus and Ctrl+minus: the Band view's lane zoom while it shows, else the main score's zoom.
    private static void ZoomHotkey(ScorePreviewView score, Func<BandScoreMenu?>? bandMenu, int direction)
    {
        if (bandMenu?.Invoke() is { } band) band.WheelZoom(direction > 0);
        else score.ZoomStep(direction);
    }

    /// <summary>"Show standard notation": a shown staff turns it off (tablature only), anything else turns both on.</summary>
    private static void ToggleStandardNotation(ScorePreviewView score)
    {
        var showing = score.Probe.Notation != NotationMode.TabOnly;
        score.Probe.Notation = showing ? NotationMode.TabOnly : NotationMode.TabAndStaff;
        score.Refresh();
    }

    /// <summary>"Show note length outline": flips the outline on the cell under the cursor.</summary>
    private static void ToggleNoteOutline(ScorePreviewView score)
    {
        score.Probe.Appearance.ShowNoteOutline = !score.Probe.Appearance.ShowNoteOutline;
        score.Refresh();
    }

    /// <summary>"Page" or "Continuous" in the page layout group.</summary>
    private static void SetContinuous(ScorePreviewView score, MainWindowSettings? settings, bool continuous)
    {
        score.Probe.Appearance.CenterSystems = continuous;
        if (settings is not null) { settings.Settings.PreferredContinuousScoreView = continuous; settings.SaveSettingsDeferred(); }
        score.PageLayoutChanged();
    }

    /// <summary>"Vertical" or "Horizontal" in the scrolling group (horizontal is one line that scrolls sideways).</summary>
    private static void SetHorizontal(ScorePreviewView score, MainWindowSettings? settings, bool horizontal)
    {
        score.Probe.HorizontalScroll = horizontal;
        if (settings is not null) { settings.Settings.PreferredHorizontalScoreView = horizontal; settings.SaveSettingsDeferred(); }
        score.Refresh();
    }
}
