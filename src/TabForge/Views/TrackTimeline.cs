using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TabForge.Controllers;
using TabForge.Diagnostics;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Plugins;
using TabForge.Services;
using TabForge.Visualization;

namespace TabForge.Views;

/// <summary>Custom-drawn timeline: ruler, sections, one precise cell per measure per track.</summary>
internal sealed partial class TrackTimeline : FrameworkElement
{
    internal sealed record SectionDragSnapshot(SongProject Project, TimelineGeometry Geometry,
        int StartBar, int EndBar, double VerticalScrollOffset, Color Color, string Title);
    internal sealed record SectionDragPreview(SectionDragSnapshot Snapshot, double X, double InsertionX, double PointerX);

    private SongProject? _project;
    private double _measureWidth = 30;
    private TimelineGeometry? _geometry;

    public SongProject? Project
    {
        get => _project;
        set
        {
            if (ReferenceEquals(_project, value)) return;
            _project = value;
            _geometry = null;
            _sectionHitsCache = null;
            ResetActivityCache();
            EnsureTimelineGeometry();
        }
    }

    public double MeasureWidth
    {
        get => _measureWidth;
        set
        {
            if (Math.Abs(_measureWidth - value) < 0.001) return;
            _measureWidth = value;
            _geometry = null;
            _sectionHitsCache = null;
            EnsureTimelineGeometry();
        }
    }
    public int SelectedBar = -1;
    public int SelectedTrack = -1;
    public int PlayheadBar = -1;
    public int ScoreSelectionStart = -1;
    public int ScoreSelectionEnd = -1;
    public int LoopStart = -1;
    public bool AreaVisible;
    internal double BarX(int bar) => XOfBar(Math.Clamp(bar, 0, Math.Max(0, BarCount)));

    private bool _areaMoving;
    private int _areaMoveStart, _areaMoveEnd, _areaMoveTarget = -1;
    public event EventHandler<int>? AreaMoveFinished;

    public void BeginAreaMove(int start, int end)
    {
        _areaMoving = true;
        _areaMoveStart = start;
        _areaMoveEnd = end;
        _areaMoveTarget = -1;
        Focusable = true;
        Focus();
        Cursor = Cursors.SizeWE;
    }

    private void FinishAreaMove(int target)
    {
        _areaMoving = false;
        _areaMoveTarget = -1;
        Cursor = null;
        InvalidateVisual();
        AreaMoveFinished?.Invoke(this, target);
    }

    // Insert-before position nearest the pointer (bar boundary).
    private int AreaMoveTargetAt(double x)
    {
        var bar = BarAt(x);
        return x - XOfBar(bar) > (XOfBar(bar + 1) - XOfBar(bar)) / 2 ? bar + 1 : bar;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_areaMoving && e.Key == Key.Escape) { FinishAreaMove(-1); e.Handled = true; }
    }
    public IReadOnlyList<(int Start, int End)> SkipRanges = Array.Empty<(int, int)>();
    public int LoopEnd = -1;
    /// <summary>Draw the loop region overlay only when loop playback is actually enabled; an
    /// always-on blue box with no playback meaning was confusing.</summary>
    public bool LoopEnabled;
    public bool ShowIndividualNotes;
    public bool ShowContinuousBlocks;
    public bool HideEmptyTimelineGrid = true;
    public bool ShowBarGlow = true;
    public double SectionGlowIntensity = 0.45;
    public bool ShowSectionNames = true;
    public bool ShowBarNumbers = true;
    public bool MatchSimilarSectionColours = true;

    /// <summary>
    /// True when two colours would read as "the same" at a glance: close hue with similar brightness
    /// (or both nearly grey and similarly bright). Exact equality misses near-duplicates.
    /// </summary>
    public static string SectionFamily(string? title) => SectionColours.Family(title);

    public bool AnimateSectionDragging = true;

    public event EventHandler<int>? BarClicked;
    public event EventHandler<int>? TrackClicked;
    /// <summary>A plain click (no drag past the threshold) on the grid, ruler, section lane or a lane: clears the selection.</summary>
    public event EventHandler? PlainClicked;
    public event EventHandler<(int start, int end)>? RangeDragged;
    public event EventHandler<(int from, int to)>? TrackReordered;
    /// <summary>A track drag has started moving (the model is untouched until the drop).</summary>
    public event EventHandler<(int from, int insertBefore)>? SectionReordered;
    public event EventHandler? SectionDragStarted;
    public event EventHandler? SectionDragCancelled;
    public event EventHandler<SectionDragPreview?>? SectionDragPreviewChanged;
    public event EventHandler<int>? SectionContextRequested;
    public event EventHandler<(int bar, int track)>? ContextRequested;
    public event EventHandler<(int from, int to, double deltaY, bool active)>? TrackDragPreviewChanged;

    private readonly VisualTheme _theme = new();
    private int _dragStartBar = -1;
    private bool _dragging;
    private Point _dragStart;
    private long _dragStartTicks;
    /// <summary>A range drag needs the button held this long: a click made while the hand is still moving is a click.</summary>
    private const int RangeDragHoldMs = 140;
    private int _dragMode;              // 0 = undecided, 1 = bar range, 2 = track reorder
    private int _dragTrackFrom = -1;
    private int _dragTrackTo = -1;
    private double[] _trackPreviewOffsets = Array.Empty<double>();
    private bool _renderingDragPreview;
    private TimeSpan _lastPreviewFrame;
    private int _hoverSectionIndex = -1;
    private int _pressedSectionIndex = -1;
    private int _sectionPressOriginIndex = -1;
    private bool _sectionDragging;
    // Plain drag (no Ctrl): only the section tab moves. No lane animation, no content preview, bars untouched.
    private bool _markerDragging;
    private SectionHit _markerDragHit;
    private double _markerDragX;
    private int _markerDragTargetBar = -1;
    private int _sectionDropBefore = -1;
    private bool _sectionSettling;
    private bool _renderingSectionPreview;
    private MarkerModel? _sectionDragMarker;
    private SectionDragSnapshot? _sectionDragSnapshot;
    private SectionHit[]? _sectionDragHitsSnapshot;
    private SectionHit[]? _sectionPreviewAnimationHits;
    private double _sectionDragGrabOffset;
    private double _sectionDragX;
    private TimeSpan _lastSectionPreviewFrame;
    private readonly Dictionary<MarkerModel, double> _sectionPreviewPositions = new();
    private readonly Dictionary<MarkerModel, double> _sectionPreviewTargets = new();
    private List<SectionHit>? _sectionHitsCache;
    private int _sectionDragStartBar = -1;
    private int _sectionDragEndBar = -1;
    private int _lastSectionTarget = -1;
    private int _activityGeneration;
    private ActivityCacheEntry[][]? _activityCache;
    private int[][]? _activityGenerations;
    private TrackModel[]? _activityTracks;
    /// <summary>Source/target rows of an in-progress track drag (drawn as a highlight).</summary>
    public int DragTrackFrom = -1;
    public int DragTrackTo = -1;
    public double DragTrackDeltaY;
    public double VerticalScrollOffset;

    private readonly Dictionary<int, Drawing> _dragLanes = new();
    private double _dragLaneScroll = double.NaN;
    private CacheMode? _cacheBeforeDrag;
    private bool _animatingLanes;

    /// <summary>
    /// Start of a drag/settle animation (track or section): lanes are recorded once and replayed, and the
    /// GPU texture cache is suspended because the surface changes every frame until the animation ends.
    /// </summary>
    private void BeginLaneAnimation()
    {
        if (_animatingLanes) return;
        _animatingLanes = true;
        _dragLanes.Clear();
        _cacheBeforeDrag = CacheMode;
        CacheMode = null;
    }

    private void EndLaneAnimation()
    {
        if (!_animatingLanes) return;
        _animatingLanes = false;
        _dragLanes.Clear();
        ReleaseLaneVisuals();
        InvalidateVisual();
        CacheMode = _cacheBeforeDrag;
        _cacheBeforeDrag = null;
    }

    private int _laneAnimToken;

    /// <summary>True while the lanes glide after an order change (self-test hook).</summary>
    internal bool IsAnimatingLanes => _animatingLanes;

    /// <summary>
    /// After an order change: each lane glides from where it was (<paramref name="deltas"/>[track] = old top - new top) to its
    /// place. Uses the same retained lane visuals as a drag (one transform animation per lane, no per-frame relayout).
    /// </summary>
    public void AnimateLanes(double[] deltas, double durationMs)
    {
        if (Project is null || durationMs <= 0 || DragTrackFrom >= 0 || _animatingLanes || !deltas.Any(d => Math.Abs(d) >= 0.5)) return;
        BeginLaneAnimation();
        InvalidateVisual();
        UpdateLayout();   // renders the lanes into their retained visuals
        if (_laneLayer is null) { EndLaneAnimation(); return; }
        var token = ++_laneAnimToken;
        for (var i = 0; i < deltas.Length && i < _laneVisuals.Count; i++)
        {
            if (Math.Abs(deltas[i]) < 0.5) continue;
            _laneVisuals[i].Shift.BeginAnimation(TranslateTransform.YProperty,
                new System.Windows.Media.Animation.DoubleAnimation(deltas[i], 0, TimeSpan.FromMilliseconds(durationMs)) { EasingFunction = ArrangementPanel.OrderEase });
        }
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(durationMs + 40) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (token == _laneAnimToken && DragTrackFrom < 0) EndLaneAnimation();
        };
        timer.Start();
    }

    public void SetDragPreview(int from, int to, double deltaY)
    {
        if (from >= 0 && DragTrackFrom < 0) BeginLaneAnimation();
        else if (from < 0 && DragTrackFrom >= 0) EndLaneAnimation();
        DragTrackFrom = from;
        DragTrackTo = to;
        DragTrackDeltaY = deltaY;
        if (from < 0)
        {
            if (_renderingDragPreview) CompositionTarget.Rendering -= AdvanceDragPreview;
            _renderingDragPreview = false;
            _lastPreviewFrame = TimeSpan.Zero;
            Array.Clear(_trackPreviewOffsets);
        }
        else
        {
            if (_trackPreviewOffsets.Length != (Project?.Tracks.Count ?? 0))
                _trackPreviewOffsets = new double[Project?.Tracks.Count ?? 0];
            if (!_renderingDragPreview)
            {
                CompositionTarget.Rendering += AdvanceDragPreview;
                _renderingDragPreview = true;
            }
        }
        if (!TryShiftLanesOnly()) InvalidateVisual();
    }

    private int _shiftedFrom = -1;

    // Mid-drag frames only move lanes: update the retained lane transforms and redraw the small overlay
    // (insertion caret, loop box) without re-running OnRender or the layout pass behind it.
    private bool TryShiftLanesOnly()
    {
        if (!_animatingLanes || _laneLayer is null || Project is null) return false;
        var trackCount = Project.Tracks.Count;
        if (DragTrackFrom < 0 || DragTrackFrom != _shiftedFrom || _laneVisuals.Count != trackCount) return false;
        for (var drawIndex = 0; drawIndex < trackCount; drawIndex++)
        {
            var t = DragTrackFrom < trackCount && drawIndex == trackCount - 1 ? DragTrackFrom
                : DragTrackFrom < trackCount && drawIndex >= DragTrackFrom ? drawIndex + 1 : drawIndex;
            var shift = _laneVisuals[drawIndex].Shift;
            var y = TrackPreviewTranslation(t);
            if (shift.Y != y) shift.Y = y;
        }
        using (Draw.UseDpi(this))
        using (var dc = OverlayVisual.RenderOpen()) DrawLaneOverlay(dc);
        return true;
    }

    private void AdvanceDragPreview(object? sender, EventArgs e)
    {
#if DEBUG
        using var performance = RenderPerformance.Measure(RenderPerformance.PerformanceCategory.Drag);
#endif
        if (DragTrackFrom < 0 || Project is null) return;
        var now = (e as RenderingEventArgs)?.RenderingTime ?? TimeSpan.Zero;
        var elapsed = _lastPreviewFrame == TimeSpan.Zero ? 1.0 / 60.0 : Math.Clamp((now - _lastPreviewFrame).TotalSeconds, 0, 0.05);
        _lastPreviewFrame = now;
        var smoothing = 1 - Math.Exp(-elapsed / 0.025);
        var moved = false;
        var settled = true;
        for (var i = 0; i < _trackPreviewOffsets.Length; i++)
        {
            var target = i == DragTrackFrom ? DragTrackDeltaY :
                DragTrackFrom < DragTrackTo && i > DragTrackFrom && i <= DragTrackTo ? -MovingRowHeight :
                DragTrackFrom > DragTrackTo && i >= DragTrackTo && i < DragTrackFrom ? MovingRowHeight : 0;
            if (i == DragTrackFrom) _trackPreviewOffsets[i] = target;
            else
            {
                var difference = target - _trackPreviewOffsets[i];
                if (Math.Abs(difference) > 0.25)
                {
                    _trackPreviewOffsets[i] += difference * smoothing;
                    moved = true;
                    settled = false;
                }
                else
                {
                    if (Math.Abs(difference) > 0.001) moved = true;
                    _trackPreviewOffsets[i] = target;
                }
            }
        }
        if (moved && !TryShiftLanesOnly()) InvalidateVisual();
        if (settled)
        {
            CompositionTarget.Rendering -= AdvanceDragPreview;
            _renderingDragPreview = false;
            _lastPreviewFrame = TimeSpan.Zero;
        }
    }

    private int BarCount => Project is null ? 0 : EnsureTimelineGeometry().BarCount;

    private double WidthOfBar(int bar)
        => Project is null ? MeasureWidth : EnsureTimelineGeometry().WidthOfBar(bar);

    /// <summary>Width of one bar (used by the playhead overlay).</summary>
    public double BarWidthOf(int bar) => WidthOfBar(bar);

    public double XOfBar(int bar)
    {
        if (Project is null) return bar * MeasureWidth;
        return EnsureTimelineGeometry().XOfBar(bar);
    }

    public double TotalWidth => Project is null ? 0 : EnsureTimelineGeometry().TotalWidth;

    internal TimelineGeometry Geometry => EnsureTimelineGeometry();

    private TimelineGeometry EnsureTimelineGeometry()
    {
        if (_geometry is not null && Project is not null) return _geometry;
        if (Project is null) throw new InvalidOperationException("Timeline geometry requires a project.");
        _geometry = new TimelineGeometry(Project, MeasureWidth);
        return _geometry;
    }

    /// <summary>Revalidates low-frequency project structure without rebuilding unchanged widths.</summary>
    public void ValidateTimelineGeometry()
    {
        if (Project is null)
        {
            _geometry = null;
            return;
        }
        if (_geometry is null || !_geometry.Matches(Project, MeasureWidth))
        {
            _geometry = new TimelineGeometry(Project, MeasureWidth);
            _sectionHitsCache = null;
        }
        ValidateActivityCache();
    }

    public void RebuildTimelineGeometry()
    {
        _geometry = Project is null ? null : new TimelineGeometry(Project, MeasureWidth);
        _sectionHitsCache = null;
    }

    private void ResetActivityCache()
    {
        _dragLanes.Clear();
        _activityCache = null;
        _activityGenerations = null;
        _activityTracks = null;
        _activityGeneration++;
    }

    private void ValidateActivityCache()
    {
        var project = Project;
        if (project is null) { ResetActivityCache(); return; }
        var valid = _activityTracks is not null && _activityCache is not null &&
                    _activityGenerations is not null && _activityTracks.Length == project.Tracks.Count;
        if (valid)
        {
            for (var track = 0; track < project.Tracks.Count; track++)
                if (!ReferenceEquals(_activityTracks![track], project.Tracks[track]) ||
                    _activityCache![track].Length != project.Tracks[track].Measures.Count)
                {
                    valid = false;
                    break;
                }
        }
        if (valid) return;

        _activityTracks = new TrackModel[project.Tracks.Count];
        _activityCache = new ActivityCacheEntry[project.Tracks.Count][];
        _activityGenerations = new int[project.Tracks.Count][];
        for (var track = 0; track < project.Tracks.Count; track++)
        {
            var model = project.Tracks[track];
            _activityTracks[track] = model;
            _activityCache[track] = new ActivityCacheEntry[model.Measures.Count];
            _activityGenerations[track] = new int[model.Measures.Count];
            Array.Fill(_activityGenerations[track], ++_activityGeneration);
        }
    }

    public void InvalidateActivities(int trackIndex, int firstBar, int lastBar)
    {
        ValidateActivityCache();
        if (_activityCache is null || _activityGenerations is null ||
            trackIndex < 0 || trackIndex >= _activityCache.Length || _activityCache[trackIndex].Length == 0)
            return;
        var start = Math.Clamp(firstBar, 0, _activityCache[trackIndex].Length);
        var end = Math.Clamp(lastBar, 0, _activityCache[trackIndex].Length - 1);
        if (end < start) return;
        var generation = ++_activityGeneration;
        for (var bar = start; bar <= end; bar++) _activityGenerations[trackIndex][bar] = generation;
    }

    public void InvalidateActivities()
    {
        _dragLanes.Clear();
        ResetActivityCache();
        ValidateActivityCache();
    }
}
