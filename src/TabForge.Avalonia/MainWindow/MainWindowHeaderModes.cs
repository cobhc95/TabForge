using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Docking;
using TabForge.Documents;
using TabForge.Docking;

namespace TabForge.Avalonia;

// Owns: the header strip that compact mode and the Band view share, and the window parts both modes hide. The strip belongs to the mode entered
//   last (HeaderModeCaption): its caption, its capsules, its caption menu and its close button come from that mode's handlers. While any mode is on,
//   the window's menu row is hidden; the Arrangement shows its compact content only while compact mode is on (the Band view alone keeps the normal
//   bottom and fits the track list), and the last mode to leave gives the menu row and the Arrangement's full rows back and fits the track list
//   once the mode's own exit put its layout back. The workspace hold is taken
//   by the first mode on (the layout before it) and released by the last one off. That layout is also the one the last mode to leave restores
//   (LayoutBeforeFirstMode), so a mode that leaves after the first one restores the same layout. The side panels compact mode hid stay hidden while
//   any mode is on (SidePanelsHiddenByModes), and HideSidePanelsHeldByModes holds and hides them again after another layout is applied.
// Does not own: each mode's own changes to the dock (the side panels of compact mode, the Band layout), the handlers (the modes pass them in),
//   the strip's look (CompactTrackStrip), the menu row and timeline helpers (MainWindowCompactLayout).
// Tests: TabForge.Avalonia.Tests.CompactBandHeaderTests.
// Threads: UI thread only.
public sealed class MainWindowHeaderModes
{
    private readonly Window _window;
    private readonly DockWorkspaceController _dock;
    private readonly MainWindowDocumentPanes _panes;
    private readonly MainWindowSettings? _settings;
    private readonly Action _fitTrackList;
    private readonly HeaderModeCaption _modes = new();
    private readonly Dictionary<HeaderMode, HeaderModeHandlers> _handlers = new();
    private ArrangementView? _arrangement;

    /// <param name="fitTrackList">Fits the timeline pane to its track rows again, when the Arrangement goes back to its full rows (the Band view alone,
    /// or the last mode leaving); the implementation defers its one fit to the rebuilt tree's next layout pass.</param>
    public MainWindowHeaderModes(Window window, DockWorkspaceController dock, MainWindowDocumentPanes panes, MainWindowTabs tabs, MainWindowSettings? settings, Action fitTrackList)
    {
        _window = window;
        _dock = dock;
        _panes = panes;
        _settings = settings;
        _fitTrackList = fitTrackList;
        Strip = new CompactTrackStrip();
        // A tab switch shows the other song in the strip and in the mode's cards (the modes read their song when the strip refreshes).
        tabs.ActiveChanged += Refresh;
        // The dock rebuilds the Arrangement whenever its layout changes, so a pane made while a mode is on takes the compact content too.
        panes.ArrangementCreated += OnArrangementCreated;
    }

    /// <summary>The header strip: the window places it above the panes, and it shows while a mode is on.</summary>
    public CompactTrackStrip Strip { get; }

    /// <summary>The layout the first mode on was entered with; the last mode to leave restores it. Null while no mode is on.</summary>
    public DockWorkspaceState? LayoutBeforeFirstMode { get; private set; }

    /// <summary>The side panels compact mode hid on its entry: they stay hidden while any mode is on. Filled and cleared by compact mode.</summary>
    public List<string> SidePanelsHiddenByModes { get; } = new();

    /// <summary>Whether the mode is on.</summary>
    public bool IsOn(HeaderMode mode) => _modes.IsOn(mode);

    /// <summary>Whether any header mode is on.</summary>
    public bool AnyOn => _modes.AnyOn;

    /// <summary>
    /// The mode turns on. <paramref name="layoutBefore"/> is the dock as it was before the mode changed it: it is held for the saved workspace when
    /// this is the first mode on. The first mode also hides the menu row and fits the Arrangement's compact content.
    /// </summary>
    public void Enter(HeaderMode mode, HeaderModeHandlers handlers, DockWorkspaceState layoutBefore)
    {
        var first = !_modes.AnyOn;
        _modes.Enter(mode);
        _handlers[mode] = handlers;
        if (first)
        {
            LayoutBeforeFirstMode = layoutBefore;
            _settings?.HoldWorkspace(layoutBefore);
            _arrangement = _panes.Arrangement;
            MainWindowCompactLayout.SetMenuRowVisible(_window, Strip, false);
        }
        ApplyArrangementContent();
        Refresh();
    }

    /// <summary>
    /// The mode turns off. The last mode off gives the menu row, the Arrangement's full rows and the workspace hold back, and fits the track list
    /// once the caller's restored layout has laid out (a mode swap shows a layout, so a splitter the user dragged is replaced); a mode that stays
    /// off with another still on gives back nothing. The mode puts its own panels back after this call.
    /// </summary>
    public void Leave(HeaderMode mode)
    {
        if (!_modes.IsOn(mode)) return;
        _modes.Leave(mode);
        _handlers.Remove(mode);
        if (!_modes.AnyOn)
        {
            LayoutBeforeFirstMode = null;
            _settings?.HoldWorkspace(null);
            MainWindowCompactLayout.SetMenuRowVisible(_window, Strip, true);
            _arrangement?.SetCompact(false);
            _arrangement = null;
            // The Arrangement goes back to its full rows, and the mode's own exit applies its layout after this call: the deferred
            // one-shot waits for that rebuilt tree's layout pass, so the pane is fitted after the final rebuild, as a song shown is.
            _fitTrackList();
        }
        else ApplyArrangementContent();
        Refresh();
    }

    /// <summary>
    /// Hides again the side panels the modes hold hidden. While a mode is still on, the side panels a layout applied meanwhile shows (the layout
    /// the Band view restores when it leaves, for one) join the held ones, so they stay hidden until the last mode leaves.
    /// </summary>
    public void HideSidePanelsHeldByModes()
    {
        // Perf E2b (H4/H5): hiding N panels one by one rebuilt the dock N times; batch into one Show.
        using (_dock.DeferRefresh())
        {
            if (AnyOn)
            {
                foreach (var id in DockPaneTable.SideIds.Where(_dock.IsPanelVisible).ToList())
                {
                    if (!SidePanelsHiddenByModes.Contains(id)) SidePanelsHiddenByModes.Add(id);
                }
            }
            foreach (var id in SidePanelsHiddenByModes) _dock.SetPanelVisible(id, false);
        }
    }

    /// <summary>A new Arrangement made while a mode is on becomes the one the modes compact and fit.</summary>
    private void OnArrangementCreated(ArrangementView arrangement)
    {
        if (!AnyOn) return;
        _arrangement = arrangement;
        ApplyArrangementContent();
    }

    /// <summary>Fits the timeline pane to the compact content again, after the dock layout was replaced while a mode is still on.</summary>
    public void RefitTimeline() => ApplyArrangementContent();

    /// <summary>
    /// The Arrangement's content follows compact mode alone: compact on, it hides the track rows and fits the pane to the compact content; off, it
    /// shows its full rows and the pane is fitted to the track rows again (the Band view alone, or compact mode leaving while the Band view stays on).
    /// </summary>
    private void ApplyArrangementContent()
    {
        if (_arrangement is not { } arrangement) return;
        var compact = IsOn(HeaderMode.Compact);
        arrangement.SetCompact(compact);
        if (compact) MainWindowCompactLayout.FitTimelinePaneOnNextLayout(_dock, arrangement);
        else _fitTrackList();
    }

    /// <summary>Re-reads which capsules are lit: the shown cards or the selected track changed.</summary>
    public void SyncLit() => Strip.SyncLit();

    private void Refresh()
    {
        if (_modes.Top is not { } top)
        {
            Strip.SetOn(false, null);
            return;
        }
        var handlers = _handlers[top];
        // The capsules show the Band view's cards whenever it is on, even when compact mode was entered last: compact mode must not change them.
        var pills = _handlers.TryGetValue(HeaderMode.Band, out var band) ? band : handlers;
        Strip.IsLit = pills.IsLit;
        Strip.PillClicked = pills.PillClicked;
        Strip.PillToolTip = pills.PillToolTip;
        Strip.MenuRequested = handlers.Menu;
        Strip.CloseRequested = handlers.Close;
        Strip.SetCaption(_modes.Caption);
        Strip.SetOn(true, handlers.Song());
    }
}

/// <summary>What the shared header strip asks of the mode it currently shows: the song, the lit capsules, the clicks and the menu and close.</summary>
/// <param name="Song">The song the strip's capsules are the tracks of (read at each refresh).</param>
/// <param name="IsLit">Whether the capsule of a track index is lit.</param>
/// <param name="PillClicked">Runs with a capsule's track index when it is clicked.</param>
/// <param name="PillToolTip">The hover text of a capsule, given its track name.</param>
/// <param name="Menu">Opens the mode's menu on the anchor at the placement (the caption, or the pointer on a right-click).</param>
/// <param name="Close">Runs when the strip's close button is pressed.</param>
public sealed record HeaderModeHandlers(
    Func<SongDocument?> Song,
    Func<int, bool> IsLit,
    Action<int> PillClicked,
    Func<string, string> PillToolTip,
    Action<Control, PlacementMode> Menu,
    Action Close);
