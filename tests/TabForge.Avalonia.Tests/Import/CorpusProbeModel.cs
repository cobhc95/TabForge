using System.Collections.Generic;
using System.Linq;
using TabForge.Models;
using TabForge.Playback;

namespace TabForge.Avalonia.Tests;

// Owns: the data of the corpus counting probe: the cause names, the key of one counted bar and its expected/actual tally.
// Does not own: counting the written notes (CorpusProbeWritten), joining the compiled events (CorpusProbeActual) or the CSV report (CorpusProbeReport).
// Tests: CorpusProbeTests.

/// <summary>Cause flags of a delta between the written notes and the compiled attacks (the audit's list, plus the few the probe adds: TEMPO_MAP, SWING, STRUM, JOIN_MISS and the *_UNEXPLAINED buckets).</summary>
internal static class ProbeCause
{
    public const string TieMerge = "TIE_MERGE", LegatoMerge = "LEGATO_MERGE", LegatoRange = "LEGATO_RANGE", TieOrphan = "TIE_ORPHAN", Grace = "GRACE", Repeat = "REPEAT";
    public const string DupDrop = "DUP_DROP", VoiceFold = "VOICE_FOLD", CellOverflow = "CELL_OVERFLOW", Truncated = "TRUNCATED";
    public const string SlideIn = "SLIDEIN", TupletShift = "TUPLET_SHIFT", TempoMap = "TEMPO_MAP", Swing = "SWING", Strum = "STRUM";
    public const string LetRingCap = "LETRING_CAP", Retrig = "RETRIG", JoinMiss = "JOIN_MISS";
    public const string CountUnexplained = "COUNT_UNEXPLAINED", OnsetUnexplained = "ONSET_UNEXPLAINED", DurationUnexplained = "DUR_UNEXPLAINED";
}

/// <summary>One counted bar: a track's source bar on its n-th performance, in one voice lane.</summary>
internal readonly record struct ProbeBarKey(int Track, int SrcBar, int Occurrence, int Voice);

/// <summary>Expected vs compiled attacks of one <see cref="ProbeBarKey"/>, with the facts the cause flags are chosen from.</summary>
internal sealed class ProbeBarTally
{
    public int Expected;
    public int Actual;
    /// <summary>Causes that would explain more attacks than written / fewer attacks than written (set while counting the written notes).</summary>
    public HashSet<string> ExtraCandidates { get; } = new();
    public HashSet<string> MissingCandidates { get; } = new();
    /// <summary>Notes (cell, string) whose sound a later tie continues: their compiled duration is longer than the written one by design.</summary>
    public HashSet<(int Cell, int String)> TieOrigins { get; } = new();
    public IReadOnlyList<TabCell> Cells { get; init; } = new List<TabCell>();
    public MeasureModel? Measure { get; init; }
    public ScoreBar Bar { get; init; }
    public int NominalSlots { get; init; }

    /// <summary>Cause name to the amount it explains (attacks for count causes, events for onset / duration causes).</summary>
    public Dictionary<string, int> Causes { get; } = new();
    public double OnsetErrMs;
    public double DurationErrMs;

    public int Delta => Actual - Expected;
    public void Blame(string cause, int amount) => Causes[cause] = Causes.GetValueOrDefault(cause) + amount;
    public string CauseList => string.Join('|', Causes.Keys.OrderBy(k => k, System.StringComparer.Ordinal));
}
