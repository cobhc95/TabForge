using System.Windows;
using System.Windows.Controls;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Views;

// Owns: the window-side answers the dock layout controller asks for (the workspace, the Panels menu, the fretboard pane and its border),
//   on top of the shared pane host. Holds no subscription and no state of its own.
// Does not own: the layout logic (DockLayoutController) or the workspace (DockWorkspace).
// Tests: TestDockPaneTable, TestFretboardPaneSize, TestBandNeverDocked.
internal sealed class DockHost : IDockLayoutHost
{
    private readonly IPaneHost _pane;
    private readonly Func<DockWorkspace?> _dock;

    public DockHost(IPaneHost pane, Func<DockWorkspace?> dock, MenuItem dockPanelsMenu, InstrumentPanel instrument, Border instrumentHost)
    {
        _pane = pane;
        _dock = dock;
        DockPanelsMenu = dockPanelsMenu;
        Instrument = instrument;
        InstrumentHost = instrumentHost;
    }

    public Window Window => _pane.Window;
    public AppSettings Settings => _pane.Settings;
    public TabEditorControl Editor => _pane.Editor;
    public SongProject Project => _pane.Project;
    public TrackModel? SelectedTrack => _pane.SelectedTrack;
    public void SaveSettings() => _pane.SaveSettings();
    public void SetStatus(string text) => _pane.SetStatus(text);

    public DockWorkspace? Dock => _dock();
    public MenuItem DockPanelsMenu { get; }
    public InstrumentPanel Instrument { get; }
    public Border InstrumentHost { get; }
}
