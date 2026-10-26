using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using TabForge.Avalonia.Platform;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Presenters;

namespace TabForge.Avalonia.Views;

// Owns: the piano roll surface's drawing, in theme tokens (Palette PianoRoll*, the loop and marquee tokens, the theme brushes): rows (black-key rows
//       darker, C rows with a stronger line), bar, beat and snap-subdivision lines, the loop tint, the notes (track colour, brighter = louder; selected
//       in the selection colour with a light outline; muted hollow and dashed), the drag previews, the keys with C labels, the ruler and the
//       velocity lane. Only the visible rows, bars and notes are drawn. Selected notes keep their fill and get a bright outline.
// Does not own: the geometry and the gestures (the other PianoRollSurface parts) or the playhead (PianoRollPlayheadLayer).
// Tests: TabForge.Avalonia.Tests.PianoRollViewTests.
public sealed partial class PianoRollSurface
{
    private const double Line = 1, StrongLine = 1.5, SelectedOutline = 1.5, MutedDash = 2, MutedGap = 1.5, NoteRadius = 2;
    private const double LabelSize = 10, LabelGap = 4, MinSubPixels = 6, QuietShare = 0.65, LoopBracket = 3, StemWidth = 3, HeadRadius = 2.5;
    private const double MutedBar = 0.45, ValueLineRows = 10;
    private const double LaneLabelSize = 10, ChevronSize = 4, ScaleTint = 0.10;
    private static readonly IReadOnlyList<string> Swatches = Enumerable.Range(1, 14).Select(i => "SectionColour" + i).ToList();
    private const string Font = InstrumentPanelView.Font;
    private static readonly IReadOnlyList<bool> BlackKeys = new[] { false, true, false, true, false, false, true, false, true, false, true, false };

    internal static bool IsBlack(int pitch) => BlackKeys[((pitch % 12) + 12) % 12];

    // Bar numbers and C labels, made once per value so a repaint formats no string. Ceiling: one per bar number and per octave shown. Thread: UI only.
    private readonly Dictionary<int, string> _barLabels = new();
    private readonly Dictionary<int, string> _keyLabels = new();

    private string BarLabel(int bar)
    {
        if (!_barLabels.TryGetValue(bar, out var text)) _barLabels[bar] = text = (bar + 1).ToString(CultureInfo.InvariantCulture);
        return text;
    }

    private string KeyLabel(int pitch)
    {
        if (!_keyLabels.TryGetValue(pitch, out var text)) _keyLabels[pitch] = text = PianoRollPositionText.PitchName(pitch);
        return text;
    }

    private bool Light => ActualThemeVariant == ThemeVariant.Light;

    private UiColor Ink(string paletteKey) => AvaloniaPalette.Get(paletteKey, Light);

    /// <summary>A note's fill: the track colour, brighter the louder (a quiet note fades towards the row colour).</summary>
    internal static UiColor NoteFill(UiColor track, UiColor row, int velocity) =>
        UiTokens.Blend(track, row, (1 - Math.Clamp(velocity, 1, 127) / 127.0) * QuietShare);

    public override void Render(DrawingContext context)
    {
        var d = _draw.Use(context);
        var width = Bounds.Width;
        if (width <= 0 || Bounds.Height <= 0) return;
        d.PushClip(GridLeft, GridTop, ViewportWidth, ViewportHeight);
        DrawRows(d, width);
        DrawGridLines(d);
        DrawLoop(d);
        DrawNotes(d);
        DrawPreviews(d);
        d.Pop();
        DrawKeys(d);
        DrawRuler(d, width);
        DrawReadout(d);
        DrawLane(d, width);
    }

    private void DrawRows(IDrawContext d, double width)
    {
        var white = Ink("PianoRollRowWhite");
        var black = Ink("PianoRollRowBlack");
        var c = new UiStroke(Ink("PianoRollLineC"), Line);
        var (first, last) = VisibleRows();
        var scale = ScaleClasses;
        var tint = Colours.Get("AccentBrush").WithOpacity(ScaleTint);
        for (var r = first; r <= last; r++)
        {
            var pitch = Rows[r];
            var y = GridTop + r * RowPx - ScrollY;
            d.Rect(IsBlack(pitch) ? black : white, null, GridLeft, y, width - GridLeft, RowPx);
            if (scale is not null && scale.Contains(pitch % 12)) d.Rect(tint, null, GridLeft, y, width - GridLeft, RowPx);
            if (pitch % 12 == 0) d.Line(c, GridLeft, y + RowPx, width, y + RowPx);
        }
        var hovered = _hover is var (_, hp, _) ? hp : _keyHover;   // the band across the grid on the row under the pointer
        if (hovered >= 0) d.Rect(Ink("PianoRollHoverRow"), null, GridLeft, YOf(hovered), width - GridLeft, RowPx);
    }

    private void DrawGridLines(IDrawContext d)
    {
        var bar = new UiStroke(Ink("PianoRollLineBar"), Line);
        var beat = new UiStroke(Ink("PianoRollLineBeat"), Line);
        var sub = new UiStroke(Ink("PianoRollLineSub"), Line);
        var snap = Snap();
        var map = Presenter.Map;
        var from = BeatAt(GridLeft);
        var to = BeatAt(GridLeft + ViewportWidth);
        var at = map.BarAtBeat(from).StartBeat;
        while (at <= to)
        {
            var b = map.BarAtBeat(at + 1e-6);
            var x = XOf(b.StartBeat);
            var step = snap is null ? 0 : map.GridStep(snap.Grid, b.StartBeat);
            if (step * PixelsPerBeat >= MinSubPixels && step > 0)
                for (var s = b.StartBeat + step; s < b.EndBeat - 1e-6; s += step) d.Line(sub, XOf(s), GridTop, XOf(s), GridBottom);
            for (var q = b.StartBeat + 1; q < b.EndBeat - 1e-6; q++) d.Line(beat, XOf(q), GridTop, XOf(q), GridBottom);
            d.Line(bar, x, GridTop, x, GridBottom);
            at = b.EndBeat;
        }
    }

    private void DrawNotes(IDrawContext d)
    {
        var notes = Presenter.Notes;
        var selected = Presenter.Selected;
        var track = TrackColour() ?? Colours.Get("AccentBrush");
        var row = Ink("PianoRollRowWhite");
        var outline = new UiStroke(Ink("PianoRollSelectedOutline"), SelectedOutline);
        var from = BeatAt(GridLeft);
        var to = BeatAt(GridLeft + ViewportWidth);
        var (firstRow, lastRow) = VisibleRows();
        var moving = (_gesture is Gesture.Move or Gesture.ResizeEnd or Gesture.ResizeStart) && _moved;
        if (Ghosts.Count > 0)   // the other takes and tracks: faint, behind, never edited
        {
            var ghost = Ink("PianoRollGhost");
            foreach (var g in Ghosts)
                if (g.End >= from && g.Start <= to) { var r = NoteRect(g); d.Rect(ghost, null, r.X, r.Y, r.Width, r.Height, NoteRadius); }
        }
        for (var i = 0; i < notes.Count; i++)
        {
            var note = notes[i];
            var rowIndex = RowIndex(note.Pitch);
            if (note.End < from || note.Start > to || rowIndex < firstRow || rowIndex > lastRow) continue;
            if (_erased.Contains(i)) continue;
            var isSelected = selected.Contains(i);
            if (moving && isSelected && !_copy) continue;   // drawn at its preview place below
            DrawNote(d, note, isSelected, FillOf(note, track, row, VelocityOf(i, note)), outline);
            DrawNoteValue(d, note, VelocityOf(i, note), _gesture is Gesture.VelocityTool or Gesture.Velocity && _velocityPreview.ContainsKey(i));
        }
        if (!moving) return;
        foreach (var preview in DragPreview()) DrawNote(d, preview, true, FillOf(preview, track, row, preview.Velocity), outline);
    }

    /// <summary>A note's colour in the chosen mode: the track colour brighter the louder, velocity heat, pitch class or channel.</summary>
    private UiColor FillOf(PianoRollNote note, UiColor track, UiColor row, int velocity) => ColourMode switch
    {
        "Velocity" => Heat(Math.Clamp(velocity, 1, 127) / 127.0),
        "Lane" => LaneId == PianoRollControllers.Velocity ? Heat(Math.Clamp(velocity, 1, 127) / 127.0) : Heat(LaneShare(note.Start)),
        "Pitch" => Ink(Swatches[((note.Pitch % 12) + 12) % 12]),
        "Channel" => note.Channel >= 0 ? Ink(Swatches[note.Channel % Swatches.Count]) : NoteFill(track, row, velocity),
        _ => NoteFill(track, row, velocity),
    };

    /// <summary>How a note is drawn: filled unless muted; the bright outline when selected (selected = fill + outline, muted = hollow dashed,
    /// selected and muted = hollow with the bright outline).</summary>
    internal static (bool Filled, bool Outlined) NoteLook(bool selected, bool muted) => (!muted, selected);

    /// <summary>A note: its fill (a muted note is hollow and dashed), and a bright outline when it is selected (a selected muted note: hollow with
    /// the bright outline). The selection never replaces the fill.</summary>
    private static readonly IReadOnlyList<string> HeatStops = new[] { "PianoRollHeat0", "PianoRollHeat1", "PianoRollHeat2", "PianoRollHeat3", "PianoRollHeat4" };

    /// <summary>The heat ramp (Reaper style): blue low, green, yellow, orange, red high; <paramref name="share"/> 0 to 1.</summary>
    private UiColor Heat(double share)
    {
        var at = Math.Clamp(share, 0, 1) * (HeatStops.Count - 1);
        var low = Math.Min((int)Math.Floor(at), HeatStops.Count - 2);
        return UiTokens.Blend(Ink(HeatStops[low]), Ink(HeatStops[low + 1]), at - low);
    }

    /// <summary>The shown controller lane's value at <paramref name="beat"/> as a share of its range (the "By lane" colours).</summary>
    private double LaneShare(double beat)
    {
        var min = PianoRollControllers.Min(LaneId);
        var max = PianoRollControllers.Max(LaneId);
        return (PianoRollControllers.ValueAt(_points, LaneId, beat) - min) / (double)Math.Max(1, max - min);
    }

    // Value labels (0 to 127) shown on notes while their velocity is dragged; made once each. Ceiling 128. Thread: UI only.
    private readonly string[] _values = new string[128];

    private string ValueLabel(int value)
    {
        var v = Math.Clamp(value, 0, 127);
        return _values[v] ??= v.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>The thin line inside a tall enough note at its velocity's height (what the top-edge drag moves), and while a velocity drag runs,
    /// the value written above the note.</summary>
    private void DrawNoteValue(IDrawContext d, PianoRollNote note, int velocity, bool dragging)
    {
        var r = NoteRect(note);
        if (r.Height >= ValueLineRows)
        {
            var y = r.Bottom - Math.Clamp(velocity, 0, 127) / 127.0 * r.Height;
            d.Line(new UiStroke(Ink("PianoRollNoteValue"), Line), r.X + 1, y, r.Right - 1, y);
        }
        if (!dragging) return;
        var text = d.Text(ValueLabel(velocity), Font, LabelSize, Colours.Get("TextStrongBrush"), d.PixelsPerDip);
        d.DrawText(text, r.X + LabelGap / 2, r.Y - text.Height);
    }

    private void DrawNote(IDrawContext d, PianoRollNote note, bool selected, UiColor fill, UiStroke outline)
    {
        var r = NoteRect(note);
        var (filled, outlined) = NoteLook(selected, note.Muted);
        if (!filled)
            d.Rect(null, outlined ? outline : new UiStroke(fill, Line, Dash: MutedDash, Gap: MutedGap), r.X + Line / 2, r.Y + Line / 2, Math.Max(1, r.Width - Line), Math.Max(1, r.Height - Line), NoteRadius);
        else d.Rect(fill, outlined ? outline : null, r.X, r.Y, r.Width, r.Height, NoteRadius);
        if (note.Voice > 0 && r.Width > 2 * StrongLine)   // the tab's second voice: a dashed inner edge
            d.Rect(null, new UiStroke(Colours.Get("TextStrongBrush"), Line, Dash: MutedDash, Gap: MutedGap), r.X + StrongLine, r.Y + StrongLine, r.Width - 2 * StrongLine, Math.Max(1, r.Height - 2 * StrongLine), NoteRadius);
    }

    private void DrawPreviews(IDrawContext d)
    {
        if (StepInput) d.Line(new UiStroke(Colours.Get("CursorBrush"), StrongLine), XOf(StepBeat), GridTop, XOf(StepBeat), GridBottom);
        if (_gesture is Gesture.Marquee or Gesture.Erase && _moved)
        {
            var box = MarqueeRect();
            d.Rect(Ink("MarqueeFill"), new UiStroke(Ink("MarqueeBorder"), Line, Dash: MutedDash * 2, Gap: MutedGap * 2), box.X, box.Y, box.Width, box.Height);
        }
        var track = TrackColour() ?? Colours.Get("AccentBrush");
        if (_gesture == Gesture.Draw)
            d.Rect(track, new UiStroke(Colours.Get("TextStrongBrush"), Line), XOf(_pressBeat), YOf(_pressPitch), Math.Max(2, (_drawEnd - _pressBeat) * PixelsPerBeat), Math.Max(1, RowPx - 1), NoteRadius);
        else if (_gesture == Gesture.None && _hover is var (beat, pitch, index))
        {
            if (index >= 0 && index < Presenter.Notes.Count)   // the hover outline of the note under the pointer
            {
                var r = NoteRect(Presenter.Notes[index]);
                d.Rect(null, new UiStroke(Colours.Get("HoverAccentBrush"), StrongLine), r.X - Line, r.Y - Line, r.Width + 2 * Line, r.Height + 2 * Line, NoteRadius);
            }
            if (Presenter.Tool == PianoRollTool.Split && index >= 0)
                d.Line(new UiStroke(Colours.Get("CursorBrush"), StrongLine), XOf(beat), YOf(pitch) - 2, XOf(beat), YOf(pitch) + RowPx + 1);
            else if (Presenter.Tool == PianoRollTool.Draw && index < 0)
                d.Rect(track.WithOpacity(QuietShare), null, XOf(beat), YOf(pitch), Math.Max(2, NewLength(beat) * PixelsPerBeat), Math.Max(1, RowPx - 1), NoteRadius);
        }
    }

    private void DrawKeys(IDrawContext d)
    {
        var white = Ink("PianoRollKeyWhite");
        var black = Ink("PianoRollKeyBlack");
        var label = Ink("PianoRollKeyLabel");
        var edge = new UiStroke(Ink("PianoRollLineBeat"), Line);
        var lit = Ink("PianoRollKeyLive");
        var hover = Ink("PianoRollKeyHover");
        d.PushClip(0, GridTop, GridLeft, ViewportHeight);
        if (NoteNames)
        {
            DrawNameRows(d, lit, hover, edge);
            d.Pop();
            return;
        }
        d.Rect(white, null, 0, GridTop, GridLeft, ViewportHeight);
        var (first, last) = VisibleRows();
        for (var r = first; r <= last; r++)
        {
            var pitch = Rows[r];
            var y = GridTop + r * RowPx - ScrollY;
            var sounding = pitch == _keyDown;
            if (IsBlack(pitch)) d.Rect(sounding ? lit : black, null, 0, y, GridLeft * 0.62, RowPx);
            else if (sounding) d.Rect(lit, null, 0, y, GridLeft, RowPx);
            if (pitch == _keyHover && !sounding) d.Rect(hover, null, 0, y, IsBlack(pitch) ? GridLeft * 0.62 : GridLeft, RowPx);
            if ((pitch % 12 == 0 || FoldedLabels) && RowPx >= LabelSize - 2)
            {
                d.Line(edge, 0, y + RowPx, GridLeft, y + RowPx);
                var text = d.Text(KeyLabel(pitch), Font, LabelSize, label, d.PixelsPerDip);
                d.DrawText(text, GridLeft - text.Width - LabelGap, y + (RowPx - text.Height) / 2);
            }
        }
        d.Line(edge, GridLeft - Line / 2, GridTop, GridLeft - Line / 2, GridBottom);
        d.Pop();
    }

    /// <summary>The key strip as note names (Reaper style): a row per pitch, alternate shading, black-key rows tinted, the name at the right edge.</summary>
    private void DrawNameRows(IDrawContext d, UiColor lit, UiColor hover, UiStroke edge)
    {
        var (row, alt, blackRow, ink) = (Ink("PianoRollNameRow"), Ink("PianoRollNameRowAlt"), Ink("PianoRollNameRowBlack"), Ink("PianoRollNameText"));
        var (first, last) = VisibleRows();
        for (var r = first; r <= last; r++)
        {
            var pitch = Rows[r];
            var y = GridTop + r * RowPx - ScrollY;
            d.Rect(pitch == _keyDown ? lit : pitch == _keyHover ? hover : IsBlack(pitch) ? blackRow : r % 2 == 0 ? row : alt, null, 0, y, GridLeft, RowPx);
            if (RowPx < LabelSize - 2) continue;
            var text = d.Text(NameLabel(pitch), Font, Math.Min(LabelSize, RowPx - 1), ink, d.PixelsPerDip);
            d.DrawText(text, GridLeft - text.Width - LabelGap, y + (RowPx - text.Height) / 2);
        }
        d.Line(edge, GridLeft - Line / 2, GridTop, GridLeft - Line / 2, GridBottom);
    }

    // Row names, made once per pitch and naming so a repaint formats no string. Ceiling: 128 per naming. Thread: UI only.
    private readonly Dictionary<(int, bool), string> _names = new();

    private string NameLabel(int pitch)
    {
        if (!_names.TryGetValue((pitch, DrumNames), out var text)) _names[(pitch, DrumNames)] = text = PianoRollPositionText.KeyName(pitch, DrumNames);
        return text;
    }

    // The readout above the key strip: the note under the pointer and its velocity ("C#5 73"), or the pitch under it. Remade only when it changes.
    private (int Pitch, int Velocity, long Hundredths) _readoutKey = (-1, -1, -1);
    private string _readout = "";

    private void DrawReadout(IDrawContext d)
    {
        var pitch = _hover is var (_, hp, _) ? hp : _keyHover;
        if (pitch < 0) return;
        var velocity = _hover is var (_, _, index) && index >= 0 && index < Presenter.Notes.Count ? VelocityOf(index, Presenter.Notes[index]) : -1;
        var hundredths = double.IsNaN(_hoverBeat) ? -1 : (long)Math.Floor(_hoverBeat * 100);
        if (_readoutKey != (pitch, velocity, hundredths))
        {
            _readoutKey = (pitch, velocity, hundredths);
            _readout = hundredths < 0 ? PianoRollPositionText.Readout(pitch, velocity >= 0 ? velocity : null, DrumNames)
                : PianoRollPositionText.Readout(PianoRollPositionText.Short(Presenter.Map, _hoverBeat), pitch, velocity >= 0 ? velocity : null, DrumNames);
        }
        var text = d.Text(_readout, Font, LabelSize, Colours.Get("TextBrush"), d.PixelsPerDip);
        d.DrawText(text, LabelGap, (GridTop - text.Height) / 2);
    }

    private void DrawLane(IDrawContext d, double width)
    {
        var header = Colours.Get("Panel2Brush");
        var text = Colours.Get("MutedBrush");
        d.Rect(header, null, 0, GridBottom, width, LaneHeaderSize);
        d.Line(new UiStroke(Ink("PianoRollLineBar"), Line), 0, GridBottom, width, GridBottom);
        var cx = GridLeft / 2;
        var cy = GridBottom + LaneHeaderSize / 2;
        var chevron = d.Paths.BuildPath(s =>
        {
            if (LaneShown) { s.BeginFigure(new UiPoint(cx - ChevronSize, cy - ChevronSize / 2), false, false); s.LineTo(new UiPoint(cx, cy + ChevronSize / 2)); s.LineTo(new UiPoint(cx + ChevronSize, cy - ChevronSize / 2)); }
            else { s.BeginFigure(new UiPoint(cx - ChevronSize / 2, cy - ChevronSize), false, false); s.LineTo(new UiPoint(cx + ChevronSize / 2, cy)); s.LineTo(new UiPoint(cx - ChevronSize / 2, cy + ChevronSize)); }
        });
        d.Path(null, new UiStroke(text, StrongLine), chevron);
        var title = d.Text(PianoRollControllers.NameOf(LaneId) + (LanePicker ? "  ▾" : ""), Font, LaneLabelSize, text, d.PixelsPerDip);
        d.DrawText(title, GridLeft + LabelGap, GridBottom + (LaneHeaderSize - title.Height) / 2);
        if (!LaneShown) return;
        d.Rect(Ink("PianoRollLaneSurface"), null, 0, LaneTop, width, Lane);
        d.PushClip(GridLeft, LaneTop, ViewportWidth, Lane);
        if (LaneId != PianoRollControllers.Velocity)
        {
            DrawControllerLane(d);
            d.Pop();
            return;
        }
        var notes = Presenter.Notes;
        var from = BeatAt(GridLeft);
        var to = BeatAt(GridLeft + ViewportWidth);
        for (var i = 0; i < notes.Count; i++)
        {
            if (notes[i].Start < from - 1 || notes[i].Start > to) continue;
            var colour = BarColour(i);
            var velocity = VelocityOf(i, notes[i]);
            var x = XOf(notes[i].Start);
            var y = VelocityY(velocity);
            d.Rect(colour, null, x, y, StemWidth, LaneTop + Lane - y);
            d.Ellipse(colour, new UiStroke(colour, Line), x + StemWidth / 2, y, HeadRadius, HeadRadius);
        }
        d.Pop();
        var scale = new UiStroke(Ink("PianoRollLineSub"), Line);
        d.Line(scale, GridLeft, VelocityY(64), width, VelocityY(64));
    }

    /// <summary>The colour of note <paramref name="index"/>'s velocity bar: the bright selection colour (opaque) when selected, else the track
    /// colour; a muted note's bar is dimmed, never hidden.</summary>
    internal UiColor BarColour(int index)
    {
        var selected = Presenter.Selected.Contains(index);
        var colour = selected ? Ink("PianoRollSelectedOutline") : TrackColour() ?? Colours.Get("AccentBrush");
        return index < Presenter.Notes.Count && Presenter.Notes[index].Muted && !selected ? colour.WithOpacity(MutedBar) : colour;
    }

    /// <summary>The y of a velocity bar's top in the lane.</summary>
    internal double VelocityY(int velocity) => LaneTop + Lane - HeadRadius - Math.Clamp(velocity, 0, 127) / 127.0 * Math.Max(1, Lane - 2 * HeadRadius - 2);

    /// <summary>The velocity a note shows now: a lane drag's preview, else its own.</summary>
    private int VelocityOf(int index, PianoRollNote note) => _velocityPreview.TryGetValue(index, out var v) ? v : note.Velocity;
}
