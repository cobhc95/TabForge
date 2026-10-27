using TabForge.Models;

namespace TabForge.Services;

// Owns: the ONE bar insert on every track that all bar-insert paths share (Bar.Insert / Ctrl+Insert and the Insert bar tool, Insert > Bar > Before / After / Several bars /
//   At the end and the Add bar tool, the timeline bar menu's Insert bar rows, Range.InsertBefore / InsertAfter, the range paste's gap and the score's bars paste as an insert):
//   every track gets the bars at the same index (a track shorter than the song is padded first, so its bars never land at its own end), sections move with their bars
//   (BarRangeEditor.Insert), a tie into the bar after the new bars is dropped when the new bar in front of it holds no notes (nothing is left to tie from), and the clips that
//   start at or after the insertion point move later by the new bars' time. Bar.Duplicate and Edit.RepeatSelection insert their copies through it too.
// Does not own: the undo step (BarStructureEdits / the callers), the cursor and status text (the hosts), what goes into the bars (the callers).
// Tests: TabForge.Avalonia.Tests.BarInsertTests.
/// <summary>Inserts bars before a bar on every track, keeping sections, ties, auto bars and clips in step. Pure model code.</summary>
public static class BarInsert
{
    private const double Eps = 0.0005;

    /// <summary>
    /// Inserts <paramref name="count"/> empty bars before bar <paramref name="at"/> (the bar count is the song end) on every track; each is built like the bar in front of the
    /// insertion point (meter, key, clef; bar 1's when <paramref name="at"/> is 0). With <paramref name="fillRests"/> each starts as one whole-bar rest. Returns the old-to-new
    /// bar map, or null when nothing was inserted (no tracks, no count, or the song would grow past its limit).
    /// </summary>
    public static int[]? Empty(SongProject project, int at, int count, bool fillRests)
    {
        var barCount = BarRangeEditor.MaxMeasures(project);
        if (project.Tracks.Count == 0 || count < 1 || barCount + count > InputLimits.MaxMeasuresPerTrack) return null;
        var template = Math.Max(0, Math.Clamp(at, 0, barCount) - 1);
        var blanks = project.Tracks.Select(track =>
        {
            var source = track.Measures.Count == 0 ? null : track.Measures[Math.Min(template, track.Measures.Count - 1)];
            return Enumerable.Range(0, count).Select(_ => Blank(source)).ToList();
        }).ToList();
        return Bars(project, at, blanks, fillRests);
    }

    /// <summary>
    /// Inserts <paramref name="bars"/> (one list per track, in track order; a missing bar is an empty one) before bar <paramref name="at"/> on every track, then keeps the rest of the
    /// song in step (see the class comment). With <paramref name="fillRests"/> the new bars are filled with rests. Returns the old-to-new bar map, or null when there is nothing to insert.
    /// </summary>
    public static int[]? Bars(SongProject project, int at, IReadOnlyList<List<MeasureModel>> bars, bool fillRests)
    {
        var barCount = BarRangeEditor.MaxMeasures(project);
        var count = bars.Select(track => track.Count).DefaultIfEmpty(0).Max();
        if (project.Tracks.Count == 0 || count < 1) return null;
        at = Math.Clamp(at, 0, barCount);
        double? point = barCount == 0 ? null
            : BarRangeGaps.Span(project, Math.Min(at, barCount - 1), Math.Min(at, barCount - 1)) is { } p ? (at < barCount ? p.From : p.To) : null;
        var map = BarRangeEditor.Insert(project, at, bars);
        DropTiesInto(project, at + count - 1);
        if (fillRests) for (var t = 0; t < project.Tracks.Count; t++) BarFill.FillBars(project, t, at, at + count - 1);
        // Auto bars the clips added at the end are still the last bars when the new ones went in before them.
        if (at < barCount && project.AutoBars.Count > 0 && project.AutoBars.EndBars == barCount)
            project.AutoBars = project.AutoBars with { EndBars = barCount + count };
        if (point is { } from && BarRangeGaps.Span(project, at, at + count - 1) is { } added)
            foreach (var track in project.Tracks)
                foreach (var clip in track.AudioClips)
                    if (clip.StartSec >= from - Eps) clip.StartSec += added.To - added.From;
        return map;
    }

    private static MeasureModel Blank(MeasureModel? template) => new()
    {
        TimeSigNum = template?.TimeSigNum, TimeSigDenom = template?.TimeSigDenom, KeySignature = template?.KeySignature,
        KeySignatureMinor = template?.KeySignatureMinor, Clef = template?.Clef ?? Clefs.Guitar,
    };

    // The first beat of each voice of the bar after the last new bar was tied from the bar that used to be in front of it: when the new bar there holds no notes in that voice
    // the tie would hang over it, so it goes (a copied bar in front, as Duplicate makes, keeps it).
    private static void DropTiesInto(SongProject project, int lastNew)
    {
        foreach (var track in project.Tracks)
        {
            if (lastNew < 0 || lastNew + 1 >= track.Measures.Count) continue;
            MeasureModel before = track.Measures[lastNew], after = track.Measures[lastNew + 1];
            for (var voice = 0; voice < 2; voice++)
                if (!before.CellsForVoice(voice).Any(cell => cell.Notes.Count > 0) && after.CellsForVoice(voice).FirstOrDefault(MusicTime.ConsumesTime) is { Notes.Count: > 0 } first)
                {
                    first.IsTied = false;
                    foreach (var note in first.Notes) note.Tied = false;
                }
        }
    }
}
