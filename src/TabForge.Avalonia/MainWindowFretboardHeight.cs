using System.Collections.Generic;
using System.Linq;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views.Docking;
using TabForge.Documents;
using TabForge.Platform;

namespace TabForge.Avalonia;

// Owns: the height the fretboard pane starts at in each tab: the default (InstrumentPanelView.PanelHeight) for a tab whose splitter the user has not
//   moved, and the height the user set by moving it in that tab, which a switch back to that tab restores. Hiding and showing the fretboard
//   keeps that height too: a shown pane is rebuilt at the dock's default ratio, so it is started at the tab's height again.
// Does not own: the split fit (DockWorkspaceController.StartPanelAtHeight), the timeline's fit (MainWindowTrackListFit) or the tab list (MainWindowTabs).
// Tests: TabForge.Avalonia.Tests.FretboardNewTabHeightTests.
// Threads: UI thread only.
internal sealed class MainWindowFretboardHeight
{
    private const string PanelId = "instrument";

    private readonly DockWorkspaceController _dock;
    private readonly MainWindowTabs _songs;
    private readonly IUiThread _uiThread;

    /// <summary>The user's fretboard height per open tab, kept when the tab is left; key: the tab's song; ceiling: the open tabs (pruned on each change).</summary>
    private readonly Dictionary<SongDocument, double> _userHeights = new();

    /// <summary>The tab the dock shows the fretboard of; set when a tab change is applied.</summary>
    private SongDocument? _shown;

    /// <summary>Whether the fretboard pane was shown after the last layout rebuild; a rebuild that shows it again restarts it at its height.</summary>
    private bool _paneShown;

    public MainWindowFretboardHeight(DockWorkspaceController dock, MainWindowTabs songs, IUiThread uiThread)
    {
        _dock = dock;
        _songs = songs;
        _uiThread = uiThread;
        _shown = songs.Active;
        _paneShown = dock.IsPanelVisible(PanelId);
    }

    /// <summary>
    /// Runs before the timeline refits on a tab change and before each layout rebuild: the shown tab keeps the height the user set there, read while its
    /// layout still holds it.
    /// </summary>
    public void RememberShownHeight()
    {
        if (_shown is { } shown && _dock.UserSetPanelHeight(PanelId) is { } height) _userHeights[shown] = height;
        foreach (var song in _userHeights.Keys.Where(song => !_songs.Songs.Contains(song)).ToList()) _userHeights.Remove(song);
    }

    /// <summary>
    /// Runs after the timeline refits on a tab change: the shown tab's fretboard starts at its remembered height, or the default, once the layout
    /// has run.
    /// </summary>
    public void ApplyShownTab()
    {
        _shown = _songs.Active;
        StartAtShownHeight();
    }

    /// <summary>Runs after each layout rebuild: a fretboard the rebuild shows again starts at the shown tab's height, not the dock's default ratio.</summary>
    public void RestoreWhenShown()
    {
        var shown = _dock.IsPanelVisible(PanelId);
        if (shown && !_paneShown) StartAtShownHeight();
        _paneShown = shown;
    }

    /// <summary>Refits the shown tab's fretboard to its height after the window or the dock changed size; a splitter height dragged this session is kept, and a hidden pane is left alone.</summary>
    public void Refit() => _uiThread.Post(() =>
    {
        if (!_dock.IsPanelVisible(PanelId)) return;
        RememberShownHeight();
        if (_dock.UserSetPanelHeight(PanelId) is { } dragged) _dock.RestartPanelAtHeight(PanelId, dragged);   // keep the dragged pixels, not the ratio
        else _dock.StartPanelAtHeight(PanelId, HeightOfShownTab());
    });

    /// <summary>Restarts the fretboard at the shown tab's height on the next UI turn (the Background priority comes after the layout pass).</summary>
    private void StartAtShownHeight() => _uiThread.Post(() => _dock.RestartPanelAtHeight(PanelId, HeightOfShownTab()), UiPriority.Background);

    private double HeightOfShownTab() =>
        _shown is { } song && _userHeights.TryGetValue(song, out var height) ? height : InstrumentPanelView.PanelHeight;
}
