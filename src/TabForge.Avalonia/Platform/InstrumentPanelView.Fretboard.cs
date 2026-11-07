using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Playback;
using TabForge.Presenters;
using TabForge.Services;
using TabForge.Views.Score;

namespace TabForge.Avalonia.Platform;

/// <summary>
/// The fretboard drawing of the instrument panel: the scaled board with its fret wires, nut, inlays, fret and string labels and colour legend,
/// the scale marks, the lit and pointer-hover markers, and the playing notes in their five roles, with the drawing sizes they use.
/// Owns: the fretboard drawing and its marker, legend and scale-mark helpers.
/// Does not own: the panel's kind, look, picking and layout (InstrumentPanelView.cs), the labels (InstrumentPanelView.Labels.cs), the keyboard
/// and drum layouts (InstrumentPanelView.Layouts.cs) or the playing note lists (InstrumentPanelView.Playback.cs).
/// Tests: TabForge.Avalonia.Tests.InstrumentPanelViewTests, TabForge.Avalonia.Tests.InstrumentFretNumberTests, TabForge.Avalonia.Tests.FretboardPlaybackMarkerTests.
/// </summary>
public sealed partial class InstrumentPanelView
{
    // Fretboard drawing sizes (virtual DIPs).
    /// <summary>The room a fret number needs beyond its own width inside its cell, in DIPs.</summary>
    private const double FretLabelClearance = 4;
    private const double FretNumberTop = 5;
    /// <summary>How solid the ghost dot under the pointer is: a click on it would write a note there.</summary>
    private const double HoverGhostOpacity = 0.4;
    private const double StringLabelFontSize = 14.5;
    private const double StringLabelInset = 12;
    private const double StringLabelLift = 7;

    // The wood, binding and capo drawing lives in InstrumentPanelView.FretboardWood.cs (decoded once for all
    // panels; missing or unreadable resources leave the themed flat fill in place).

    /// <summary>The fretboard in its virtual space, scaled up to the pane.</summary>
    private void DrawScaledFretboard(IDrawContext d, Func<string, UiColor> ink)
    {
        var s = FretboardScale();
        d.PushScale(s, s);
        DrawFretboard(d, ink);
        d.Pop();
    }

    private void DrawFretboard(IDrawContext d, Func<string, UiColor> ink)
    {
        var layout = FretboardLayout();
        var board = layout.Board;
        var scene = new FretboardScene(d, ink, layout, _look.Style, FretInput.Bass, _assets, _inlays, _look.MarkerBrightness);
        if (!DrawCachedBoard(d, ink, layout))
        {
            FretboardArtist.DrawBackdrop(scene);
            FretboardArtist.DrawInlays(scene);
            FretboardArtist.DrawWires(scene);
        }
        // Fret numbers under the wood: fret 1 and every even fret (every fret when the range is short).
        var woodBottom = FretboardArtist.Wood(layout, scene.Bass).Bottom;
        for (var fret = layout.FirstFret; fret <= layout.LastFret; fret++)
        {
            var number = d.Text(FretLabel(fret), Font, FretboardSizeRules.FretNumberFontSize(layout.StringGap) * _look.FretNumberScale, ink("FretLabel"), d.PixelsPerDip);
            var cell = Math.Abs(FretboardGeometryRules.WireX(layout, fret) - FretboardGeometryRules.WireX(layout, fret - 1));
            // A number shows when it fits its fret cell; the crowded high frets keep the even ones (and fret 1) as before.
            if (cell < number.Width + FretLabelClearance && (fret % 2 != 0 || cell < number.Width * 0.8) && fret != layout.FirstFret) continue;
            d.DrawText(number, FretboardGeometryRules.FretX(layout, fret) - number.Width / 2, woodBottom + FretNumberTop);
        }
        // Sounding strings vibrate for their note's duration (frame-driven phase; labels stay put), as far and as fast as the settings allow.
        FretboardArtist.DrawStrings(scene, _wobble ??= StringWobble);
        if (_look.StringNumbers)
            for (var s = 0; s < layout.Strings; s++)
            {
                var label = d.Text(StringLabel(s), Font, StringLabelFontSize, ink("FretLabel"), d.PixelsPerDip);
                d.DrawText(label, board.X - StringLabelInset - label.Width - 8, FretboardGeometryRules.StringY(layout, s) - StringLabelLift);
            }
        DrawBehindCapo(d, ink, layout, FretboardArtist.Wood(layout, scene.Bass));
        DrawCapo(d, ink, layout);
        FretboardArtist.DrawBrightness(scene, _look.BoardBrightness);
        DrawShadedCells(d, ink, layout);
        var noteInk = NoteInk(ink);
        DrawLegend(d, noteInk, FretboardArtist.Frame(layout, scene.Bass, _look.Style));   // as tall as the whole neck, binding included
        DrawScaleMarks(d, ink, layout, Math.Min(ScaleMarkMaxRadius, layout.StringGap * FretboardMarkerSizeRules.MaxRadiusShareOfGap));
        var radius = MarkerGeometry(PlaybackMarkerRole.Now, layout).Radius;
        if (_playbackShown)
        {
            DrawPlayingNotes(d, noteInk, layout);
            DrawHoverGhost(d, ink, layout, radius);   // the write preview shows over the playback dots too
            return;
        }
        foreach (var n in _lit)
        {
            if (n.Dead) MarkDead(d, noteInk, layout, n.StringIndex, n.Fret, radius);
            else MarkDot(d, noteInk, layout, n.StringIndex, n.Fret, noteInk("InstrumentCurrent"), radius);
        }
        DrawHoverGhost(d, ink, layout, radius);
        DrawLandingPreview(d, ink);
    }

    /// <summary>Dims the neck behind each string's capo: nothing can be played there, and written frets start at the capo.</summary>
    private static void DrawBehindCapo(IDrawContext d, Func<string, UiColor> ink, FretboardGeometryRules.Layout layout, UiRect wood) =>
        Controls.FretboardBoardPainter.DrawBehindCapo(d, ink, layout, wood);

    /// <summary>The cached vibration delegate: a lambda here would allocate on every frame.</summary>
    private Func<int, (double Offset, int Fret, double Blur)>? _wobble;

    /// <summary>One string's very slight sideways offset and its blur: its sounding note's vibration when playback shows and vibration is on, else still.</summary>
    internal (double Offset, int Fret, double Blur) StringWobble(int stringIndex)
    {
        if (_look.VibrationPeak <= 0) return default;
        if (PreviewVibration(stringIndex) is { } preview) return preview;
        if (_playbackShown)
            foreach (var sounding in _playing.Now)
                if (sounding.StringIndex == stringIndex) return (VibrationOffset(sounding), sounding.Fret, RingBlur(sounding));
        return default;
    }

    /// <summary>The translucent grey ghost dot under the pointer, with its fret number: where a click writes a note. Grey (the legible-text token), so it never reads as a playback dot.</summary>
    private void DrawHoverGhost(IDrawContext d, Func<string, UiColor> ink, FretboardGeometryRules.Layout layout, double radius)
    {
        if (_hover is not { } hover) return;
        MarkFret(d, ink, layout, hover.String, hover.Fret, ink("InstrumentLegible").WithOpacity(HoverGhostOpacity), radius, NoteNameOf(hover.String, hover.Fret));
    }

    /// <summary>The playing notes on the board: recent faint, the sounding notes (or the released one at a rest) on top;
    /// the next movement, the upcoming moves, their let-ring tags and the hand's movement line only while previewing.</summary>

    private void DrawPlayingNotes(IDrawContext d, Func<string, UiColor> ink, FretboardGeometryRules.Layout layout)
    {
        var preview = _look.PreviewHorizon >= 0;
        if (preview) foreach (var move in _movement) FretboardPlaybackMarkers.DrawMovement(d, ink, layout, move.From, move.To);
        if (_look.PreviewPrevious) foreach (var n in _playing.Recent)
        {
            var g = MarkerGeometry(PlaybackMarkerRole.Recent, layout);
            FretboardPlaybackMarkers.DrawRecent(d, ink, layout, n, LabelOf(n), g, NumberSize(PlaybackMarkerRole.Recent, g), RecentBrightness(n));
        }
        if (preview)
        {
            for (var i = 0; i < _playing.Upcoming.Count; i++)
            {
                var n = _playing.Upcoming[i];
                var g = MarkerGeometry(PlaybackMarkerRole.Upcoming, layout);
                FretboardPlaybackMarkers.DrawUpcoming(d, ink, layout, n, LabelOf(n), g, NumberSize(PlaybackMarkerRole.Upcoming, g), UpcomingBrightness(_playing.UpcomingDepth[i]), _look.RingDots);
            }
            foreach (var n in _playing.Next)
            {
                if (FretboardMovementRules.Repeats(_playing.Now, n)) continue;
                var g = MarkerGeometry(PlaybackMarkerRole.Next, layout);
                FretboardPlaybackMarkers.DrawNext(d, ink, layout, n, LabelOf(n), g, NumberSize(PlaybackMarkerRole.Next, g), _look.RingDots);
                FretboardPlaybackMarkers.DrawTag(d, ink, layout, n, g, _look.FretNumberScale, ink("InstrumentNext"));
            }
        }
        foreach (var n in _playing.Now)
        {
            var g = MarkerGeometry(PlaybackMarkerRole.Now, layout);
            FretboardPlaybackMarkers.DrawNow(d, ink, layout, n, LabelOf(n), g, NumberSize(PlaybackMarkerRole.Now, g), _look.RingDots);
            FretboardPlaybackMarkers.DrawTag(d, ink, layout, n, g, _look.FretNumberScale, ink("InstrumentCurrent"));
        }
        foreach (var n in _playing.Released)
        {
            var g = MarkerGeometry(PlaybackMarkerRole.ReleasedNow, layout);
            FretboardPlaybackMarkers.DrawReleasedNow(d, ink, layout, n, LabelOf(n), g, NumberSize(PlaybackMarkerRole.ReleasedNow, g));
        }
    }

    /// <summary>The radius and size factor of a role's marker on this board, from the number scale and the marker size setting.</summary>
    private FretboardPlaybackMarkers.Geometry MarkerGeometry(PlaybackMarkerRole role, FretboardGeometryRules.Layout layout) =>
        FretboardPlaybackMarkers.GeometryOf(role, layout.FretWidth, _look.FretNumberScale, FretboardMarkerSizeRules.Scale(_look.MarkerSizePercent), layout.StringGap);

    private double NumberSize(PlaybackMarkerRole role, FretboardPlaybackMarkers.Geometry g) => FretboardPlaybackMarkers.NumberSizeOf(role, g, _look.FretNumberScale);

    /// <summary>A marker on a sounding note: the note name inside the dot when note names are on, else the fret number.</summary>
    private void MarkDot(IDrawContext d, Func<string, UiColor> ink, FretboardGeometryRules.Layout layout, int stringIndex, int fret, UiColor fill, double radius)
    {
        if (_look.RingDots && stringIndex >= 0 && stringIndex < layout.Strings)
        {
            var at = FretboardGeometryRules.PositionOfTab(layout, stringIndex, fret);
            d.Ellipse(null, new UiStroke(fill, FretboardPlaybackMarkers.RingDotWidth), at.X, at.Y, radius, radius);
            var text = d.Text(NoteNameOf(stringIndex, fret) ?? (fret + layout.CapoOf(stringIndex)).ToString(), Font, FretboardMarkerSizeRules.LabelFontSize(radius), ThemeColours.InkOn(ink("InstrumentBoard")), d.PixelsPerDip, weight: 700);
            d.DrawText(text, at.X - text.Width / 2, at.Y - text.Height / 2);
            return;
        }
        MarkFret(d, ink, layout, stringIndex, fret, fill, radius, NoteNameOf(stringIndex, fret));
    }

    // A cached delegate avoids allocating in steady playback frames. Only role colours change: background/theme detection and text stay intact.
    private Func<string, UiColor>? _baseNoteInk, _adjustedNoteInk;
    private Func<string, UiColor> NoteInk(Func<string, UiColor> ink)
    {
        if (_look.NoteBrightness == 100 && !_look.HasNoteColours) return ink;
        _baseNoteInk = ink;
        return _adjustedNoteInk ??= AdjustedNoteColour;
    }

    // The person's own note colours replace the theme's role colours (the legend and every dot read them here), then the note brightness applies.
    private UiColor AdjustedNoteColour(string key)
    {
        var own = key switch
        {
            "InstrumentCurrent" or "FretNow" => _look.NowColour,
            "InstrumentNext" => _look.NextColour,
            "InstrumentAccent" => _look.UpcomingColour,
            _ => null,
        };
        var colour = own ?? _baseNoteInk!(key);
        if (key is not ("InstrumentCurrent" or "FretNow" or "InstrumentNext" or "InstrumentAccent" or "InstrumentPast")) return colour;
        if (_look.NoteBrightness == 100) return colour;
        var alpha = FretboardStyles.BrightnessVeil(_look.NoteBrightness);
        if (_look.NoteBrightness >= 100) return ThemeColours.Brighten(colour, alpha);
        return new UiColor(colour.A, (byte)Math.Round(colour.R * (1 - alpha)), (byte)Math.Round(colour.G * (1 - alpha)), (byte)Math.Round(colour.B * (1 - alpha)));
    }

    /// <summary>One fret marker: a filled bubble with its label inside (the fret's real place on the neck, its written fret plus its string's capo, unless a note name is given), inked for contrast on the bubble's colour.
    /// The label's font size follows the radius (see <see cref="FretboardMarkerSizeRules.LabelFontSize"/>), so a two-digit number still fits inside the dot.</summary>
    internal static void MarkFret(IDrawContext d, Func<string, UiColor> ink, FretboardGeometryRules.Layout layout, int stringIndex, int fret, UiColor fill, double radius, string? label = null)
    {
        if (stringIndex < 0 || stringIndex >= layout.Strings) return;
        var p = FretboardGeometryRules.PositionOfTab(layout, stringIndex, fret);
        d.Ellipse(fill, new UiStroke(ink("InstrumentBackground"), 1.5), p.X, p.Y, radius, radius);
        // The bubble is drawn opaque for the ink choice, so a faded recent dot still gets a legible number.
        var opaque = new UiColor(255, fill.R, fill.G, fill.B);
        var text = d.Text(label ?? (fret + layout.CapoOf(stringIndex)).ToString(), Font, FretboardMarkerSizeRules.LabelFontSize(radius), ThemeColours.InkOn(opaque), d.PixelsPerDip, weight: 700);
        d.DrawText(text, p.X - text.Width / 2, p.Y - text.Height / 2);
    }

    /// <summary>
    /// The legend card's frame beside the board. Unit: 1 at the design size, smaller when the board is shorter so the card never grows taller than the board (never below
    /// <see cref="LegendMinUnit"/>); it sizes the text, the width and the gaps. TileH: the height of the colour tiles and of the Scales and Modify tiles, which also takes up any height the board has beyond the
    /// design size, so the card is as tall as the board at every size. Left and Top: the card's edges, bottom-aligned with the board and never above the board's top strip.
    /// </summary>
    private readonly record struct LegendBox(double Unit, double Left, double Top, double TileH, double Height)
    {
        public double TilesTop(double pad, double title) => Top + (pad + title) * Unit;
    }

    private LegendBox LegendFrame(UiRect board)
    {
        var design = LegendTitleHeight + LegendRowsHeight + LegendScalesGap + LegendTileHeight + 2 * LegendCardPad;
        var unit = Math.Clamp(board.Height / design, LegendMinUnit, 1);
        var stretch = Math.Max(0, board.Height - design * unit) / 2;   // the board's spare height, shared by the two tile rows
        var height = design * unit + 2 * stretch;
        return new LegendBox(unit, board.Right + LegendGap - LegendCardPad, Math.Max(LegendTopMinimum, board.Bottom - height), LegendTileHeight * unit + stretch, height);
    }

    /// <summary>Where the Scales and Modify tiles start: under the colour tiles and the recent row, when it is listed.</summary>
    private double LegendActionsTop(LegendBox box) =>
        box.TilesTop(LegendCardPad, LegendTitleHeight) + box.TileH + (_look.PreviewPrevious ? LegendRowHeight * box.Unit : 0) + LegendScalesGap * box.Unit;

    // The legend never shrinks below this fraction of its design size, so its names stay readable.
    private const double LegendMinUnit = 0.45;

    /// <summary>The colour legend beside the board: now, next, upcoming and recent while previewing, else now and recent only
    /// (the legend lists only what can appear), in the palette colours the legend uses.</summary>
    private void DrawLegend(IDrawContext d, Func<string, UiColor> ink, UiRect board)
    {
        if (_look.LegendMinimised) return;   // only the round restore button shows (FretboardLegendToggle)
        var rows = LegendRowVariants[(_look.PreviewHorizon >= 0 ? 2 : 0) | (_look.PreviewPrevious ? 1 : 0)];
        var box = LegendFrame(board);
        var (u, left, top) = (box.Unit, box.Left, box.Top);
        var x = left + LegendCardPad * u;        // the content's left edge
        var y0 = top + LegendCardPad * u;        // the content's top edge
        // The card: rounded, with its title over a thin rule; the colour tiles and the Scales and Modify tiles sit inside it.
        d.Rect(ink("FretboardCardFill"), new UiStroke(ink("FretboardCardStroke"), 1), left, top, LegendCardWidth * u, box.Height, 8 * u);
        var title = d.Text("Legend", Font, 12.5 * u, ink("FretboardCardTitle"), d.PixelsPerDip, weight: 600);
        d.DrawText(title, x + 2 * u, y0 + (LegendTitleHeight * u - title.Height) / 2 - 2 * u);
        d.Line(new UiStroke(ink("FretboardCardStroke"), 1), left + 6 * u, y0 + (LegendTitleHeight - 3) * u, left + (LegendCardWidth - 6) * u, y0 + (LegendTitleHeight - 3) * u);
        var tiles = rows.Length - (_look.PreviewPrevious ? 1 : 0);
        var ty = y0 + LegendTitleHeight * u;
        var inner = (box.TileH - LegendTileHeight * u) / 2;   // a taller tile keeps its dot and name centred
        for (var i = 0; i < tiles; i++)
        {
            // One tile per colour: tinted and edged in its own colour, the dot over its name.
            var (tx, tw) = LegendTileSpan(left, i, tiles, u);
            var colour = ink(rows[i].Key);
            d.Rect(new UiColor(LegendTileTint, colour.R, colour.G, colour.B), new UiStroke(new UiColor(LegendTileEdge, colour.R, colour.G, colour.B), 1), tx, ty, tw, box.TileH, LegendTileRadius * u);
            d.Ellipse(colour, new UiStroke(ink("InstrumentWood"), 1.5), tx + tw / 2, ty + inner + 14 * u, (LegendDotRadius + 1) * u, (LegendDotRadius + 1) * u);
            var name = d.Text(rows[i].Label, Font, 11 * u, ink("InstrumentLegible"), d.PixelsPerDip);
            d.DrawText(name, tx + (tw - name.Width) / 2, ty + inner + 25 * u);
        }
        if (!_look.PreviewPrevious) return;
        // The recent colour has no setting, so it stays a plain row under the tiles.
        var y = ty + box.TileH;
        d.Ellipse(ink(rows[^1].Key), new UiStroke(ink("InstrumentWood"), 1.5), x + LegendDotX * u, y + LegendDotY * u, LegendDotRadius * u, LegendDotRadius * u);
        var label = d.Text(rows[^1].Label, Font, 12 * u, ink("InstrumentLegible"), d.PixelsPerDip);
        d.DrawText(label, x + 17 * u, y + (LegendRowHeight * u - label.Height) / 2);
    }

    // Tile alphas: the tint under the dot and name, and its edge.
    private const byte LegendTileTint = 0x33, LegendTileEdge = 0x88;

    /// <summary>The left edge and width of tile <paramref name="index"/> of <paramref name="count"/>, inside a card whose left edge is <paramref name="cardLeft"/>, at legend unit <paramref name="unit"/> (board units).</summary>
    private static (double X, double Width) LegendTileSpan(double cardLeft, int index, int count, double unit)
    {
        var gap = LegendTileGap * unit;
        var width = (LegendCardWidth * unit - 2 * LegendRowInset * unit - gap * (count - 1)) / count;
        return (cardLeft + LegendRowInset * unit + index * (width + gap), width);
    }

    /// <summary>The height of the legend's colour block at the design size: the tile row, and the recent row under it while it is listed.</summary>
    private double LegendRowsHeight => LegendTileHeight + (_look.PreviewPrevious ? LegendRowHeight : 0);

    // Immutable variants; never retain theme colours, sizes or per-panel state in this shared table.
    private static readonly ImmutableArray<ImmutableArray<(string Key, string Label)>> LegendRowVariants =
    [
        [("InstrumentCurrent", "Now")],
        [("InstrumentCurrent", "Now"), ("InstrumentPast", "Recent")],
        [("InstrumentCurrent", "Now"), ("InstrumentNext", "Next"), ("InstrumentAccent", "Upcoming")],
        [("InstrumentCurrent", "Now"), ("InstrumentNext", "Next"), ("InstrumentAccent", "Upcoming"), ("InstrumentPast", "Recent")],
    ];

    // A legend row's dot: its centre from the row's left and top, and its radius (board units).
    private const double LegendDotX = 6, LegendDotY = 7, LegendDotRadius = 4.5;

    // A clickable legend row is inset this far from the card's sides (board units), so it stays inside the card.
    private const double LegendRowInset = 8;

    /// <summary>The neck's whole visible extent (wood and binding), which the legend matches in height.</summary>
    internal UiRect LegendBoard() => FretboardArtist.Frame(FretboardLayout(), FretInput.Bass, _look.Style);

    /// <summary>The legend tile for <paramref name="role"/> in the row of colour tiles, in panel coordinates: null on the keyboard and
    /// drums, and for next and upcoming while the legend does not list them (look-ahead off).</summary>
    public UiRect? LegendRowBounds(FretNoteRole role)
    {
        if (_kind != InstrumentPanelKind.Fretboard || _look.LegendMinimised) return null;
        var row = role == FretNoteRole.Now ? 0 : _look.PreviewHorizon < 0 ? -1 : role == FretNoteRole.Next ? 1 : 2;
        if (row < 0) return null;
        var board = LegendBoard();
        var s = FretboardScale();
        var box = LegendFrame(board);
        var (x, w) = LegendTileSpan(box.Left, row, _look.PreviewHorizon < 0 ? 1 : 3, box.Unit);
        return new UiRect(x * s, box.TilesTop(LegendCardPad, LegendTitleHeight) * s, w * s, box.TileH * s);
    }

    /// <summary>Where the Scales and Modify buttons go on a fretboard: two tiles in one row under the colour tiles, the same height and corners, in panel coordinates, and the size of one legend unit
    /// in real pixels (the drawing scale times the legend's own unit), which sizes their icon and text. Null for the keyboard and drums.</summary>
    public (UiRect Scales, UiRect Customize, double Unit)? ActionTileBounds()
    {
        if (_kind != InstrumentPanelKind.Fretboard || _look.LegendMinimised) return null;
        var board = LegendBoard();
        var s = FretboardScale();
        var box = LegendFrame(board);
        var y = LegendActionsTop(box) * s;
        UiRect Tile(int index) { var (x, w) = LegendTileSpan(box.Left, index, 2, box.Unit); return new UiRect(x * s, y, w * s, box.TileH * s); }
        return (Tile(0), Tile(1), box.Unit * s);
    }

    /// <summary>True while the fretboard's legend is minimised to its round button.</summary>
    internal bool LegendMinimised => _kind == InstrumentPanelKind.Fretboard && _look.LegendMinimised;

    // The minimised legend's round button, in board units at the legend's unit; the minimise button in the title row.
    private const double LegendRestoreSize = 30, LegendMinimiseSize = 18;

    /// <summary>
    /// Where the legend's minimise and restore button goes, in panel coordinates, and the legend unit in real pixels: a small button at the right of the title row while the legend is open,
    /// the round restore button where the card's bottom-left corner was while it is minimised. Null for the keyboard and drums.
    /// </summary>
    public (UiRect Rect, bool Minimised, double Unit)? LegendToggleBounds()
    {
        if (_kind != InstrumentPanelKind.Fretboard) return null;
        var board = LegendBoard();
        var s = FretboardScale();
        var box = LegendFrame(board);
        var (u, left, top) = (box.Unit, box.Left, box.Top);
        if (_look.LegendMinimised)
        {
            var cardBottom = top + box.Height;
            return (new UiRect(left * s, (cardBottom - LegendRestoreSize * u) * s, LegendRestoreSize * u * s, LegendRestoreSize * u * s), true, u * s);
        }
        var size = LegendMinimiseSize * u;
        return (new UiRect((left + LegendCardWidth * u - 6 * u - size) * s, (top + LegendCardPad * u + (LegendTitleHeight * u - size) / 2 - 1.5 * u) * s, size * s, size * s), false, u * s);
    }
}
