using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Presenters;

namespace TabForge.Avalonia.Platform;

/// <summary>
/// The cells of one Avalonia track row, in the WPF default column order and widths (TrackColumnLayout.DefaultOrder): cogwheel, instrument kind,
/// record arm, number, name (flexible), split FX button, M, S, volume slider and pan slider. It places each cell and draws it in theme colours.
/// Owns: the column geometry, the cell boxes, the slider value mapping and the cell drawing.
/// Does not own: the row geometry (TimelineView), the instrument art (TrackIconArt), the pointer gestures (TimelineInputController), the edits.
/// Tests: TabForge.Avalonia.Tests.TrackRowControlsTests.
/// </summary>
public static class TrackRowCells
{
    public enum Cell { Settings, Arm, Fx, FxPower, Mute, Solo, Volume, Pan, Kind }

    /// <summary>The state a cell is drawn in: idle, under the pointer (lighter), or held down by a press (darker).</summary>
    public enum CellPaint { Idle, Hover, Pressed }

    /// <summary>How far the hover blends a cell's body toward white, the pressed state toward the press colour, and the hover edge toward white.</summary>
    public const double HoverBlend = 0.18, PressedBlend = 0.35, HoverEdgeBlend = 0.45;

    public const double GridLeft = 6, GridRight = 10, NameWidth = 138;
    public const double SliderMarginLeft = 6, SliderMarginRight = 10;
    private const string Font = ChromeMetrics.UiFontFamily;

    /// <summary>The WPF columns in their default order with their widths; "name" is the flexible one.</summary>
    public static readonly IReadOnlyList<(string Id, string Label, double Width)> Columns = new (string Id, string Label, double Width)[]
    {
        ("settings", "", 24), ("kind", "", 32), ("colour", "", 22), ("number", "#", 28), ("name", "TRACK", NameWidth),
        ("fx", "FX", 56), ("mute", "M", 26), ("solo", "S", 26), ("volume", "VOLUME", 82), ("pan", "PAN", 82),
    };

    /// <summary>One column's left edge and width in a track row.</summary>
    public readonly record struct ColumnSpan(double Left, double Width)
    {
        public double Right => Left + Width;
    }

    /// <summary>
    /// Every column of a track row for one list width: the single source of the x positions and widths that the cells, the sliders, the hit tests,
    /// the column titles and the name editor all read. The name column takes the extra width; every column after it shifts as a block.
    /// </summary>
    public readonly record struct TrackRowLayout(ColumnSpan Settings, ColumnSpan Kind, ColumnSpan Colour, ColumnSpan Number, ColumnSpan Name,
        ColumnSpan Fx, ColumnSpan Mute, ColumnSpan Solo, ColumnSpan Volume, ColumnSpan Pan)
    {
        /// <summary>The span of a column by its id ("settings", "kind", "colour", "number", "name", "fx", "mute", "solo", "volume", "pan").</summary>
        public ColumnSpan Of(string id) => id switch
        {
            "settings" => Settings, "kind" => Kind, "colour" => Colour, "number" => Number, "name" => Name,
            "fx" => Fx, "mute" => Mute, "solo" => Solo, "volume" => Volume, "pan" => Pan,
            _ => throw new ArgumentException("Unknown track column " + id, nameof(id)),
        };

        /// <summary>The whole list width: the last column's right edge and the right margin.</summary>
        public double Width => Pan.Right + GridRight;
    }

    private static double FixedWidthOf(string id)
    {
        foreach (var column in Columns)
            if (column.Id == id) return column.Width;
        throw new ArgumentException("Unknown track column " + id, nameof(id));
    }

    /// <summary>The columns of a track row when the name column is <paramref name="nameWidth"/> wide; the fixed widths come from <see cref="Columns"/>.</summary>
    public static TrackRowLayout Layout(double nameWidth)
    {
        var x = GridLeft;
        ColumnSpan Next(string id)
        {
            var width = id == "name" ? nameWidth : FixedWidthOf(id);
            var span = new ColumnSpan(x, width);
            x += width;
            return span;
        }
        return new TrackRowLayout(Next("settings"), Next("kind"), Next("colour"), Next("number"), Next("name"), Next("fx"), Next("mute"), Next("solo"),
            Next("volume"), Next("pan"));
    }

    /// <summary>The list width with the default name column.</summary>
    public static double TotalWidth => TotalWidthFor(NameWidth);

    /// <summary>The whole list width when the name column is <paramref name="nameWidth"/> wide; the other columns keep their widths.</summary>
    public static double TotalWidthFor(double nameWidth) => Layout(nameWidth).Width;

    /// <summary>Left edge and width of a column when the name column is <paramref name="nameWidth"/> wide.</summary>
    public static ColumnSpan Column(string id, double nameWidth = NameWidth) => Layout(nameWidth).Of(id);

    /// <summary>The left edge that centres a title of <paramref name="titleWidth"/> over its column.</summary>
    public static double TitleLeft(string id, double titleWidth, double nameWidth = NameWidth)
    {
        var span = Column(id, nameWidth);
        return span.Left + (span.Width - titleWidth) / 2;
    }

    /// <summary>The box of a cell's control in a row whose top is <paramref name="top"/>, with the WPF control sizes.</summary>
    public static Rect Box(Cell cell, double top, double rowHeight, double nameWidth = NameWidth)
    {
        var layout = Layout(nameWidth);
        Rect Centred(ColumnSpan span, double w, double h) => new(span.Left + (span.Width - w) / 2, top + (rowHeight - h) / 2, w, h);
        switch (cell)
        {
            case Cell.Settings: return Centred(layout.Settings, 24, 24);
            case Cell.Arm: return Centred(layout.Colour, 20, 20);
            case Cell.Fx: { var b = Centred(layout.Fx, 50, 22); return b.WithWidth(b.Width * 0.58); }
            case Cell.FxPower: { var b = Centred(layout.Fx, 50, 22); return new(b.X + b.Width * 0.58, b.Y, b.Width * 0.42, b.Height); }
            case Cell.Mute: return Centred(layout.Mute, 23, 22);
            case Cell.Solo: return Centred(layout.Solo, 23, 22);
            case Cell.Kind: return Centred(layout.Kind, 24, 24);
            default:
                var slider = cell == Cell.Volume ? layout.Volume : layout.Pan;
                return new(slider.Left + SliderMarginLeft, top + (rowHeight - 18) / 2, slider.Width - SliderMarginLeft - SliderMarginRight, 18);
        }
    }

    /// <summary>The slider value the WPF sliders show: volume 0..127, pan -64..63 (the model's 0..127 less the centre).</summary>
    public static int SliderValue(TrackModel track, Cell cell) => cell == Cell.Volume ? Math.Clamp(track.Volume, 0, MidiLimits.Max) : Math.Clamp(track.Pan, 0, MidiLimits.Max) - 64;

    /// <summary>The slider value at <paramref name="x"/> across <paramref name="box"/>, over <paramref name="range"/> (the cell's own range when null).</summary>
    public static int SliderValueAt(Cell cell, Rect box, double x, (int Minimum, int Maximum)? range = null)
    {
        var (minimum, maximum) = range ?? SliderRange(cell);
        // The thumb's centre travels from half a thumb in from each end, as the drawn thumb does.
        var travel = Math.Max(1, box.Width - SliderThumbWidth);
        var t = Math.Clamp((x - box.X - SliderThumbWidth / 2) / travel, 0, 1);
        return minimum + (int)Math.Round(t * (maximum - minimum));
    }

    /// <summary>The x of the thumb's centre for <paramref name="value"/> over <paramref name="range"/> (the inverse of <see cref="SliderValueAt"/>).</summary>
    public static double SliderThumbCentre(Rect box, int value, (int Minimum, int Maximum) range) =>
        box.X + SliderThumbWidth / 2 + Math.Max(1, box.Width - SliderThumbWidth) * (value - range.Minimum) / Math.Max(1, range.Maximum - range.Minimum);

    /// <summary>The pointer's offset from the thumb centre when <paramref name="x"/> is on the thumb of <paramref name="value"/> (a press there grabs the thumb where it is), else 0.</summary>
    public static double SliderGrabOffset(Rect box, int value, (int Minimum, int Maximum) range, double x)
    {
        var offset = SliderThumbCentre(box, value, range) - x;
        return Math.Abs(offset) <= SliderThumbWidth / 2 ? offset : 0;
    }

    /// <summary>The column titles row: the cog, the note icon, the colour dot and the labels, each centred over its column of the layout for <paramref name="nameWidth"/>.</summary>
    public static void DrawHeader(IDrawContext d, Func<string, UiColor> ink, double top, double height, double nameWidth = NameWidth)
    {
        var muted = ink("TextMuted");
        foreach (var (id, label, _) in Columns)
        {
            var (x, w) = Column(id, nameWidth);
            if (id == "settings") Glyph(d, CogGeometry.Value, muted, x + (w - 12) / 2, top + (height - 12) / 2, 12);
            else if (id == "kind") { var t = d.Text("♪", Font, 12, muted, d.PixelsPerDip, weight: 600); d.DrawText(t, x + (w - t.Width) / 2, top + (height - t.Height) / 2); }
            else if (id == "colour") d.Ellipse(ink("Danger"), null, x + w / 2, top + height / 2, 3, 3);
            else
            {
                var t = d.Text(label, Font, 12, muted, d.PixelsPerDip, weight: 600);
                d.DrawText(t, TitleLeft(id, t.Width, nameWidth), top + (height - t.Height) / 2);
            }
        }
    }

    /// <summary>
    /// Draws the hover or pressed state over a cell body that is already drawn: the body's colour blended toward white (hover) or toward the
    /// press colour (pressed), with a brighter edge. Draws nothing when idle; an active colour (muted, soloed, armed) keeps its hue under the blend.
    /// </summary>
    public static void DrawCellState(IDrawContext d, Func<string, UiColor> ink, Rect box, UiColor body, UiColor edge, CellPaint paint, double radius = 4)
    {
        if (paint == CellPaint.Idle) return;
        var pressed = paint == CellPaint.Pressed;
        var fill = ThemeColours.Blend(body, ink(pressed ? "PressSurface" : "OverlayInk"), pressed ? PressedBlend : HoverBlend);
        var rim = ThemeColours.Blend(edge, ink("OverlayInk"), HoverEdgeBlend);
        d.Rect(fill, new UiStroke(rim, 1), box.X, box.Y, box.Width, box.Height, radius);
    }

    public static void DrawCog(IDrawContext d, Func<string, UiColor> ink, Rect box, CellPaint paint = CellPaint.Idle)
    {
        DrawCellState(d, ink, box, ink("InstrumentBackground"), ink("InstrumentBoardEdge"), paint);
        Glyph(d, CogGeometry.Value, ink("TextPrimary"), box.X + (box.Width - 13) / 2, box.Y + (box.Height - 13) / 2, 13);
    }

    /// <summary>The instrument icon cell's hover and pressed state, the same look as the cogwheel: the caller draws the icon art over it.</summary>
    public static void DrawKind(IDrawContext d, Func<string, UiColor> ink, Rect box, CellPaint paint = CellPaint.Idle) =>
        DrawCellState(d, ink, box, ink("InstrumentBackground"), ink("InstrumentBoardEdge"), paint);

    /// <summary>The WPF record-arm dot: a red ring and glowing core when armed, a soft red core when idle.</summary>
    public static void DrawArm(IDrawContext d, Func<string, UiColor> ink, Rect box, bool armed, CellPaint paint = CellPaint.Idle)
    {
        var red = ink("Danger");
        var (cx, cy, r) = (box.Center.X, box.Center.Y, box.Width / 2 - 1);
        if (armed)
        {
            d.Ellipse(red with { A = 0x55 }, null, cx, cy, r + 1, r + 1);
            d.Ellipse(ThemeColours.Shade(red, 0.4), new UiStroke(red, 1.4), cx, cy, r, r);
            DrawCellState(d, ink, box, red, red, paint, box.Width / 2);
            d.Ellipse(red, null, cx, cy, r * 0.45, r * 0.45);
        }
        else
        {
            d.Ellipse(ink("PanelBackground"), new UiStroke(ink("GeneralBorder"), 1.2), cx, cy, r, r);
            DrawCellState(d, ink, box, ink("PanelBackground"), ink("GeneralBorder"), paint, box.Width / 2);
            d.Ellipse(red with { A = 0x90 }, null, cx, cy, r * 0.4, r * 0.4);
        }
    }

    /// <summary>The WPF split FX button: "FX" (accent when the chain is on and holds plug-ins) and the power symbol (green when the chain is on); <paramref name="hoverPart"/> is 0 (FX), 1 (power) or -1.</summary>
    public static void DrawFx(IDrawContext d, Func<string, UiColor> ink, Rect label, Rect power, bool chainOn, int pluginCount, int hoverPart, bool pressed = false)
    {
        var border = ink("TrackToggleEdge");
        d.Rect(ink("TrackToggleFill"), new UiStroke(border, 1), label.X + 0.5, label.Y + 0.5, label.Width + power.Width - 1, label.Height - 1, 4);
        var shade = pressed ? "PressSurface" : "HoverSurface";
        if (hoverPart == 0) d.Rect(ink(shade), null, label.X + 1, label.Y + 1, label.Width - 1, label.Height - 2, 3);
        if (hoverPart == 1) d.Rect(ink(shade), null, power.X, power.Y + 1, power.Width - 1, power.Height - 2, 3);
        d.Line(new UiStroke(border, 1), power.X, power.Y + 3, power.X, power.Bottom - 3);
        var text = d.Text("FX", Font, 11, pluginCount > 0 ? (chainOn ? ink("Accent") : ink("TextPrimary")) : ink("TextMuted"), d.PixelsPerDip, weight: 700);
        d.DrawText(text, label.X + (label.Width - text.Width) / 2, label.Y + (label.Height - text.Height) / 2);
        var pen = new UiStroke(chainOn ? ink("TrackAudible") : ink("TextMuted"), 1.6, RoundCaps: true);
        var (cx, cy) = (power.Center.X, power.Center.Y + 0.5);
        var r = Math.Min(power.Height, power.Width) * 0.28;
        double A(double deg) => deg * Math.PI / 180;
        d.Path(null, pen, d.Paths.BuildPath(s =>
        {
            s.BeginFigure(new UiPoint(cx + r * Math.Sin(A(-60)), cy - r * Math.Cos(A(-60))), false, false);
            s.ArcTo(new UiPoint(cx + r * Math.Sin(A(240)), cy - r * Math.Cos(A(240))), r, r, 0, true, true);
        }));
        d.Line(pen, cx, cy - r - 1.2, cx, cy - r * 0.15);
    }

    /// <summary>The value range of a track's slider: volume 0..127, pan -64..63.</summary>
    public static (int Minimum, int Maximum) SliderRange(Cell cell) => cell == Cell.Volume ? (0, MidiLimits.Max) : (-64, 63);

    /// <summary>The value a double-click resets a track's slider to: volume 100, pan 0 (centre), declared with the mixer's faders in <see cref="MixerStripPresenter"/>.</summary>
    public static int SliderDefault(Cell cell) => cell == Cell.Volume ? MixerStripPresenter.VolumeDefault : MixerStripPresenter.PanDefault;

    /// <summary>
    /// The WPF arrangement slider (App.xaml ArrangementSlider): an 8 DIP rail in the track colours, a 6 DIP fill in the slider fill colour up to
    /// the thumb, and an 18 x 14 DIP thumb that prints the value. The thumb travels the rail's width less its own width. The value runs from
    /// <paramref name="minimum"/> to <paramref name="maximum"/>, as the track's or the master row's range.
    /// </summary>
    public static void DrawSlider(IDrawContext d, Func<string, UiColor> ink, Rect box, int minimum, int maximum, int value, CellPaint paint = CellPaint.Idle)
    {
        var t = Math.Clamp((value - minimum) / (double)(maximum - minimum), 0, 1);
        var cy = box.Center.Y;
        var travel = Math.Max(0, box.Width - SliderThumbWidth);
        var thumbLeft = box.X + t * travel;
        d.Rect(ink("TrackSliderTrack"), new UiStroke(ink("TrackSliderTrackBorder"), 1), box.X, cy - SliderRailHeight / 2, box.Width, SliderRailHeight, SliderRailHeight / 2);
        d.Rect(ink("SliderTrackFill"), null, box.X, cy - SliderFillHeight / 2, Math.Max(0, thumbLeft - box.X), SliderFillHeight, SliderFillHeight / 2);
        d.Rect(ink("SliderThumbFace"), new UiStroke(ink("SliderThumbBorder"), 1), thumbLeft, cy - SliderThumbHeight / 2, SliderThumbWidth, SliderThumbHeight, SliderThumbCorner);
        DrawCellState(d, ink, new Rect(thumbLeft, cy - SliderThumbHeight / 2, SliderThumbWidth, SliderThumbHeight), ink("SliderThumbFace"), ink("SliderThumbBorder"), paint, SliderThumbCorner);
        var label = d.Text(value.ToString(System.Globalization.CultureInfo.InvariantCulture), Font, SliderValueFontSize, ink("SliderThumbText"), d.PixelsPerDip, weight: 600);
        d.DrawText(label, thumbLeft + (SliderThumbWidth - label.Width) / 2, cy - label.Height / 2 - 0.5);
    }

    private const double SliderRailHeight = 8, SliderFillHeight = 6, SliderThumbWidth = 18, SliderThumbHeight = 14, SliderThumbCorner = 2, SliderValueFontSize = 9;

    /// <summary>Draws a 16x16 path glyph of the WPF icon set scaled to <paramref name="size"/>.</summary>
    private static void Glyph(IDrawContext d, IUiPath path, UiColor fill, double x, double y, double size)
    {
        d.PushTranslate(x, y);
        d.PushScale(size / 16, size / 16);
        d.Path(fill, null, path);
        d.Pop();
        d.Pop();
    }

    /// <summary>The WPF IconCog geometry (App.xaml), parsed once. Key: none (one value); ceiling: one geometry; read on the UI thread.</summary>
    private static readonly Lazy<IUiPath> CogGeometry = new(() => new AvaloniaPath(Geometry.Parse(TabForge.Platform.Theme.IconGeometry.Cog)));
}
