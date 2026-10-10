using System.Windows;
using TabForge.Controllers;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

/// <summary>Controllers that hold their view through an interface run against tiny fakes, with no WPF window.</summary>
public static partial class SelfTest
{
    private sealed class FakeSelectionEditor : ISelectionEditor
    {
        public (int, int, int, int)? Range;
        public int SelectCalls;
        public bool HasSelection => Range is not null;
        public int SelectedTrackIndex { get; set; }
        public TrackModel? Track { get; } = new();
        public (int StartMeasure, int StartCell, int EndMeasure, int EndCell) SelectionCellRange => Range ?? (0, 0, 0, 0);
        public bool SelectionMatches(int a, int b, int c, int d) => Range == (a, b, c, d);
        public void SelectRange(int a, int b, int c, int d) { SelectCalls++; Range = (a, b, c, d); }
        public void ClearSelection(bool notify = true) => Range = null;
    }

    private static void TestSelectionSyncFake()
    {
        var model = new SelectionModel();
        var editor = new FakeSelectionEditor();
        SelectionOrigin? seen = null;
        var sync = new SelectionSync(model, editor, o => seen = o);

        model.SetRange(0, 1, 3, SelectionOrigin.Timeline);
        Check("selection sync: a timeline range reaches the editor and the timeline callback",
            editor.Range is (1, _, 3, _) && seen == SelectionOrigin.Timeline);

        editor.Range = (2, 0, 4, 0);
        sync.PushFromEditor();
        Check("selection sync: the editor's selection is written to the model", model.StartBar == 2 && model.EndBar == 4);

        var calls = editor.SelectCalls;
        sync.ApplyToEditor();
        Check("selection sync: re-applying a matching range is a no-op", editor.SelectCalls == calls);

        model.Clear(SelectionOrigin.Timeline);
        Check("selection sync: clearing the model clears the editor", !editor.HasSelection);
    }

    private sealed class FakeFitRows : ITrackListRows
    {
        public double ActualHeight { get; set; } = 100;
        public double TrackRowHeight { get; private set; } = 34;
        public double DefaultRowHeight => 34;
        public double MaxRowHeight => 51;
        public double CollapsedHeight => 150;
        public event SizeChangedEventHandler? SizeChanged { add { } remove { } }
        public event Action? ResetTrackListHeightRequested { add { } remove { } }
        public double PreferredHeight() => 200;
        public double PreferredHeightAt(double rowHeight) => rowHeight * 6;
        public double RowHeightForPaneHeight(double paneHeight) => 34;
        public bool SetTrackRowHeight(double height) { TrackRowHeight = height; return true; }
        public void BeginResizePreview() { }
        public void EndResizePreview() { }
    }

    private sealed class FakeFitDock : ITrackListDock
    {
        public double? Fitted;
        public double ActualHeight => 0;
        public event EventHandler? LayoutChanged { add { } remove { } }
        public event EventHandler<DockSplitterEventArgs>? SplitterInteraction { add { } remove { } }
        public bool FitPanelHeight(string panelId, double height) { Fitted = height; return true; }
        public void SetPanelHeightLimits(string panelId, Func<(double Min, double Max)?>? limits) { }
    }

    private sealed class FakeFitHost : ITrackListFitHost
    {
        public TimelineSettings Timeline { get; } = new() { AutoFitTrackList = true };
        public ITrackListRows Arrangement { get; } = new FakeFitRows();
        public ITrackListDock Dock { get; } = new FakeFitDock();
        public System.Windows.Threading.Dispatcher Dispatcher => System.Windows.Threading.Dispatcher.CurrentDispatcher;
        public string? Status;
        public void SaveSettings() { }
        public void SetStatus(string text) => Status = text;
    }

    private static void TestTrackListFitFake()
    {
        var host = new FakeFitHost();
        var fit = new TrackListFitController(host, new FrameworkElement());
        fit.FitToTracks();
        Check("track list fit: the dock is sized to the rows' preferred height", ((FakeFitDock)host.Dock).Fitted == 200);
        fit.ResetRowHeight();
        Check("track list fit: reset restores the default row height and reports it",
            host.Arrangement.TrackRowHeight == 34 && host.Status == "Track row height reset");
    }
}
