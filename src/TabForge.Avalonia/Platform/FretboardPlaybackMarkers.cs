using System;
using System.Collections.Generic;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Playback;
using TabForge.Presenters;

namespace TabForge.Avalonia.Platform;

/// <summary>The playing role of a fretboard marker: sounding, released at a rest, next, upcoming or recent.</summary>
internal enum PlaybackMarkerRole { Now, ReleasedNow, Next, Upcoming, Recent }

// Owns: how a playing note is drawn on the fretboard: each role's radius, halo, fill, outline and number, the technique tag pill and the movement
// line. Sizes are named here; colours are theme-table keys.
// Does not own: which note has which role (InstrumentPlaybackSnapshot), the board geometry (FretboardGeometryRules), or when the panel redraws (InstrumentPanelView).
// Tests: TabForge.Avalonia.Tests.FretboardPlaybackMarkerTests.
internal static class FretboardPlaybackMarkers
{
    /// <summary>The radius cap and the share of the fret width of a role's marker: sounding and released notes, the next move, and the rest.</summary>
    public const double NowRadiusCap = 16;
    public const double NowRadiusPerFret = 0.44;
    public const double NextRadiusCap = 13;
    public const double NextRadiusPerFret = 0.36;
    public const double OtherRadiusCap = 12;
    public const double OtherRadiusPerFret = 0.32;
    /// <summary>The number size of a role's marker in DIPs, before the number scale and the marker size factor.</summary>
    public const double NowNumberSize = 13;
    public const double NextNumberSize = 12;
    public const double RecentNumberSize = 11;
    public const double UpcomingNumberSize = 10;
    /// <summary>The halo of a sounding note: its radius plus this grow, at this opacity of the current colour.</summary>
    public const double HaloGrow = 4;
    public const double HaloOpacity = 0.20;
    /// <summary>A released sounding note at a rest: a hollow ring in the past colour at this width and opacity.</summary>
    public const double ReleasedRingWidth = 1.4;
    public const double ReleasedRingOpacity = 0.7;
    /// <summary>The next move: a tinted fill and a thick outline in its colour.</summary>
    public const double NextFillOpacity = 0.30;
    public const double NextOutlineWidth = 2.2;
    /// <summary>The sounding note's and the upcoming move's outline widths; the recent note keeps the panel's own outline.</summary>
    public const double NowOutlineWidth = 1.2;
    public const double UpcomingOutlineWidth = 1.2;
    public const double RecentOutlineWidth = 1.5;
    /// <summary>The technique tag pill: a background-coloured fill at this opacity, an outline in the role's colour, and its text and box sizes.</summary>
    public const double TagFillOpacity = 0.92;
    public const double TagOutlineWidth = 1.4;
    public const double TagFontSize = 8.5;
    public const double TagHeight = 13;
    public const double TagLift = 17;
    public const double TagSide = 3;
    public const double TagCorner = 6.5;
    public const double TagMinWidth = 22;
    public const double TagCharWidth = 6.2;
    public const double TagPadding = 10;
    /// <summary>The movement line from the sounding shape to the next: its width and opacity, and how far its middle bends toward the target.</summary>
    public const double MovementLineWidth = 1.8;
    public const double MovementLineOpacity = 0.75;
    public const double MovementLineBend = 0.15;

    /// <summary>The radius and the size factor a role's marker is drawn at.</summary>
    internal readonly record struct Geometry(double Radius, double Factor);

    /// <summary>The marker's radius: the role's radius for the fret width, times the number scale, then the marker size factor (never past the string gap).</summary>
    internal static Geometry GeometryOf(PlaybackMarkerRole role, double fretWidth, double numberScale, double markerScale, double stringGap)
    {
        var baseRadius = role switch
        {
            PlaybackMarkerRole.Now or PlaybackMarkerRole.ReleasedNow => Math.Min(NowRadiusCap, fretWidth * NowRadiusPerFret),
            PlaybackMarkerRole.Next => Math.Min(NextRadiusCap, fretWidth * NextRadiusPerFret),
            _ => Math.Min(OtherRadiusCap, fretWidth * OtherRadiusPerFret),
        } * numberScale;
        var factor = FretboardMarkerSizeRules.Factor(markerScale, baseRadius, stringGap);
        return new Geometry(baseRadius * factor, factor);
    }

    /// <summary>The number size of a role's marker, scaled like its radius and never past the marker's radius, so a two-digit number stays inside
    /// the dot when the gap fit has shrunk the marker below its base size.</summary>
    internal static double NumberSizeOf(PlaybackMarkerRole role, Geometry geometry, double numberScale)
    {
        var wanted = numberScale * Math.Max(geometry.Factor, 1) * role switch
        {
            PlaybackMarkerRole.Now or PlaybackMarkerRole.ReleasedNow => NowNumberSize,
            PlaybackMarkerRole.Next => NextNumberSize,
            PlaybackMarkerRole.Recent => RecentNumberSize,
            _ => UpcomingNumberSize,
        };
        return Math.Min(wanted, FretboardMarkerSizeRules.LabelFontSize(geometry.Radius));
    }

    /// <summary>The sounding note: a tight halo, a solid marker with a background outline.</summary>
    internal static void DrawNow(IDrawContext d, Func<string, UiColor> ink, FretboardGeometryRules.Layout layout, NoteEvent note, string label, Geometry g, double numberSize)
    {
        if (PositionOf(layout, note) is not { } at) return;
        var colour = ink(IsLight(ink) ? "FretNow" : "InstrumentCurrent");
        d.Ellipse(colour.WithOpacity(HaloOpacity), null, at.X, at.Y, g.Radius + HaloGrow, g.Radius + HaloGrow);
        d.Ellipse(colour, new UiStroke(Ring(ink, IsLight(ink), ink("InstrumentBackground")), NowOutlineWidth), at.X, at.Y, g.Radius, g.Radius);
        DrawNumber(d, ink, at, label, colour, numberSize, bold: true);
    }

    /// <summary>A released sounding note at a rest: a hollow ring in the past colour, the number kept faint.</summary>
    internal static void DrawReleasedNow(IDrawContext d, Func<string, UiColor> ink, FretboardGeometryRules.Layout layout, NoteEvent note, string label, Geometry g, double numberSize)
    {
        if (PositionOf(layout, note) is not { } at) return;
        d.Ellipse(null, new UiStroke(ink("InstrumentPast").WithOpacity(ReleasedRingOpacity), ReleasedRingWidth), at.X, at.Y, g.Radius, g.Radius);
        DrawNumber(d, ink, at, label, ink("InstrumentLegible").WithOpacity(ReleasedRingOpacity), numberSize, bold: false);
    }

    /// <summary>The next move: a tinted fill with a thick outline in its colour (an outline, never a glowing note).</summary>
    internal static void DrawNext(IDrawContext d, Func<string, UiColor> ink, FretboardGeometryRules.Layout layout, NoteEvent note, string label, Geometry g, double numberSize)
    {
        if (PositionOf(layout, note) is not { } at) return;
        var colour = ink("InstrumentNext");
        var light = IsLight(ink);
        var fill = light ? colour : colour.WithOpacity(NextFillOpacity);   // light: a solid disc with the thin white ring
        d.Ellipse(fill, new UiStroke(light ? ink("InstrumentWhite") : colour, light ? LightRingWidth : NextOutlineWidth), at.X, at.Y, g.Radius, g.Radius);
        DrawNumber(d, ink, at, label, colour, numberSize, bold: false, over: fill);
    }

    /// <summary>An upcoming move: the accent at its depth brightness, outlined in the accent.</summary>
    internal static void DrawUpcoming(IDrawContext d, Func<string, UiColor> ink, FretboardGeometryRules.Layout layout, NoteEvent note, string label, Geometry g, double numberSize, double brightness)
    {
        if (PositionOf(layout, note) is not { } at) return;
        var accent = ink("InstrumentAccent");
        var fill = accent.WithOpacity(brightness);
        d.Ellipse(fill, new UiStroke(IsLight(ink) ? ink("InstrumentWhite") : accent, IsLight(ink) ? LightRingWidth : UpcomingOutlineWidth), at.X, at.Y, g.Radius, g.Radius);
        DrawNumber(d, ink, at, label, fill, numberSize, bold: false);
    }

    /// <summary>A recently released note: the past colour fading at its brightness, with the panel's own thin outline.</summary>
    internal static void DrawRecent(IDrawContext d, Func<string, UiColor> ink, FretboardGeometryRules.Layout layout, NoteEvent note, string label, Geometry g, double numberSize, double brightness)
    {
        if (PositionOf(layout, note) is not { } at) return;
        if (IsLight(ink))
        {
            // light: a white disc with a grey ring and a grey number
            var white = ink("InstrumentWhite").WithOpacity(brightness);
            d.Ellipse(white, new UiStroke(ink("InstrumentPast"), LightRingWidth), at.X, at.Y, g.Radius, g.Radius);
            var grey = d.Text(label, InstrumentPanelView.Font, numberSize, ink("InstrumentGrey"), d.PixelsPerDip, weight: 700);
            d.DrawText(grey, at.X - grey.Width / 2, at.Y - grey.Height / 2);
            return;
        }
        var fill = ink("InstrumentPast").WithOpacity(brightness);
        d.Ellipse(fill, new UiStroke(Ring(ink, false, ink("InstrumentBackground")), RecentOutlineWidth), at.X, at.Y, g.Radius, g.Radius);
        DrawNumber(d, ink, at, label, fill, numberSize, bold: false);
    }

    /// <summary>The technique tag pill of a sounding or next note: above the marker, or beside it when there is no room above the board.</summary>
    internal static void DrawTag(IDrawContext d, Func<string, UiColor> ink, FretboardGeometryRules.Layout layout, NoteEvent note, Geometry g, double numberScale, UiColor roleColour)
    {
        if (note.Technique is not { Length: > 0 } tag || note.Strum != 0) return;
        if (PositionOf(layout, note) is not { } at) return;
        var width = Math.Max(TagMinWidth, tag.Length * TagCharWidth + TagPadding) * numberScale;
        var height = TagHeight * numberScale;
        var above = at.Y - g.Radius - TagLift * numberScale;
        var onTop = above >= layout.Board.Y;
        var x = onTop ? at.X - width / 2 : at.X + g.Radius + TagSide;
        var y = onTop ? above : at.Y - height / 2;
        var light = IsLight(ink);   // light: a white label with a grey border and dark text
        d.Rect(light ? ink("InstrumentWhite") : ink("InstrumentBackground").WithOpacity(TagFillOpacity), new UiStroke(light ? ink("InstrumentPast") : roleColour, light ? LightRingWidth : TagOutlineWidth), x, y, width, height, TagCorner * numberScale);
        var text = d.Text(tag, InstrumentPanelView.Font, TagFontSize * numberScale, light ? ink("InstrumentText") : ink("InstrumentLegible"), d.PixelsPerDip, weight: 700);
        d.DrawText(text, x + (width - text.Width) / 2, y + (height - text.Height) / 2);
    }

    /// <summary>The movement line: one cubic curve from a sounding position to the one the same finger plays next, bending toward the target.</summary>
    internal static void DrawMovement(IDrawContext d, Func<string, UiColor> ink, FretboardGeometryRules.Layout layout, (int String, int Fret) from, (int String, int Fret) to)
    {
        var start = FretboardGeometryRules.PositionOf(layout, from.String, from.Fret);
        var end = FretboardGeometryRules.PositionOf(layout, to.String, to.Fret);
        var midY = (start.Y + end.Y) / 2 + (end.Y - start.Y) * MovementLineBend;
        var path = d.Paths.BuildPath(sink =>
        {
            sink.BeginFigure(start, false, false);
            sink.BezierTo(new UiPoint(start.X, midY), new UiPoint(end.X, midY), end);
        });
        d.Path(null, new UiStroke(ink("InstrumentNext").WithOpacity(MovementLineOpacity), MovementLineWidth), path);
    }

    private static UiPoint? PositionOf(FretboardGeometryRules.Layout layout, NoteEvent note) =>
        note.StringIndex < 0 || note.StringIndex >= layout.Strings ? null : FretboardGeometryRules.PositionOf(layout, note.StringIndex, note.Fret);

    /// <summary>A marker's number, inked for contrast on the marker's surface: its fill over the board, or <paramref name="over"/> when it is translucent.</summary>
    private static void DrawNumber(IDrawContext d, Func<string, UiColor> ink, UiPoint at, string label, UiColor fill, double size, bool bold, UiColor? over = null)
    {
        var light = IsLight(ink);   // light: a bold white number on the solid disc
        var surface = Over(over ?? fill, ink("InstrumentBoard"));
        var text = d.Text(label, InstrumentPanelView.Font, size, light ? ink("InstrumentWhite") : ThemeColours.InkOn(surface), d.PixelsPerDip, weight: bold || light ? 700 : 600);
        d.DrawText(text, at.X - text.Width / 2, at.Y - text.Height / 2);
    }

    /// <summary>The light theme's thin white ring of a solid marker.</summary>
    public const double LightRingWidth = 1;

    // The theme variant the delegate reads, told from the board colour (cheap, no allocation).
    internal static bool IsLight(Func<string, UiColor> ink) => ThemeColours.InkOn(ink("InstrumentBoard")) == ThemeColours.InkDark;

    private static UiColor Ring(Func<string, UiColor> ink, bool light, UiColor dark) => light ? ink("InstrumentWhite") : dark;

    /// <summary>The colour a translucent colour shows over an opaque one: its own alpha blended on the board.</summary>
    internal static UiColor Over(UiColor top, UiColor bottom)
    {
        var a = top.A / 255.0;
        return new UiColor(255, (byte)Math.Round(top.R * a + bottom.R * (1 - a)), (byte)Math.Round(top.G * a + bottom.G * (1 - a)), (byte)Math.Round(top.B * a + bottom.B * (1 - a)));
    }
}
