using TabForge.Models;

namespace TabForge.Features.Recording.Rules;

// Owns: the pass-aware position over a repeat (written bar + pass) and its two-way mapping to performed (playback) seconds.
// Does not own: the pass mapping it is built on (RepeatPasses), the bar grid rules it mirrors (AudioClipTimelineRules) or any drawing.
// Tests: TabForge.Avalonia.Tests.RepeatMapTests.

/// <summary>
/// A timeline position over a repeat: the written bar, how far through it (0 to 1), and which play (1-based pass).
/// Outside repeats the pass is always 1. Drawing only: playback still runs on performed seconds.
/// </summary>
public readonly record struct RepeatPosition(int WrittenBar, double Fraction, int Pass);

/// <summary>The pass-aware mapping between written bars and performed seconds, one function of <see cref="RepeatPosition"/>.</summary>
public static class RepeatMap
{
    private const double MillisecondsPerSecond = 1000;

    /// <summary>
    /// The performed second of <paramref name="pos"/>: the start of its pass-th performance of the bar
    /// (<see cref="RepeatPasses.PerformedStartForPass"/>) plus the fraction through that play's own length
    /// (voltas differ per play). NaN when that play does not exist.
    /// </summary>
    public static double ToPerformed(PerformedBar[]? performed, RepeatPosition pos)
    {
        if (performed is null || performed.Length == 0 || pos.Pass < 1) return double.NaN;
        var startSec = RepeatPasses.PerformedStartForPass(performed, pos.WrittenBar, pos.Pass);
        if (double.IsNaN(startSec)) return double.NaN;
        var play = 0;
        foreach (var entry in performed)
        {
            if (entry.Bar != pos.WrittenBar) continue;
            if (++play != pos.Pass) continue;
            var lengthSec = (entry.EndMs - entry.StartMs) / MillisecondsPerSecond;
            return startSec + Math.Clamp(pos.Fraction, 0, 1) * Math.Max(0, lengthSec);
        }
        return double.NaN;
    }

    /// <summary>
    /// The inverse of <see cref="ToPerformed"/>: the written bar and fraction holding <paramref name="seconds"/>
    /// (same probe as <see cref="Presenters.AudioClipTimelineRules.BarAtSeconds"/>) plus the pass, the 1-based
    /// occurrence of that bar up to the found position. Times outside the song clamp to the first/last bar.
    /// </summary>
    public static RepeatPosition FromPerformed(PerformedBar[]? performed, double seconds)
    {
        if (performed is null || performed.Length == 0) return new RepeatPosition(0, 0, 1);
        var ms = Math.Max(0, seconds) * MillisecondsPerSecond;
        var index = LastStartingAtOrBefore(performed, ms);
        if (index < 0) return new RepeatPosition(performed[0].Bar, 0, 1);
        if (ms >= performed[^1].EndMs) return new RepeatPosition(performed[^1].Bar, 1, PassCount(performed, performed[^1].Bar));
        var bar = performed[index];
        var length = bar.EndMs - bar.StartMs;
        var fraction = length > 0 ? (ms - bar.StartMs) / length : 0;
        var pass = 0;
        for (var k = 0; k <= index; k++)
            if (performed[k].Bar == bar.Bar) pass++;
        return new RepeatPosition(bar.Bar, Math.Clamp(fraction, 0, 1), Math.Max(1, pass));
    }

    /// <summary>The number of times <paramref name="bar"/> is performed (1 outside repeats).</summary>
    public static int PassCount(PerformedBar[]? performed, int bar)
    {
        if (performed is null) return 0;
        var count = 0;
        foreach (var entry in performed)
            if (entry.Bar == bar) count++;
        return count;
    }

    /// <summary>The index of the last performed bar that starts at or before <paramref name="ms"/>; -1 when none does.</summary>
    private static int LastStartingAtOrBefore(PerformedBar[] performed, double ms)
    {
        var low = 0;
        var high = performed.Length - 1;
        var found = -1;
        while (low <= high)
        {
            var mid = (low + high) / 2;
            if (performed[mid].StartMs <= ms) { found = mid; low = mid + 1; }
            else high = mid - 1;
        }
        return found;
    }
}
