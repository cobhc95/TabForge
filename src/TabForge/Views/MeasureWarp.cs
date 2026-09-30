namespace TabForge.Views;

/// <summary>
/// Horizontal spacing of one bar, standard: each onset-to-onset gap gets room proportional to
/// duration^<see cref="Exponent"/> rather than to its raw length. Long notes are compressed and short
/// ones expanded, so a single 32nd no longer forces the whole bar onto a 32nd-wide uniform grid.
/// Maps rhythmic positions (sixteenth-note slots) to a 0..1 fraction of the bar width and back.
/// </summary>
internal sealed class MeasureWarp
{
    public const double Exponent = 0.62;

    private readonly double[] _slots;     // breakpoints in slots, ascending, first 0, last = bar length
    private readonly double[] _fractions; // matching 0..1 positions

    public double Slots { get; }
    /// <summary>Sum of gap weights: the bar's width in "sixteenth-equivalents" (16 for 16 sixteenths).</summary>
    public double TotalWeight { get; }

    private MeasureWarp(double slots, double[] points, double[] fractions, double totalWeight)
    {
        Slots = slots;
        _slots = points;
        _fractions = fractions;
        TotalWeight = totalWeight;
    }

    public static double Weight(double gapSlots) => gapSlots <= 0 ? 0 : Math.Pow(gapSlots, Exponent);

    /// <summary>
    /// <paramref name="leadWeight"/>/<paramref name="trailWeight"/> reserve room (in sixteenth-equivalents)
    /// before the first and after the last note, e.g. for repeat signs, so notes never crowd them.
    /// </summary>
    public static MeasureWarp Build(IEnumerable<double> onsets, double slots, double leadWeight = 0, double trailWeight = 0)
    {
        slots = Math.Max(1, slots);
        var points = onsets.Where(o => double.IsFinite(o) && o > 0.0001 && o < slots - 0.0001)
            .Select(o => Math.Round(o, 4)).Append(0).Append(slots).Distinct().OrderBy(o => o).ToArray();
        // Space before the first onset and after the last one keeps sixteenth-per-slot weighting.
        var weights = new double[points.Length];
        var total = 0.0;
        for (var i = 1; i < points.Length; i++)
        {
            total += Weight(points[i] - points[i - 1]);
            weights[i] = total;
        }
        var full = total + leadWeight + trailWeight;
        var fractions = weights.Select(w => full <= 0 ? 0 : (leadWeight + w) / full).ToArray();
        return new MeasureWarp(slots, points, fractions, full);
    }

    /// <summary>Position of a rhythmic point (slots from the bar start) as a 0..1 fraction of the bar.</summary>
    public double Fraction(double slot)
    {
        if (slot <= 0) return _fractions[0] + slot / Slots;
        if (slot >= Slots) return 1 + (slot - Slots) / Slots;
        var i = Array.BinarySearch(_slots, slot);
        if (i >= 0) return _fractions[i];
        i = ~i;
        var a = _slots[i - 1]; var b = _slots[i];
        return _fractions[i - 1] + (_fractions[i] - _fractions[i - 1]) * (slot - a) / (b - a);
    }

    /// <summary>Inverse of <see cref="Fraction"/>: the rhythmic position at a 0..1 bar fraction.</summary>
    public double SlotAt(double fraction)
    {
        fraction = Math.Clamp(fraction, _fractions[0], _fractions[^1]);
        for (var i = 1; i < _fractions.Length; i++)
        {
            if (fraction > _fractions[i]) continue;
            var fa = _fractions[i - 1]; var fb = _fractions[i];
            return fb - fa <= 1e-9 ? _slots[i] : _slots[i - 1] + (_slots[i] - _slots[i - 1]) * (fraction - fa) / (fb - fa);
        }
        return Slots;
    }

    /// <summary>Visual centre of the beat starting at <paramref name="slot"/> (half its first step, at most half a slot).</summary>
    public double CenterFraction(double slot)
    {
        var i = Array.BinarySearch(_slots, Math.Round(slot, 4));
        var next = i >= 0 && i + 1 < _slots.Length ? _slots[i + 1] : slot + 1;
        return Fraction(slot + Math.Min(0.5, (next - slot) / 2));
    }

    /// <summary>Minimum bar width so that every pair of neighbouring onsets gets its clearance.</summary>
    public double WidthFor(IReadOnlyList<(double Start, double Clearance)> gaps)
    {
        var width = 0.0;
        foreach (var (start, clearance) in gaps)
        {
            var i = Array.BinarySearch(_slots, Math.Round(start, 4));
            if (i < 0 || i + 1 >= _slots.Length) continue;
            var span = _fractions[i + 1] - _fractions[i];
            if (span > 1e-9) width = Math.Max(width, clearance / span);
        }
        return width;
    }
}
