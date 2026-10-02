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
}

/// <summary>
/// Keeps the track-list dock exactly as tall as its rows (no empty band under the last track) and turns a drag of its
/// splitter into a row height: the rows (track controls and timeline lanes) stretch to fill the dock, between the default
/// height and 3x; dragging smaller keeps the default height and the list scrolls. Only while the auto-fit setting is on.
/// </summary>
internal sealed class TrackListFitController
{
    public const string PanelId = "timeline";
    /// <summary>The dock hands the panel this much less than the height it was asked to fit (measured: its own border), so the panel's height + this is what compares with the preferred height.</summary>
    internal const double Chrome = 2;
    private readonly ITrackListFitHost _host;
    private bool _fitting, _dragging, _fitPending, _dragPending;
    private bool _userShort;   // the user dragged the dock shorter than its rows: leave it there until the tracks change

    public TrackListFitController(ITrackListFitHost host, FrameworkElement owner)
    {
        _host = host;
        owner.SizeChanged += (_, _) => ScheduleShrink();
        host.Arrangement.SizeChanged += (_, _) => ScheduleShrink();
        host.Dock.LayoutChanged += (_, _) => ScheduleShrink();
        host.Dock.SplitterInteraction += OnSplitter;
        // While auto-fit is on the splitter stops where every row fits at the default height (above: no clipped rows) and at the
        // largest row height (below: no empty space).
        host.Dock.SetPanelHeightLimits(PanelId, () => Enabled
            ? (host.Arrangement.PreferredHeightAt(ArrangementPanel.DefaultTrackRowHeight), host.Arrangement.PreferredHeightAt(ArrangementPanel.MaxTrackRowHeight))
            : null);
    }

    private bool Enabled => _host.Timeline.AutoFitTrackList;

    /// <summary>Applies the saved row height (the default while auto-fit is off) without touching the dock.</summary>
    public void ApplyRowHeight() =>
        _host.Arrangement.SetTrackRowHeight(Enabled ? _host.Timeline.TrackRowHeight : ArrangementPanel.DefaultTrackRowHeight);

    /// <summary>Tracks, groups or the document changed: size the dock to the rows (grow or shrink).</summary>
    public void FitToTracks()
    {
        _userShort = false;
        ApplyRowHeight();
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
            var want = _host.Arrangement.PreferredHeight();
            if (!allowGrow && _host.Arrangement.ActualHeight + Chrome <= want + 1) return;
            _host.Dock.FitPanelHeight(PanelId, want);
        }
        finally { _fitting = false; }
    }

    /// <summary>Back to the default row height and a dock that fits the rows.</summary>
    public void ResetRowHeight()
    {
        _host.Timeline.TrackRowHeight = ArrangementPanel.DefaultTrackRowHeight;
        _host.SaveSettings();
        FitToTracks();
        _host.SetStatus("Track row height reset");
    }

    private void OnSplitter(object? sender, DockSplitterEventArgs e)
    {
        if (!Enabled || !e.Vertical || !e.Second.Contains(PanelId)) return;
        switch (e.Phase)
        {
            case DockSplitterPhase.Started: _dragging = true; break;
            case DockSplitterPhase.Delta:
                _dragging = true;
                if (_dragPending) break;   // at most one row-height update per layout pass
                _dragPending = true;
                _host.Dispatcher.BeginInvoke(DispatcherPriority.Render, () => { _dragPending = false; if (_dragging) StretchToDock(); });
                break;
            case DockSplitterPhase.Completed:
                _dragging = false;
                StretchToDock();
                var rowHeight = _host.Arrangement.TrackRowHeight;
                if (Math.Abs(_host.Timeline.TrackRowHeight - rowHeight) > 0.05) { _host.Timeline.TrackRowHeight = rowHeight; _host.SaveSettings(); }
                _userShort = _host.Arrangement.ActualHeight + Chrome < _host.Arrangement.PreferredHeight() - 1;
                if (!_userShort) Fit(allowGrow: true);   // past the maximum row height: snap back to the rows
                break;
            case DockSplitterPhase.DoubleClick: ResetRowHeight(); break;
        }
    }

    private void StretchToDock() =>
        _host.Arrangement.SetTrackRowHeight(_host.Arrangement.RowHeightForPaneHeight(_host.Arrangement.ActualHeight + Chrome));
}
