using TabForge.Models;
using TabForge.Views;

namespace TabForge;

/// <summary>Ties and slurs on the staff stop short of a chord-mate's accidental and start / end clear of ghost brackets.</summary>
public static partial class SelfTest
{
    private static void TestStaffArcInsets()
    {
        StaffNotationNote Note(double x, double y, bool ghost = false, string? accidental = null) => new()
        {
            Source = new TabNote { StringIndex = 1, Fret = 3, Ghost = ghost }, WrittenMidi = 60, StaffStep = 0, Letter = 0, Octave = 4, Alteration = 0,
            Y = y, X = x, Accidental = accidental
        };
        StaffNotationBeat Beat(double x, params StaffNotationNote[] notes) => new()
        {
            CellIndex = 0, Cell = new TabCell(), StartSlots = 0, DurationSlots = 1, CenterX = x, Flags = 0, Notes = notes.ToList()
        };

        var from = Note(40, 50); var fromBeat = Beat(40, from);
        var to = Note(100, 50); var mate = Note(100, 56, accidental: "♯"); var toBeat = Beat(100, to, mate);
        var (start, end) = StaffNotationRenderer.ArcInsets(fromBeat, from, toBeat, to, 20);
        Check("staff arcs: an arc to a chord whose other note carries an accidental at its height stops short of the accidental", start == 5 && end > 12, $"{start} {end}");

        var plainTo = Note(100, 50); var plainBeat = Beat(100, plainTo);
        Check("staff arcs: nothing to avoid keeps the plain 5 px head-to-head arc", StaffNotationRenderer.ArcInsets(fromBeat, from, plainBeat, plainTo, 20) == (5, 5));

        var farMate = Note(100, 90, accidental: "♯");
        var farBeat = Beat(100, to, farMate);
        Check("staff arcs: an accidental well above or below the arc's height is ignored", StaffNotationRenderer.ArcInsets(fromBeat, from, farBeat, to, 20) == (5, 5));

        var ghostFrom = Note(40, 50, ghost: true); var ghostTo = Note(100, 50, ghost: true);
        var (gStart, gEnd) = StaffNotationRenderer.ArcInsets(Beat(40, ghostFrom), ghostFrom, Beat(100, ghostTo), ghostTo, 20);
        Check("staff arcs: between ghost notes the arc starts after the first ')' and ends before the second '('", gStart > 10 && gEnd > 10, $"{gStart} {gEnd}");

        var closeTo = Note(55, 50); var closeMate = Note(55, 56, accidental: "♯");
        Check("staff arcs: with no room to stop short the plain arc is kept",
            StaffNotationRenderer.ArcInsets(fromBeat, from, Beat(55, closeTo, closeMate), closeTo, 20) == (5, 5));

        // Shape: an inset arc keeps both control points inside its own span (a control point past the end hooks the arc back);
        // the plain arc keeps its historic control points.
        var hooks = 0;
        for (var span = 20.0; span <= 120; span += 1)
        for (var e = 5.0; e <= 30; e += 0.5)
        {
            if (span - e - 5 < 12) continue;
            var (s, c1, c2, en, _) = StaffNotationRenderer.ArcShape(0, span, 5, e);
            if (!(s <= c1 && c1 <= c2 && c2 <= en)) hooks++;
        }
        Check("staff arcs: an inset arc's control points stay between its ends (no hook back)", hooks == 0, $"{hooks}");
        var plain = StaffNotationRenderer.ArcShape(10, 70, 5, 5);
        Check("staff arcs: the plain head-to-head arc keeps its shape",
            Math.Abs(plain.Start - 15) < 1e-9 && Math.Abs(plain.Control1 - 28) < 1e-9 && Math.Abs(plain.Control2 - 52) < 1e-9 &&
            Math.Abs(plain.End - 65) < 1e-9 && Math.Abs(plain.BowSpan - 60) < 1e-9, $"{plain}");
    }
}
