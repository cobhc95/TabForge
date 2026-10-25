using System;
using System.Runtime.CompilerServices;
using Avalonia;
using TabForge.Audio;
using TabForge.Audio.Contracts;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Platform;

/// <summary>
/// The waveform outline drawn inside an audio clip of the arrangement timeline: the peak level of the clip's file, as one filled shape across the
/// clip box, centred on the lane as WPF draws it. The outline is built once per clip and replayed on every frame until the file, offset, length,
/// width, height, gain or visible span changes. Until the file has been read the clip shows its status (loading stripe, lock, warning); the timeline redraws when it is ready.
/// Files are read in the shown document media scope (the same as import and playback), so an approved folder shows its waveform.
/// Owns: the per-clip outline cache (keyed by clip and held weakly, so a closed timeline frees it) and the redraw request when a file finishes reading.
/// Does not own: the peak data (Core <c>WaveformCache</c>), the clip box (<see cref="TimelineAudioClips"/>) or the loop markers.
/// Tests: TabForge.Avalonia.Tests.TimelineAudioClipTests.
/// </summary>
public sealed partial class TimelineWaveforms
{
    private const double OutlineFillOpacity = 0.9;
    private const double HalfHeightMargin = 6;
    private const double CentreShift = 4;
    private const double MinEdge = 0.5;
    private const double ColumnCentre = 0.5;

    private sealed class Outline
    {
        public float[]? Peaks;
        public double Offset, Length, Width, Height, Gain, BoxX, From, To;
        public IUiPath? Path;
    }

    /// <summary>The zoom-scale range a cached outline stretches over instead of rebuilding; a step beyond it rebuilds (WPF's 0.2-5x).</summary>
    private const double MinZoomReuseScale = 0.2, MaxZoomReuseScale = 5;

    // Keyed by clip, weakly held: an outline lives as long as its clip, and a closed timeline releases them. Read and written on the UI thread only.
    private readonly ConditionalWeakTable<AudioClip, Outline> _outlines = new();

    /// <summary>Outlines built so far (test probe: a clip whose file is not read yet builds none).</summary>
    public int OutlinesBuilt { get; private set; }

    /// <summary>Columns built by the last outline build (test probe: stays within the viewport plus margins, not the clip length).</summary>
    public int WaveColumnsBuilt { get; private set; }

    // The scrolled-in span of the timeline (content x and width) the outlines build for, plus one viewport of margin each side;
    // width <= 0 = unknown, the whole clip builds. Set from the viewer's scroll, so a scroll inside the margin reuses the outline.
    private double _viewFrom, _viewWidth;

    /// <summary>The scrolled-in part of the timeline (content x and width); 0 width = unknown, the whole clip draws.</summary>
    internal void SetViewport(double from, double width)
    {
        _viewFrom = from;
        _viewWidth = width;
    }

    private bool _zooming;

    /// <summary>While zooming, a cached outline draws scaled along x instead of rebuilding; the settled redraw rebuilds at full quality.</summary>
    internal void SetZooming(bool zooming) => _zooming = zooming;

    private readonly IUiThread _ui;
    private readonly Action _redraw;
    private readonly Func<MediaContext> _media;
    private readonly WaveformService _service;

    /// <summary>Subscribes to the waveform reader; <paramref name="redraw"/> runs on <paramref name="ui"/> when a file finishes reading.
    /// <paramref name="media"/> gives the shown document media scope every request is made in.</summary>
    public TimelineWaveforms(IUiThread ui, Action redraw, Func<MediaContext> media, WaveformService service)
    {
        _service = service;
        _ui = ui;
        _redraw = redraw;
        _media = media;
        // The reader raises its event on a worker thread: the redraw is posted to the UI thread.
        service.SubscribeWeak(this, static (waveforms, _) => waveforms._ui.Post(waveforms._redraw));
    }

    /// <summary>Draws the outline of <paramref name="clip"/> inside <paramref name="box"/>, in the track colour <paramref name="colour"/> at <paramref name="opacity"/>.
    /// While the file has no peaks yet the clip shows its status instead (<paramref name="ink"/> colours the status glyphs). Only the scrolled-in span (plus margins) builds; a scroll inside the margin reuses the outline.</summary>
    public void Draw(IDrawContext d, AudioClip clip, Rect box, UiColor colour, double opacity, double songWidth = double.NaN, UiColor? ink = null)
    {
        var media = _media();
        var peaks = _service.Get(clip.File, media);
        if (peaks is null || peaks.Length == 0)
        {
            if (clip.SourceLengthSec > 0) DrawStatus(d, _service.StatusOf(clip.File, media).State, box, ink ?? colour);
            return;
        }
        if (clip.SourceLengthSec <= 0) return;
        var span = ClipGesturePresenter.WaveSpanOf(box.X, box.Right, double.IsNaN(songWidth) ? box.Right : songWidth, _viewFrom, _viewWidth);
        if (span.To <= span.From) return;
        var gain = Gain.FromDb(clip.GainDb);
        var outline = _outlines.GetOrCreateValue(clip);
        if (TryDrawScaled(d, outline, peaks, clip, box, gain, colour, opacity, span)) return;
        if (!IsCurrent(outline, peaks, clip, box, gain, span)) Build(d, outline, peaks, clip, box, gain, span);
        d.PushTranslate(0, box.Y + box.Height / 2 + CentreShift);
        d.Path(colour.WithOpacity(OutlineFillOpacity * opacity), null, outline.Path!);
        d.Pop();
    }

    /// <summary>Draws the cached outline stretched along x while zooming (the swell stays readable; the settled redraw rebuilds it sharp).</summary>
    private bool TryDrawScaled(IDrawContext d, Outline outline, float[] peaks, AudioClip clip, Rect box, double gain, UiColor colour, double opacity, WaveSpan span)
    {
        if (!_zooming || outline.Path is null || !ReferenceEquals(outline.Peaks, peaks) || outline.Offset != clip.OffsetSec ||
            outline.Length != clip.SourceLengthSec || outline.Height != box.Height || outline.Gain != gain ||
            outline.Width <= 0 || outline.Width == box.Width) return false;
        var scaleX = box.Width / outline.Width;
        if (scaleX <= MinZoomReuseScale || scaleX >= MaxZoomReuseScale) return false;
        var cachedFrom = box.X + (outline.From - outline.BoxX) * scaleX;
        var cachedTo = box.X + (outline.To - outline.BoxX) * scaleX;
        if (cachedFrom > span.From || cachedTo < span.To) return false;
        d.PushTranslate(box.X, box.Y + box.Height / 2 + CentreShift);
        d.PushScale(scaleX, 1);
        d.PushTranslate(-outline.BoxX, 0);
        d.Path(colour.WithOpacity(OutlineFillOpacity * opacity), null, outline.Path);
        d.Pop(); d.Pop(); d.Pop();
        return true;
    }

    private static bool IsCurrent(Outline outline, float[] peaks, AudioClip clip, Rect box, double gain, WaveSpan span) =>
        outline.Path is not null && ReferenceEquals(outline.Peaks, peaks) && outline.Offset == clip.OffsetSec && outline.Length == clip.SourceLengthSec &&
        outline.Width == box.Width && outline.Height == box.Height && outline.Gain == gain && outline.BoxX == box.X && outline.From == span.From && outline.To == span.To;

    private void Build(IDrawContext d, Outline outline, float[] peaks, AudioClip clip, Rect box, double gain, WaveSpan span)
    {
        var half = box.Height / 2 - HalfHeightMargin;
        var loops = ClipLoop.Loops(clip);
        var period = ClipLoop.PeriodSec(clip);
        var columns = (int)(span.To - span.From);
        var heights = new double[columns];
        for (var i = 0; i < columns; i++)
        {
            var (first, end) = ClipGesturePresenter.PeakRange(span.From + i, box.X, box.Width, clip.SourceLengthSec, clip.OffsetSec, loops, period,
                WaveformService.SecondsPerPeak, peaks.Length);
            var peak = 0f;
            for (var p = first; p < end; p++) peak = Math.Max(peak, peaks[p]);
            var height = Math.Sqrt(Math.Clamp(peak * gain, 0, 1)) * half;
            heights[i] = height < MinEdge ? 0 : height;
        }
        // One filled shape (top edge left to right, bottom edge back) rather than a line per column.
        outline.Path = d.Paths.BuildPath(sink =>
        {
            sink.BeginFigure(new UiPoint(span.From + ColumnCentre, -heights[0]), true, true);
            for (var i = 1; i < columns; i++) sink.LineTo(new UiPoint(span.From + i + ColumnCentre, -heights[i]));
            for (var i = columns - 1; i >= 0; i--) sink.LineTo(new UiPoint(span.From + i + ColumnCentre, Math.Max(MinEdge, heights[i])));
        });
        WaveColumnsBuilt = columns;
        outline.Peaks = peaks; outline.Offset = clip.OffsetSec; outline.Length = clip.SourceLengthSec; outline.Width = box.Width; outline.Height = box.Height;
        outline.Gain = gain; outline.BoxX = box.X; outline.From = span.From; outline.To = span.To;
        OutlinesBuilt++;
    }
}
