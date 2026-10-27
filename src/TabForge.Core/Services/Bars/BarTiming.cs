using TabForge.Models;

namespace TabForge.Services;

// Owns: exact bar and cell duration arithmetic in ticks (960 per quarter), independent of the editor's rounded 16th grid.
// Does not own: the red bar marking (MusicTime.AnalyzeBar) or navigation.
// Tests: BarTimingTests.
public enum BarTimingStatus { Complete, Underfull, Overfull }

/// <summary>An exact duration in ticks (960 per quarter), kept as a reduced fraction so tuplets never lose time to rounding.</summary>
public readonly struct Ticks : IEquatable<Ticks>, IComparable<Ticks>
{
    public long Numerator { get; }
    public long Denominator { get; }

    public Ticks(long numerator, long denominator)
    {
        if (denominator == 0) throw new ArgumentOutOfRangeException(nameof(denominator));
        if (denominator < 0) { numerator = -numerator; denominator = -denominator; }
        var gcd = Gcd(Math.Abs(numerator), denominator);
        Numerator = numerator / gcd;
        Denominator = denominator / gcd;
    }

    public static readonly Ticks Zero = new(0, 1);

    public static Ticks FromWhole(long ticks) => new(ticks, 1);

    private static long Gcd(long a, long b)
    {
        while (b != 0) { var next = a % b; a = b; b = next; }
        return a == 0 ? 1 : a;
    }

    private static long Den(Ticks ticks) => ticks.Denominator == 0 ? 1 : ticks.Denominator;

    public static Ticks operator +(Ticks a, Ticks b)
        => new(a.Numerator * Den(b) + b.Numerator * Den(a), Den(a) * Den(b));

    public static Ticks operator -(Ticks a, Ticks b)
        => new(a.Numerator * Den(b) - b.Numerator * Den(a), Den(a) * Den(b));

    public static bool operator <(Ticks a, Ticks b) => a.Numerator * Den(b) < b.Numerator * Den(a);

    public static bool operator >(Ticks a, Ticks b) => a.Numerator * Den(b) > b.Numerator * Den(a);

    public static bool operator <=(Ticks a, Ticks b) => a.Numerator * Den(b) <= b.Numerator * Den(a);

    public static bool operator >=(Ticks a, Ticks b) => a.Numerator * Den(b) >= b.Numerator * Den(a);

    public static bool operator ==(Ticks a, Ticks b) => a.Equals(b);

    public static bool operator !=(Ticks a, Ticks b) => !a.Equals(b);

    public static implicit operator Ticks(long wholeTicks) => new(wholeTicks, 1);

    public double Value => (double)Numerator / Den(this);

    public int CompareTo(Ticks other) => this < other ? -1 : this > other ? 1 : 0;

    public bool Equals(Ticks other) => Numerator == other.Numerator && Den(this) == Den(other);

    public override bool Equals(object? obj) => obj is Ticks other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Numerator, Den(this));

    public override string ToString() => Den(this) == 1 ? Numerator.ToString(System.Globalization.CultureInfo.InvariantCulture)
        : $"{Numerator}/{Den(this)}";
}

/// <summary>
/// A bar's exact fill: its capacity from the time signature and the time its cells consume. The red marking in the editor
/// comes from <see cref="Status"/> (spec rules R1-R9); <see cref="MusicTime.AnalyzeBar"/> is kept unchanged for its other callers.
/// </summary>
public static class BarTiming
{
    /// <summary>Tolerance of the fill check: 1/1000 of a quarter (0.96 ticks), like the editor's slot epsilon.</summary>
    private static readonly Ticks ToleranceTicks = new(24, 25);

    /// <summary>The bar's exact capacity in ticks: numerator × 3840 / denominator.</summary>
    public static Ticks BarCapacityTicks(int numerator, int denominator)
        => new((long)Math.Max(1, numerator) * 3840, Math.Max(1, denominator));

    /// <summary>The exact written duration of a cell; a grace-only cell takes no time.</summary>
    public static Ticks CellTicks(TabCell cell)
    {
        if (MusicTime.IsGraceOnly(cell)) return Ticks.Zero;
        var ticks = ValueTicks(cell.DurationDenominator <= 0 ? 16 : cell.DurationDenominator, cell.Dots);
        var (tupletNum, tupletDen) = cell.Tuplet;
        if (tupletNum > 0 && tupletDen > 0) ticks = new Ticks(ticks.Numerator * tupletDen, ticks.Denominator * tupletNum);
        return ticks;
    }

    /// <summary>
    /// The time the voice's temporal cells consume (notes, rests, ties and annotated beats count once per cell, a chord once);
    /// grace-only cells take none. <paramref name="beforeCell"/> limits the sum to cells before an index (the free slot's start).
    /// </summary>
    public static Ticks VoiceUsedTicks(MeasureModel measure, int voice, int beforeCell = int.MaxValue)
    {
        var cells = voice >= 2 ? measure.Voice2Cells : measure.Cells;
        var used = Ticks.Zero;
        for (var i = 0; i < cells.Count && i < beforeCell; i++)
            if (MusicTime.ConsumesTime(cells[i])) used += CellTicks(cells[i]);
        return used;
    }

    /// <summary>
    /// The worst voice's fill state under the red-bar spec R1-R9: per-voice pure sum, any voice with events wrong is red,
    /// empty voices ignored, empty bars OK, whole-bar rests OK, anacrusis/free-time/simile exempt, 1/1000-quarter tolerance.
    /// </summary>
    public static BarTimingStatus Status(MeasureModel measure, int numerator, int denominator)
    {
        if (measure.FreeTime || measure.SimileOneBar || measure.SimileTwoBar) return BarTimingStatus.Complete;
        if (measure.Anacrusis) return BarTimingStatus.Complete;
        if (IsWholeBarRest(measure)) return BarTimingStatus.Complete;
        var capacity = BarCapacityTicks(numerator, denominator);
        var status = VoiceStatus(measure, 1, capacity);
        return Worst(status, VoiceStatus(measure, 2, capacity));
    }

    /// <summary>True when a secondary voice holds notes; a rest-only secondary voice is empty (Guitar Pro / TuxGuitar behaviour).</summary>
    public static bool HasSecondaryNotes(IReadOnlyList<TabCell> cells)
    {
        for (var i = 0; i < cells.Count; i++) if (MusicTime.ConsumesTime(cells[i]) && cells[i].Notes.Count > 0) return true;
        return false;
    }

    /// <summary>Voice 1 is checked for under and overfull; a secondary voice only when it has notes, and then only for overfull (the rest is implied rests).</summary>
    private static BarTimingStatus VoiceStatus(MeasureModel measure, int voice, Ticks capacity)
    {
        var secondary = voice >= 2;
        if (secondary ? !HasSecondaryNotes(measure.Voice2Cells) : !HasTimedCells(measure.Cells)) return BarTimingStatus.Complete;
        var used = VoiceUsedTicks(measure, voice);
        if (used > capacity + ToleranceTicks) return BarTimingStatus.Overfull;
        if (!secondary && used < capacity - ToleranceTicks) return BarTimingStatus.Underfull;
        return BarTimingStatus.Complete;
    }

    /// <summary>
    /// True when voice 1 is exactly one plain rest of any length (no notes, no annotation) and the secondary voice holds no notes: Guitar Pro's
    /// empty bar, drawn as a whole-bar rest (R1, also the old exporter's lone quarter rest). Two or more rests that do not fill the bar stay underfull.
    /// </summary>
    private static bool IsWholeBarRest(MeasureModel measure)
    {
        if (HasSecondaryNotes(measure.Voice2Cells)) return false;
        TabCell? single = null;
        var count = 0;
        for (var i = 0; i < measure.Cells.Count && count <= 1; i++)
            if (MusicTime.ConsumesTime(measure.Cells[i])) { count++; single = measure.Cells[i]; }
        return count == 1 && single is { IsRest: true, HasAnnotation: false } && single.Notes.Count == 0;
    }

    private static BarTimingStatus Worst(BarTimingStatus a, BarTimingStatus b) => (BarTimingStatus)Math.Max((int)a, (int)b);

    private static bool HasTimedCells(IReadOnlyList<TabCell> cells)
    {
        for (var i = 0; i < cells.Count; i++) if (MusicTime.ConsumesTime(cells[i])) return true;
        return false;
    }

    private static Ticks ValueTicks(int denominator, int dots)
    {
        var ticks = new Ticks(3840, Math.Clamp(denominator <= 0 ? 16 : denominator, 1, 64));
        if (dots == 1) ticks = new Ticks(ticks.Numerator * 3, ticks.Denominator * 2);
        else if (dots >= 2) ticks = new Ticks(ticks.Numerator * 7, ticks.Denominator * 4);
        return ticks;
    }
}
