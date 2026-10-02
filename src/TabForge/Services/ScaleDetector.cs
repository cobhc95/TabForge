using TabForge.Models;

namespace TabForge.Services;

/// <summary>One scale (root + type) and how well it fits a set of notes.</summary>
public sealed record ScaleCandidate(string Root, string Scale, double Coverage, int UnusedScaleNotes, double Score)
{
    /// <summary>The value the scale highlight uses, e.g. "E Natural Minor".</summary>
    public string Highlight => $"{Root} {Scale}";
    public string NoteList => string.Join(" ", MusicTheoryService.ScaleNotes(Root, Scale));
}

// Owns: finding which scales the notes of a passage belong to.
// Does not own: the scale highlight display.
// Tests: TestScaleFinder.
/// <summary>
/// "Find scale": which scales the notes of a passage (or the whole song) belong to. Notes are weighted by
/// length, so passing tones count less than held notes; drum tracks and dead notes are ignored.
/// </summary>
public static class ScaleDetector
{
    public sealed class NoteSummary
    {
        public double[] Weights { get; } = new double[12];
        public int? FirstPitchClass { get; set; }
        public int? LastPitchClass { get; set; }
        public int NoteCount { get; set; }
        public double Total => Weights.Sum();
    }

    /// <summary>Collects pitch classes from the given tracks between two (measure, cell) positions, inclusive.</summary>
    public static NoteSummary Collect(IEnumerable<TrackModel> tracks, int startMeasure, int startCell, int endMeasure, int endCell)
    {
        var summary = new NoteSummary();
        foreach (var track in tracks)
        {
            if (track.Kind == TrackKind.Drums || track.MidiChannel == 9) continue;
            for (var m = Math.Max(0, startMeasure); m <= endMeasure && m < track.Measures.Count; m++)
            {
                var cells = track.Measures[m].Cells;
                var from = m == startMeasure ? Math.Max(0, startCell) : 0;
                var to = m == endMeasure ? Math.Min(cells.Count - 1, endCell) : cells.Count - 1;
                for (var c = from; c <= to; c++)
                {
                    var cell = cells[c];
                    if (cell.IsRest || cell.Notes.Count == 0) continue;
                    var length = 1.0 / Math.Max(1, cell.DurationDenominator) * (cell.Dots == 2 ? 1.75 : cell.Dots == 1 ? 1.5 : 1);
                    foreach (var note in cell.Notes)
                    {
                        if (note.Dead) continue;
                        var midi = PitchOf(track, note);
                        if (midi < 0) continue;
                        var pc = midi % 12;
                        summary.Weights[pc] += note.Ghost ? length * 0.5 : length;
                        summary.FirstPitchClass ??= pc;
                        summary.LastPitchClass = pc;
                        summary.NoteCount++;
                    }
                }
            }
        }
        return summary;
    }

    private static int PitchOf(TrackModel track, TabNote note)
    {
        if (note.StringIndex >= 0 && note.StringIndex < track.StringTunings.Count)
            return track.PitchOf(note.StringIndex, note.Fret);
        return note.MidiValue > 0 ? note.MidiValue : -1;
    }

    /// <summary>Every root x scale, best first. Coverage is the share of the (weighted) notes inside the scale.</summary>
    public static List<ScaleCandidate> Rank(NoteSummary notes)
    {
        var total = notes.Total;
        var result = new List<ScaleCandidate>();
        if (total <= 0) return result;
        var used = Enumerable.Range(0, 12).Where(pc => notes.Weights[pc] > 0).ToHashSet();
        var mostCommon = Enumerable.Range(0, 12).OrderByDescending(pc => notes.Weights[pc]).First();
        for (var root = 0; root < 12; root++)
        {
            foreach (var (name, intervals) in MusicTheoryService.Scales)
            {
                var pcs = intervals.Select(i => (root + i) % 12).ToHashSet();
                var inside = pcs.Sum(pc => notes.Weights[pc]);
                var coverage = inside / total;
                var unused = pcs.Count(pc => !used.Contains(pc));
                // Fit matters most; then fewer unused scale notes; then a root the music leans on.
                // The root bonus stays smaller than one missing note, so a scale that fits every note always
                // ranks above one that does not; among equal fits the plain major / minor names come first.
                var score = coverage * 100 - unused * 2.5
                            + notes.Weights[root] / total * 6
                            + (notes.LastPitchClass == root ? 2 : 0)
                            + (notes.FirstPitchClass == root ? 1 : 0)
                            + (mostCommon == root ? 1.5 : 0)
                            + (name is "Major" or "Natural Minor" ? 1 : 0);
                result.Add(new ScaleCandidate(MusicTheoryService.NoteNames[root], name, coverage, unused, score));
            }
        }
        return result.OrderByDescending(c => c.Score).ThenBy(c => c.UnusedScaleNotes).ToList();
    }
}
