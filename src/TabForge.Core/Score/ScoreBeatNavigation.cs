using TabForge.Services;

namespace TabForge.Views.Score;

/// <summary>A plain or dotted written value (no tuplet): the duration a new slot takes.</summary>
public readonly record struct DurationChoice(int Denominator, int Dots);

// Owns: the pure duration rule for the free slot after the last beat of a bar.
// Does not own: where the cursor goes (CursorPositions) or changing the model (ScoreEditCommands).
// Tests: BarTimingTests.
public static class ScoreBeatNavigation
{
    // Whole to 64th, plain and dotted, largest first; the note-value set of BarFill.Sizes extended to the 32nd and 64th.
    private static readonly IReadOnlyList<(int Denominator, int Dots)> Values = new (int Denominator, int Dots)[]
    {
        (1, 0), (2, 1), (2, 0), (4, 1), (4, 0), (8, 1), (8, 0),
        (16, 1), (16, 0), (32, 1), (32, 0), (64, 1), (64, 0)
    };

    /// <summary>
    /// The duration of the new slot after the previous beat: the previous duration when it fits the remaining bar time, else the
    /// largest plain or dotted standard value that fits, else null. Never a tuplet in the fallback.
    /// </summary>
    public static Ticks? NewSlotDuration(Ticks previousTicks, Ticks remainingTicks)
    {
        if (remainingTicks <= Ticks.Zero) return null;
        if (previousTicks > Ticks.Zero && previousTicks <= remainingTicks) return previousTicks;
        foreach (var (denominator, dots) in Values)
        {
            var candidate = ValueTicks(denominator, dots);
            if (candidate <= remainingTicks) return candidate;
        }
        return null;
    }

    /// <summary>The plain or dotted written value with exactly these ticks; null for a tuplet, a double dot or another length.</summary>
    public static DurationChoice? WrittenValue(Ticks ticks)
    {
        foreach (var (denominator, dots) in Values)
            if (ValueTicks(denominator, dots) == ticks) return new DurationChoice(denominator, dots);
        return null;
    }

    private static Ticks ValueTicks(int denominator, int dots)
    {
        var ticks = new Ticks(3840, denominator);
        if (dots == 1) ticks = new Ticks(ticks.Numerator * 3, ticks.Denominator * 2);
        else if (dots >= 2) ticks = new Ticks(ticks.Numerator * 7, ticks.Denominator * 4);
        return ticks;
    }
}
