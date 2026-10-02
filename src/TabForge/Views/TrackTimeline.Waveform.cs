using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using TabForge.Audio;
using TabForge.Audio.Contracts;
using TabForge.Models;
using TabForge.Services;
using TabForge.Visualization;

namespace TabForge.Views;

// TrackTimeline waveform: owns drawing a clip's outline for the visible span only (plus one viewport of margin each side), cached per clip.
// Does not own the peak data (WaveformCache) or the clip box (DrawAudioLane). Tests: TestClipWaveformSpan.
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
    private double _waveFrom = double.NaN, _waveTo = double.NaN;

    /// <summary>Waveform columns built by the last draw (test probe: stays within the viewport plus margins, not the clip length).</summary>
    internal int WaveColumnsBuilt { get; private set; }

    /// <summary>The scrolled-in part of the timeline (content x and width); 0 width = unknown, the whole clip is drawn.</summary>
    internal void SetViewport(double from, double width)
    {
        _viewFrom = from; _viewWidth = width;
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
        if (cache.Geometry is null || !ReferenceEquals(cache.Peaks, peaks) || cache.Offset != clip.OffsetSec || cache.Length != clip.SourceLengthSec ||
            cache.Width != box.Width || cache.Height != box.Height || cache.Gain != gain || cache.From != from || cache.To != to || cache.BoxX != box.X)
        {
            var mid = 0.0;
            var half = box.Height / 2 - 6;
            var geometry = new StreamGeometry();
            var built = 0;
            using (var g = geometry.Open())
                for (var x = from; x < to; x += 1, built++)
                {
                    var fileFrom = clip.OffsetSec + (x - box.X) / box.Width * clip.SourceLengthSec;
                    var fileTo = clip.OffsetSec + (x + 1 - box.X) / box.Width * clip.SourceLengthSec;
                    var i0 = Math.Max(0, (int)(fileFrom / WaveformCache.SecondsPerPeak));
                    var i1 = Math.Min(peaks.Length, Math.Max(i0 + 1, (int)Math.Ceiling(fileTo / WaveformCache.SecondsPerPeak)));
                    var peak = 0f;
                    for (var i = i0; i < i1; i++) peak = Math.Max(peak, peaks[i]);
                    var h = Math.Sqrt(Math.Clamp(peak * gain, 0, 1)) * half;
                    if (h < 0.5) continue;
                    g.BeginFigure(new Point(x + 0.5, mid - h), false, false);
                    g.LineTo(new Point(x + 0.5, mid + h), true, false);
                }
            geometry.Freeze();
            WaveColumnsBuilt = built;
            cache.Peaks = peaks; cache.Offset = clip.OffsetSec; cache.Length = clip.SourceLengthSec; cache.Width = box.Width;
            cache.Height = box.Height; cache.Gain = gain; cache.From = from; cache.To = to; cache.BoxX = box.X; cache.Geometry = geometry;
        }
        dc.PushTransform(new TranslateTransform(0, box.Y + box.Height / 2 + 4));
        dc.DrawGeometry(null, Draw.Pen(colour, 1, 0.9 * alpha), cache.Geometry);
        dc.Pop();
    }
}
