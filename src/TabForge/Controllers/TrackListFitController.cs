using System.Windows;
using System.Windows.Threading;
using TabForge.Services;
using TabForge.Views;

namespace TabForge.Controllers;

/// <summary>What the track-list fit controller needs from its window.</summary>
internal interface ITrackListFitHost
{
    TimelineSettings Timeline { get; }
    ArrangementPanel Arrangement { get; }
    DockWorkspace Dock { get; }
    Dispatcher Dispatcher { get; }
    void SaveSettings();
    void SetStatus(string text);
    /// <summary>The least height the score pane keeps (one full system); 0 when unknown.</summary>
    double MinScoreHeight => 0;
}

// Owns: the track-list dock height: fitting it to its rows and turning a splitter drag into a row height.
// Does not own: the track row controls and the dock layout persistence.
// Tests: TestTrackListFit.
/// <summary>
/// Keeps the track-list dock exactly as tall as its rows (no empty band under the last track) and turns a drag of its
/// splitter into a row height: the rows (track controls and timeline lanes) stretch to fill the dock, between the default
/// height and 1.5x; dragging smaller keeps the default height and the list scrolls. Only while the auto-fit setting is on.
/// </summary>
internal sealed class TrackListFitController
{
    public const string PanelId = "timeline";
    /// <summary>The dock hands the panel this much less than the height it was asked to fit (measured: its own border), so the panel's height + this is what compares with the preferred height.</summary>
    internal const double Chrome = 2;
    private readonly ITrackListFitHost _host;
    private bool _fitting, _dragging, _fitPending;
    private bool _uncapped;   // a dragged-to height is the user's: the cap does not pull it back
    private bool _userShort;   // the user dragged the dock shorter than its rows: leave it there until the tracks change

    public TrackListFitController(ITrackListFitHost host, FrameworkElement owner)
    {
        _host = host;
        owner.SizeChanged += (_, _) => ScheduleShrink();
        host.Arrangement.SizeChanged += (_, _) => ScheduleShrink();
        host.Dock.LayoutChanged += (_, _) => ScheduleShrink();
        host.Dock.SplitterInteraction += OnSplitter;
        host.Arrangement.ResetTrackListHeightRequested += ResetRowHeight;
        // While auto-fit is on the splitter stops where every row fits at the default height (above: no clipped rows) and at the
        // largest row height (below: no empty space).
        host.Dock.SetPanelHeightLimits(PanelId, () => Enabled
            ? (Math.Min(ArrangementPanel.CollapsedPaneHeight, host.Arrangement.PreferredHeightAt(ArrangementPanel.DefaultTrackRowHeight)), host.Arrangement.PreferredHeightAt(ArrangementPanel.MaxTrackRowHeight))
            : null);
    }

    private bool Enabled => _host.Timeline.AutoFitTrackList;

    /// <summary>Applies the saved row height (the default while auto-fit is off) without touching the dock.</summary>
    public void ApplyRowHeight() =>
        _host.Arrangement.SetTrackRowHeight(Enabled ? _host.Timeline.TrackRowHeight : ArrangementPanel.DefaultTrackRowHeight);

    /// <summary>Tracks, groups or the document changed: size the dock to the rows (grow or shrink).</summary>
    public void FitToTracks()
    {
        ApplyRowHeight();
        // A pane the user collapsed stays at its saved height (never past the rows); anything else fits the rows.
        var saved = _host.Timeline.TrackListHeight;
        _userShort = Enabled && saved > 0 && saved < _host.Arrangement.PreferredHeight() - 1;
        if (_userShort) { _host.Dock.FitPanelHeight(PanelId, saved); return; }
        Fit(allowGrow: true);
    }

    /// <summary>Dock, window or scale changes: only remove empty space; a dock the user made shorter stays shorter.</summary>
    public void ScheduleShrink()
    {
        if (_fitPending || _fitting || _dragging || !Enabled) return;
        _fitPending = true;
        _host.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => { _fitPending = false; Fit(allowGrow: !_userShort); });
    }

    /// <summary>Runs a pending shrink-fit now (tests).</summary>
    internal void Flush() { if (_fitPending) { _fitPending = false; Fit(allowGrow: !_userShort); } }

    private void Fit(bool allowGrow)
    {
        if (!Enabled || _fitting || _dragging) return;
        _fitting = true;
        try
        {
            var want = CappedHeight();
            if (!allowGrow && _host.Arrangement.ActualHeight + Chrome <= want + 1) return;
            _host.Dock.FitPanelHeight(PanelId, want);
        }
        finally { _fitting = false; }
    }

    /// <summary>The rows' height, capped at 45% of the space shared with the score and leaving the score one system; never below the collapsed height. Extra rows scroll.</summary>
    private double CappedHeight()
    {
        var want = _host.Arrangement.PreferredHeight();
        var shared = _host.Dock.ActualHeight;
        if (shared <= 1 || _host.MinScoreHeight <= 0 || _uncapped) return want;
        var cap = Math.Min(shared * 0.45, shared - _host.MinScoreHeight);
        return Math.Min(want, Math.Max(ArrangementPanel.CollapsedPaneHeight, cap));
    }

    /// <summary>Back to the default row height and a dock that fits the rows.</summary>
    public void ResetRowHeight()
    {
        _host.Timeline.TrackRowHeight = ArrangementPanel.DefaultTrackRowHeight;
        _host.Timeline.TrackListHeight = 0;
        _host.SaveSettings();
        FitToTracks();
        _host.SetStatus("Track row height reset");
    }

    private void OnSplitter(object? sender, DockSplitterEventArgs e)
    {
        if (!e.Vertical || !(e.Second.Contains(PanelId) || e.First.Contains(PanelId))) return;
        // During the drag the panel shows its blurred, stretched snapshot; the rows are resized once when the drag ends.
        if (e.Phase == DockSplitterPhase.Started) _host.Arrangement.BeginResizePreview();
        if (e.Phase == DockSplitterPhase.Completed) _host.Arrangement.EndResizePreview();
        if (!Enabled || !e.Second.Contains(PanelId)) return;
        switch (e.Phase)
        {
            case DockSplitterPhase.Started: _dragging = true; break;
            case DockSplitterPhase.Delta: _dragging = true; break;
            case DockSplitterPhase.Completed:
                _dragging = false;
                StretchToDock();
                var rowHeight = _host.Arrangement.TrackRowHeight;
                if (Math.Abs(_host.Timeline.TrackRowHeight - rowHeight) > 0.05) { _host.Timeline.TrackRowHeight = rowHeight; _host.SaveSettings(); }
                _userShort = _host.Arrangement.ActualHeight + Chrome < _host.Arrangement.PreferredHeight() - 1;
                var kept = _userShort ? Math.Round(_host.Arrangement.ActualHeight + Chrome) : 0;
                if (Math.Abs(_host.Timeline.TrackListHeight - kept) > 0.5) { _host.Timeline.TrackListHeight = kept; _host.SaveSettings(); }
                if (!_userShort) { _uncapped = true; try { Fit(allowGrow: true); } finally { _uncapped = false; } }   // past the maximum row height: snap back to the rows
                break;
            case DockSplitterPhase.DoubleClick: ResetRowHeight(); break;
        }
    }

    private void StretchToDock() =>
        _host.Arrangement.SetTrackRowHeight(_host.Arrangement.RowHeightForPaneHeight(_host.Arrangement.ActualHeight + Chrome));
}
