using TabForge.Models;
using TabForge.Platform;

namespace TabForge.Services;

// Owns: the arrangement timeline's per-bar activity summary (note, beat and rest counts, miniature events), its cache lookup keyed by
//       measure object, generation and project revision, and the muted-track fade and desaturate maths.
// Does not own: where the cache lives, painting the miniature, which track is muted, or toolkit colours.
// Tests: no dedicated test class.
/// <summary>What one drawn timeline cell shows of a bar. Pure functions, no UI types.</summary>
public static class TimelineActivityRules
{
    /// <summary>One sounding cell drawn as a miniature block; <see cref="DrumVelocities"/> holds up to four note velocities for drum tracks, else is empty.</summary>
    public readonly record struct MiniatureEvent(int CellIndex, double ConsumedSlots, int NoteCount, int[] DrumVelocities);

    public readonly record struct Activity(int Notes, int Beats, int Rests, int CellCount, bool HasContent, MiniatureEvent[] MiniatureEvents);

    /// <summary>
    /// One cache slot. Key: the measure object, the owner's generation and the project's content revision. Ceiling: one slot per bar of
    /// each track, held in the owner's arrays and rebuilt when the track list changes. Thread: UI thread only.
    /// </summary>
    public readonly record struct CacheEntry(MeasureModel? Measure, int Generation, int Revision, Activity Value);

    private static readonly int[] NoVelocities = Array.Empty<int>();

    /// <summary>The cached summary when the slot still matches the key, else a fresh one stored into <paramref name="slot"/>.</summary>
    public static Activity Resolve(ref CacheEntry slot, MeasureModel measure, int generation, int revision, bool isDrums)
    {
        if (ReferenceEquals(slot.Measure, measure) && slot.Generation == generation && slot.Revision == revision) return slot.Value;
        var activity = Build(measure, isDrums);
        slot = new CacheEntry(measure, generation, revision, activity);
        return activity;
    }

    public static Activity Build(MeasureModel measure, bool isDrums)
    {
        var notes = 0;
        var beats = 0;
        var rests = 0;
        foreach (var cell in measure.Cells)
        {
            if (cell.IsRest) { rests++; continue; }
            if (cell.Notes.Count == 0) continue;
            notes += cell.Notes.Count;
            beats++;
        }

        var miniature = new List<MiniatureEvent>();
        var index = 0;
        while (index < measure.Cells.Count)
        {
            var cell = measure.Cells[index];
            var consumed = MusicTime.ConsumeSlots(cell);
            if (!cell.IsRest && cell.Notes.Count > 0)
            {
                var velocities = NoVelocities;
                if (isDrums)
                {
                    var count = Math.Min(4, cell.Notes.Count);
                    velocities = new int[count];
                    for (var note = 0; note < count; note++)
                        velocities[note] = cell.Notes[note].Velocity;
                }
                miniature.Add(new MiniatureEvent(index, consumed, cell.Notes.Count, velocities));
            }
            index += Math.Max(1, (int)Math.Ceiling(consumed - 0.001));
        }
        return new Activity(notes, beats, rests, measure.Cells.Count, notes > 0, miniature.ToArray());
    }

    /// <summary>Opacity of a lane element: <paramref name="normal"/>, moving to <paramref name="silenced"/> as the muted-track dimming setting rises.</summary>
    public static double FadeBy(double normal, double silenced, double dim) => normal + (silenced - normal) * Math.Clamp(dim, 0, 1);

    /// <summary>The colour moved toward its luma grey by <paramref name="amount"/>, fully opaque.</summary>
    public static UiColor Desaturate(UiColor c, double amount)
    {
        var g = 0.3 * c.R + 0.59 * c.G + 0.11 * c.B;
        return new UiColor(255, (byte)(c.R + (g - c.R) * amount), (byte)(c.G + (g - c.G) * amount), (byte)(c.B + (g - c.B) * amount));
    }
}
