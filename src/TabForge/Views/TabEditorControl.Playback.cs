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

namespace TabForge.Views;

// TabEditorControl: playhead and the retained per-system drawings.
public sealed partial class TabEditorControl
{
    // ---------- playback ----------

    public void SetPlayhead(int measure, int cell)
    {
        PlaybackMeasure = measure;
        PlaybackCell = cell;
        // Playback-only repaint: keeps the cached engraving of systems whose playback state is unchanged.
        base.InvalidateVisual();
    }

    // ---- retained per-system engraving ----
    // Every playback beat used to re-engrave all visible systems. Each engraved system is now kept as a
    // frozen drawing and replayed while its playback state (sounding/struck notes) is unchanged; the
    // system holding the playhead is always re-engraved. Any other repaint request (edits, selection,
    // cursor, hover, colours, zoom, layout) clears the cache, so output is identical to before.
    private readonly Dictionary<int, (long Signature, Drawing Drawing)> _systemDrawings = new();
    private object? _systemDrawingsLayout;
    private int _systemDrawingsDpi = -1;

    /// <summary>Requests a full repaint (drops cached system drawings).</summary>
    public new void InvalidateVisual()
    {
        _systemDrawings.Clear();
        base.InvalidateVisual();
    }

    private long SystemPlaybackSignature(ScoreSystemPosition system)
    {
        unchecked
        {
            long hash = PlaybackActive ? 17 : 3;
            int first = system.FirstMeasure, last = system.LastMeasure;
            foreach (var (bar, cell, str) in _soundingNow)
                if (bar >= first && bar <= last) hash += (bar * 7919L + cell * 131L + str + 1) * 0x9E3779B1L;
            foreach (var (bar, cell, str) in _struckNow)
                if (bar >= first && bar <= last) hash += (bar * 6151L + cell * 257L + str + 1) * 0x85EBCA77L;
            // A frozen copy that holds (or lacks) the playing-bar band must not outlive a change of bar, colour or opacity.
            if (Track is { } track && PlayingBarMeasure(track) is var banded and >= 0 && banded >= first && banded <= last)
                hash += ((banded + 1) * 0x27D4EB2FL + PlayingBarColor.GetHashCode() * 31L + BitConverter.DoubleToInt64Bits(PlayingBarOpacity)) | 1L;
            return hash;
        }
    }

    // ---- playing-bar highlight (opt-in) ----
    // A translucent band over the whole bar that is playing, drawn behind the notes. It rides on the system engraving that
    // already re-renders when the playing bar changes (the playing system is always engraved live, every other system
    // replays its frozen drawing), so a tick inside one bar adds no work: the band's brush and rectangle are rebuilt only
    // when the bar, the colour, the opacity or the geometry changes (PlayingBarBuilds counts those rebuilds).

    /// <summary>Show a translucent band over the bar that is playing. Off by default.</summary>
    public bool PlayingBarEnabled { get => _barEnabled; set { if (_barEnabled == value) return; _barEnabled = value; InvalidateVisual(); } }
    private bool _barEnabled;
    /// <summary>Colour of the playing-bar band (its opacity is applied on top).</summary>
    public Color PlayingBarColor { get => _barColor; set { if (_barColor == value) return; _barColor = value; InvalidateVisual(); } }
    private Color _barColor = Color.FromRgb(0xFF, 0xE0, 0x66);
    /// <summary>Opacity of the band, 0..1.</summary>
    public double PlayingBarOpacity { get => _barOpacity; set { if (_barOpacity == value) return; _barOpacity = value; InvalidateVisual(); } }
    private double _barOpacity = 0.20;
    /// <summary>Also band the edit cursor's bar while playback is stopped.</summary>
    public bool PlayingBarWhenStopped { get => _barWhenStopped; set { if (_barWhenStopped == value) return; _barWhenStopped = value; InvalidateVisual(); } }
    private bool _barWhenStopped;
    /// <summary>How often the band's brush and rectangle were rebuilt (self-test).</summary>
    internal int PlayingBarBuilds { get; private set; }

    private (int Measure, Rect Rect, Color Colour, double Opacity) _barKey = (-1, Rect.Empty, default, 0);
    private System.Windows.Media.Brush? _barBrush;

    /// <summary>The bar to band right now, or -1.</summary>
    private int PlayingBarMeasure(TrackModel track)
    {
        if (!PlayingBarEnabled) return -1;
        if (PlaybackActive) return PlaybackMeasure >= 0 && PlaybackMeasure < track.Measures.Count ? PlaybackMeasure : -1;
        if (PlayingBarWhenStopped && !HideCursor && SelectedMeasure >= 0 && SelectedMeasure < track.Measures.Count) return SelectedMeasure;
        return -1;
    }

    /// <summary>
    /// The band's rectangle in score coordinates (the bar's full width, from above the first staff to below the last) and its
    /// brush, or null when nothing is banded. Rebuilds only when the inputs change.
    /// </summary>
    internal (Rect Rect, System.Windows.Media.Brush Brush)? PlayingBarBand(TrackModel track, ScoreSystemPosition system, ScoreMeasurePosition position)
    {
        if (PlayingBarMeasure(track) != position.MeasureIndex) return null;
        var staffTop = StaffTop(system.Index);
        var tabTop = TabTop(system.Index);
        var strings = Math.Max(1, track.StringTunings.Count);
        var showStaff = Notation != NotationMode.TabOnly;
        var showTab = Notation != NotationMode.StaffOnly;
        var top = (showStaff ? staffTop : tabTop) - 8;
        var bottom = (showTab ? tabTop + (strings - 1) * StringGap : staffTop + 4 * StaffGap) + 10;
        var rect = new Rect(position.X, top, position.Width, bottom - top);
        var opacity = Math.Clamp(PlayingBarOpacity, 0, 1);
        var key = (position.MeasureIndex, rect, PlayingBarColor, opacity);
        if (_barBrush is null || !_barKey.Equals(key))
        {
            var brush = new SolidColorBrush(Color.FromArgb((byte)Math.Round(255 * opacity), PlayingBarColor.R, PlayingBarColor.G, PlayingBarColor.B));
            brush.Freeze();
            _barBrush = brush;
            _barKey = key;
            PlayingBarBuilds++;
        }
        return (rect, _barBrush);
    }

    /// <summary>Self-test: where the band would sit for a bar, whether or not it is shown.</summary>
    internal Rect PlayingBarBandRectFor(int bar)
    {
        var track = Track!;
        var layout = GetScoreLayout(track);
        var position = layout.Measure(bar);
        var system = position.SystemIndex;
        var staffTop = StaffTop(system); var tabTop = TabTop(system);
        var top = (Notation != NotationMode.TabOnly ? staffTop : tabTop) - 8;
        var bottom = (Notation != NotationMode.StaffOnly ? tabTop + (Math.Max(1, track.StringTunings.Count) - 1) * StringGap : staffTop + 4 * StaffGap) + 10;
        return new Rect(position.X, top, position.Width, bottom - top);
    }

    /// <summary>The playing bar's band for self-tests and overlays: null when off or nothing plays.</summary>
    internal Rect? PlayingBarRect()
    {
        var track = Track;
        if (track is null) return null;
        var bar = PlayingBarMeasure(track);
        if (bar < 0) return null;
        var layout = GetScoreLayout(track);
        var position = layout.Measure(bar);
        return PlayingBarBand(track, layout.Systems[position.SystemIndex], position)?.Rect;
    }

    public void ClearPlayhead()
    {
        PlaybackMeasure = -1;
        PlaybackCell = -1;
        InvalidateVisual();
    }
}
