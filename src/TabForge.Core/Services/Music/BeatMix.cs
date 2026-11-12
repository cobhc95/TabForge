// Owns: where a beat's mix changes live, across every track: the mix point on the cell, the bar's tempo (a bar tempo or a tempo point at the beat's slot, with its
//       ramp) and the question "does this bar change anything" for the timeline's marker. Also the write of a tempo to a beat for every track.
// Does not own: the dialog (MixTableWindow / MixTablePresenter), playback of the points (ScoreToMidiCompiler), reading them from files (the importers).
// Tests: run headlessly by the audit self-test (SelfTestMixTableAudit).
using TabForge.Models;

namespace TabForge.Services;

/// <summary>A beat's tempo: the BPM and the beats it takes to get there (0 = immediately).</summary>
public readonly record struct BeatTempo(int Tempo, int TransitionBeats);

/// <summary>Beat-level view of the mix changes of a song, over all tracks. Stateless.</summary>
public static class BeatMix
{
    private const double SlotTolerance = 0.5;

    /// <summary>The start slot of a cell of a bar (the first lane).</summary>
    public static double SlotOf(MeasureModel measure, int cell) =>
        cell >= 0 && cell < measure.Cells.Count ? MusicTime.Onsets(measure.Cells)[cell] : 0;

    /// <summary>The tempo that takes effect at <paramref name="slot"/> of the bar, from any track (the tempo is shared): a bar tempo at slot 0, or a tempo point on that slot.</summary>
    public static BeatTempo? TempoAt(SongProject project, int bar, double slot)
    {
        foreach (var track in project.Tracks)
        {
            if (bar >= track.Measures.Count) continue;
            var m = track.Measures[bar];
            if (m.MidBarTempos?.FirstOrDefault(p => Math.Abs(p.Slot - slot) < SlotTolerance) is { } point)
                return new BeatTempo(point.Tempo, (int)Math.Round(point.RampSlots / MusicTime.SlotsPerQuarter));
            if (slot < SlotTolerance && m.TempoChange is int t) return new BeatTempo(t, 0);
        }
        return null;
    }

    /// <summary>The mix point shown for a beat: the selected track's own, else one another track set for every track at that beat.</summary>
    public static MixChange? MixAt(SongProject project, int trackIndex, int bar, int cell)
    {
        if (CellMix(project, trackIndex, bar, cell) is { } own) return own;
        for (var t = 0; t < project.Tracks.Count; t++)
            if (t != trackIndex && CellMix(project, t, bar, cell) is { AllTracks: true } shared) return shared;
        return null;
    }

    private static MixChange? CellMix(SongProject project, int track, int bar, int cell)
    {
        if (track < 0 || track >= project.Tracks.Count || bar >= project.Tracks[track].Measures.Count) return null;
        var cells = project.Tracks[track].Measures[bar].Cells;
        return cell >= 0 && cell < cells.Count && cells[cell].Mix is { IsEmpty: false } mix ? mix : null;
    }

    /// <summary>The cell of a bar whose beat starts at <paramref name="slot"/> (the last beat that starts at or before it).</summary>
    public static int CellAt(MeasureModel measure, double slot)
    {
        var starts = MusicTime.Onsets(measure.Cells);
        var cell = 0;
        for (var i = 0; i < starts.Length; i++) if (starts[i] <= slot + SlotTolerance) cell = i;
        return cell;
    }

    /// <summary>
    /// The first beat of a bar that carries a change on any track, as (track, cell): the earliest of the tempo points and the mix points, on
    /// <paramref name="preferredTrack"/> when it has the change (or when the change is a tempo, which every track shares). Null when the bar changes nothing.
    /// </summary>
    public static (int Track, int Cell)? FirstChange(SongProject project, int bar, int preferredTrack)
    {
        (double Slot, int Track, int Cell)? best = null;
        void Offer(double slot, int track, int cell)
        {
            if (best is null || slot < best.Value.Slot - SlotTolerance || (Math.Abs(slot - best.Value.Slot) < SlotTolerance && track == preferredTrack && best.Value.Track != preferredTrack))
                best = (slot, track, cell);
        }
        var own = preferredTrack >= 0 && preferredTrack < project.Tracks.Count && bar < project.Tracks[preferredTrack].Measures.Count ? preferredTrack : -1;
        for (var t = 0; t < project.Tracks.Count; t++)
        {
            if (bar >= project.Tracks[t].Measures.Count) continue;
            var m = project.Tracks[t].Measures[bar];
            var starts = MusicTime.Onsets(m.Cells);
            for (var c = 0; c < m.Cells.Count; c++) if (m.Cells[c].Mix is { IsEmpty: false }) Offer(starts[c], t, c);
            var shared = own >= 0 ? own : t;   // a tempo belongs to every track: shown on the track being edited
            if (m.TempoChange is not null) Offer(0, shared, 0);
            foreach (var p in m.MidBarTempos ?? new()) Offer(p.Slot, shared, CellAt(project.Tracks[shared].Measures[bar], p.Slot));
        }
        return best is { } b ? (b.Track, b.Cell) : null;
    }

    /// <summary>True when the beat carries a mix point or a tempo, from any track.</summary>
    public static bool BeatHasChange(SongProject project, int track, int bar, int cell) =>
        MixAt(project, track, bar, cell) is not null || (track >= 0 && track < project.Tracks.Count && bar < project.Tracks[track].Measures.Count
            && TempoAt(project, bar, SlotOf(project.Tracks[track].Measures[bar], cell)) is not null);

    /// <summary>True when any track changes sound, effects or tempo anywhere in the bar.</summary>
    public static bool BarHasChange(SongProject project, int bar)
    {
        foreach (var track in project.Tracks)
        {
            if (bar >= track.Measures.Count) continue;
            var m = track.Measures[bar];
            if (m.TempoChange is not null || m.MidBarTempos is { Count: > 0 }) return true;
            if (m.Cells.Any(c => c.Mix is { IsEmpty: false }) || m.Voice2Cells.Any(c => c.Mix is { IsEmpty: false })) return true;
        }
        return false;
    }

    /// <summary>Every bar that changes something, for the timeline marker.</summary>
    public static HashSet<int> BarsWithChanges(SongProject project)
    {
        var bars = new HashSet<int>();
        var longest = project.Tracks.Count == 0 ? 0 : project.Tracks.Max(t => t.Measures.Count);
        for (var bar = 0; bar < longest; bar++) if (BarHasChange(project, bar)) bars.Add(bar);
        return bars;
    }

    /// <summary>Sets (or, when <paramref name="tempo"/> is null, removes) the tempo at a beat of a bar on every track; a ramp is a tempo point, an instant change at the bar start the bar tempo.</summary>
    public static void SetTempo(SongProject project, int bar, double slot, BeatTempo? tempo)
    {
        foreach (var track in project.Tracks)
        {
            if (bar >= track.Measures.Count) continue;
            var m = track.Measures[bar];
            m.MidBarTempos?.RemoveAll(p => Math.Abs(p.Slot - slot) < SlotTolerance);
            if (slot < SlotTolerance) m.TempoChange = null;
            if (tempo is { } t)
            {
                if (slot < SlotTolerance && t.TransitionBeats == 0) m.TempoChange = t.Tempo;
                else
                {
                    (m.MidBarTempos ??= new()).Add(new TempoPoint(Math.Round(slot, 3), t.Tempo, t.TransitionBeats * MusicTime.SlotsPerQuarter));
                    m.MidBarTempos.Sort((a, b) => a.Slot.CompareTo(b.Slot));
                }
            }
            if (m.MidBarTempos is { Count: 0 }) m.MidBarTempos = null;
        }
    }
}
