using TabForge.Avalonia.Views;
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using TabForge.Avalonia.Features.Recording.Presenter;
using TabForge.Avalonia.Features.Recording.View;
using TabForge.Avalonia.Platform;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Platform;

// Owns: viewport, visible-range and block colour helpers.
// Does not own: the rest of this type (see the main file).
// Tests: no dedicated test class.
public sealed partial class TimelineView
{
    /// <summary>The top of a track row in the body, with its per-row offset (the rows that move while one is dragged).</summary>
    private double RowDrawTop(int row) => RowTop(row) + (RowOffsets is { } offsets ? offsets[row] : 0);

    /// <summary>Whether a row's lane overlaps the band the track rows are drawn in; a row wholly above or below it is not drawn.</summary>
    private bool RowInBand(int row)
    {
        var top = RowDrawTop(row);
        return top + RowHeightOf(row) > TrackRowsTop && top < TrackRowsBottom;
    }

    // The bars a row loops over: every bar with no viewer, else the bars the viewer shows plus one neighbour each side
    // (a bar half off the edge still passes the per-cell viewport check). Matches the checked set exactly, so <see cref="DrawnBarCount"/> is unchanged.
    private ScrollViewer? _viewer;

    private (int First, int End) VisibleBarRange(int bars)
    {
        if (_viewer is not { } viewer || viewer.Viewport.Width <= UiTiming.TimelineMinViewportDips || bars == 0) return (0, bars);
        return (Math.Clamp(BarAtX(viewer.Offset.X) - 1, 0, bars), Math.Clamp(BarAtX(viewer.Offset.X + viewer.Viewport.Width) + 2, 0, bars));
    }

    /// <summary>The timeline's visible x-span (the viewer's offset and viewport); NaN when it sits in no viewer (the whole span shows).</summary>
    internal (double From, double Width) VisibleSpan =>
        _viewer is { } viewer && viewer.Viewport.Width > UiTiming.TimelineMinViewportDips ? (viewer.Offset.X, viewer.Viewport.Width) : (double.NaN, 0);

    /// <summary>While zooming, cached waveform outlines draw scaled along x instead of rebuilding; the settled redraw rebuilds at full quality.</summary>
    internal void SetWaveZooming(bool zooming) => Waveforms.SetZooming(zooming);

    /// <summary>Whether a bar's span overlaps the viewer's visible width (the whole body when there is no viewer).</summary>
    private bool InViewport(double x, double cellWidth)
    {
        if (_viewer is not { } viewer) return true;
        var left = viewer.Offset.X;
        return x + cellWidth >= left && x <= left + viewer.Viewport.Width;
    }

    // The body shows different bars after each horizontal scroll, and a scroll moves the viewer without redrawing the body, so each scroll redraws it.
    // The scroll also hands the viewer span to the waveform outlines, so a scroll inside their margin reuses them instead of rebuilding.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _viewer = this.FindAncestorOfType<ScrollViewer>();
        if (_viewer is not null)
        {
            Waveforms.SetViewport(_viewer.Offset.X, _viewer.Viewport.Width);
            _viewer.ScrollChanged += OnViewerScrolled;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_viewer is not null) _viewer.ScrollChanged -= OnViewerScrolled;
        _viewer = null;
    }

    private void OnViewerScrolled(object? sender, ScrollChangedEventArgs e)
    {
        if (_viewer is { } viewer) Waveforms.SetViewport(viewer.Offset.X, viewer.Viewport.Width);
        InvalidateVisual();
    }

    // The parsed track colours, keyed by the track. Each entry keeps the colour text and the fallback it was parsed from, so a changed colour
    // parses again. Ceiling: one entry per track, cleared on each Reload. Thread: UI thread only.
    private readonly Dictionary<TrackModel, (string Hex, UiColor Fallback, UiColor Colour)> _trackColours = new(ReferenceEqualityComparer.Instance);

    internal UiColor TrackColourOf(TrackModel track, UiColor fallback)
    {
        if (_trackColours.TryGetValue(track, out var cached) && cached.Hex == track.ColorHex && cached.Fallback == fallback) return cached.Colour;
        var colour = TamedTrackColour(track.ColorHex, fallback);
        _trackColours[track] = (track.ColorHex, fallback, colour);
        return colour;
    }

    // The activity summary of each bar of each track, by row and bar. Key: the measure object, the timeline generation (bumped by each Reload)
    // and the project's content revision. Ceiling: one entry per bar of each track, rebuilt on each Reload. Thread: UI thread only.
    private TimelineActivityRules.CacheEntry[][] _activityCache = Array.Empty<TimelineActivityRules.CacheEntry[]>();
    private int _activityGeneration;

    private TimelineActivityRules.Activity ActivityOf(TrackModel track, int row, int bar) =>
        TimelineActivityRules.Resolve(ref _activityCache[row][bar], track.Measures[bar], _activityGeneration, _song.ContentRevision, track.Kind == TrackKind.Drums);

    // Whether each bar of each track fills the master lane, by track and bar. Key: rebuilt by Reload, which every edit that can change bar content marks;
    // Draw reads it and never evaluates the activity rules. Ceiling: one bool per bar of each track. Thread: UI thread only.
    private bool[][] _masterLaneActivity = Array.Empty<bool[]>();

    /// <summary>Whether each bar of each track fills the master lane (a test reads it next to <see cref="TimelineActivityRules.Build"/>).</summary>
    internal bool[][] MasterLaneActivity => _masterLaneActivity;

    /// <summary>The soft white glow round a bar block: a wide faint outline and a thin brighter one.</summary>
    private void DrawBarGlow(IDrawContext d, Func<string, UiColor> ink, double x, double y, double width)
    {
        d.Rect(null, new UiStroke(ink("OverlayInk").WithOpacity(BarGlowOuterOpacity), BarGlowOuterWidth), x - 1, y - 1, width + 2, RowHeight - 4, 3);
        d.Rect(null, new UiStroke(ink("OverlayInk").WithOpacity(BarGlowInnerOpacity), BarGlowInnerWidth), x, y, width, RowHeight - 6, 2);
    }

    /// <summary>One continuous line over the bars from <paramref name="fromBar"/> up to <paramref name="toBar"/> (not included) in the continuous mode.</summary>
    private void DrawContinuousRun(IDrawContext d, int fromBar, int toBar, double top, UiColor colour) =>
        TimelineNoteMarks.DrawContinuousRun(d, BarX(fromBar) + 1, BarX(toBar) - 1, top, RowHeight, colour.WithOpacity(TimelineNoteMarks.ContinuousLineOpacity));

    /// <summary>The track colour as the timeline shows it: the Core <see cref="SectionColourPresenter.Tame"/> of the stored colour (the WPF rule), or <paramref name="fallback"/> when it does not parse.</summary>
    internal static UiColor TamedTrackColour(string hex, UiColor fallback)
    {
        if (!Color.TryParse(hex, out var c)) return fallback;
        var tamed = SectionColourPresenter.Tame(new Rgba(c.R, c.G, c.B, c.A));
        return tamed.ToUi();
    }

    /// <summary>Opacity of a plain bar block: <see cref="TimelineNoteMarks.NotesCellOpacity"/>, fading to <see cref="MutedBlockOpacity"/> with the muted-track dimming.</summary>
    private double BlockOpacity(bool muted)
    {
        var dim = Math.Clamp(MutedTrackDimPercent, 0, 100) / 100.0;
        var full = TimelineNoteMarks.NotesCellOpacity;
        return muted ? full + (MutedBlockOpacity - full) * dim : full;
    }

    private const double MutedBlockOpacity = 0.16;
}
