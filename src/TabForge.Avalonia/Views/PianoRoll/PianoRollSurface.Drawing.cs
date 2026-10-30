using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

// Owns: the piano roll surface's drawing, in theme tokens (Palette PianoRoll*, the loop and marquee tokens, the theme brushes): rows (black-key rows
//       darker, C rows with a stronger line), bar, beat and snap-subdivision lines, the loop tint, the notes (track colour, brighter = louder; a thin
//       edge inside each plain note so touching notes stay apart; selected in the selection colour with a light outline; muted hollow and dashed), the
//       drag previews, the keys with C labels, the ruler and the velocity lane. Only the visible rows, bars and notes are drawn. Selected notes keep
//       their fill and get a bright outline.
// Does not own: the geometry and the gestures (the other PianoRollSurface parts) or the playhead (PianoRollPlayheadLayer).
// Tests: TabForge.Avalonia.Tests.PianoRollViewTests.
public sealed partial class PianoRollSurface
{
    private const double Line = 1, StrongLine = 1.5, SelectedOutline = 1.5, MutedDash = 2, MutedGap = 1.5, NoteRadius = 2;
    private const double LabelSize = 10, LabelGap = 4, MinSubPixels = 6, QuietShare = 0.65, LoopBracket = 3, StemWidth = 3, HeadRadius = 2.5;
    private const double MutedBar = 0.45, ValueLineRows = 10;
    internal const double LaneLabelSize = 10;
    private const double ScaleTint = 0.10;
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
        var gridLine = Ink("PianoRollLineBeat");
        var (first, last) = VisibleRows();
        var scale = ScaleClasses;
        var tint = Colours.Get("AccentBrush").WithOpacity(ScaleTint);
        for (var r = first; r <= last; r++)
        {
            var pitch = Rows[r];
            var y = GridTop + r * RowPx - ScrollY;
            var plain = IsBlack(pitch) ? black : white;
            d.Rect(ScaleRowFill(plain, gridLine, pitch), null, GridLeft, y, width - GridLeft, RowPx);
            if (scale is not null && scale.Contains(pitch % 12)) d.Rect(tint, null, GridLeft, y, width - GridLeft, RowPx);
            if (pitch % 12 == 0) d.Line(c, GridLeft, y + RowPx, width, y + RowPx);
        }
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
        var edge = new UiStroke(Ink("PianoRollNoteEdge"), DialogMetrics.PianoRollNoteEdge);
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
            if (_erased.Contains(i)) { DrawErased(d, note, track, row, VelocityOf(i, note)); continue; }   // still there, tinted: deleted only on release
            var isSelected = selected.Contains(i);
            if (moving && isSelected && !_copy) continue;   // drawn at its preview place below
            DrawNote(d, note, isSelected, NoteDrawFill(note, isSelected, track, row, VelocityOf(i, note)), outline, edge);
            DrawNoteValue(d, note, VelocityOf(i, note), _gesture is Gesture.VelocityTool or Gesture.Velocity && _velocityPreview.ContainsKey(i));
        }
        if (!moving) return;
        foreach (var preview in DragPreview()) DrawNote(d, preview, true, NoteDrawFill(preview, true, track, row, preview.Velocity), outline, edge);
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

    /// <summary>Draws one note. A plain note's <paramref name="edge"/> is drawn inside its rectangle (inset by half its width), so two touching
    /// notes on one row keep a visible line between them; a selected note keeps its outline and a muted note its dashed hollow.</summary>
    private void DrawNote(IDrawContext d, PianoRollNote note, bool selected, UiColor fill, UiStroke outline, UiStroke edge)
    {
        var r = NoteRect(note);
        var (filled, outlined) = NoteLook(selected, note.Muted);
        if (!filled)
            d.Rect(null, outlined ? outline : new UiStroke(fill, Line, Dash: MutedDash, Gap: MutedGap), r.X + Line / 2, r.Y + Line / 2, Math.Max(1, r.Width - Line), Math.Max(1, r.Height - Line), NoteRadius);
        else if (outlined) d.Rect(fill, outline, r.X, r.Y, r.Width, r.Height, NoteRadius);
        else
        {
            d.Rect(fill, null, r.X, r.Y, r.Width, r.Height, NoteRadius);
            var inset = edge.Width / 2;
            d.Rect(null, edge, r.X + inset, r.Y + inset, Math.Max(1, r.Width - edge.Width), Math.Max(1, r.Height - edge.Width), NoteRadius);
        }
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
        if (_gesture == Gesture.Lasso && _moved && LassoPoints.Count > 1)
        {
            var edge = new UiStroke(Ink("MarqueeBorder"), Line, Dash: MutedDash * 2, Gap: MutedGap * 2);
            for (var i = 0; i < LassoPoints.Count; i++) d.Line(edge, LassoPoints[i].X, LassoPoints[i].Y, LassoPoints[(i + 1) % LassoPoints.Count].X, LassoPoints[(i + 1) % LassoPoints.Count].Y);
        }
        if (_gesture == Gesture.SplitLine && _moved && SplitPoints.Count > 1)
        {
            var cut = new UiStroke(Colours.Get("CursorBrush"), StrongLine);
            for (var i = 1; i < SplitPoints.Count; i++) d.Line(cut, SplitPoints[i - 1].X, SplitPoints[i - 1].Y, SplitPoints[i].X, SplitPoints[i].Y);
        }
        DrawEraseLabel(d);
        var track = TrackColour() ?? Colours.Get("AccentBrush");
        DrawGhost(d, track);
        DrawCutMarker(d);
        if (_gesture == Gesture.Draw)
            d.Rect(track, new UiStroke(Colours.Get("TextStrongBrush"), Line), XOf(_pressBeat), YOf(_pressPitch), Math.Max(2, (_drawEnd - _pressBeat) * PixelsPerBeat), Math.Max(1, RowPx - 1), NoteRadius);
        else if (_gesture == Gesture.None && _hover is var (beat, pitch, index))
        {
            if (index >= 0 && index < Presenter.Notes.Count)   // the hover outline of the note under the pointer
            {
                var r = NoteRect(Presenter.Notes[index]);
                d.Rect(null, new UiStroke(Colours.Get("HoverAccentBrush"), StrongLine), r.X - Line, r.Y - Line, r.Width + 2 * Line, r.Height + 2 * Line, NoteRadius);
            }
            if (Presenter.Tool == PianoRollTool.Erase && index >= 0 && index < Presenter.Notes.Count)   // the click would delete it: the same danger tint
                DrawErased(d, Presenter.Notes[index], track, Ink("PianoRollRowWhite"), VelocityOf(index, Presenter.Notes[index]));
            if (Presenter.Tool == PianoRollTool.Split && index >= 0)
                d.Line(new UiStroke(Ink("Danger"), CutWidth, Dash: CutDash, Gap: CutGap), XOf(beat), YOf(pitch) - CutOverhang, XOf(beat), YOf(pitch) + RowPx + CutOverhang);   // where a click would cut: the cut marker's style
            else if (Presenter.Tool == PianoRollTool.Draw && index < 0)
                d.Rect(track.WithOpacity(QuietShare), null, XOf(beat), YOf(pitch), Math.Max(2, NewLength(beat) * PixelsPerBeat), Math.Max(1, RowPx - 1), NoteRadius);
        }
    }

    // The key strip's paint lives in PianoRollSurface.KeyboardDraw (per-key edges, tints, previews).

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
        var x = _hover is null ? GridLeft + LabelGap : LabelGap;   // the Piano / Names header covers the corner: a key readout starts past it
        d.DrawText(text, x, (GridTop - text.Height) / 2);
    }

    /// <summary>The lane header's title: the Program lane also names the instrument in force at its last point (the General MIDI name).</summary>
    internal string LaneTitle()
    {
        if (LaneId != PianoRollControllers.Program) return PianoRollControllers.NameOf(LaneId);
        var value = _points.Count == 0 ? PianoRollControllers.Centre(LaneId)
            : PianoRollControllers.ValueAt(_points, LaneId, _points[_points.Count - 1].Beat);
        return $"{PianoRollControllers.NameOf(LaneId)} {value} {GeneralMidi.NameOf(value)}";
    }

    private void DrawLane(IDrawContext d, double width)
    {
        var header = _laneHeaderHover ? Colours.Get("HoverBrush") : Colours.Get("Panel2Brush");
        d.Rect(header, null, 0, GridBottom, width, LaneHeaderSize);   // the lane button (PianoRollView.LaneButton) sits over the left end of this strip
        d.Line(new UiStroke(Ink("PianoRollLineBar"), Line), 0, GridBottom, width, GridBottom);
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
