using System.Linq;
using TabForge.Models;

namespace TabForge.Services;

/// <summary>A fermata on a beat: where it starts, how long the beat is (sixteenth slots) and how much longer it is held (1.0 = +100%).</summary>
public readonly record struct FermataHold(double Slot, double LengthSlots, double Factor);

/// <summary>
/// One fermata hold inside a performed bar. BaseStartMs/BaseLengthMs are the beat's position and length in the bar's own
/// time (without any hold); ExtraMs is how much longer it is held. Everything after the beat moves later by ExtraMs.
/// </summary>
public readonly record struct FermataSpan(double Slot, double LengthSlots, double BaseStartMs, double BaseLengthMs, double ExtraMs)
{
    /// <summary>Bar-relative time without holds to bar-relative performed time (inside a hold the beat is stretched evenly).</summary>
    public static double Warp(FermataSpan[]? spans, double baseMs)
    {
        if (spans is null) return baseMs;
        var ms = baseMs;
        foreach (var s in spans)
        {
            if (baseMs <= s.BaseStartMs) break;
            ms += s.BaseLengthMs > 0 ? s.ExtraMs * Math.Min(1, (baseMs - s.BaseStartMs) / s.BaseLengthMs) : s.ExtraMs;
        }
        return ms;
    }

    /// <summary>Inverse of <see cref="Warp"/>: performed bar-relative time back to time without holds.</summary>
    public static double Unwarp(FermataSpan[]? spans, double performedMs)
    {
        if (spans is null) return performedMs;
        var shift = 0.0;
        foreach (var s in spans)
        {
            var heldStart = s.BaseStartMs + shift;
            if (performedMs <= heldStart) break;
            var heldLength = s.BaseLengthMs + s.ExtraMs;
            if (performedMs < heldStart + heldLength)
                return s.BaseStartMs + (performedMs - heldStart) * (heldLength > 0 ? s.BaseLengthMs / heldLength : 0);
            shift += s.ExtraMs;
        }
        return performedMs - shift;
    }

    public static double TotalExtraMs(FermataSpan[]? spans)
    {
        var total = 0.0;
        if (spans is not null) foreach (var s in spans) total += s.ExtraMs;
        return total;
    }
}

// Owns: fermata timing: how a hold stretches a bar for every track.
// Does not own: the playback timeline compilation and the notation of fermatas.
// Tests: TestFermataPlayback.
/// <summary>
/// Fermata timing. A fermata is a timing event for the whole score: a hold on any track stretches the bar for every
/// track, so playback, the playhead, MIDI export and the offline render (which all use the compiled timeline) agree.
/// The maths underneath is <see cref="MusicTime"/>'s tempo map; this only adds the hold.
/// </summary>
public static class FermataTime
{
    /// <summary>How much longer a fermata holds its beat: 1.0 = +100%. The model stores only on/off, so a short/long type plugs in here.</summary>
    public static double Factor(TabCell cell) => 1.0;

    /// <summary>A beat is held when the cell flag is set (editor, GP import) or a note carries the legacy "Fermata" technique (old files, exporter accepts it).</summary>
    public static bool HasFermata(TabCell cell) => cell.Fermata || cell.Notes.Any(n => n.Techniques.Contains("Fermata"));

    /// <summary>
    /// The fermata holds of a bar, sorted by slot. Scans every track and both voices with the cursor walk playback uses.
    /// Holds at the same slot on several tracks are one hold (the longest).
    /// </summary>
    public static List<FermataHold> Holds(SongProject p, int barIndex)
    {
        var holds = new Dictionary<double, FermataHold>();
        var restHolds = new Dictionary<double, bool>();
        var barSlots = MusicTime.BarSlots(p, barIndex);
        foreach (var track in p.Tracks)
        {
            if (barIndex < 0 || barIndex >= track.Measures.Count) continue;
            var source = barIndex;
            if (track.Measures[barIndex].SimileOneBar && barIndex > 0) source = barIndex - 1;
            else if (track.Measures[barIndex].SimileTwoBar && barIndex > 1) source = barIndex - 2;
            var measure = track.Measures[source];
            foreach (var cells in new[] { measure.Cells, measure.Voice2Cells })
            {
                var cursor = 0.0;
                for (var i = 0; i < cells.Count; i++)
                {
                    var cell = cells[i];
                    if (cell.Notes.Count > 0 || cell.IsRest || cell.HasAnnotation)
                    {
                        var length = MusicTime.ConsumeSlots(cell);
                        if (HasFermata(cell) && !cell.IsGrace && cursor < barSlots)
                        {
                            var hold = new FermataHold(cursor, Math.Min(length, barSlots - cursor), Factor(cell));
                            var isRest = cell.Notes.Count == 0;
                            // GP files store a fermata on the bar, so a re-import puts it on every track's beat at that position,
                            // including the whole-bar rests of tracks that are silent there. A rest never out-holds a sounding
                            // beat at the same slot; otherwise the longest hold wins.
                            if (!holds.TryGetValue(cursor, out var old)) { holds[cursor] = hold; restHolds[cursor] = isRest; }
                            else if (restHolds[cursor] == isRest ? old.Factor * old.LengthSlots < hold.Factor * hold.LengthSlots : restHolds[cursor]) { holds[cursor] = hold; restHolds[cursor] = isRest; }
                        }
                        cursor += length;
                    }
                    else if (cursor <= i) cursor = i + 1;
                    if (cursor >= barSlots) break;
                }
            }
        }
        return holds.Values.OrderBy(h => h.Slot).ToList();
    }

    /// <summary>A bar's holds placed in the bar's own (unheld) milliseconds through the tempo map, or null when it has none.</summary>
    public static FermataSpan[]? Spans(SongProject p, int barIndex, double tempoScale, int startTempo)
    {
        var holds = Holds(p, barIndex);
        if (holds.Count == 0) return null;
        var bar = MusicTime.BarOf(p, barIndex);
        var spans = new FermataSpan[holds.Count];
        for (var i = 0; i < holds.Count; i++)
        {
            var start = MusicTime.OffsetMs(bar, holds[i].Slot, startTempo, tempoScale);
            var end = MusicTime.OffsetMs(bar, holds[i].Slot + holds[i].LengthSlots, startTempo, tempoScale);
            spans[i] = new FermataSpan(holds[i].Slot, holds[i].LengthSlots, start, end - start, (end - start) * holds[i].Factor);
        }
        return spans;
    }
}
