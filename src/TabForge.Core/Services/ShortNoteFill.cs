using TabForge.Models;

namespace TabForge.Services;

// Owns: the rest fill of a bar that holds a note shorter than a sixteenth: the rests after the last written beat start exactly where it ends.
// Does not own: the fill of plain bars (BarFill), bars with tuplets or grace beats (left as they are), and gaps between written beats.
// Tests: no dedicated test class.
/// <summary>
/// A 32nd or 64th ends between two sixteenth cells, so the plain fill (one cell per sixteenth) would leave a silent gap after it. Here the
/// bar is written in the packed form (<see cref="BarGrid.WriteBar"/>: beats in order, exact onsets in RhythmicPosition) and the fill rests
/// after the last written beat carry the remaining time: a 64th and a 32nd up to the next sixteenth, then the plain fill's rest sizes.
/// Typing on the first of those rests therefore places the note directly after the previous one.
/// </summary>
internal static class ShortNoteFill
{
    private const double Eps = 1e-6;

    private static bool IsWritten(TabCell cell) => cell.Notes.Count > 0 || cell.HasAnnotation || (cell.IsRest && cell.WrittenRest);

    /// <summary>True when the voice holds a written beat shorter than a sixteenth and no tuplet or grace beat.</summary>
    public static bool Applies(IReadOnlyList<TabCell> cells)
    {
        var any = false;
        foreach (var cell in cells)
        {
            if (cell.Tuplet.Item1 > 0 || cell.IsGrace) return false;
            any |= IsWritten(cell) && MusicTime.CellSlots(cell) < 1 - Eps;
        }
        return any;
    }

    /// <summary>True when the voice (no tuplet or grace beat) holds a beat shorter than a sixteenth, or a beat written off the grid or before its
    /// own cell: its beats are timed by their onsets, not by their cells.</summary>
    public static bool Packed(IReadOnlyList<TabCell> cells)
    {
        var packed = false;
        for (var i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            if (cell.Tuplet.Item1 > 0 || cell.IsGrace) return false;
            packed |= (IsWritten(cell) && MusicTime.CellSlots(cell) < 1 - Eps)
                || (cell.RhythmicPosition is { } at && (at < i - Eps || Math.Abs(at - Math.Round(at)) > Eps));
        }
        return packed;
    }

    /// <summary>
    /// After a length change: the beats from the first one in <paramref name="keep"/> on are laid end to end by their exact lengths, each where
    /// the one before ends, however many cells that takes (a bar that no longer fits stays overfull; no beat is dropped). Rests after the last
    /// note go unless <paramref name="keep"/> holds them (the rest fill refills the end).
    /// </summary>
    public static bool CloseUp(List<TabCell> cells, int slots, IReadOnlySet<TabCell> keep)
    {
        var onsets = BarGrid.Onsets(cells);
        var last = cells.FindLastIndex(c => MusicTime.IsBeat(c) && (!c.IsRest || c.HasAnnotation || keep.Contains(c)));
        if (last < 0) return false;
        var beats = new List<(double Pos, TabCell Cell)>();
        double? end = null;
        for (var i = 0; i <= last; i++)
        {
            if (!MusicTime.IsBeat(cells[i])) continue;
            if (end is null && keep.Contains(cells[i])) end = onsets[i];
            var at = end ?? onsets[i];
            beats.Add((at, cells[i]));
            if (end is not null) end = at + MusicTime.CellSlots(cells[i]);
        }
        var written = BarGrid.WriteBar(beats, slots);
        cells.Clear();
        cells.AddRange(written);
        return true;
    }

    /// <summary>Rewrites the voice: its beats up to the last written one keep their onsets, the fill rests after it are laid anew.</summary>
    public static bool Fill(List<TabCell> cells, int slots)
    {
        var onsets = BarGrid.Onsets(cells);
        var last = cells.FindLastIndex(IsWritten);
        var beats = new List<(double Pos, TabCell Cell)>();
        var end = 0.0;
        for (var i = 0; i <= last; i++)
        {
            if (!IsWritten(cells[i]) && !cells[i].IsRest) continue;
            beats.Add((onsets[i], cells[i]));
            end = Math.Max(end, onsets[i] + MusicTime.CellSlots(cells[i]));
        }
        while (end < slots - Eps && beats.Count < InputLimits.MaxCellsPerMeasure)
        {
            var (size, denominator, dots) = RestAt(end, slots - end);
            if (size <= 0) break;   // an end finer than a 64th: the remainder stays open
            beats.Add((end, new TabCell { IsRest = true, DurationDenominator = denominator, Dots = dots }));
            end += size;
        }
        var written = BarGrid.WriteBar(beats, slots);
        cells.Clear();
        cells.AddRange(written);
        return true;
    }

    private static (double Size, int Denominator, int Dots) RestAt(double at, double room)
    {
        static bool On(double value, double grid) => Math.Abs(value / grid - Math.Round(value / grid)) < Eps;
        if (!On(at, 0.25)) return (0, 0, 0);
        if (!On(at, 0.5)) return (0.25, 64, 0);
        if (!On(at, 1)) return (0.5, 32, 0);
        var slot = (int)Math.Round(at); var left = (int)Math.Floor(room + Eps);
        foreach (var s in BarFill.Sizes)
            if (s.Size <= left && slot % s.Align == 0 && (s.Dots == 0 || s.Size == left)) return (s.Size, s.Denominator, s.Dots);
        return (0, 0, 0);
    }
}
