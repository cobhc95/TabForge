using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using TabForge.Audio;
using TabForge.Audio.Contracts;
using TabForge.Models;
using TabForge.Services;
using TabForge.Visualization;

namespace TabForge.Views;

// Owns: a clip's waveform outline, drawn for the visible span only (plus one viewport of margin each side), cached per clip
//   (WaveCache), and the loop markers drawn on it.
// Does not own: the peak data (Audio/WaveformCache.cs) and the clip box (TrackTimeline.Clips.cs).
// Tests: TestClipWaveformSpan.

internal sealed partial class TrackTimeline
{
    private sealed class WaveGeometry
    {
        public float[]? Peaks;
        public double Offset, Length, Width, Height, Gain, From, To, BoxX;
        public StreamGeometry? Geometry;
    }

    private static readonly ConditionalWeakTable<AudioClip, WaveGeometry> WaveCache = new();
    private double _viewFrom, _viewWidth;
    // The pixel span the last render drew bar cells for (NaN: every bar was drawn).
    private double _barsFrom = double.NaN, _barsTo = double.NaN;

    /// <summary>The bars a render draws: those within one viewport either side of the visible span (all bars when no viewport is
    /// known). A long song zoomed in would otherwise send thousands of rounded cells to the render thread on every zoom step.</summary>
    private (int First, int End) DrawnBars(int bars)
    {
        if (_viewWidth <= 0 || bars == 0) { _barsFrom = _barsTo = double.NaN; return (0, bars); }
        var from = Math.Max(0, _viewFrom - _viewWidth);
        var to = _viewFrom + 2 * _viewWidth;
        _barsFrom = from <= 0 ? double.NegativeInfinity : from;
        _barsTo = to >= XOfBar(bars) ? double.PositiveInfinity : to;
        return (Math.Clamp(BarAt(from), 0, bars), Math.Clamp(BarAt(to) + 2, 0, bars));
    }
    private double _waveFrom = double.NaN, _waveTo = double.NaN;

    /// <summary>Waveform columns built by the last draw (test probe: stays within the viewport plus margins, not the clip length).</summary>
    internal int WaveColumnsBuilt { get; private set; }

    private bool _waveZooming;

    /// <summary>While zooming, a cached outline is drawn scaled along x instead of rebuilt; the settled redraw rebuilds at full quality.</summary>
    internal void SetWaveZooming(bool zooming)
    {
        if (_waveZooming == zooming) return;
        _waveZooming = zooming;
        UpdateCache();
        if (!zooming) InvalidateVisual();
    }

    /// <summary>The scrolled-in part of the timeline (content x and width); 0 width = unknown, the whole clip is drawn.</summary>
    internal void SetViewport(double from, double width)
    {
        _viewFrom = from; _viewWidth = width;
        if (width > 0 && !double.IsNaN(_barsFrom) && (from < _barsFrom || from + width > _barsTo)) { InvalidateVisual(); return; }   // scrolled past the drawn bars
        if (double.IsNaN(_waveFrom) || width <= 0) return;
        if (from < _waveFrom || from + width > _waveTo) InvalidateVisual();   // scrolled past the drawn margin
    }

    private void DrawWaveform(DrawingContext dc, AudioClip clip, Rect box, Color colour, double alpha, double width)
    {
        var peaks = WaveformCache.Get(clip.File, Media);
        if (peaks is null || peaks.Length == 0 || clip.SourceLengthSec <= 0) return;
        var from = Math.Floor(Math.Max(box.X, 0));
        var to = Math.Min(box.Right, width);
        if (_viewWidth > 0)
        {
            from = Math.Max(from, Math.Floor(_viewFrom - _viewWidth));
            to = Math.Min(to, Math.Ceiling(_viewFrom + 2 * _viewWidth));
            _waveFrom = from <= Math.Max(box.X, 0) ? double.NegativeInfinity : from + _viewWidth;
            _waveTo = to >= Math.Min(box.Right, width) ? double.PositiveInfinity : to - _viewWidth;
        }
        if (to <= from) return;
        var gain = Gain.FromDb(clip.GainDb);
        var cache = WaveCache.GetOrCreateValue(clip);
        var scaleX = 1.0;
        if (_waveZooming && cache.Geometry is not null && ReferenceEquals(cache.Peaks, peaks) && cache.Offset == clip.OffsetSec && cache.Length == clip.SourceLengthSec &&
            cache.Height == box.Height && cache.Gain == gain && cache.Width > 0 && cache.Width != box.Width)
        {
            var sx = box.Width / cache.Width;
            var cachedFrom = box.X + (cache.From - cache.BoxX) * sx;
            var cachedTo = box.X + (cache.To - cache.BoxX) * sx;
            if (sx is > 0.2 and < 5 && cachedFrom <= from && cachedTo >= to) scaleX = sx;
        }
        if (scaleX != 1.0)
        {
            dc.PushTransform(new TranslateTransform(box.X, box.Y + box.Height / 2 + 4));
            dc.PushTransform(new ScaleTransform(scaleX, 1));
            dc.PushTransform(new TranslateTransform(-cache.BoxX, 0));
            dc.DrawGeometry(Draw.Solid(colour, 0.9 * alpha), null, cache.Geometry);
            dc.Pop(); dc.Pop(); dc.Pop();
            _waveFrom = double.NaN;   // scrolling during the zoom redraws
            return;
        }
        if (cache.Geometry is null || !ReferenceEquals(cache.Peaks, peaks) || cache.Offset != clip.OffsetSec || cache.Length != clip.SourceLengthSec ||
            cache.Width != box.Width || cache.Height != box.Height || cache.Gain != gain || cache.From != from || cache.To != to || cache.BoxX != box.X)
        {
            var mid = 0.0;
            var half = box.Height / 2 - 6;
            // One filled outline (top edge left to right, bottom edge back), not one stroked line per column: a stroke under the zoom's
            // x-scale is re-widened on the render thread every frame, a filled shape scales for free and looks the same at 1 px columns.
            var loops = ClipLoop.Loops(clip);
            var period = ClipLoop.PeriodSec(clip);
            var tops = new List<Point>((int)(to - from) + 1);
            var built = 0;
            for (var x = from; x < to; x += 1, built++)
                {
                    var fileFrom = (x - box.X) / box.Width * clip.SourceLengthSec;
                    var fileTo = (x + 1 - box.X) / box.Width * clip.SourceLengthSec;
                    if (loops)   // past the end of the media the waveform repeats
                    {
                        fileFrom %= period;
                        fileTo = fileFrom + (fileTo - (x - box.X) / box.Width * clip.SourceLengthSec);
                    }
                    fileFrom += clip.OffsetSec; fileTo += clip.OffsetSec;
                    var i0 = Math.Max(0, (int)(fileFrom / WaveformCache.SecondsPerPeak));
                    var i1 = Math.Min(peaks.Length, Math.Max(i0 + 1, (int)Math.Ceiling(fileTo / WaveformCache.SecondsPerPeak)));
                    var peak = 0f;
                    for (var i = i0; i < i1; i++) peak = Math.Max(peak, peaks[i]);
                    var h = Math.Sqrt(Math.Clamp(peak * gain, 0, 1)) * half;
                    tops.Add(new Point(x + 0.5, h < 0.5 ? 0 : h));
                }
            var geometry = new StreamGeometry();
            if (tops.Count > 0)
                using (var g = geometry.Open())
                {
                    g.BeginFigure(new Point(tops[0].X, mid - tops[0].Y), true, true);
                    for (var i = 1; i < tops.Count; i++) g.LineTo(new Point(tops[i].X, mid - tops[i].Y), true, false);
                    for (var i = tops.Count - 1; i >= 0; i--) g.LineTo(new Point(tops[i].X, mid + Math.Max(0.5, tops[i].Y)), true, false);
                }
            geometry.Freeze();
            WaveColumnsBuilt = built;
            cache.Peaks = peaks; cache.Offset = clip.OffsetSec; cache.Length = clip.SourceLengthSec; cache.Width = box.Width;
            cache.Height = box.Height; cache.Gain = gain; cache.From = from; cache.To = to; cache.BoxX = box.X; cache.Geometry = geometry;
        }
        dc.PushTransform(new TranslateTransform(0, box.Y + box.Height / 2 + 4));
        dc.DrawGeometry(Draw.Solid(colour, 0.9 * alpha), null, cache.Geometry);
        dc.Pop();
    }

    /// <summary>A looping clip: a thin dashed line where each pass of the media ends (drawn over the waveform or notes, outside the cached outline).</summary>
    private void DrawLoopMarkers(DrawingContext dc, AudioClip clip, Rect box, double alpha, double width)
    {
        if (!ClipLoop.Loops(clip)) return;
        var speed = Math.Clamp(clip.Speed, 0.25, 4);
        var period = ClipLoop.PeriodSec(clip);
        var pen = Draw.DashedPen(Color.FromArgb((byte)(140 * alpha), _theme.Text.R, _theme.Text.G, _theme.Text.B), 1, 3, 3);
        var g = new StreamGeometry();
        using (var ctx = g.Open())
            for (var k = 1; k < ClipLoop.MaxPieces; k++)
            {
                var sec = clip.StartSec + k * period / speed;
                if (sec >= clip.EndSec - 1e-6) break;
                var x = ClipEndX(clip.StartSec, sec);
                if (x > Math.Min(box.Right, width)) break;
                if (x < 0 || (_viewWidth > 0 && x < _viewFrom - _viewWidth)) continue;
                ctx.BeginFigure(new Point(x, box.Y + 2), false, false);
                ctx.LineTo(new Point(x, box.Bottom - 2), true, false);
            }
        g.Freeze();
        dc.DrawGeometry(null, pen, g);
    }
}
