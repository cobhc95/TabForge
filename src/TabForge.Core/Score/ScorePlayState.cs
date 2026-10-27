using TabForge.Models;
using TabForge.Platform;
using TabForge.Playback;

namespace TabForge.Views.Score;

// Owns: the score's playing state as one value both apps build: whether it plays, the bar, beat and fraction of the playhead, the sounding and freshly struck notes of one track,
//   and the geometry of the playhead line and playing-bar band over a layout (the formulas of the playback overlay).
// Does not own: the clock (PlaybackTransport), the drawing (ScoreGeometryProbe), the timeline (ScoreTimeline).
// Tests: TabForge.Avalonia.Tests.ScorePlayStateTests.
internal sealed class ScorePlayState
{
    /// <summary>A struck note stays lit this long.</summary>
    public const double StruckWindowMs = 130;

    public bool Active { get; private set; }
    public int Measure { get; private set; } = -1;
    public int Cell { get; private set; } = -1;
    public double Fraction { get; private set; }
    public HashSet<(int bar, int cell, int s)> Sounding { get; } = new();
    public HashSet<(int bar, int cell, int s)> Struck { get; } = new();
    private NoteEvent[] _lastNotes = Array.Empty<NoteEvent>();
    private double _lastMs = -1;

    /// <summary>Playing at <paramref name="ms"/> in <paramref name="measure"/>: rebuilds the sounding and struck sets of <paramref name="trackIndex"/>.</summary>
    public void Set(ScoreTimeline timeline, int trackIndex, int measure, int cell, double fraction, double ms)
    {
        Active = true; Measure = measure; Cell = cell; Fraction = fraction;
        Sounding.Clear(); Struck.Clear();
        _lastNotes = timeline.NotesFor(trackIndex);
        _lastMs = ms;
        foreach (var n in NoteTimeline.SoundingAt(_lastNotes, ms)) Sounding.Add((n.Bar, n.Cell, n.StringIndex));
        foreach (var n in NoteTimeline.StruckWithin(_lastNotes, ms, StruckWindowMs)) Struck.Add((n.Bar, n.Cell, n.StringIndex));
        // Tie destinations and legato slide targets have no attack of their own: they light at their own beat all the same.
        NoteTimeline.ForEachContinuationAt(_lastNotes, ms, StruckWindowMs, _lightContinuation);
    }

    public ScorePlayState() => _lightContinuation = LightContinuation;

    private readonly Action<NoteContinuation, bool> _lightContinuation;

    private void LightContinuation(NoteContinuation c, bool struck)
    {
        Sounding.Add((c.Bar, c.Cell, c.StringIndex));
        if (struck) Struck.Add((c.Bar, c.Cell, c.StringIndex));
    }

    /// <summary>Moves the playhead within its beat without touching the lit-note sets.</summary>
    public void SetFraction(double fraction) => Fraction = fraction;

    /// <summary>True when a note starts or ends between two times: the only moments the lit notes need a repaint.</summary>
    public bool NeedsRepaint(double fromMs, double toMs) => NoteTimeline.AnyBoundaryBetween(_lastNotes, fromMs, toMs);

    public void Clear()
    {
        Active = false; Measure = -1; Cell = -1; Fraction = 0; _lastMs = -1;
        Sounding.Clear(); Struck.Clear();
    }

    /// <summary>The playhead line (x, top, bottom) in page coordinates, or null when stopped or the bar is not on the track.</summary>
    public (double X, double Top, double Bottom)? PlayheadLine(ScoreGeometryProbe host) => Line(host);

    private (double X, double Top, double Bottom)? Line(ScoreGeometryProbe host)
    {
        var track = host.Track;
        if (!Active || track is null || track.IsAudio || Measure < 0 || Measure >= track.Measures.Count) return null;
        var position = host.Layout.GetLayout(track).Measure(Measure);
        var warp = host.Layout.WarpFor(track, Measure);
        var slots = Math.Min(warp.Slots, host.SlotsFor(Measure));
        var x = position.X + warp.PlayheadFraction(Math.Clamp(Fraction, 0, 1) * slots) * position.Width;
        var (top, bottom) = Rows(host, track, position.SystemIndex, 12, 14, host.Appearance.PlayheadFullRow);
        return (x, top, bottom);
    }

    /// <summary>The width of the playing bar in page coordinates (the look-ahead estimate), or null when stopped or the bar is not on the track.</summary>
    public double? CurrentBarWidth(ScoreGeometryProbe host)
    {
        var track = host.Track;
        if (!Active || track is null || track.IsAudio || Measure < 0 || Measure >= track.Measures.Count) return null;
        return host.Layout.GetLayout(track).Measure(Measure).Width;
    }

    /// <summary>The band over the playing bar (the bar's full width), or null when the band is off or nothing plays.</summary>
    public (UiRect Rect, UiColor Colour)? PlayingBar(ScoreGeometryProbe host, TrackModel track, ScoreMeasurePosition position)
    {
        var appearance = host.Appearance;
        if (!appearance.PlayingBarEnabled || !Active || Measure != position.MeasureIndex) return null;
        var (top, bottom) = Rows(host, track, position.SystemIndex, 8, 10);
        var opacity = Math.Clamp(appearance.PlayingBarOpacity, 0, 1);
        return (new UiRect(position.X, top, position.Width, bottom - top), appearance.PlayingBarColor with { A = (byte)Math.Round(255 * opacity) });
    }

    // Top and bottom of the playing marks: `above` over the first row and `below` under the last row (the line and the band differ by a few DIPs).
    private static (double Top, double Bottom) Rows(ScoreGeometryProbe host, TrackModel track, int system, double above, double below, bool full = false)
    {
        var showStaff = full || host.Notation != NotationMode.TabOnly;
        var showTab = full || host.Notation != NotationMode.StaffOnly;
        var top = (showStaff ? host.StaffTop(system) : host.TabTop(system)) - above;
        var bottom = (showTab ? host.TabTop(system) + (Math.Max(1, track.StringTunings.Count) - 1) * host.StringGap : host.StaffTop(system) + 4 * host.StaffGap) + below;
        return (top, bottom);
    }
}
