
namespace TabForge.Models;

// Owns: counts of TabCell and its owned collections for the performance audit, so the 112 MB gap is accounted before any memory rewrite.
// Does not own: the model (SongProject.Cells.cs), the timing cache (SongExtentMeasureCache) or the timeline (ScoreTimeline).
// Tests: none (a diagnostic probe for owner runs).
// Threads: UI thread; allocates only its small result.
internal static class TabCellMemoryProbe
{
    public sealed record Counts(long Cells, long EmptyCells, long CellsWithNotes, long Notes, long EmptyNoteLists, long EmptyWhammyLists, long BendLists, long TechniqueSets);

    /// <summary>Counts every cell of both voices, empty and written, and the owned collections. No collection is allocated.</summary>
    public static Counts Count(SongProject project)
    {
        long cells = 0, empty = 0, withNotes = 0, notes = 0, emptyNotes = 0, emptyWhammy = 0, bends = 0, techniques = 0;
        foreach (var track in project.Tracks)
        {
            foreach (var measure in track.Measures)
            {
                CountVoice(measure.Cells);
                CountVoice(measure.Voice2Cells);
            }
        }
        return new Counts(cells, empty, withNotes, notes, emptyNotes, emptyWhammy, bends, techniques);

        void CountVoice(List<TabCell> voice)
        {
            foreach (var cell in voice)
            {
                cells++;
                var noteCount = cell.Notes.Count;
                notes += noteCount;
                if (noteCount == 0) { empty++; emptyNotes++; }
                else withNotes++;
                if (cell.WhammyPoints.Count == 0) emptyWhammy++;
                foreach (var note in cell.Notes)
                {
                    bends++;
                    techniques++;
                }
            }
        }
    }

    /// <summary>Lower-bound retained bytes for the empty lists: an empty List object is about 32 bytes (header plus fields, no backing array). TabCell itself is much larger, so the lists are a small part of the gap unless the song is huge.</summary>
    public static long EstimatedEmptyListBytes(Counts counts) => (counts.EmptyNoteLists + counts.EmptyWhammyLists) * 32L;
}
