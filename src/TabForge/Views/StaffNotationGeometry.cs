using System.Globalization;
using System.Windows;
using System.Windows.Media;
using TabForge.Models;
using TabForge.Services;
using TabForge.Visualization;
using RenderDraw = TabForge.Visualization.Draw;
using TabForge.Views.Score;

namespace TabForge.Views;

/// <summary>Glyph metrics and pure geometry shared by staff layout and drawing: staff constants, durations, ledger lines, ghost clusters, arc shapes.</summary>
internal static class StaffNotationGeometry
{
    public const double StaffGap = 9.0;
    internal const double StemLength = 3.0 * StaffGap;
    internal const double BeamThickness = 1.7;
    internal const double BeamGap = 3.4;
    internal const double HeadRadiusX = StaffGap * 0.56;
    internal const double HeadRadiusY = StaffGap * 0.38;
    internal const double MiddleLineOffset = 2 * StaffGap;
    internal const double PositionEpsilon = 0.001;
    /// <summary>How far a standard ledger line runs past the notehead on each side (the marks beside a ledger note clear it).</summary>
    internal const double LedgerOverhang = 3.5;
    internal static bool OnLedger(StaffNotationMeasureLayout layout, StaffNotationNote note) =>
        note.Y < layout.StaffTop - 1 || note.Y > layout.StaffTop + 4 * StaffGap + 1;
    /// <summary>Imported tuplet beats sit on whole file ticks, so neighbours drift by a fraction of a slot from their exact ratio.</summary>
    internal const double TupletTickSlack = 0.15;

    internal static readonly int[] NaturalPitchClasses = { 0, 2, 4, 5, 7, 9, 11 };
    internal static readonly int[] SharpOrder = { 3, 0, 4, 1, 5, 2, 6 };
    internal static readonly int[] FlatOrder = { 6, 2, 5, 1, 4, 0, 3 };

    /// <summary>Staff y of each sound of a drum beat, from the track's drum map (a beamed drum beat keeps a nominal MinY/MaxY).</summary>
    internal static IEnumerable<double> DrumHeadYs(StaffNotationBeat beat)
    {
        foreach (var n in beat.Cell.Notes)
        {
            var entry = beat.DrumMap?.Invoke(n.MidiValue > 0 ? n.MidiValue : n.Fret);
            yield return entry is null ? beat.MinY : beat.StaffTop + entry.StaffStep * StaffGap / 2;
        }
    }

    public static string RestGlyph(TabCell cell) => NormalizeDuration(cell.DurationDenominator) switch
    {
        <= 1 => "𝄻",
        2 => "𝄼",
        4 => "𝄽",
        8 => "𝄾",
        16 => "𝄿",
        32 => "𝅀",
        _ => "𝅁"
    };

    /// <summary>Harmonics the reference writes at the fretted pitch (not the sounding one): artificial, tap, pinch and semi.</summary>
    internal static bool IsWrittenAtFret(IEnumerable<string> techniques) =>
        techniques.Any(t => t is "ArtificialHarmonic" or "TapHarmonic" or "PinchHarmonic" or "SemiHarmonic");

    internal static bool HasHarmonic(HashSet<string> techniques) =>
        techniques.Contains("Harmonic") || techniques.Contains("ArtificialHarmonic") ||
        techniques.Contains("PinchHarmonic") || techniques.Contains("TapHarmonic") ||
        techniques.Contains("SemiHarmonic") || techniques.Contains("FeedbackHarmonic");

    /// <summary>Bend text next to a notehead: amount of the peak (full, 1/2, 1 1/2 ...), with P.B. before a pre-bend.</summary>
    internal static string BendNotationLabel(TabNote note)
    {
        var points = ScoreMarkText.EffectiveBendPoints(note);
        var peak = points.Count == 0 ? 4 : points.Max(p => p.Value);
        var amount = ScoreMarkText.BendAmountLabel(peak);
        var pre = note.BendTypeName.StartsWith("Prebend", StringComparison.Ordinal) || points.Count > 0 && points[0].Offset < 1 && points[0].Value > 0.01;
        var release = note.BendTypeName.EndsWith("Release", StringComparison.Ordinal) || points.Count > 1 && points[^1].Value < peak - 0.01 && points[^1].Value < 0.5;
        return (pre ? "P.B. " : "") + amount + (release ? " R" : "");
    }

    /// <summary>How far left of the beat centre the first grace head sits: clear of the main notes' accidentals and ghost brackets.
    /// <paramref name="ghostRoom"/> is <see cref="GhostRoom"/> for the beat.</summary>
    internal static double GraceOffset(StaffNotationBeat beat, double ghostRoom)
    {
        var offset = 15.0;
        if (beat.Notes.Count == 0) return offset;
        var lead = beat.CenterX - beat.Notes.Min(n => n.X) + HeadRadiusX;
        if (ghostRoom > 0) offset = Math.Max(offset, lead + ghostRoom + 13);   // the grace slash clears the "(" (its ink ends 10 px left of the head)
        if (beat.Notes.Any(n => n.Accidental is not null))
        {
            var maxColumn = beat.Notes.Where(n => n.Accidental is not null).Max(n => n.AccidentalColumn);
            offset = Math.Max(offset, lead + 6.5 + 4.5 + maxColumn * 10 + 4 + 4 + ghostRoom);
        }
        return offset;
    }

    /// <summary>Extra room ghost brackets take left of the chord (8 px, more beside a ledger line): accidentals and grace notes sit outside it.</summary>
    internal static double GhostRoom(double staffTop, StaffNotationBeat beat)
    {
        var room = 0.0;
        foreach (var n in beat.Notes)
        {
            if (!n.Source.Ghost) continue;
            var ledger = n.Y < staffTop - 1 || n.Y > staffTop + 4 * StaffGap + 1;
            room = Math.Max(room, 8 + (ledger ? GhostLedgerPad : 0));
        }
        return room;
    }

    /// <summary>The (up to three) grace notes in the order they are drawn right to left: the latest in time first.</summary>
    internal static IReadOnlyList<StaffNotationNote> GraceDrawOrder(StaffNotationBeat beat) =>
        beat.GraceNotes.OrderBy(g => g.Source.GraceOnsetOffsetSlots).Take(3).Reverse().ToList();

    internal static int NormalizeDuration(int denominator)
        => Math.Clamp(denominator <= 0 ? 16 : denominator, 1, 64);

    /// <summary>Accidentals hang off the chord's leftmost head, so a head displaced to the left (seconds) never sits under an accidental.</summary>
    internal static double AccidentalX(StaffNotationMeasureLayout layout, StaffNotationBeat beat, StaffNotationNote note) =>
        beat.Notes.Min(n => n.X) - HeadRadiusX - 6.5 - GhostRoom(layout.StaffTop, beat) - note.AccidentalColumn * 10;

    internal const double GhostLedgerPad = LedgerOverhang + 2;
    /// <summary>Distance from a ghost cluster's first head to the "(" glyph's origin (its ink ends about 3.5 px from the head).</summary>
    internal const double GhostOpenGap = 8.5;
    /// <summary>Ink of a 13 px bracket drawn at y: centre y + 10.5, half-height 5.6. A lone ghost head keeps the bracket's
    /// historic 2.5 px drop; a cluster's brackets are centred on it and stretched to cover every head.</summary>
    internal const double GhostInkCentre = 10.5, GhostInkHalf = 5.6;

    /// <summary>Clusters of ghost heads in one beat (heads closer than a bracket's height share one pair of brackets);
    /// InkY/Half are the brackets' ink centre and half-height, Pad clears ledger lines.</summary>
    internal static List<(double L, double R, double InkY, double Half, double Pad)> GhostClusters(StaffNotationMeasureLayout layout, StaffNotationBeat beat)
    {
        var result = new List<(double, double, double, double, double)>();
        var ghosts = beat.Notes.Where(n => n.Source.Ghost).OrderBy(n => n.Y).ToList();
        for (var i = 0; i < ghosts.Count;)
        {
            var j = i;
            while (j + 1 < ghosts.Count && ghosts[j + 1].Y - ghosts[j].Y < 13) j++;
            var group = ghosts.GetRange(i, j - i + 1);
            var span = group[^1].Y - group[0].Y;
            var pad = group.Any(n => OnLedger(layout, n)) ? GhostLedgerPad : 0;
            var mid = (group[0].Y + group[^1].Y) / 2;
            result.Add((group.Min(n => n.X), group.Max(n => n.X), span > 0 ? mid : mid + 2.5, GhostInkHalf + span / 2, pad));
            i = j + 1;
        }
        return result;
    }

    /// <summary>
    /// Where an arc between two heads starts and ends so it stays clear of the ink around them: a ghost bracket after the first head
    /// and in front of the second, and the accidentals of the second beat that sit at about the arc's height. Returns (5, 5), the plain
    /// head-to-head arc, when there is nothing to avoid or no room to avoid it in.
    /// </summary>
    internal static (double Start, double End) ArcInsets(StaffNotationBeat originBeat, StaffNotationNote origin,
        StaffNotationBeat destBeat, StaffNotationNote dest, double staffTop)
    {
        double start = 5, end = 5;
        if (origin.Source.Ghost) start = Math.Max(start, HeadRadiusX + 8 + (OnLedgerAt(staffTop, origin) ? GhostLedgerPad : 0));
        if (dest.Source.Ghost) end = Math.Max(end, HeadRadiusX + GhostOpenGap + 1.5 + (OnLedgerAt(staffTop, dest) ? GhostLedgerPad : 0));
        var room = GhostRoom(staffTop, destBeat);
        var leftmost = destBeat.Notes.Min(n => n.X);
        foreach (var other in destBeat.Notes)
        {
            if (other.Accidental is null || Math.Abs(other.Y - dest.Y) > 14) continue;
            var accidentalLeft = leftmost - HeadRadiusX - 6.5 - room - other.AccidentalColumn * 10 - 4.5;
            end = Math.Max(end, dest.X - accidentalLeft + 1.5);
        }
        // Too tight to stop short (the arc would turn back on itself): keep the plain head-to-head arc.
        return dest.X - end - (origin.X + start) < 12 ? (5, 5) : (start, end);
    }

    internal static bool OnLedgerAt(double staffTop, StaffNotationNote note) => note.Y < staffTop - 1 || note.Y > staffTop + 4 * StaffGap + 1;

    internal static double RestCenterY(TabCell cell, double staffTop)
        => staffTop + (NormalizeDuration(cell.DurationDenominator) <= 1 ? 3 * StaffGap : 2 * StaffGap);

    internal static IReadOnlyList<double> LedgerLinePositions(double y, double staffTop)
    {
        var staffBottom = staffTop + 4 * StaffGap;
        var lines = new List<double>();
        var nextY = staffTop - StaffGap;
        if (y < staffTop - StaffGap / 2)
        {
            while (nextY >= y - PositionEpsilon)
            {
                lines.Add(nextY);
                nextY -= StaffGap;
            }
        }
        else if (y > staffBottom + StaffGap / 2)
        {
            nextY = staffBottom + StaffGap;
            while (nextY <= y + PositionEpsilon)
            {
                lines.Add(nextY);
                nextY += StaffGap;
            }
        }
        return lines;
    }

    internal static IReadOnlyList<StaffLedgerLineSegment> LedgerLineSegments(
        StaffNotationMeasureLayout layout, LedgerLineMode mode)
    {
        if (mode == LedgerLineMode.Hidden) return Array.Empty<StaffLedgerLineSegment>();
        if (layout.TryGetLedgerSegments(mode, out var cached)) return cached;

        var halfWidth = mode == LedgerLineMode.Standard ? HeadRadiusX + LedgerOverhang : HeadRadiusX + 2.4;
        var onsets = new List<List<StaffNotationBeat>>();
        foreach (var beat in layout.Beats)
        {
            if (beat.IsDrum || beat.Notes.Count == 0) continue;
            if (onsets.Count == 0 || Math.Abs(onsets[^1][0].StartSlots - beat.StartSlots) > PositionEpsilon)
                onsets.Add(new List<StaffNotationBeat>());
            onsets[^1].Add(beat);
        }

        var result = new List<StaffLedgerLineSegment>();
        foreach (var onset in onsets)
        {
            var lineYs = new Dictionary<int, double>();
            var intervals = new Dictionary<int, List<(double Left, double Right)>>();
            foreach (var beat in onset)
            foreach (var note in beat.Notes)
            foreach (var ledgerY in LedgerLinePositions(note.Y, layout.StaffTop))
            {
                var key = (int)Math.Round(ledgerY * 100);
                lineYs[key] = ledgerY;
                if (!intervals.TryGetValue(key, out var lineIntervals))
                    intervals[key] = lineIntervals = new List<(double Left, double Right)>();
                lineIntervals.Add((note.X - halfWidth, note.X + halfWidth));
            }

            foreach (var (key, lineIntervals) in intervals)
            {
                var ordered = lineIntervals.OrderBy(interval => interval.Left).ToArray();
                if (ordered.Length == 0) continue;
                var left = ordered[0].Left;
                var right = ordered[0].Right;
                foreach (var interval in ordered.Skip(1))
                {
                    if (interval.Left <= right + 0.75)
                    {
                        right = Math.Max(right, interval.Right);
                        continue;
                    }
                    result.Add(new StaffLedgerLineSegment(left, right, lineYs[key]));
                    left = interval.Left;
                    right = interval.Right;
                }
                result.Add(new StaffLedgerLineSegment(left, right, lineYs[key]));
            }
        }
        var resultArray = result.ToArray();
        layout.SetLedgerSegments(mode, resultArray);
        return resultArray;
    }

    /// <summary>Thickness of the staff lines and of the ledger lines: they are one unit.</summary>
    internal const double StaffLineThickness = 1.0;

    /// <summary>
    /// Horizontal shape of a tie / slur between heads at <paramref name="x1"/> and <paramref name="x2"/>: start, the two control points,
    /// end, and the span the bow height is taken from. The plain (5, 5) arc keeps its historic shape exactly; an inset arc places its
    /// control points inside its own (shorter) span, otherwise a control point past the end would make the arc hook back on itself.
    /// </summary>
    internal static (double Start, double Control1, double Control2, double End, double BowSpan) ArcShape(double x1, double x2, double startInset, double endInset)
    {
        var start = x1 + startInset;
        var end = x2 - endInset;
        if (startInset == 5 && endInset == 5) return (start, x1 + (x2 - x1) * 0.30, x1 + (x2 - x1) * 0.70, end, Math.Abs(x2 - x1));
        var drawn = end - start;
        return (start, start + drawn * 0.25, start + drawn * 0.75, end, Math.Abs(drawn) + 10);
    }

    internal static Geometry FlagGeometry(double x, double y, bool up)
    {
        var direction = up ? 1.0 : -1.0;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            // A full flag: it leaves the stem tip with some body (2.6 px thick, under the 3.4 px gap between stacked flags), sweeps right
            // and curls down to a point 15 px below, like the reference's eighth, sixteenth and thirty-second flags.
            context.BeginFigure(new Point(x, y), true, true);
            context.BezierTo(new Point(x + 2, y + 3.8 * direction), new Point(x + 6, y + 6 * direction),
                new Point(x + 3.2, y + 13 * direction), true, false);
            context.BezierTo(new Point(x + 3.5, y + 8.5 * direction), new Point(x + 2.2, y + 6.5 * direction),
                new Point(x, y + 2.8 * direction), true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    internal static int FlagsFor(int denominator) => NormalizeDuration(denominator) switch
    {
        <= 4 => 0,
        8 => 1,
        16 => 2,
        32 => 3,
        _ => 4
    };

}
