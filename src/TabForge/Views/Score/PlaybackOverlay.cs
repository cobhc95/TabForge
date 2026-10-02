using System.Windows;
using System.Windows.Media;
using TabForge.Models;
using TabForge.Playback;

namespace TabForge.Views.Score;

/// <summary>What the playback overlay reads from the editor that owns it.</summary>
internal interface IPlaybackOverlayHost : IScorePageHost
{

    /// <summary>Repaints without dropping the retained system drawings.</summary>
    void RepaintPlaybackOnly();
}

/// <summary>
/// The playback state of the score and the geometry derived from it: the playhead position, the timeline whose notes are
/// highlighted, the sets of sounding and freshly struck notes, and the playing-bar band. It is the editor's playback surface.
/// </summary>
internal sealed class PlaybackOverlay : IScorePlayhead
{
    private readonly IPlaybackOverlayHost _host;

    public PlaybackOverlay(IPlaybackOverlayHost host) => _host = host;

    public int Measure { get; set; } = -1;
    public int Cell { get; set; } = -1;
    public ScoreTimeline? Timeline { get; set; }
    public int TrackIndex { get; set; }
    public double Ms { get; set; }
    public double Fraction { get; set; }
    public int[]? BarRemap { get; set; }

    private bool _active;

    /// <summary>True while the transport runs. Start and stop only (never per tick): repaint so no frozen system keeps the playing-bar band or the dimmed cursor.</summary>
    public bool Active
    {
        get => _active;
        set { if (_active == value) return; _active = value; _host.RepaintAll(); }
    }

    /// <summary>Notes sounding at the current time, as (bar, cell, string).</summary>
    public HashSet<(int bar, int cell, int s)> Sounding { get; } = new();

    /// <summary>Notes struck within the last 130 ms.</summary>
    public HashSet<(int bar, int cell, int s)> Struck { get; } = new();

    /// <summary>Forgets the timeline and the bar map (the document is gone).</summary>
    public void Release()
    {
        Timeline = null;
        BarRemap = null;
        Sounding.Clear();
        Struck.Clear();
    }

    // ---- IScorePlayhead ----

    bool IScorePlayhead.Active { get => Active; set => Active = value; }
    void IScorePlayhead.Bind(ScoreTimeline? timeline, int[]? barRemap, int trackIndex) { Timeline = timeline; BarRemap = barRemap; TrackIndex = trackIndex; }
    void IScorePlayhead.Clear() => Clear();
    void IScorePlayhead.SetPlayhead(int measure, int cell) => SetPlayhead(measure, cell);
    double IScorePlayhead.Fraction { set => Fraction = value; }
    double IScorePlayhead.Ms { set => Ms = value; }
    bool IScorePlayhead.NeedsRepaint(double fromMs, double toMs) => NeedsRepaint(fromMs, toMs);
    (double X, double Top, double Bottom)? IScorePlayhead.PlayheadGeometry() => PlayheadGeometry();
    IReadOnlyList<(double X, double EndX, double Top, double Bottom)> IScorePlayhead.PlaybackDurationGeometries() => DurationGeometries();
    (int SystemIndex, double SystemLeft, double SystemRight, double PlayheadX, double BarWidth)? IScorePlayhead.PlaybackHorizontalGeometry(int measure, double fraction) => HorizontalGeometry(measure, fraction);

    public void SetPlayhead(int measure, int cell)
    {
        Measure = measure;
        Cell = cell;
        // Playback-only repaint: keeps the cached engraving of systems whose playback state is unchanged.
        _host.RepaintPlaybackOnly();
    }

    public void Clear()
    {
        Measure = -1;
        Cell = -1;
        _host.RepaintAll();
    }

    // ---- sounding notes (canonical timeline, binary search) ----

    /// <summary>Rebuilds the sounding and struck sets for the current time. Shared with the fretboard (NoteTimeline) so the score and the fretboard always agree.</summary>
    public void RebuildSoundingSets()
    {
        Sounding.Clear();
        Struck.Clear();
        var timeline = Timeline;
        if (timeline is null || Ms < 0 || (Ms == 0 && !Active)) return; // a note at exactly 0 sounds at the very start of playback
        var notes = timeline.NotesFor(TrackIndex);
        if (notes.Length == 0) return;

        foreach (var n in NoteTimeline.SoundingAt(notes, Ms))
            Sounding.Add((MapBar(n.Bar), n.Cell, n.StringIndex));
        foreach (var n in NoteTimeline.StruckWithin(notes, Ms, 130))
            Struck.Add((MapBar(n.Bar), n.Cell, n.StringIndex));
    }

    private int MapBar(int sourceBar)
    {
        var remap = BarRemap;
        return remap is not null && sourceBar >= 0 && sourceBar < remap.Length ? remap[sourceBar] : sourceBar;
    }

    /// <summary>
    /// True when the sounding-note overlay must be repainted between two musical times, i.e. a note starts or ends. This keeps
    /// the highlight exactly as long as the note sounds without repainting the whole score page on every tick.
    /// </summary>
    public bool NeedsRepaint(double fromMs, double toMs)
    {
        var notes = Timeline?.NotesFor(TrackIndex);
        if (notes is null) return true;
        return NoteTimeline.AnyBoundaryBetween(notes, fromMs, toMs);
    }

    /// <summary>The system's playback signature: a retained drawing is replayed only while it is unchanged.</summary>
    public long SystemSignature(ScoreSystemPosition system)
    {
        unchecked
        {
            long hash = Active ? 17 : 3;
            int first = system.FirstMeasure, last = system.LastMeasure;
            foreach (var (bar, cell, str) in Sounding)
                if (bar >= first && bar <= last) hash += (bar * 7919L + cell * 131L + str + 1) * 0x9E3779B1L;
            foreach (var (bar, cell, str) in Struck)
                if (bar >= first && bar <= last) hash += (bar * 6151L + cell * 257L + str + 1) * 0x85EBCA77L;
            // A frozen copy that holds (or lacks) the playing-bar band must not outlive a change of bar, colour or opacity.
            var appearance = _host.Appearance;
            if (_host.Track is { IsAudio: false } track && PlayingBarMeasure(track) is var banded and >= 0 && banded >= first && banded <= last)
                hash += ((banded + 1) * 0x27D4EB2FL + appearance.PlayingBarColor.GetHashCode() * 31L + BitConverter.DoubleToInt64Bits(appearance.PlayingBarOpacity)) | 1L;
            return hash;
        }
    }

    // ---- geometry ----

    private double Top(int system, bool showStaff) => (showStaff ? _host.StaffTop(system) : _host.TabTop(system)) - 12;

    private double Bottom(TrackModel track, int system, bool showTab)
        => showTab ? _host.TabTop(system) + (Math.Max(1, track.StringTunings.Count) - 1) * _host.StringGap + 14 : _host.StaffTop(system) + 4 * _host.StaffGap + 14;

    /// <summary>Playback caret geometry in the editor's own coordinates. Null when there is no playback position.</summary>
    public (double X, double Top, double Bottom)? PlayheadGeometry()
    {
        var track = _host.Track;
        if (!Active || track is null || track.IsAudio || Measure < 0 || Cell < 0) return null;   // an audio track has no notation to follow
        if (Measure >= track.Measures.Count) return null;
        var layout = _host.Layout;
        var measurePosition = layout.GetLayout(track).Measure(Measure);
        var system = measurePosition.SystemIndex;
        var caretWarp = layout.WarpFor(track, Measure);
        var x = measurePosition.X + caretWarp.Fraction(Math.Clamp(Fraction, 0, 1) * caretWarp.Slots) * measurePosition.Width;
        var zoom = _host.Zoom;
        return (x * zoom, Top(system, _host.Notation != NotationMode.TabOnly) * zoom, Bottom(track, system, _host.Notation != NotationMode.StaffOnly) * zoom);
    }

    /// <summary>
    /// Full score geometry for the active note/chord event. Its bounds are derived from the canonical event's fixed onset/end times,
    /// not the moving playhead, and are split across score systems when a tied or sustained event crosses bar boundaries.
    /// </summary>
    public IReadOnlyList<(double X, double EndX, double Top, double Bottom)> DurationGeometries()
    {
        var track = _host.Track;
        var timeline = Timeline;
        if (!Active || track is null || track.IsAudio || timeline is null)
            return Array.Empty<(double, double, double, double)>();

        var notes = timeline.NotesFor(TrackIndex);
        if (notes.Length == 0) return Array.Empty<(double, double, double, double)>();
        var lookback = Math.Max(6000, timeline.LongestSoundingNoteMs + 1);
        var sounding = NoteTimeline.SoundingAt(notes, Ms, lookback);
        if (sounding.Count == 0) return Array.Empty<(double, double, double, double)>();

        // Chord notes may have staggered attacks. Keep the block anchored to the newest active score
        // event, while including every string in that cell/voice and performed bar in its full span.
        var active = sounding.OrderByDescending(note => note.OnsetMs).First();
        var playedBarStart = timeline.BarAt(active.OnsetMs).StartMs;
        // Notes are sorted by onset, so the chord's notes sit next to the active one: scan only its
        // neighbourhood instead of the whole track every frame.
        var activeIndex = Array.IndexOf(notes, active);
        var chordList = new List<NoteEvent>(8);
        for (var i = Math.Max(0, activeIndex < 0 ? 0 : activeIndex - 64); i < notes.Length; i++)
        {
            var note = notes[i];
            if (activeIndex >= 0 && note.OnsetMs > active.OnsetMs + 1) break;
            if (note.Bar == active.Bar && note.Cell == active.Cell && note.VoiceIndex == active.VoiceIndex &&
                Math.Abs(timeline.BarAt(note.OnsetMs).StartMs - playedBarStart) < 0.5)
                chordList.Add(note);
        }
        var chord = chordList.ToArray();
        if (chord.Length == 0) chord = new[] { active };

        var startMs = chord.Min(note => note.OnsetMs);
        var endMs = chord.Max(note => note.EndMs);
        if (endMs <= startMs || timeline.Bars.Count == 0)
            return Array.Empty<(double, double, double, double)>();

        var engine = _host.Layout;
        var layout = engine.GetLayout(track);
        var showTab = _host.Notation != NotationMode.StaffOnly;
        var showStaff = _host.Notation != NotationMode.TabOnly;
        var zoom = _host.Zoom;
        var geometries = new List<(double X, double EndX, double Top, double Bottom)>();
        foreach (var bar in timeline.Bars)
        {
            var segmentStart = Math.Max(startMs, bar.StartMs);
            var segmentEnd = Math.Min(endMs, bar.EndMs);
            if (segmentEnd <= segmentStart + 0.001 || bar.Bar < 0 || bar.Bar >= track.Measures.Count || bar.Slots <= 0)
                continue;

            var measurePosition = layout.Measure(bar.Bar);
            var startSlot = bar.SlotFraction(segmentStart) * bar.Slots;
            var endSlot = bar.SlotFraction(segmentEnd) * bar.Slots;
            if (endSlot <= startSlot + 0.001) continue;

            var system = measurePosition.SystemIndex;
            var durationWarp = engine.WarpFor(track, bar.Bar);
            var left = measurePosition.X + durationWarp.Fraction(startSlot / bar.Slots * durationWarp.Slots) * measurePosition.Width;
            var right = measurePosition.X + durationWarp.Fraction(endSlot / bar.Slots * durationWarp.Slots) * measurePosition.Width;
            geometries.Add((left * zoom, right * zoom, Top(system, showStaff) * zoom, Bottom(track, system, showTab) * zoom));
        }
        return geometries;
    }

    /// <summary>Horizontal playback geometry in the scaled score-page coordinate space.</summary>
    public (int SystemIndex, double SystemLeft, double SystemRight, double PlayheadX, double BarWidth)? HorizontalGeometry(int measure, double fraction)
    {
        var track = _host.Track;
        if (track is null || track.Measures.Count == 0) return null;
        measure = Math.Clamp(measure, 0, track.Measures.Count - 1);
        var engine = _host.Layout;
        var layout = engine.GetLayout(track);
        var bar = layout.Measure(measure);
        var system = layout.Systems[bar.SystemIndex];
        fraction = double.IsFinite(fraction) ? Math.Clamp(fraction, 0, 1) : 0;
        // Time fraction -> spaced position (notes are spaced by duration, not uniformly).
        var warp = engine.WarpFor(track, measure);
        var spaced = warp.Fraction(fraction * warp.Slots);
        var zoom = _host.Zoom;
        return (system.Index, system.X * zoom, (system.X + system.Width) * zoom,
            (bar.X + spaced * bar.Width) * zoom, bar.Width * zoom);
    }

    // ---- playing-bar highlight (opt-in) ----
    // A translucent band over the whole bar that is playing, drawn behind the notes. It rides on the system engraving that
    // already re-renders when the playing bar changes (the playing system is always engraved live, every other system
    // replays its frozen drawing), so a tick inside one bar adds no work: the band's brush and rectangle are rebuilt only
    // when the bar, the colour, the opacity or the geometry changes (PlayingBarBuilds counts those rebuilds).

    /// <summary>How often the band's brush and rectangle were rebuilt (self-test).</summary>
    public int PlayingBarBuilds { get; private set; }

    private (int Measure, Rect Rect, Color Colour, double Opacity) _barKey = (-1, Rect.Empty, default, 0);
    private Brush? _barBrush;

    /// <summary>The bar to band right now, or -1.</summary>
    public int PlayingBarMeasure(TrackModel track)
    {
        var appearance = _host.Appearance;
        if (!appearance.PlayingBarEnabled) return -1;
        if (Active) return Measure >= 0 && Measure < track.Measures.Count ? Measure : -1;
        var selected = _host.SelectedMeasure;
        if (appearance.PlayingBarWhenStopped && !_host.HideCursor && selected >= 0 && selected < track.Measures.Count) return selected;
        return -1;
    }

    private Rect BandRect(TrackModel track, ScoreMeasurePosition position)
    {
        var system = position.SystemIndex;
        var staffTop = _host.StaffTop(system);
        var tabTop = _host.TabTop(system);
        var strings = Math.Max(1, track.StringTunings.Count);
        var top = (_host.Notation != NotationMode.TabOnly ? staffTop : tabTop) - 8;
        var bottom = (_host.Notation != NotationMode.StaffOnly ? tabTop + (strings - 1) * _host.StringGap : staffTop + 4 * _host.StaffGap) + 10;
        return new Rect(position.X, top, position.Width, bottom - top);
    }

    /// <summary>
    /// The band's rectangle in score coordinates (the bar's full width, from above the first staff to below the last) and its
    /// brush, or null when nothing is banded. Rebuilds only when the inputs change.
    /// </summary>
    public (Rect Rect, Brush Brush)? PlayingBarBand(TrackModel track, ScoreSystemPosition system, ScoreMeasurePosition position)
    {
        if (PlayingBarMeasure(track) != position.MeasureIndex) return null;
        var rect = BandRect(track, position);
        var appearance = _host.Appearance;
        var opacity = Math.Clamp(appearance.PlayingBarOpacity, 0, 1);
        var key = (position.MeasureIndex, rect, appearance.PlayingBarColor, opacity);
        if (_barBrush is null || !_barKey.Equals(key))
        {
            var brush = new SolidColorBrush(Color.FromArgb((byte)Math.Round(255 * opacity), appearance.PlayingBarColor.R, appearance.PlayingBarColor.G, appearance.PlayingBarColor.B));
            brush.Freeze();
            _barBrush = brush;
            _barKey = key;
            PlayingBarBuilds++;
        }
        return (rect, _barBrush);
    }

    /// <summary>Self-test: where the band would sit for a bar, whether or not it is shown.</summary>
    public Rect BandRectFor(int bar)
    {
        var track = _host.Track!;
        return BandRect(track, _host.Layout.GetLayout(track).Measure(bar));
    }

    /// <summary>The playing bar's band for self-tests and overlays: null when off or nothing plays.</summary>
    public Rect? PlayingBarRect()
    {
        var track = _host.Track;
        if (track is null) return null;
        var bar = PlayingBarMeasure(track);
        if (bar < 0) return null;
        var layout = _host.Layout.GetLayout(track);
        var position = layout.Measure(bar);
        return PlayingBarBand(track, layout.Systems[position.SystemIndex], position)?.Rect;
    }
}
