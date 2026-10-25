// Owns: sizing the timeline pane of the main window to its track rows (the WPF "Auto-resize track list to fit" default): the rows' height, capped by the Core
//   rule, applied when the dock is laid out or resized.
// Does not own: the fit maths (DockSizeRules.TrackListHeight), the split drawing (DockSplitPanel) or the saved layout (UserRatio keeps the saved ratio).
// Tests: TabForge.Avalonia.Tests.MainWindowDockParityTests, TabForge.Avalonia.Tests.TrackListLaunchFitTests.
using System;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Docking;
using TabForge.Docking;
using TabForge.Platform;

namespace TabForge.Avalonia;

/// <summary>Fits the timeline pane's height to its rows. Thread: UI. Holds the dock and the pane source.</summary>
internal sealed class MainWindowTrackListFit
{
    private const string PanelId = "timeline";
    private const double ScoreKeepHeight = 300;   // the least the score keeps: about one system of tab and staff with its header
    private const double TrackListMaxShare = 1;   // the track list shows every row; only the score's least height caps it
    private readonly DockWorkspaceController _dock;
    private readonly DockWorkspaceView _view;
    private readonly Func<ArrangementView?> _arrangement;
    private readonly Func<bool> _enabled;
    private readonly IUiThread _uiThread;
    private bool _retryQueued;
    private bool _replaceUserSplit;
    private int _pendingRetries;

    /// <summary>How many Background turns a not-yet-laid-out fit waits at most; a hidden timeline never lays out, so the chain always ends.</summary>
    private const int MaxFitRetries = 8;

    public MainWindowTrackListFit(DockWorkspaceController dock, DockWorkspaceView view, Func<ArrangementView?> arrangement, Func<bool> enabled, IUiThread uiThread)
    {
        _enabled = enabled;
        _dock = dock;
        _view = view;
        _arrangement = arrangement;
        _uiThread = uiThread;
        view.SizeChanged += (_, _) => Fit();
    }

    /// <summary>
    /// The height that shows the header and every row, capped like the WPF fit; with "Auto-resize track list to fit" off, the saved split comes back.
    /// A splitter the user dragged keeps its place until the next song, tab or layout is shown (<see cref="FitReplacingUserSplit"/>).
    /// When the dock's split is not laid out yet, the fit re-posts on the UI thread's next turns until it applies (a rebuild settles over
    /// several passes) or the attempts run out, so one early turn never leaves the pane unfitted.
    /// </summary>
    public void Fit()
    {
        if (FitNow()) { _pendingRetries = 0; return; }
        if (_retryQueued) return;
        _pendingRetries = MaxFitRetries;
        _retryQueued = true;
        _uiThread.Post(RetryFit, UiPriority.Background);
    }

    /// <summary>One more fit attempt after a layout pass; re-posts while the split still has no size, up to <see cref="MaxFitRetries"/> turns.</summary>
    private void RetryFit()
    {
        _retryQueued = false;
        if (FitNow()) { _pendingRetries = 0; return; }
        if (_pendingRetries-- <= 0) return;
        _retryQueued = true;
        _uiThread.Post(RetryFit, UiPriority.Background);
    }

    /// <summary>Fits for a song, tab or layout just shown: the splitter the user dragged in this session goes back to the fit.</summary>
    public void FitReplacingUserSplit()
    {
        _replaceUserSplit = true;
        Fit();
    }

    /// <summary>Fits for a layout just shown whose split is not laid out yet (the Keyboard mode swap rebuilds the dock, so the synchronous fit finds no split
    /// size and the first turns may run before the new tree lays out): waits for the timeline's next layout pass, when the rebuilt split has its size,
    /// then fits once, replacing the user splitter. Falls back to the immediate fit when no timeline is shown.</summary>
    public void FitReplacingUserSplitOnNextLayout()
    {
        if (_arrangement()?.Timeline is not { } timeline) { FitReplacingUserSplit(); return; }
        void FitOnce(object? sender, EventArgs e)
        {
            timeline.LayoutUpdated -= FitOnce;
            FitReplacingUserSplit();
        }
        timeline.LayoutUpdated += FitOnce;
    }

    /// <summary>Fits once; false when the split could not be fitted yet (not laid out).</summary>
    private bool FitNow()
    {
        if (!_enabled()) { _dock.RestorePanelHeight(PanelId); return true; }
        if (_arrangement() is not { } arrangement) return true;
        var timeline = arrangement.Timeline;
        var collapsed = arrangement.TopBandHeight + TimelineView.RulerHeight + TimelineView.SectionHeight + timeline.BaseRowHeight + 2;
        var fitted = _dock.FitPanelHeight(PanelId, DockSizeRules.TrackListHeight(arrangement.NaturalHeight, _view.Bounds.Height, ScoreKeepHeight, collapsed, TrackListMaxShare), _replaceUserSplit);
        if (fitted) _replaceUserSplit = false;
        return fitted;
    }
}
