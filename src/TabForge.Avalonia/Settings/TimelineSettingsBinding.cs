using System;
using System.Collections.Generic;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: copying the Timeline and Tracks rows of the Settings window onto every arrangement pane: the ruler bar numbers, the section names, brackets,
//   glow and bracket thickness, the hide-empty-grid, bar glow, track line, playhead style, row height, Add-track lane, master lane and similar section
//   colour rows, the zoom and the track list width, plus the overview pane's visibility and the track list fit. Each pane re-lays out only when a row
//   that changes its size changed.
// Does not own: the settings file and its validation (MainWindowSettings, SettingsValidator), the lane-mode rows (MainWindowArrangementModes), the
//   drawing (TimelineView, TrackHeaderColumn, MasterLaneStrip) or the pane lifetime (MainWindowDocumentPanes). The row TuningIconOpensWindow
//   has no Avalonia feature to drive yet (no tuning icon). SectionDragAnimation (with Reduce animations) drives the section slide; AutoRemoveEmptyLanes is read by MainWindowClipRefresh.
// Tests: TabForge.Avalonia.Tests.TimelineSettingsBindingTests.

/// <summary>Keeps the arrangement panes in step with the Timeline and Tracks pages of the Settings window. Thread: UI. Holds the panes weakly, so a pane the dock dropped is freed.</summary>
internal sealed class TimelineSettingsBinding
{
    // Every arrangement pane the dock made and still keeps; dead references are dropped on each Apply. Key: none. Ceiling: the panes alive at once. Thread: UI.
    private readonly List<WeakReference<ArrangementView>> _panes = new();
    private readonly Action<bool>? _showOverview;
    private readonly Action? _layoutChanged;
    private AppSettings _settings;
    private bool _overviewShown;
    private bool _autoFit;
    private double _trackListWidth;

    /// <param name="settings">The live settings; their values are the baseline the next change is measured from.</param>
    /// <param name="showOverview">Shows or hides the arrangement overview pane; called only when the row changed.</param>
    /// <param name="layoutChanged">Called after a row that changes the pane's height or the fit changed, so the host fits the track list again.</param>
    public TimelineSettingsBinding(AppSettings settings, Action<bool>? showOverview = null, Action? layoutChanged = null)
    {
        _settings = settings;
        _showOverview = showOverview;
        _layoutChanged = layoutChanged;
        _overviewShown = settings.Appearance.ShowArrangementOverview;
        _autoFit = settings.Timeline.AutoFitTrackList;
        _trackListWidth = settings.Timeline.TrackListWidth;
    }

    /// <summary>Starts following <paramref name="pane"/>: it takes the current settings at once.</summary>
    public void Attach(ArrangementView pane)
    {
        _panes.Add(new WeakReference<ArrangementView>(pane));
        Push(pane, _settings, force: true);
    }

    /// <summary>Pushes every Timeline row of <paramref name="settings"/> into every live pane.</summary>
    public void Apply(AppSettings settings)
    {
        _settings = settings;
        var layoutRowChanged = false;
        foreach (var pane in LivePanes()) layoutRowChanged |= Push(pane, settings, force: false);
        var overview = settings.Appearance.ShowArrangementOverview;
        if (overview != _overviewShown) { _overviewShown = overview; _showOverview?.Invoke(overview); }
        var autoFit = settings.Timeline.AutoFitTrackList;
        if (autoFit != _autoFit) { _autoFit = autoFit; layoutRowChanged = true; }
        _trackListWidth = settings.Timeline.TrackListWidth;
        if (layoutRowChanged) _layoutChanged?.Invoke();
    }

    /// <summary>Moves every live pane to the saved zoom <paramref name="factor"/> without touching any other row.</summary>
    public void ApplyZoom(double factor)
    {
        foreach (var pane in LivePanes()) SetZoom(pane, factor);
    }

    // The panes still alive; the references to dropped ones are removed here.
    private List<ArrangementView> LivePanes()
    {
        _panes.RemoveAll(reference => !reference.TryGetTarget(out _));
        var live = new List<ArrangementView>(_panes.Count);
        foreach (var reference in _panes) if (reference.TryGetTarget(out var pane)) live.Add(pane);
        return live;
    }

    private static void SetZoom(ArrangementView pane, double factor)
    {
        if (pane.Timeline.ZoomFactor != TimelineZoomRules.Clamp(factor)) pane.SetTimelineZoom(factor);
    }

    /// <summary>Sets the rows on one pane; true when its height changed (the host then fits the track list again).</summary>
    private bool Push(ArrangementView pane, AppSettings settings, bool force)
    {
        var timeline = settings.Timeline;
        var view = pane.Timeline;
        pane.Follow = settings.Follow;
        pane.Transport.IsVisible = settings.General.ShowToolbar;   // Show the toolbar: the transport bar (the menu row's Open / Save group follows in MainWindow.Compose)
        pane.Input.SectionAnimator.Enabled = timeline.SectionDragAnimation && !settings.Appearance.ReduceAnimations;
        view.ShowBarNumbers = timeline.ShowBarNumbers;
        view.ShowSectionNames = timeline.ShowSectionNames;
        view.ShowSectionBrackets = timeline.ShowSectionBrackets;
        view.SectionGlowIntensity = settings.Follow.SectionGlowIntensity;
        view.SectionBracketThickness = settings.Appearance.SectionBracketThickness;
        view.HideEmptyGrid = timeline.HideEmptyGrid;
        view.BarGlow = timeline.BarGlow;
        view.ShowTrackLines = timeline.ShowTrackLines;
        view.ShadeSelectedTrack = timeline.ShadeSelectedTrack;
        view.PlayheadStyle = timeline.PlayheadStyle;
        view.MasterLook = MasterLaneStrip.Look.From(timeline);
        view.MatchSimilarSectionColours = timeline.MatchSimilarSectionColours;
        pane.Header.InvalidateVisual();   // the track list reads the track lines and the selected shade from the timeline

        // The rows that change the pane's height: the header column is measured from the timeline's height, so it reloads when that moved.
        var heightBefore = view.Height;
        var naturalBefore = pane.NaturalHeight;
        view.BaseRowHeight = UiMetrics.Row(timeline.TrackRowHeight, settings.Appearance);   // lane rows and track list rows both read this one height
        pane.AddTrackLaneShown = timeline.ShowAddTrackLane;
        view.MasterLaneShown = timeline.ShowMasterTrack;
        var heightChanged = heightBefore != view.Height || naturalBefore != pane.NaturalHeight;
        if (heightChanged) pane.Header.Reload(pane.Document.Project);

        SetZoom(pane, timeline.ZoomFactor);
        if (force || timeline.TrackListWidth != _trackListWidth) pane.SetTrackListWidth(TrackListWidthRules.ResolveSaved(timeline.TrackListWidth, settings.Appearance));
        return heightChanged;
    }
}
