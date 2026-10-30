using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
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
    private const double BoardCorner = 3;
    private const double NutWidth = 5;
    private const double FretWireWidth = 1.6;
    private const double InlayRadius = 3.4;
    /// <summary>Light theme: thicker nut, 3px fret wires and bigger inlay dots.</summary>
    private const double LightNutWidth = 7.5, LightFretWireWidth = 3, LightInlayScale = 1.3;
    /// <summary>The brightness setting's full step mixes the lit colour this far toward white.</summary>
    private const double BrightenLimit = 0.5;
    /// <summary>Above this many frets in view, only the odd frets are left unnumbered; a shorter range numbers every fret.</summary>
    private const int EveryFretLabelRange = 6;
    private const double FretNumberTop = 5;
    /// <summary>How solid the ghost dot under the pointer is: a click on it would write a note there.</summary>
    private const double HoverGhostOpacity = 0.4;
    private const double StringLabelFontSize = 14.5;
    private const double StringLabelInset = 12;
    private const double StringLabelLift = 7;

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
        d.Rect(ink("InstrumentWood"), new UiStroke(ink("InstrumentBoardEdge"), 1), board.X, board.Y, board.Width, board.Height, BoardCorner);
        DrawShadedCells(d, ink, layout);
        var light = FretboardPlaybackMarkers.IsLight(ink);
        // Fret wires between the frets, and the nut on fret 1's left edge; both span the board exactly.
        for (var fret = layout.FirstFret; fret <= layout.LastFret + 1; fret++)
        {
            var x = FretboardGeometryRules.FretX(layout, fret - 0.5);
            var nut = fret == 1;
            d.Line(new UiStroke(ink(nut ? "InstrumentNut" : "InstrumentFretWire"), nut ? (light ? LightNutWidth : NutWidth) : (light ? LightFretWireWidth : FretWireWidth)), x, board.Y, x, board.Bottom);
        }
        foreach (var fret in InlayFrets)
        {
            if (fret < layout.FirstFret || fret > layout.LastFret) continue;
            var x = FretboardGeometryRules.FretX(layout, fret);
            var middle = board.Y + board.Height / 2;
            var inlay = light ? ink("FretInlay") : InlayFill(ink);
            var dot = light ? InlayDot * LightInlayScale : InlayDot;
            if (fret % 12 == 0)
            {
                d.Ellipse(inlay, null, x, middle - layout.StringGap / 2, dot, dot);
                d.Ellipse(inlay, null, x, middle + layout.StringGap / 2, dot, dot);
            }
            else d.Ellipse(inlay, null, x, middle, dot, dot);
        }
        // Fret numbers under the board: fret 1 and every even fret (every fret when the range is short).
        for (var fret = layout.FirstFret; fret <= layout.LastFret; fret++)
        {
            if (fret % 2 != 0 && fret != layout.FirstFret && layout.LastFret - layout.FirstFret > EveryFretLabelRange) continue;
            var number = d.Text(fret.ToString(), Font, FretboardSizeRules.FretNumberFontSize(layout.StringGap) * _look.FretNumberScale, ink("FretLabel"), d.PixelsPerDip);
            d.DrawText(number, FretboardGeometryRules.FretX(layout, fret) - number.Width / 2, board.Bottom + FretNumberTop);
        }
        for (var s = 0; s < layout.Strings; s++)
        {
            var y = FretboardGeometryRules.StringY(layout, s);
            // Strings run from the nut to the board's right edge; the same thickness rule.
            d.Line(new UiStroke(ink("InstrumentString"), 0.8 + (layout.Strings - 1 - s) * 0.22), board.X, y, board.Right, y);
            // String labels in the gutter left of the nut, string 1 the highest.
            var label = d.Text(StringLabel(s), Font, StringLabelFontSize, ink("FretLabel"), d.PixelsPerDip);
            d.DrawText(label, board.X - FretboardGeometryRules.LeftGutter + StringLabelInset, y - StringLabelLift);
        }
        DrawLegend(d, ink, board);
        DrawScaleMarks(d, ink, layout, Math.Min(ScaleMarkMaxRadius, layout.StringGap * FretboardMarkerSizeRules.MaxRadiusShareOfGap));
        var radius = MarkerGeometry(PlaybackMarkerRole.Now, layout).Radius;
        if (_playbackShown)
        {
            DrawPlayingNotes(d, ink, layout);
            DrawHoverGhost(d, ink, layout, radius);   // the write preview shows over the playback dots too
            return;
        }
        foreach (var n in _lit)
        {
            if (n.Dead) MarkDead(d, ink, layout, n.StringIndex, n.Fret, radius);
            else MarkDot(d, ink, layout, n.StringIndex, n.Fret, ink("InstrumentCurrent"), radius);
        }
        DrawHoverGhost(d, ink, layout, radius);
        DrawLandingPreview(d, ink);
    }

    /// <summary>The translucent grey ghost dot under the pointer, with its fret number: where a click writes a note. Grey (the legible-text token), so it never reads as a playback dot.</summary>
    private void DrawHoverGhost(IDrawContext d, Func<string, UiColor> ink, FretboardGeometryRules.Layout layout, double radius)
    {
        if (_hover is not { } hover) return;
        MarkFret(d, ink, layout, hover.String, hover.Fret, ink("InstrumentLegible").WithOpacity(HoverGhostOpacity), radius);
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
                FretboardPlaybackMarkers.DrawUpcoming(d, ink, layout, n, LabelOf(n), g, NumberSize(PlaybackMarkerRole.Upcoming, g), UpcomingBrightness(_playing.UpcomingDepth[i]));
            }
            foreach (var n in _playing.Next)
            {
                if (FretboardMovementRules.Repeats(_playing.Now, n)) continue;
                var g = MarkerGeometry(PlaybackMarkerRole.Next, layout);
                FretboardPlaybackMarkers.DrawNext(d, ink, layout, n, LabelOf(n), g, NumberSize(PlaybackMarkerRole.Next, g));
                FretboardPlaybackMarkers.DrawTag(d, ink, layout, n, g, _look.FretNumberScale, ink("InstrumentNext"));
            }
        }
        foreach (var n in _playing.Now)
        {
            var g = MarkerGeometry(PlaybackMarkerRole.Now, layout);
            FretboardPlaybackMarkers.DrawNow(d, ink, layout, n, LabelOf(n), g, NumberSize(PlaybackMarkerRole.Now, g));
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
        MarkFret(d, ink, layout, stringIndex, fret, fill, radius, NoteNameOf(stringIndex, fret));
    }

    /// <summary>The lit-note fill: the marker colour setting, brightened by the brightness setting.</summary>
    private UiColor InlayFill(Func<string, UiColor> ink) => ThemeColours.Brighten(ink(_look.MarkerColourKey), _look.MarkerBrightness * BrightenLimit);

    /// <summary>The inlay dot's radius: the inlay radius scaled by the marker size setting.</summary>
    private double InlayDot => InlayRadius * _look.MarkerSizePercent / 100.0;

    /// <summary>One fret marker: a filled bubble with its label inside (the fret number unless a note name is given), inked for contrast on the bubble's colour.
    /// The label's font size follows the radius (see <see cref="FretboardMarkerSizeRules.LabelFontSize"/>), so a two-digit number still fits inside the dot.</summary>
    internal static void MarkFret(IDrawContext d, Func<string, UiColor> ink, FretboardGeometryRules.Layout layout, int stringIndex, int fret, UiColor fill, double radius, string? label = null)
    {
        if (stringIndex < 0 || stringIndex >= layout.Strings) return;
        var p = FretboardGeometryRules.PositionOf(layout, stringIndex, fret);
        d.Ellipse(fill, new UiStroke(ink("InstrumentBackground"), 1.5), p.X, p.Y, radius, radius);
        // The bubble is drawn opaque for the ink choice, so a faded recent dot still gets a legible number.
        var opaque = new UiColor(255, fill.R, fill.G, fill.B);
        var text = d.Text(label ?? fret.ToString(), Font, FretboardMarkerSizeRules.LabelFontSize(radius), ThemeColours.InkOn(opaque), d.PixelsPerDip, weight: 700);
        d.DrawText(text, p.X - text.Width / 2, p.Y - text.Height / 2);
    }

    /// <summary>Top of the colour legend: bottom-aligned with the board, with the Scales button under it, never above the board's top strip.</summary>
    /// <remarks>The Scales button is 24 real pixels, so it is that many virtual DIPs at the drawing scale.</remarks>
    private double LegendTop(UiRect board) => Math.Max(LegendTopMinimum, board.Bottom - (LegendRowCount * LegendRowHeight + LegendScalesGap + ScalesButtonHeight / FretboardScale()));

    /// <summary>The colour legend beside the board: now, next, upcoming and recent while previewing, else now and recent only
    /// (the legend lists only what can appear), in the palette colours the legend uses.</summary>
    private void DrawLegend(IDrawContext d, Func<string, UiColor> ink, UiRect board)
    {
        var rows = new List<(string Key, string Label)> { ("InstrumentCurrent", "now") };
        if (_look.PreviewHorizon >= 0) { rows.Add(("InstrumentNext", "next")); rows.Add(("InstrumentAccent", "upcoming")); }
        if (_look.PreviewPrevious) rows.Add(("InstrumentPast", "recent"));
        var x = board.Right + LegendGap;
        var top = LegendTop(board);
        for (var i = 0; i < rows.Count; i++)
        {
            var y = top + i * LegendRowHeight;
            d.Ellipse(ink(rows[i].Key), new UiStroke(ink("InstrumentWood"), 1.5), x + 4, y + 7, 4.5, 4.5);
            var label = d.Text(rows[i].Label, Font, 12, ink("InstrumentLegible"), d.PixelsPerDip);
            d.DrawText(label, x + 14, y + (LegendRowHeight - label.Height) / 2);
        }
    }

    /// <summary>Where the Scales button goes on a fretboard: under the legend, in panel coordinates. Null for the keyboard and drums.</summary>
    public UiPoint? ScalesAnchor()
    {
        if (_kind != InstrumentPanelKind.Fretboard) return null;
        var board = FretboardLayout().Board;
        var s = FretboardScale();
        return new UiPoint((board.Right + LegendGap - 2) * s, (LegendTop(board) + LegendRowCount * LegendRowHeight + LegendScalesGap) * s);
    }
}
