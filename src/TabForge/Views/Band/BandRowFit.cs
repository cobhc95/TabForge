using TabForge.Models;

namespace TabForge.Views.Band;

// Owns: the default height of the Band rows: the main (first) row's lane holds two of its own systems (lines of music) and every other
//   row's one when that fits, else one each; a row is exactly as tall as its content, and spare height goes to whole systems, as many
//   as fit the rows' viewport and at least one. An instrument above its lane gets what one system per row leaves, between the least it
//   stays readable at and its natural height, so it gives way before the lanes do and grows back before rows gain a second system.
//   When one system each still does not fit, the lanes get lower (the lane draws its one system smaller, down to MinShare of its full
//   height) and only then do the rows scroll. A wanted number is cut down to what fits. A row dragged to its own height keeps it.
// Does not own: a system's height or what a row has on top of its lane (BandRow.SystemHeight, StackedExtra, Chrome), the classic
//   "rows per screen" share (BandView.RowHeight) or which mode is on (BandLayoutState.AutoSystems).
// Tests: TestBandRowSizing.
internal static class BandRowFit
{
    private const int MaxSystems = 8;
    private const double Gap = 2, UnknownSystem = 120;

    /// <summary>Works out how many systems the main (first) row's lane and every other row's lane hold, and gives every instrument that lies
    /// above its lane an equal share of the height that leaves. Wanted numbers (0 = automatic) are cut down to what fits. Below 1 the
    /// result is the share of one system's full height each lane gets when one each does not fit.</summary>
    public static (double Others, double Main) Fit(IReadOnlyList<BandRow> rows, Func<BandRow, double?> own, double viewport, int wantedOthers, int wantedMain)
    {
        double taken = 0, others = 0, mainOne = 0, least = 0.4, leastInstruments = 0, naturalInstruments = 0;
        var instruments = 0;
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            taken += row.StackedExtra - row.InstrumentHeightNow + Gap;
            if (row.InstrumentAbove) { instruments++; leastInstruments += row.InstrumentMin; naturalInstruments += row.InstrumentNatural; }
            if (own(row) is { } height) taken += height;
            else if (row.Lane.Vertical)
            {
                taken += row.Chrome;
                if (i == 0) mainOne = One(row); else others += One(row);
                // A tab-only lane still shows its whole system at 0.4 of the full height (smallest text, blank top padding cut); notation needs half.
                if (row.Lane.Editor.Notation != NotationMode.TabOnly) least = 0.5;
            }
            else taken += row.Chrome + One(row);   // a horizontal lane is one line whatever the room
        }
        double Need(double main, double other) => taken + mainOne * main + others * other;
        double m = 1, o = 1;
        if (mainOne + others > 0)
        {
            if (Need(1, 1) + leastInstruments > viewport) m = o = Math.Max(least, (viewport - taken - leastInstruments) / (mainOne + others));
            else
            {
                o = wantedOthers > 0 ? wantedOthers : 1;
                m = wantedMain > 0 ? wantedMain : o + 1;
                while ((m > 1 || o > 1) && Need(m, o) + leastInstruments > viewport) { if (m >= o && m > 1) m--; else o--; }
                // Automatic: height left over once the instruments have their natural size goes to whole systems, one more in every row at a time.
                if (wantedOthers <= 0 && wantedMain <= 0) while (m < MaxSystems && Need(m + 1, o + 1) + naturalInstruments <= viewport) { m++; o++; }
            }
        }
        for (var i = 0; i < rows.Count && instruments > 0; i++) if (rows[i].InstrumentAbove) rows[i].SetInstrumentRoom((viewport - Need(m, o)) / instruments);
        return (o, m);
    }

    /// <summary>The row's whole height with <paramref name="systems"/> systems in its lane.</summary>
    public static double Height(BandRow row, double systems) => row.StackedExtra + row.Chrome + Math.Floor(One(row) * (row.Lane.Vertical ? systems : 1));

    private static double One(BandRow row) => row.SystemHeight > 1 ? row.SystemHeight : UnknownSystem;
}
