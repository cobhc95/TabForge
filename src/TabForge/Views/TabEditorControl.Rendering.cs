using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TabForge.Diagnostics;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Services;
using RenderDraw = TabForge.Visualization.Draw;
using TabForge.Views.Score;

namespace TabForge.Views;

// Owns: the score's draw loop: which systems are engraved for the viewport, the per-system draw with failure containment
//   (DrawSystemContained: one bad mark shows a short note and the other systems still draw), the drawing-error counters, and the
//   Band lane's horizontal slide.
// Does not own: the engraving of individual marks (Views/Score/ScoreRenderer.*) and the playback overlay
//   (TabEditorControl.Playback.cs).
// Tests: TestTabEditorFrozenSystems.

public sealed partial class TabEditorControl
{
    // ================= rendering =================

    private Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    protected override void OnRender(DrawingContext dc)
    {
        using var slowTrace = TabForge.Views.SlowTrace.Measure("score render");
#if DEBUG
        using var performance = RenderPerformance.Measure(RenderPerformance.PerformanceCategory.Score);
#endif
        using var dpiScope = TabForge.Visualization.Draw.UseDpi(this);   // text shaped for this editor's own monitor
        base.OnRender(dc);
        dc.PushTransform(new ScaleTransform(_zoom, _zoom));
        var bg = Appearance.DarkPaper ? Appearance.DarkPaperColor : Appearance.LightPaperColor;
        var ink = Appearance.DarkPaper ? Appearance.DarkInkColor : Appearance.LightInkColor;
        ScoreText.NormalInk = ink;
        var faint = Appearance.DarkPaper ? C("#5A636F") : C("#8A8A8A");
        // Staff and string lines should guide the eye without cutting through noteheads or labels.
        var line = Appearance.DarkPaper ? Appearance.DarkStaffLineColor : Appearance.LightStaffLineColor;
        var accent = Appearance.AccentColor;
        var cursorColor = Appearance.CursorColor;
        var playColor = Appearance.PlaybackColor;
        var errorColor = Appearance.ErrorColor;

        dc.DrawRectangle(ScoreText.Brush(bg), null, new Rect(0, 0, PageWidth, Math.Max(400, ActualHeight / _zoom)));

        var track = Track;
        if (track is null || _project is null)
        {
            ScoreText.DrawCenteredIn(ScoreTextArea.Header, dc, "TabForge", HeaderCentreX, 60, 30, ScoreText.Brush(ink), FontWeights.SemiBold, "Segoe UI");
            ScoreText.DrawCenteredIn(ScoreTextArea.Header, dc, "Create or open a score", HeaderCentreX, 104, 14, ScoreText.Brush(faint));
            dc.Pop();
            return;
        }

        if (track.IsAudio)
        {
            // An audio track has no notation: one centred message instead of an empty score.
            AudioTrackPlaceholder.Draw(dc, this, _zoom, HeaderCentreX, ScoreText.Brush(Appearance.DarkPaper ? C("#9AA3AF") : C("#5F6670")));   // readable on either paper (AA for large text)
            dc.Pop();
            return;
        }
        _renderer.DrawHeader(dc, _project, track, ink, faint);

        var scoreLayout = Layout.GetLayout(track);
        var systems = scoreLayout.SystemCount;
        // One lookup per render for the exact sounding notes.
        _playback.RebuildSoundingSets();
        var scoreFacts = _layout.EnsureScoreFacts(track, _project);
        var palmMutePassages = scoreFacts.PalmMutePassages;
        var fadePassages = scoreFacts.FadePassages;
        // Engrave only the systems the viewport can show: a long score is dozens of systems, and
        // repainting all of them on every playback tick was the single biggest CPU cost.
        var (firstSystem, lastSystem) = VisibleSystems(systems);
        UpdateHorizontalBand();
        _drawnFirstSystem = firstSystem;
        _drawnLastSystem = lastSystem;
        _systemCache.Bind(scoreLayout, TabForge.Visualization.Draw.DpiKey);
        var activeSystem = _playback.Measure >= 0 && _playback.Measure < track.Measures.Count ? scoreLayout.SystemForMeasure(_playback.Measure) : -1;
        for (var s = firstSystem; s <= lastSystem; s++)
        {
            var system = scoreLayout.Systems[s];
            // One-line mode draws only the visible band live (cheap); a cached drawing would hold a stale band.
            if (s == activeSystem || HorizontalScroll)
            {
                // Reads the continuous playback position (progress fill): always engraved live.
                DrawSystemContained(dc, track, system, ink, faint, line, accent, cursorColor, playColor, errorColor, palmMutePassages, fadePassages);
                _systemCache.Remove(s);
                continue;
            }
            var signature = _playback.SystemSignature(system);
            if (!_systemCache.TryGet(s, signature, out var drawing))
            {
                var group = new DrawingGroup();
                using (var recorder = group.Open())
                    DrawSystemContained(recorder, track, system, ink, faint, line, accent, cursorColor, playColor, errorColor, palmMutePassages, fadePassages);
                group.Freeze();
                drawing = group;
                _systemCache.Store(s, signature, drawing);
            }
            if (_exportRange is var (from, to) && (s < from || s > to)) { dc.PushClip(EmptyClip); dc.DrawDrawing(drawing); dc.Pop(); } else
            dc.DrawDrawing(drawing);
        }
        // Forget systems that scrolled well out of view so the cache stays small.
        _systemCache.Trim(firstSystem, lastSystem);
        dc.Pop();
    }

    /// <summary>
    /// Command-line runs (self-test, bar audit, render) set this so a drawing error still fails them loudly; the
    /// interactive app contains it per system instead (see <see cref="DrawSystemContained"/>).
    /// </summary>
    internal static bool RethrowRenderFailures;
    /// <summary>Drawing errors contained since start (self-test and diagnostics read it).</summary>
    internal static int ContainedRenderFailures;
    /// <summary>Self-test hook: throws for the system index it is given, to prove the containment.</summary>
    internal static Action<int>? RenderFaultInjection;
    private static readonly HashSet<string> LoggedRenderFailures = new(StringComparer.Ordinal);

    /// <summary>
    /// One system's engraving with the failure contained: a bug in one mark must not take the editor down or turn every
    /// repaint into an error dialog (WPF re-runs a failed render on each layout pass). The failed system shows a short
    /// note, the error is logged once per distinct cause, and the other systems draw normally.
    /// </summary>
    private void DrawSystemContained(DrawingContext dc, TrackModel track, ScoreSystemPosition system, Color ink, Color faint, Color line, Color accent, Color cursorColor, Color playColor, Color errorColor,
        IReadOnlyList<PalmMutePassage> palmMutePassages, IReadOnlyList<FadePassage> fadePassages)
    {
        if (RethrowRenderFailures)
        {
            RenderFaultInjection?.Invoke(system.Index);
            _renderer.DrawSystem(dc, track, system, ink, faint, line, accent, cursorColor, playColor, errorColor, palmMutePassages, fadePassages);
            return;
        }
        try
        {
            RenderFaultInjection?.Invoke(system.Index);
            _renderer.DrawSystem(dc, track, system, ink, faint, line, accent, cursorColor, playColor, errorColor, palmMutePassages, fadePassages);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) // Not logged: render path: no logging per frame; the fault is counted
        {
            ContainedRenderFailures++;
            var key = ex.GetType().FullName + "|" + ex.TargetSite + "|" + ex.Message;
            if (RenderFaultInjection is null && LoggedRenderFailures.Count < 32 && LoggedRenderFailures.Add(key))
            {
                System.Diagnostics.Debug.WriteLine($"Score drawing failed (system {system.Index + 1}): {ex}");
                try { DiagnosticFileService.WriteText(FilePathPolicy.DefaultDiagnosticsPath($"render-error-{DateTime.Now:yyyyMMdd-HHmmss}.log"), $"{DateTime.Now:O}{Environment.NewLine}system {system.Index + 1}{Environment.NewLine}{ex}"); }
                catch (Exception logError) { System.Diagnostics.Debug.WriteLine($"Render error log could not be written: {logError}"); } // Not logged: render path: no logging per frame; the fault is counted
            }
            try { ScoreText.DrawIn(ScoreTextArea.Header, dc, "This line could not be drawn (details in the diagnostics log).", system.X + 4, StaffTop(system.Index), 10, ScoreText.Brush(errorColor)); }
            catch (Exception noteError) { System.Diagnostics.Debug.WriteLine($"Render error note failed: {noteError}"); } // Not logged: render path: no logging per frame; the fault is counted
        }
    }

    /// <summary>A Band lane slides its whole engraved line itself: no ancestor scroll viewer decides which bars or systems are drawn.</summary>
    internal bool IgnoreAncestorViewport { get; init; }

    /// <summary>The video export shows one page of a score that sits in no scroll viewer: systems outside these (inclusive) are replayed under an empty clip,
    /// so the rasteriser skips them while every system keeps its place in the drawing (the pixels of the page do not change).</summary>
    internal (int First, int Last)? ExportSystemRange { get => _exportRange; set { _exportRange = value; base.InvalidateVisual(); } }
    private (int First, int Last)? _exportRange;

    private static readonly Geometry EmptyClip = FrozenEmptyClip();
    private static Geometry FrozenEmptyClip() { var g = new RectangleGeometry(new Rect(0, 0, 0, 0)); g.Freeze(); return g; }
    private ScrollViewer? _viewport;
    private bool _viewportHooked;
    private int _drawnFirstSystem;
    private int _drawnLastSystem = -1;

    /// <summary>The inclusive range of systems overlapping the viewport, with one system of margin.</summary>
    private (int First, int Last) VisibleSystems(int systems)
    {
        var last = systems - 1;
        if (_viewport is null)
        {
            _viewport = IgnoreAncestorViewport ? null : FindAncestorScrollViewer(this);
            if (_viewport is not null && !_viewportHooked)
            {
                _viewportHooked = true;
                // Culling only works if a scroll re-runs the render, but scrolling inside the band we
                // already drew needs no repaint at all (WPF just translates the cached visuals) - so
                // only invalidate when systems outside the drawn band come into view.
                _viewport.ScrollChanged += (_, _) =>
                {
                    var (first, final) = VisibleSystems(systems);
                    if (first < _drawnFirstSystem || final > _drawnLastSystem || HorizontalBandLeftBehind()) InvalidateVisual();
                };
            }
        }
        if (_viewport is null || _viewport.ViewportHeight <= 1)
            return (0, last);

        var top = _viewport.VerticalOffset / _zoom - SystemHeight;
        var bottom = (_viewport.VerticalOffset + _viewport.ViewportHeight) / _zoom + SystemHeight;
        var first = (int)Math.Floor((top - HeaderHeight) / SystemHeight);
        var final = (int)Math.Ceiling((bottom - HeaderHeight) / SystemHeight);
        return (Math.Max(0, first), Math.Min(last, Math.Max(0, final)));
    }

    // ---- one-line (horizontal) culling: draw about one screen either side of the view, repaint only
    // when the view scrolls past that band (the same idea as the vertical system culling above).
    private double _bandLeft = double.NegativeInfinity, _bandRight = double.PositiveInfinity;

    private void UpdateHorizontalBand()
    {
        if (!HorizontalScroll || _viewport is null || _viewport.ViewportWidth <= 1)
        {
            _bandLeft = double.NegativeInfinity; _bandRight = double.PositiveInfinity;
            return;
        }
        var view = _viewport.ViewportWidth / _zoom;
        _bandLeft = _viewport.HorizontalOffset / _zoom - view;
        _bandRight = (_viewport.HorizontalOffset + _viewport.ViewportWidth) / _zoom + view;
    }

    private bool InHorizontalBand(ScoreMeasurePosition measure) =>
        measure.X + measure.Width >= _bandLeft && measure.X <= _bandRight;

    private bool HorizontalBandLeftBehind()
    {
        if (!HorizontalScroll || _viewport is null) return false;
        var left = _viewport.HorizontalOffset / _zoom;
        var right = (_viewport.HorizontalOffset + _viewport.ViewportWidth) / _zoom;
        return left < _bandLeft || right > _bandRight;
    }

    private static ScrollViewer? FindAncestorScrollViewer(DependencyObject start)
    {
        var current = VisualTreeHelper.GetParent(start);
        while (current is not null)
        {
            if (current is ScrollViewer viewer) return viewer;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }


    private readonly StaffNotationRenderer _staff = new();
}
