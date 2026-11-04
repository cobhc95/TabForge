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
    /// <summary>Above this many frets in view, only the odd frets are left unnumbered; a shorter range numbers every fret.</summary>
    private const int EveryFretLabelRange = 6;
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
        var scene = new FretboardScene(d, ink, layout, _look.Style, FretInput.Bass, _assets, _inlays, _look.MarkerColourKey, _look.MarkerBrightness);
        FretboardArtist.DrawBackdrop(scene);
        FretboardArtist.DrawInlays(scene);
        FretboardArtist.DrawWires(scene);
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
        DrawLegend(d, noteInk, board);
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
    private Func<int, (double Offset, int Fret)>? _wobble;

    /// <summary>One string's sideways offset: its sounding note's vibration when playback shows and vibration is on, else still.</summary>
    internal (double Offset, int Fret) StringWobble(int stringIndex)
    {
        if (_look.VibrationPeak <= 0) return default;
        if (PreviewVibration(stringIndex) is { } preview) return preview;
        if (_playbackShown)
            foreach (var sounding in _playing.Now)
                if (sounding.StringIndex == stringIndex) return (VibrationOffset(sounding), sounding.Fret);
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

    // A cached delegate avoids allocating in steady playback frames. Only role colours change: background/theme detection and text stay intact.
    private Func<string, UiColor>? _baseNoteInk, _adjustedNoteInk;
    private Func<string, UiColor> NoteInk(Func<string, UiColor> ink)
    {
        if (_look.NoteBrightness == 100) return ink;
        _baseNoteInk = ink;
        return _adjustedNoteInk ??= AdjustedNoteColour;
    }

    private UiColor AdjustedNoteColour(string key)
    {
        var colour = _baseNoteInk!(key);
        if (key is not ("InstrumentCurrent" or "FretNow" or "InstrumentNext" or "InstrumentAccent" or "InstrumentPast")) return colour;
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

    /// <summary>Top of the colour legend: bottom-aligned with the board, with the Scales button under it, never above the board's top strip.</summary>
    /// <remarks>The Scales button is 24 real pixels, so it is that many virtual DIPs at the drawing scale.</remarks>
    private double LegendTop(UiRect board) => Math.Max(LegendTopMinimum, board.Bottom - (LegendTitleHeight + LegendRowCount * LegendRowHeight + LegendScalesGap + (ScalesButtonHeight * 2 + ButtonStackGap) / FretboardScale()));

    /// <summary>The colour legend beside the board: now, next, upcoming and recent while previewing, else now and recent only
    /// (the legend lists only what can appear), in the palette colours the legend uses.</summary>
    private void DrawLegend(IDrawContext d, Func<string, UiColor> ink, UiRect board)
    {
        var rows = LegendRowVariants[(_look.PreviewHorizon >= 0 ? 2 : 0) | (_look.PreviewPrevious ? 1 : 0)];
        var x = board.Right + LegendGap;
        var top = LegendTop(board);
        // The card: rounded, with its title over a thin rule; the rows and the Scales button sit inside it.
        var height = LegendTitleHeight + LegendRowCount * LegendRowHeight + LegendScalesGap + (ScalesButtonHeight * 2 + ButtonStackGap) / FretboardScale() + LegendCardPad;
        d.Rect(ink("FretboardCardFill"), new UiStroke(ink("FretboardCardStroke"), 1), x - LegendCardPad, top - LegendCardPad, LegendCardWidth, height + LegendCardPad, 8);
        var title = d.Text("Legend", Font, 12.5, ink("FretboardCardTitle"), d.PixelsPerDip, weight: 600);
        d.DrawText(title, x + 2, top + (LegendTitleHeight - title.Height) / 2 - 2);
        d.Line(new UiStroke(ink("FretboardCardStroke"), 1), x - LegendCardPad + 6, top + LegendTitleHeight - 3, x - LegendCardPad + LegendCardWidth - 6, top + LegendTitleHeight - 3);
        for (var i = 0; i < rows.Length; i++)
        {
            var y = top + LegendTitleHeight + i * LegendRowHeight;
            d.Ellipse(ink(rows[i].Key), new UiStroke(ink("InstrumentWood"), 1.5), x + 6, y + 7, 4.5, 4.5);
            var label = d.Text(rows[i].Label, Font, 12, ink("InstrumentLegible"), d.PixelsPerDip);
            d.DrawText(label, x + 17, y + (LegendRowHeight - label.Height) / 2);
        }
    }

    // Immutable variants; never retain theme colours, sizes or per-panel state in this shared table.
    private static readonly ImmutableArray<ImmutableArray<(string Key, string Label)>> LegendRowVariants =
    [
        [("InstrumentCurrent", "now")],
        [("InstrumentCurrent", "now"), ("InstrumentPast", "recent")],
        [("InstrumentCurrent", "now"), ("InstrumentNext", "next"), ("InstrumentAccent", "upcoming")],
        [("InstrumentCurrent", "now"), ("InstrumentNext", "next"), ("InstrumentAccent", "upcoming"), ("InstrumentPast", "recent")],
    ];

    /// <summary>Where the Scales button goes on a fretboard: under the legend, in panel coordinates. Null for the keyboard and drums.</summary>
    public UiPoint? ScalesAnchor()
    {
        if (_kind != InstrumentPanelKind.Fretboard) return null;
        var board = FretboardLayout().Board;
        var s = FretboardScale();
        return new UiPoint((board.Right + LegendGap - 2) * s, (LegendTop(board) + LegendTitleHeight + LegendRowCount * LegendRowHeight + LegendScalesGap) * s);
    }

    /// <summary>Where the Customize button goes on a fretboard: under the Scales button in the legend card, in panel coordinates. Null for the keyboard and drums.</summary>
    public UiPoint? CustomizeAnchor() =>
        ScalesAnchor() is { } scales ? new UiPoint(scales.X, scales.Y + ScalesButtonHeight + ButtonStackGap) : null;
}
