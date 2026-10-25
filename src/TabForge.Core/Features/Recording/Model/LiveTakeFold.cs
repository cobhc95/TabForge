using System;
using System.Collections.Generic;

namespace TabForge.Features.Recording.Model;

// Owns: the live (while recording) lap math: which song-time bands a growing looped take draws, their pass number and live lane.
// Does not own: the take state (LiveTakes), the drawing (TimelineLiveTakeLayer) or the landed takes (RecordedTakes).
// Tests: TabForge.Avalonia.Tests.LiveTakeFoldTests, RepeatTakeLandingTests, RepeatViewTests.

/// <summary>One drawable band of a live take: a song-time span on live lane <see cref="Row"/> (0 = the take's base lane) for lap <see cref="Pass"/>;
/// in the repeat view <see cref="PartCount"/> is how many parts the repeat it is one play of has (1 = a span played once)
/// and <see cref="TagPass"/> is the 2+ pass of an unfolded one-pass slice (the "pass k" tag; 0 = none), as <see cref="Rules.RepeatSlice.TagPass"/>.</summary>
public readonly record struct LiveTakeBand(double StartSec, double EndSec, int Row, int Pass, int PartCount = 1, int TagPass = 0);

/// <summary>
/// The one live-lap rule. Before the first wrap a take is one band from its record start to its growing front. After a wrap, every
/// lap draws on its own live lane folded onto the loop range: lap 1 runs from the record start to the loop end (any pre-roll included),
/// every later full lap covers the loop start..end, and the partial current lap grows from the loop start toward its loop position.
/// A front exactly on a loop boundary has no partial band. Lanes never march right in time and never leave the track's row.
/// </summary>
public static class LiveTakeFold
{
    /// <summary>Shorter loop ranges are treated as no loop (the same threshold the recording/live code uses).</summary>
    public const double MinimumLoopSec = 0.05;

    /// <summary>The bands of a take from <paramref name="startSec"/> grown to <paramref name="nowSec"/>; one band when there is no usable loop or no wrap yet.</summary>
    public static IReadOnlyList<LiveTakeBand> Bands(double startSec, double nowSec, (double Start, double End)? loop)
    {
        var front = Math.Max(startSec, nowSec);
        var single = new[] { new LiveTakeBand(startSec, front, 0, 1) };
        if (loop is not { } range) return single;

        var loopStart = range.Start;
        var loopEnd = range.End;
        var length = loopEnd - loopStart;
        // No usable loop, a take that began after the loop, or still inside the first lap: the ordinary live box.
        if (length < MinimumLoopSec || startSec >= loopEnd || front <= loopEnd) return single;

        var excess = front - loopEnd;
        var completedAfterFirst = (int)Math.Floor(excess / length + 1e-9);
        var remainder = excess - completedAfterFirst * length;
        var completed = 1 + completedAfterFirst;
        var bands = new List<LiveTakeBand>(completed + (remainder > 1e-9 ? 1 : 0));

        // Completed laps, kept in lap order: lap 1 starts at the record start (pre-roll included) and ends at the loop end; every
        // later lap is folded onto the loop range, each on its own live lane below the take's base lane.
        for (var lap = 1; lap <= completed; lap++)
        {
            var from = lap == 1 ? startSec : loopStart;
            bands.Add(new LiveTakeBand(from, loopEnd, lap - 1, lap));
        }

        // The current partial lap grows on the next live lane, from the loop start toward its loop position; on an exact boundary there is none.
        if (remainder > 1e-9)
            bands.Add(new LiveTakeBand(loopStart, loopStart + remainder, completed, completed + 1));
        return bands;
    }

    /// <summary>The bands of a take growing through score repeats (the repeat view): the take's performed span so far folded by the one
    /// <see cref="Rules.RepeatFold"/> — a span played once is pass 0, each play of a repeated range its play number with the repeat's part
    /// count — all on the take's lane (row 0), so the live box grows through part 1 then part 2 inside the same lane.</summary>
    public static IReadOnlyList<LiveTakeBand> RepeatBands(double startSec, double nowSec, TabForge.Models.PerformedBar[]? performed)
    {
        var front = Math.Max(startSec, nowSec);
        if (performed is null || performed.Length == 0 || front - startSec <= 1e-9)
            return new[] { new LiveTakeBand(startSec, front, 0, 1) };
        var bands = new List<LiveTakeBand>();
        foreach (var slice in Rules.RepeatFold.FoldedSlices(startSec, front, performed))
        {
            if (slice.EndSec - slice.StartSec <= 1e-9) continue;
            bands.Add(new LiveTakeBand(slice.StartSec, slice.EndSec, 0, slice.IsPart ? slice.PartIndex + 1 : 0, slice.PartCount, slice.TagPass));
        }
        return bands.Count == 0 ? new[] { new LiveTakeBand(startSec, front, 0, 1) } : bands;
    }
}
