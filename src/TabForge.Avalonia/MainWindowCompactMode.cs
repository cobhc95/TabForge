using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Docking;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Docking;
using TabForge.Documents;
using TabForge.Presenters;

namespace TabForge.Avalonia;

// Owns: View > Compact mode (the View.CompactMode command, which the menu row and the toolbar button both run): on, it hides the side
//   panels and holds the fretboard pane at its pixel height, and its header mode (MainWindowHeaderModes) hides the track rows and the menu row and fits the Arrangement; off, it restores the layout
//   the first header mode was entered with (exactly as entered, when it is the only mode), unless the Band layout is still on, which then owns the
//   layout and keeps the side panels hidden until it leaves; and the check of its row.
// Does not own: the layout maths (DockWorkspaceController, DockSizeRules in Core), the side panel list (DockPaneTable in Core), the menu rows
//   (MainMenuTable), the row geometry (TimelineView.Compact), the header strip and the hiding both modes share (MainWindowHeaderModes), that strip's
//   stand-in menu for the hidden menu row (CompactModeMenu).
// Tests: TabForge.Avalonia.Tests.MainWindowMenuTests, TabForge.Avalonia.Tests.CompactBandHeaderTests.
/// <summary>The View > Compact mode toggle, bound to the window and its dock.</summary>
public sealed class MainWindowCompactMode
{
    /// <summary>The View menu command ids this class handles.</summary>
    public static readonly IReadOnlyList<string> Commands = new[] { CommandId };

    private const string CommandId = "View.CompactMode";

    /// <summary>The fretboard / keyboard pane, which keeps its pixel height while compact mode is on.</summary>
    private const string InstrumentPanelId = "instrument";

    private readonly Window _window;
    private readonly DockWorkspaceController _dock;
    private readonly MainWindowDocumentPanes _panes;
    private readonly MainWindowHeaderModes _header;
    private readonly CompactModeMenu _menu;

    /// <summary>The layout compact mode was entered with, restored as it was on exit; null while compact mode is off.</summary>
    private DockWorkspaceState? _layoutBeforeCompact;

    private MainWindowCompactMode(Window window, DockWorkspaceController dock, MainWindowDocumentPanes panes, MainWindowHeaderModes header)
    {
        _window = window;
        _dock = dock;
        _panes = panes;
        _header = header;
        _menu = new CompactModeMenu(window);
    }

    /// <param name="commands">Receives the View.CompactMode handler.</param>
    /// <param name="window">The window whose View menu row shows the check.</param>
    /// <param name="dock">The dock whose side panels and Arrangement pane compact mode changes.</param>
    /// <param name="panes">The panes whose arrangement and song the strip shows.</param>
    /// <param name="header">The header strip both header modes share; compact mode's handlers go to it while it is on.</param>
    public static void Register(MainWindowCommands commands, Window window, DockWorkspaceController dock, MainWindowDocumentPanes panes, MainWindowHeaderModes header)
    {
        var compact = new MainWindowCompactMode(window, dock, panes, header);
        commands.Add(CommandId, compact.Toggle);
        // The menu row is built after Register, so its check takes the mode's state when the window opens.
        window.Opened += (_, _) => MainWindowViewMenu.SetRowChecks(window, CommandId, compact.IsOn);
    }

    private bool IsOn => _layoutBeforeCompact is not null;

    /// <summary>View > Compact mode: turns the mode on or off and sets the check of its row.</summary>
    private void Toggle()
    {
        if (IsOn) Exit();
        else Enter();
        MainWindowViewMenu.SetRowChecks(_window, CommandId, IsOn);
    }

    private void Enter()
    {
        // Perf E2b (H4/H5): hiding N side panels rebuilt the dock N times; batch into one Show (as Band entry does).
        using (_dock.DeferRefresh())
        {
            _layoutBeforeCompact = _dock.SnapshotLayout();
            // The side panels leave and the timeline shrinks, so a ratio-sized fretboard would grow; it keeps the height it had instead.
            _dock.HoldPanelHeight(InstrumentPanelId, _dock.PanelHeight(InstrumentPanelId));
            _header.SidePanelsHiddenByModes.Clear();
            _header.SidePanelsHiddenByModes.AddRange(DockPaneTable.SideIds.Where(_dock.IsPanelVisible).ToList());
            foreach (var id in _header.SidePanelsHiddenByModes) _dock.SetPanelVisible(id, false);
            _header.Enter(HeaderMode.Compact, Handlers(), _layoutBeforeCompact);
        }
    }

    private void Exit()
    {
        if (_layoutBeforeCompact is null) return;
        // The layout the first mode on was entered with: it is the one to restore when this is the last mode, even if the Band layout was left first.
        var layoutBeforeFirstMode = _header.LayoutBeforeFirstMode ?? _layoutBeforeCompact;
        _layoutBeforeCompact = null;
        _dock.HoldPanelHeight(InstrumentPanelId, null);
        _header.Leave(HeaderMode.Compact);
        _menu.Restore();
        _header.SidePanelsHiddenByModes.Clear();
        // The panels, their places and sizes come back exactly as entered, not rebuilt from the defaults.
        if (!_header.AnyOn) _dock.ApplyLayout(layoutBeforeFirstMode);
    }

    /// <summary>The song the strip shows: the shown arrangement's.</summary>
    private SongDocument? Song() => _panes.Arrangement?.Document as SongDocument;

    private HeaderModeHandlers Handlers() => new(
        Song,
        index => CompactModeStripPresenter.PillLit(index, Song()?.TrackIndex ?? -1),
        index => { if (Song() is { } song) song.TrackIndex = index; },
        CompactModeStripPresenter.PillTooltip,
        _menu.Open,
        Toggle);
}
