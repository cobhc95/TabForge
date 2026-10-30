using System;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using TabForge.Avalonia.Platform;
using TabForge.Platform;
using TabForge.Presenters;

namespace TabForge.Avalonia.Views;

// Owns: the piano roll's live layer over the surface, repainted alone (each playback frame, each coalesced MIDI key change) so the notes under it
//       are not redrawn: the ruler's playhead triangle and the playback needle under it (setting; stopped too, at the stop position; both in the Playback highlight colour), and the piano keys lit live: the keys
//       a MIDI keyboard holds (the Accent token) and, while playing, the keys of the track's notes sounding at the playhead (PianoRollKeyPlaying).
// Does not own: where the playhead is (the surface's PlayheadBeat hook), the held keys (PianoRollKeyState), following (PianoRollView).
// Tests: TabForge.Avalonia.Tests.PianoRollGestureTests, PianoRollKeyStateTests.
internal sealed class PianoRollPlayheadLayer : Control
{
    private const double LineWidth = 1.5, BlackShare = 0.62, NameLit = 0.55, NeedleOpacity = 0.7;
    private readonly PianoRollSurface _surface;
    private readonly AvaloniaDrawContext _draw = new();
    private readonly bool[] _live = new bool[128];       // reused each paint: no allocation per frame
    private readonly bool[] _sounding = new bool[128];
    private ThemeColourCache? _theme;
    private UiColor? _playColour;   // the playback highlight colour, read once and kept: no parsing or allocation per frame

    public PianoRollPlayheadLayer(PianoRollSurface surface)
    {
        _surface = surface;
        IsHitTestVisible = false;
        ActualThemeVariantChanged += (_, _) => _playColour = null;
    }

    /// <summary>Drops the kept playback highlight colour (the Playback highlight setting or the accent changed); the next paint reads it again.</summary>
    internal void ResetColour() => _playColour = null;

    /// <summary>The colour of the needle and the ruler marker: the Playback highlight (accent, or the custom pick).</summary>
    internal UiColor PlaybackColour => _playColour ??= _surface.PlayheadColour(ActualThemeVariant == ThemeVariant.Light);

    /// <summary>The x the needle was last drawn at (NaN when hidden or off), so a test can read it.</summary>
    internal double DrawnX { get; private set; } = double.NaN;

    /// <summary>The colour the needle was last drawn in, before its translucency (null until drawn), so a test can read it.</summary>
    internal UiColor? DrawnColour { get; private set; }

    /// <summary>How many keys the last paint lit (live or sounding), so a test can read it.</summary>
    internal int LitKeys { get; private set; }

    public override void Render(DrawingContext context)
    {
        DrawnX = double.NaN;
        var d = _draw.Use(context);
        var beat = _surface.Playing() ? _surface.PlayheadBeat() : null;
        _surface.Keys.CopyTo(_live);
        if (beat is { } at) PianoRollKeyState.Sounding(_surface.Presenter.Notes, at, _sounding);
        else System.Array.Clear(_sounding);
        DrawKeys(d);
        DrawLiveNotes(d);
        DrawMarker(d);
        if (beat is { } playhead) DrawSounding(d, playhead);
        DrawNeedle(d);
    }

    // The playback needle: a thin translucent line in the marker's colour from the ruler's triangle down through the grid and the lane, at the
    // marker's own x (also stopped: it stays at the stop position, and nothing repaints it until the playhead moves). The red step-input cursor is
    // drawn again over it so it stays visible where they meet.
    private void DrawNeedle(IDrawContext d)
    {
        if (double.IsNaN(MarkerX) || !_surface.NeedleOn()) return;
        var colour = PlaybackColour.WithOpacity(NeedleOpacity);
        d.Line(new UiStroke(colour, LineWidth), MarkerX, _surface.GridTop, MarkerX, Bounds.Height);
        DrawnX = MarkerX;
        DrawnColour = PlaybackColour;
        if (!_surface.StepInput) return;
        var red = (_theme ??= new ThemeColourCache(this)).Get("CursorBrush");
        var step = _surface.XOf(_surface.StepBeat);
        d.Line(new UiStroke(red, LineWidth), step, _surface.GridTop, step, _surface.GridBottom);
    }

    /// <summary>The x of the ruler's playhead triangle as last drawn (NaN when none), and which edge arrow shows (-1 left, 1 right, 0 none).</summary>
    internal double MarkerX { get; private set; } = double.NaN;
    internal int EdgeArrow { get; private set; }

    // The playhead's triangle in the ruler, pointing down onto the grid: always (stopped too) where the playhead is, or where a drag of it is
    // going; off screen, a small arrow at the ruler's left or right edge (a click on it scrolls there).
    private void DrawMarker(IDrawContext d)
    {
        MarkerX = double.NaN;
        EdgeArrow = 0;
        if ((_surface.PlayheadPreview ?? _surface.PlayheadBeat()) is not { } beat) return;
        var colour = PlaybackColour;
        var x = _surface.XOf(beat);
        var bottom = _surface.GridTop;
        var half = PianoRollSurface.MarkerHalf;
        if (x < _surface.GridLeft || x > Bounds.Width)
        {
            EdgeArrow = x < _surface.GridLeft ? -1 : 1;
            var tip = EdgeArrow < 0 ? _surface.GridLeft + 1 : Bounds.Width - 1;
            var back = EdgeArrow < 0 ? tip + 2 * half : tip - 2 * half;
            d.Path(colour, null, d.Paths.BuildPath(s =>
            {
                s.BeginFigure(new UiPoint(tip, bottom - half - 1), filled: true, closed: true);
                s.LineTo(new UiPoint(back, bottom - 2 * half - 1));
                s.LineTo(new UiPoint(back, bottom - 1));
            }));
            return;
        }
        d.Path(colour, null, d.Paths.BuildPath(s =>
        {
            s.BeginFigure(new UiPoint(x - half, bottom - 2 * half), filled: true, closed: true);
            s.LineTo(new UiPoint(x + half, bottom - 2 * half));
            s.LineTo(new UiPoint(x, bottom));
        }));
        MarkerX = x;
    }

    /// <summary>How many recorded-so-far notes the last paint drew, so a test can read it.</summary>
    internal int LiveDrawn { get; private set; }

    // The take being recorded: translucent boxes in the track colour over the grid (clipped to it), repainted with this layer only.
    private void DrawLiveNotes(IDrawContext d)
    {
        LiveDrawn = 0;
        var notes = _surface.LiveNotes;
        if (notes.Count == 0) return;
        var track = _surface.TrackColour() ?? AvaloniaPalette.Get("Accent", ActualThemeVariant == ThemeVariant.Light);
        var fill = TransportGlowRules.Tint(track);   // the shared translucent tint of a colour
        d.PushClip(_surface.GridLeft, _surface.GridTop, _surface.ViewportWidth, _surface.ViewportHeight);
        foreach (var note in notes)
        {
            var x = _surface.XOf(note.StartBeat);
            var width = Math.Max(2, (note.EndBeat - note.StartBeat) * _surface.PixelsPerBeat);
            if (x + width < _surface.GridLeft || x > Bounds.Width) continue;
            var y = _surface.YOf(note.Pitch);
            if (y + _surface.RowPx < _surface.GridTop || y > _surface.GridBottom) continue;
            d.Rect(fill, new UiStroke(track, 1), x, y, width, Math.Max(1, _surface.RowPx - 1));
            LiveDrawn++;
        }
        d.Pop();
    }


    /// <summary>The notes sounding at the playhead get a brighter wash (as DAWs light the notes being played).</summary>
    private void DrawSounding(IDrawContext d, double beat)
    {
        var wash = AvaloniaPalette.Get("PianoRollNoteSounding", ActualThemeVariant == ThemeVariant.Light);
        d.PushClip(_surface.GridLeft, _surface.GridTop, _surface.ViewportWidth, _surface.ViewportHeight);
        var notes = _surface.Presenter.Notes;
        for (var i = 0; i < notes.Count; i++)
        {
            var note = notes[i];
            if (note.Muted || note.Start > beat || note.End <= beat) continue;
            var r = _surface.NoteRect(note);
            d.Rect(wash, null, r.X, r.Y, r.Width, r.Height);
        }
        d.Pop();
    }

    private void DrawKeys(IDrawContext d)
    {
        var light = ActualThemeVariant == ThemeVariant.Light;
        var lit = 0;
        d.PushClip(0, _surface.GridTop, _surface.GridLeft, _surface.ViewportHeight);
        for (var pitch = 0; pitch < 128; pitch++)
        {
            if (!_live[pitch] && !_sounding[pitch]) continue;
            var y = _surface.YOf(pitch);
            if (y + _surface.RowPx < _surface.GridTop || y > _surface.GridBottom) continue;
            var colour = _live[pitch] ? AvaloniaPalette.Get("Accent", light) : PlaybackColour;   // keys of the sounding notes use the Playback highlight
            if (_surface.NoteNames) d.Rect(colour.WithOpacity(NameLit), null, 0, y, _surface.GridLeft, _surface.RowPx);   // the name shows through
            else d.Rect(colour, null, 0, y, PianoRollSurface.IsBlack(pitch) ? _surface.GridLeft * BlackShare : _surface.GridLeft, _surface.RowPx);
            lit++;
        }
        d.Pop();
        LitKeys = lit;
    }
}
