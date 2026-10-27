using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using TabForge.Platform;
using TabForge.Services;
using TabForge.Shell;

namespace TabForge.Avalonia;

// Owns: the tab-behaviour decisions the Tabs settings drive: what a middle-click on a tab does, and a middle-click on empty
//   title-bar space that opens a tab.
// Does not own: the tab drawing (TabStripView), the tab order and the new-tab action (MainWindowTabs), the drag gesture
//   (DocumentTabDragFlow), the playback on a tab switch (TabPlaybackSwitchPolicy).
// Tests: TabForge.Avalonia.Tests.TabBehaviourSettingsTests.
public enum TabMiddleClickAction { Nothing, Close, NewTab, Duplicate }

public static class TabBehaviourSettings
{
    private const string TabStackName = "Tabs";

    /// <summary>The action a middle-click on a tab performs.</summary>
    public static TabMiddleClickAction MiddleClickAction(TabSettings tabs) => tabs.MiddleClick switch
    {
        MiddleClickActions.Close => TabMiddleClickAction.Close,
        MiddleClickActions.NewTab => TabMiddleClickAction.NewTab,
        MiddleClickActions.Duplicate => TabMiddleClickAction.Duplicate,
        _ => TabMiddleClickAction.Nothing,
    };

    /// <summary>True when a middle-click on empty title-bar space opens a new tab (the Tabs setting).</summary>
    public static bool TitleBarMiddleClickOpensTab(MainWindowSettings? settings) =>
        settings?.Settings.Tabs.MiddleClickTitleBarNewTab == true;

    /// <summary>A middle press on empty title-bar space (not a tab or a button) starts a click on the given per-window tracker.</summary>
    public static void OnTitleBarPressed(PointerPressedEventArgs e, MainWindowSettings? settings, MiddleClickTracker tracker)
    {
        if (!TitleBarMiddleClickOpensTab(settings)) return;
        if (!e.GetCurrentPoint(null).Properties.IsMiddleButtonPressed || !IsEmptyTitleBar(e.Source)) return;
        e.Handled = true;
        var at = e.GetPosition(null);
        tracker.Press(new UiPoint(at.X, at.Y));
    }

    /// <summary>The release of that click: a new tab when it stays within 8 DIPs and lands on empty title-bar space too.</summary>
    public static void OnTitleBarReleased(PointerReleasedEventArgs e, MainWindowSettings? settings, MainWindowTabs songs, MiddleClickTracker tracker)
    {
        if (e.InitialPressMouseButton != MouseButton.Middle) return;
        var at = e.GetPosition(null);
        if (!tracker.Release(new UiPoint(at.X, at.Y)) || !TitleBarMiddleClickOpensTab(settings) || !IsEmptyTitleBar(e.Source)) return;
        e.Handled = true;
        songs.New();
    }

    private static bool IsEmptyTitleBar(object? target) =>
        target is Visual source && !source.GetSelfAndVisualAncestors().Any(v => v is Button || v.GetVisualParent() is StackPanel { Name: TabStackName });

    /// <summary>
    /// The same new tab from the Windows native middle-click: the hook answers HTCAPTION on the empty caption, so the press arrives
    /// as WM_NCMBUTTONDOWN / WM_NCMBUTTONUP and no Avalonia pointer event exists. Only the Windows hook calls this.
    /// </summary>
    public static void OnTitleBarMiddleClicked(MainWindowSettings? settings, MainWindowTabs songs)
    {
        if (!TitleBarMiddleClickOpensTab(settings)) return;
        songs.New();
    }
}
