using System;
using System.Collections.Generic;
using System.Linq;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Services;

namespace TabForge.Avalonia.Tests;

// Owns: the written side of the corpus counting probe: how many attacks each performed bar, track and voice should have, read from the imported score
//   in the compiler's performed order (repeats expanded, simile bars resolved) without running the compiler's note code.
// Does not own: the compiled events (CorpusProbeActual) or the report (CorpusProbeReport).
// Tests: CorpusProbeTests.
internal sealed class CorpusProbeWritten
{
    /// <summary>The previous beat of a voice lane, per string: the note that attacked the sound (Root, at Cell of Tally) and the note written last (Last, what a tie or legato slide continues from).</summary>
    private sealed record Sounding(TabNote Root, ProbeBarTally Tally, int Cell, TabNote Last);

    private sealed class Lane
    {
        public bool Seen;
        public int LastSource;
        public Dictionary<int, Sounding> Previous { get; } = new();
    }

    private readonly SongProject _project;
    private readonly ScoreTimeline _timeline;
    private readonly Dictionary<(int Track, int Voice), Lane> _lanes = new();

    public Dictionary<ProbeBarKey, ProbeBarTally> Tallies { get; } = new();
    /// <summary>For each performed bar of the timeline: which pass over its source bar it is (1-based).</summary>
    public int[] Occurrence { get; }

    public CorpusProbeWritten(SongProject project, ScoreTimeline timeline)
    {
        _project = project;
        _timeline = timeline;
        var passes = new Dictionary<int, int>();
        Occurrence = timeline.Bars.Select(bar => passes[bar.Bar] = passes.GetValueOrDefault(bar.Bar) + 1).ToArray();
        for (var p = 0; p < timeline.Bars.Count; p++)
            for (var track = 0; track < project.Tracks.Count; track++)
                CountBar(p, track);
    }

    private void CountBar(int performed, int trackIndex)
    {
        var bar = _timeline.Bars[performed];
        var track = _project.Tracks[trackIndex];
        if (track.IsAudio || bar.Bar >= track.Measures.Count) return;
        var shown = track.Measures[bar.Bar];
        var source = shown.SimileOneBar && bar.Bar > 0 ? bar.Bar - 1 : shown.SimileTwoBar && bar.Bar > 1 ? bar.Bar - 2 : bar.Bar;
        var content = track.Measures[source];
        CountLane(performed, trackIndex, 0, source, content, content.Cells);
        if (content.Voice2Cells.Count > 0) CountLane(performed, trackIndex, 1, source, content, content.Voice2Cells);
    }

    private void CountLane(int performed, int trackIndex, int voice, int source, MeasureModel content, List<TabCell> cells)
    {
        var bar = _timeline.Bars[performed];
        var key = new ProbeBarKey(trackIndex, bar.Bar, Occurrence[performed], voice);
        var tally = new ProbeBarTally { Cells = cells, Measure = content, Bar = bar, NominalSlots = MusicTime.BarSlots(_project, bar.Bar) };
        Tallies[key] = tally;
        if (!_lanes.TryGetValue((trackIndex, voice), out var lane)) _lanes[(trackIndex, voice)] = lane = new Lane();
        var jumped = lane.Seen && source != lane.LastSource + 1;   // the compiler lets no tie cross a jump
        if (jumped) lane.Previous.Clear();
        lane.Seen = true;
        lane.LastSource = source;

        var consumed = 0.0;
        var first = true;
        for (var i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            if (cell.Notes.Count == 0 && !cell.IsRest && !cell.HasAnnotation && cell.Mix is null) continue;
            consumed += MusicTime.ConsumeSlots(cell);
            if (cell.Notes.Count == 0) lane.Previous.Clear();
            else CountBeat(tally, lane, cell, i, first && jumped, _project.Tracks[trackIndex].IsDrumTrack);
            first = false;
        }
        if (consumed > tally.NominalSlots + 0.01) tally.MissingCandidates.Add(voice == 0 ? ProbeCause.CellOverflow : ProbeCause.VoiceFold);
    }

    private static void CountBeat(ProbeBarTally tally, Lane lane, TabCell cell, int cellIndex, bool afterJump, bool drums)
    {
        if (cell.Notes.Any(n => n.IsGraceNote)) tally.ExtraCandidates.Add(ProbeCause.Grace);
        var next = new Dictionary<int, Sounding>();
        foreach (var note in cell.Notes)
        {
            var slot = drums ? 1000 + note.MidiValue : note.StringIndex;   // a drum tie links the same piece, not the shared string
            lane.Previous.TryGetValue(slot, out var origin);
            var sounding = new Sounding(note, tally, cellIndex, note);
            if (cell.IsTied || note.Tied)
            {
                tally.MissingCandidates.Add(ProbeCause.TieMerge);
                if (origin is null)
                {
                    tally.Expected++;
                    tally.ExtraCandidates.Add(ProbeCause.TieOrphan + ":NO_ORIGIN");
                    if (afterJump) tally.ExtraCandidates.Add(ProbeCause.Repeat);
                }
                else
                {
                    tally.ExtraCandidates.Add(ProbeCause.TieOrphan + (origin.Last.MidiValue != note.MidiValue ? ":PITCH_DIFFERS" : ":GAP"));
                    origin.Tally.TieOrigins.Add((origin.Cell, note.StringIndex));
                    sounding = origin with { Last = note };   // the chain keeps its first attack
                }
            }
            else if (origin is not null && IsLegatoLink(origin, note))
            {
                if (WithinWheelRange(origin, note))
                {
                    tally.MissingCandidates.Add(ProbeCause.LegatoMerge);   // a legato slide's destination is not picked again, link by link
                    sounding = origin with { Last = note };
                }
                else { tally.Expected++; tally.ExtraCandidates.Add(ProbeCause.LegatoRange); }   // the compiler's pitch wheel cannot reach: it picks the note
            }
            else tally.Expected++;
            // A tremolo or trill leaves nothing open for a later tie; a grace note never hides its principal note.
            if (note.Techniques.Contains(TechniqueNames.TremoloPick) || note.Techniques.Contains(TechniqueNames.Trill)) continue;
            if (!note.IsGraceNote || !next.ContainsKey(slot)) next[slot] = sounding;
        }
        lane.Previous.Clear();
        foreach (var pair in next) lane.Previous[pair.Key] = pair.Value;
    }

    /// <summary>The compiler's legato rule: the previous note slides (legato) to this pitch and is not a dead note (a muted scrape is picked again).</summary>
    private static bool IsLegatoLink(Sounding origin, TabNote note) =>
        origin.Last.Techniques.Contains(TechniqueNames.LegatoSlide) && !origin.Last.Dead &&
        origin.Last.SlideTargetMidi == note.MidiValue && origin.Last.MidiValue != note.MidiValue;

    /// <summary>The wheel range of the compiler (12 semitones from the attacked note; 10 when the destination has a pitch curve of its own, about 2 semitones).</summary>
    private static bool WithinWheelRange(Sounding origin, TabNote note)
    {
        var curved = note.BendPoints.Count > 0 || note.Techniques.Any(t => t is "Bend" or "Vibrato" or "WideVibrato");
        return Math.Abs(note.MidiValue - origin.Root.MidiValue) <= (curved ? 10 : 12);
    }
}
