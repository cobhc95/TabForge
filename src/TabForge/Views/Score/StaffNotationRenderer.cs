using System.Windows.Media;
using TabForge.Models;

namespace TabForge.Views;

/// <summary>
/// Entry point of staff notation: <see cref="StaffNotationLayoutBuilder"/> resolves a measure into beats, beams,
/// pitch geometry and arcs, and <see cref="StaffNotationDrawing"/> draws that layout. The returned layout is also
/// used by the TAB renderer. The shared metrics live in <see cref="StaffNotationGeometry"/>.
/// </summary>
internal sealed class StaffNotationRenderer
{
    public const double StaffGap = StaffNotationGeometry.StaffGap;
    internal const double HeadRadiusX = StaffNotationGeometry.HeadRadiusX;
    internal const double StaffLineThickness = StaffNotationGeometry.StaffLineThickness;

    /// <summary>Creates the single rhythmic/geometry layout consumed by both score staves.</summary>
    public StaffNotationMeasureLayout CreateLayout(
        TrackModel? track,
        MeasureModel measure,
        int measureIndex,
        int slots,
        double x,
        double staffTop,
        double slotWidth,
        int numerator = 4,
        int denominator = 4,
        int keySignature = 0,
        double staffScale = 1.0,
        IReadOnlyList<TabCell>? cellsOverride = null,
        Func<double, double>? centerOf = null) =>
        StaffNotationLayoutBuilder.CreateLayout(track, measure, measureIndex, slots, x, staffTop, slotWidth, numerator, denominator,
            keySignature, staffScale, cellsOverride, centerOf);

    /// <summary>Draws glyphs and the already-resolved geometry; no rhythmic decisions happen here.</summary>
    public void DrawMeasure(
        DrawingContext dc,
        StaffNotationMeasureLayout layout,
        int measureIndex,
        Color ink,
        Color faint,
        Color accent,
        Color playColor,
        Color paper,
        Color staffLineColor,
        LedgerLineMode ledgerLineMode,
        IReadOnlySet<(int bar, int cell, int s)> sounding,
        IReadOnlySet<(int bar, int cell, int s)> struck) =>
        StaffNotationDrawing.DrawMeasure(dc, layout, measureIndex, ink, faint, accent, playColor, paper, staffLineColor, ledgerLineMode,
            sounding, struck);

    internal static (double Start, double End) ArcInsets(StaffNotationBeat originBeat, StaffNotationNote origin,
        StaffNotationBeat destBeat, StaffNotationNote dest, double staffTop) =>
        StaffNotationGeometry.ArcInsets(originBeat, origin, destBeat, dest, staffTop);

    internal static (double Start, double Control1, double Control2, double End, double BowSpan) ArcShape(double x1, double x2, double startInset, double endInset) =>
        StaffNotationGeometry.ArcShape(x1, x2, startInset, endInset);

    internal static IEnumerable<double> DrumHeadYs(StaffNotationBeat beat) => StaffNotationGeometry.DrumHeadYs(beat);

    internal static Color EngravingInkColor(Color ink, Color paper) => StaffNotationDrawing.EngravingInkColor(ink, paper);

    internal static Geometry FlagGeometry(double x, double y, bool up) => StaffNotationGeometry.FlagGeometry(x, y, up);

    internal static double GhostRoom(double staffTop, StaffNotationBeat beat) => StaffNotationGeometry.GhostRoom(staffTop, beat);

    internal static IReadOnlyList<StaffNotationNote> GraceDrawOrder(StaffNotationBeat beat) => StaffNotationGeometry.GraceDrawOrder(beat);

    internal static double GraceOffset(StaffNotationBeat beat, double ghostRoom) => StaffNotationGeometry.GraceOffset(beat, ghostRoom);

    internal static bool IsWrittenAtFret(IEnumerable<string> techniques) => StaffNotationGeometry.IsWrittenAtFret(techniques);

    internal static IReadOnlyList<double> LedgerLinePositions(double y, double staffTop) => StaffNotationGeometry.LedgerLinePositions(y, staffTop);

    internal static IReadOnlyList<StaffLedgerLineSegment> LedgerLineSegments(StaffNotationMeasureLayout layout, LedgerLineMode mode) =>
        StaffNotationGeometry.LedgerLineSegments(layout, mode);

    internal static int NormalizeDuration(int denominator) => StaffNotationGeometry.NormalizeDuration(denominator);

    internal static double RestCenterY(TabCell cell, double staffTop) => StaffNotationGeometry.RestCenterY(cell, staffTop);

    public static string RestGlyph(TabCell cell) => StaffNotationGeometry.RestGlyph(cell);

    internal static void SeedSkyline(StaffNotationMeasureLayout layout) => StaffNotationLayoutBuilder.SeedSkyline(layout);

    internal static Pen StaffLinePen(Color staffLineColor) => StaffNotationDrawing.StaffLinePen(staffLineColor);
}
