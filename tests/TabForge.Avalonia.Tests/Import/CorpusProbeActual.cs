using System;
using System.Collections.Generic;
using System.Linq;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Services;

namespace TabForge.Avalonia.Tests;

// Owns: the compiled side of the corpus counting probe: joins every NoteEvent to its performed bar, counts it, checks its onset and duration
//   against the written position, and turns each bar's delta into cause flags.
// Does not own: the written counts (CorpusProbeWritten) or the report (CorpusProbeReport).
// Tests: CorpusProbeTests.
internal sealed class CorpusProbeActual
{
    private const double OnsetToleranceMs = 2;
    private const double DurationToleranceMs = 9;
    private const double RetriggerWindowMs = 12;
    private const double MaxGraceLeadMs = 130;

    private readonly ScoreTimeline _timeline;
    private readonly CorpusProbeWritten _written;
    private readonly Dictionary<(int Track, int Channel, int Midi), List<double>> _onsets;

    public CorpusProbeActual(ScoreTimeline timeline, CorpusProbeWritten written)
    {
        _timeline = timeline;
        _written = written;
        _onsets = timeline.Notes.GroupBy(n => (n.TrackIndex, n.Channel, n.Midi)).ToDictionary(g => g.Key, g => g.Select(n => n.OnsetMs).OrderBy(ms => ms).ToList());
        foreach (var note in timeline.Notes) Count(note);
        foreach (var tally in written.Tallies.Values) Explain(tally);
    }

    /// <summary>The performed bar an event belongs to: the one holding its onset, or the next one when a grace or slide-in lead put the onset before its bar.</summary>
    private int PerformedBarOf(NoteEvent note)
    {
        var bars = _timeline.Bars;
        var at = _timeline.BarIndexAt(note.OnsetMs);
        // A grace before the first beat of a bar that repeats back to back sounds in the end of the pass before it, which is the same source bar.
        if (note.Cell == 0 && at + 1 < bars.Count && bars[at + 1].Bar == note.Bar && bars[at + 1].StartMs - note.OnsetMs < MaxGraceLeadMs) return at + 1;
        if (bars[at].Bar != note.Bar && at + 1 < bars.Count && bars[at + 1].Bar == note.Bar) return at + 1;
        if (bars[at].Bar != note.Bar && at > 0 && bars[at - 1].Bar == note.Bar) return at - 1;
        return at;
    }

    private void Count(NoteEvent note)
    {
        var performed = PerformedBarOf(note);
        var key = new ProbeBarKey(note.TrackIndex, note.Bar, _written.Occurrence[performed], note.VoiceIndex);
        if (!_written.Tallies.TryGetValue(key, out var tally))
            _written.Tallies[key] = tally = new ProbeBarTally { Bar = _timeline.Bars[performed] };
        tally.Actual++;
        if (note.Cell >= tally.Cells.Count) return;
        var cell = tally.Cells[note.Cell];
        var written = cell.Notes.FirstOrDefault(n => n.StringIndex == note.StringIndex);
        if (written is null || tally.Bar.Slots != tally.NominalSlots) return;   // a short bar's slots are not the written grid
        CheckTiming(tally, note, cell, written);
    }

    private void CheckTiming(ProbeBarTally tally, NoteEvent note, TabCell cell, TabNote written)
    {
        var slot = cell.RhythmicPosition ?? note.Cell;
        var start = tally.Bar.MsAtFraction(slot / tally.Bar.Slots);
        var onsetErr = note.OnsetMs - start;
        if (Math.Abs(onsetErr) > Math.Abs(tally.OnsetErrMs)) tally.OnsetErrMs = onsetErr;
        if (Math.Abs(onsetErr) > OnsetToleranceMs) { tally.Blame(OnsetCause(tally, cell, written), 1); return; }
        if (ShortensByDesign(cell, written)) return;
        var durationErr = note.DurationMs - (tally.Bar.MsAtFraction((slot + MusicTime.ConsumeSlots(cell)) / tally.Bar.Slots) - start);
        if (Math.Abs(durationErr) > Math.Abs(tally.DurationErrMs)) tally.DurationErrMs = durationErr;
        if (Math.Abs(durationErr) > DurationToleranceMs) tally.Blame(DurationCause(tally, note, written, durationErr), 1);
    }

    private static string OnsetCause(ProbeBarTally tally, TabCell cell, TabNote written)
    {
        if (written.IsGraceNote || cell.Notes.Any(n => n.IsGraceNote)) return ProbeCause.Grace;
        if (written.Techniques.Contains("SlideInBelow") || written.Techniques.Contains("SlideInAbove")) return ProbeCause.SlideIn;
        if (cell.Tuplet.Numerator > 0 || cell.RhythmicPosition is not null) return ProbeCause.TupletShift;
        if (tally.Measure?.MidBarTempos is { Count: > 0 }) return ProbeCause.TempoMap;
        if (tally.Measure is { } measure && TripletFeels.Effective(measure) != TripletFeels.None) return ProbeCause.Swing;
        if (cell.Notes.Count > 1 && cell.Notes.Any(n => n.Techniques.Any(t => t.StartsWith("Brush") || t.StartsWith("Arpeggio") || t == "Rasgueado"))) return ProbeCause.Strum;
        return ProbeCause.OnsetUnexplained;
    }

    /// <summary>Notes whose length the compiler changes on purpose, so the written value is no yardstick.</summary>
    private static bool ShortensByDesign(TabCell cell, TabNote written) =>
        cell.Staccato || cell.SoundDurationPercent != 100 || written.Dead || written.IsGraceNote || cell.Notes.Any(n => n.IsGraceNote) ||
        written.Techniques.Any(t => TechniqueNames.IsPalmMute(t) || t == TechniqueNames.TremoloPick || t == TechniqueNames.Trill || t.StartsWith("SlideIn"));

    private string DurationCause(ProbeBarTally tally, NoteEvent note, TabNote written, double durationErr)
    {
        if (tally.TieOrigins.Contains((note.Cell, note.StringIndex)) && durationErr > 0) return ProbeCause.TieMerge;
        if (written.Techniques.Contains(TechniqueNames.LetRing)) return ProbeCause.LetRingCap;
        if (durationErr < 0 && RetriggeredAtEnd(note)) return ProbeCause.Retrig;
        return ProbeCause.DurationUnexplained;
    }

    /// <summary>True when the same pitch is struck again just as this note ends (the resolver trims the earlier note to keep the retrigger audible).</summary>
    private bool RetriggeredAtEnd(NoteEvent note)
    {
        var onsets = _onsets[(note.TrackIndex, note.Channel, note.Midi)];
        var index = onsets.BinarySearch(note.EndMs - RetriggerWindowMs);
        if (index < 0) index = ~index;
        return index < onsets.Count && onsets[index] <= note.EndMs + RetriggerWindowMs && onsets[index] > note.OnsetMs;
    }

    /// <summary>Count-level causes: the bar's candidates for the sign of its delta, or the unexplained bucket when it has none.</summary>
    private static void Explain(ProbeBarTally tally)
    {
        if (tally.Delta == 0) return;
        var candidates = tally.Delta > 0 ? tally.ExtraCandidates : tally.MissingCandidates;
        if (candidates.Count == 0) tally.Blame(ProbeCause.CountUnexplained, Math.Abs(tally.Delta));
        foreach (var cause in candidates) tally.Blame(cause, Math.Abs(tally.Delta));
    }
}
