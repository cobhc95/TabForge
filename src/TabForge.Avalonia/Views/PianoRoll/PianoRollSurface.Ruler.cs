using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using TabForge.Avalonia.Platform;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Presenters;

namespace TabForge.Avalonia.Views;

// Owns: the piano roll's bar ruler, drawn and handled as the timeline ruler is: bar numbers, the section chips, the loop region as the timeline
//       draws it (the orange bar with a handle at each end, full strength while the Loop button repeats it, and the faint band down the grid);
//       a click moves the playhead (the selection stays), a drag makes a loop on the snap grid (so a loop can start and end inside a bar), a drag
//       on a loop handle moves that edge and a middle-drag moves the whole loop. The loop changes once, on release.
// Does not own: the loop region itself and its undo step (the view's MakeLoop hook, SelectionLoopLink), the loop drawing (TimelineLoopMarks).
// Tests: TabForge.Avalonia.Tests.PianoRollViewTests.
public sealed partial class PianoRollSurface
{
    private const double SectionChipPad = 3, SectionTextSize = 9, BeatLabelPixels = 40;

    // Beat labels ("2.3"), made once each so a repaint formats no string. Ceiling: one per bar and beat shown. Thread: UI only.
    private readonly Dictionary<(int, int), string> _beatLabels = new();

    private string BeatLabel(int bar, int beat)
    {
        if (!_beatLabels.TryGetValue((bar, beat), out var text))
            _beatLabels[(bar, beat)] = text = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{bar + 1}.{beat + 1}");
        return text;
    }

    private int _loopEdge;   // which handle a LoopEdge drag holds: -1 the start, 1 the end
    private (double From, double To)? _loopPreview;

    /// <summary>Clears the loop region (a plain ruler click; one undo step, the white selection stays).</summary>
    internal Action? ClearLoop { get; set; }

    /// <summary>True while the Loop button repeats the region (the bar is drawn full strength).</summary>
    internal Func<bool> LoopOn { get; set; } = () => false;

    /// <summary>The song's sections on the piano roll's clock: where each starts, its title and colour.</summary>
    internal Func<IReadOnlyList<(double Beat, string Title, UiColor Colour)>> Sections { get; set; } = () => Array.Empty<(double, string, UiColor)>();

    /// <summary>The loop region as it shows now: a ruler drag's preview, else the song's loop.</summary>
    private (double From, double To)? ShownLoop => _loopPreview ?? LoopSpan();

    /// <summary>Half the width of the ruler's playhead triangle (DIP); it is grabbed within this much more each side.</summary>
    internal const double MarkerHalf = 5;

    /// <summary>Where a drag of the playhead triangle is taking it (the layer draws the triangle there), or null.</summary>
    internal double? PlayheadPreview { get; private set; }

    private bool PressRuler(Point p, bool middle)
    {
        _loopPreview = null;
        if (!middle && PlayheadBeat() is { } playhead)
        {
            var x = XOf(playhead);
            if (x < GridLeft && p.X <= GridLeft + 3 * MarkerHalf || x > Bounds.Width && p.X >= Bounds.Width - 3 * MarkerHalf)
            {
                Reveal(playhead);   // the edge arrow: scroll to the playhead
                UserScrolled?.Invoke();
                return false;
            }
            if (Math.Abs(p.X - x) <= 2 * MarkerHalf) { _gesture = Gesture.PlayheadDrag; PlayheadPreview = playhead; return true; }
        }
        var loop = LoopSpan();
        if (middle)
        {
            if (loop is not var (from, to) || _pressBeat < from || _pressBeat > to) return false;
            _gesture = Gesture.LoopMove;
            return true;
        }
        if (loop is var (start, end) && Math.Abs(p.X - XOf(start)) <= TimelineLoopMarks.HandleHalf + EdgeGrip) { _gesture = Gesture.LoopEdge; _loopEdge = -1; return true; }
        if (loop is var (_, last) && Math.Abs(p.X - XOf(last)) <= TimelineLoopMarks.HandleHalf + EdgeGrip) { _gesture = Gesture.LoopEdge; _loopEdge = 1; return true; }
        _gesture = Gesture.Ruler;
        return true;
    }

    private void DragRuler(Point p)
    {
        var beat = SnapBeat(BeatAt(p.X), Array.Empty<int>(), _alt);
        var from = SnapBeat(_pressBeat, Array.Empty<int>(), _alt);
        _dragBeat = beat;
        var loop = LoopSpan();
        _loopPreview = _gesture switch
        {
            Gesture.Ruler => (Math.Min(from, beat), Math.Max(from, beat)),
            Gesture.LoopEdge when loop is var (start, end) => _loopEdge < 0 ? (Math.Min(beat, end - PianoRollEdits.MinLength), end) : (start, Math.Max(beat, start + PianoRollEdits.MinLength)),
            Gesture.LoopMove when loop is var (start, end) => Shifted(start, end, SnapBeat(start + BeatAt(p.X) - _pressBeat, Array.Empty<int>(), _alt) - start),
            _ => null,
        };
    }

    private static (double, double) Shifted(double start, double end, double delta)
    {
        var shift = Math.Max(delta, -start);
        return (start + shift, end + shift);
    }

    private void DragPlayhead(Point p)
    {
        PlayheadPreview = SnapBeat(BeatAt(p.X), Array.Empty<int>(), _alt);
        Playhead.InvalidateVisual();
    }

    private void ReleasePlayhead()
    {
        if (PlayheadPreview is { } beat) Seek?.Invoke(beat);   // the same seek as a ruler click
        PlayheadPreview = null;
        Playhead.InvalidateVisual();
    }

    private void ReleaseRuler()
    {
        if (_moved && _loopPreview is var (from, to) && to - from > 1e-6) MakeLoop?.Invoke(from, to, false);
        else if (_gesture == Gesture.Ruler)
        {
            Seek?.Invoke(_pressBeat);   // a plain ruler click moves the playhead and clears the loop, as the timeline ruler does
            ClearLoop?.Invoke();
        }
        _loopPreview = null;
    }

    private void DrawLoop(IDrawContext d)
    {
        if (ShownLoop is not var (from, to) || to <= from) return;
        d.Rect(Ink(TimelineLoopMarks.RegionKey), null, XOf(from), GridTop, (to - from) * PixelsPerBeat, ViewportHeight);
    }

    private void DrawRuler(IDrawContext d, double width)
    {
        var text = Colours.Get("MutedBrush");
        var tick = new UiStroke(Ink("PianoRollLineBar"), Line);
        d.Rect(Colours.Get("Panel2Brush"), null, 0, 0, width, GridTop);
        d.PushClip(GridLeft, 0, ViewportWidth, GridTop);
        var map = Presenter.Map;
        var to = BeatAt(GridLeft + ViewportWidth);
        var at = map.BarAtBeat(BeatAt(GridLeft)).StartBeat;
        var labelEvery = Math.Max(1, (int)Math.Ceiling(LabelSize * 4 / Math.Max(1, map.BarAtBeat(at + 1e-6).Beats * PixelsPerBeat)));
        while (at <= to)
        {
            var b = map.BarAtBeat(at + 1e-6);
            var x = XOf(b.StartBeat);
            d.Line(tick, x, GridTop / 2, x, GridTop);
            if (b.Bar % labelEvery == 0)
            {
                var label = d.Text(BarLabel(b.Bar), Font, LabelSize, text, d.PixelsPerDip);
                d.DrawText(label, x + LabelGap, GridTop - label.Height - 1);
            }
            if (PixelsPerBeat >= BeatLabelPixels)   // zoomed in: bar.beat under every beat, as the mockup's ruler
                for (var beat = 1; beat < (int)Math.Round(b.Beats); beat++)
                {
                    var bx = XOf(b.StartBeat + beat);
                    d.Line(tick, bx, GridTop * 3 / 4, bx, GridTop);
                    var label = d.Text(BeatLabel(b.Bar, beat), Font, LabelSize, text, d.PixelsPerDip);
                    d.DrawText(label, bx + LabelGap, GridTop - label.Height - 1);
                }
            at = b.EndBeat;
        }
        foreach (var (beat, title, colour) in Sections())
        {
            var x = XOf(beat);
            if (x > GridLeft + ViewportWidth) continue;
            var label = d.Text(title, Font, SectionTextSize, UiTokens.InkOn(colour), d.PixelsPerDip);
            d.Rect(colour, null, x, 1, label.Width + 2 * SectionChipPad, label.Height, NoteRadius);
            d.DrawText(label, x + SectionChipPad, 1);
        }
        if (ShownLoop is var (from, end) && end > from)
            TimelineLoopMarks.Draw(d, Ink, XOf(from), XOf(end), GridTop, GridTop, GridTop, LoopOn() || _loopPreview is not null);
        d.Pop();
        d.Line(tick, 0, GridTop - Line / 2, width, GridTop - Line / 2);
    }
}
