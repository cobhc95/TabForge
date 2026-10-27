using System.IO;
using TabForge.Models;

namespace TabForge.Services;

// Owns: taking a RangeSnapshot of bars first..last of every track from a song (pure: the song is not changed), including cutting the clips at the range edges.
// Does not own: the snapshot types (RangeSnapshot), the clipboard (ClipboardService) or the delete (TimelineRangeEdit).
// Tests: RangeClipboardTests.
public static class RangeCapture
{
    private const double Eps = 0.0005;

    /// <summary>
    /// The snapshot of bars <paramref name="first"/>..<paramref name="last"/> on every track, or on tracks <paramref name="firstTrack"/>..<paramref name="lastTrack"/> (indexes). With <paramref name="tabsOnly"/> only the tracks with notation are taken and no clips
    /// (the cells of a timeline highlight). Throws <see cref="InvalidDataException"/> (user-facing) when the range is empty or too long.
    /// </summary>
    public static RangeSnapshot Capture(SongProject project, int first, int last, int firstTrack = 0, int lastTrack = int.MaxValue, bool tabsOnly = false)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (last < first) (first, last) = (last, first);
        first = Math.Max(0, first);
        last = Math.Min(last, BarRangeEditor.MaxMeasures(project) - 1);
        firstTrack = Math.Max(0, firstTrack);
        lastTrack = Math.Min(lastTrack, project.Tracks.Count - 1);
        var tracks = project.Tracks.Skip(firstTrack).Take(Math.Max(0, lastTrack - firstTrack + 1)).Where(t => !tabsOnly || t.HasNotation).ToList();
        if (last < first || tracks.Count == 0) throw new InvalidDataException("Nothing to copy.");
        if (last - first + 1 > InputLimits.MaxClipBars) throw new InvalidDataException($"Copy at most {InputLimits.MaxClipBars} bars at once.");
        var span = BarRangeGaps.Span(project, first, last);
        var snapshot = new RangeSnapshot { FirstBar = first, LengthSec = span is { } s ? s.To - s.From : 0 };
        for (var bar = first; bar <= last; bar++)
        {
            var (numerator, denominator) = BarSignatures.TimeAt(project, bar);
            var (key, minor) = BarSignatures.KeyAt(project, bar);
            snapshot.Bars.Add(new RangeSnapshotBar(numerator, denominator, key, minor, MusicTime.TempoAt(project, bar)));
        }
        foreach (var track in tracks)
        {
            var copy = new RangeSnapshotTrack
            {
                SourceTrackId = track.Id, Name = track.Name, Kind = track.Kind, InstrumentName = track.InstrumentName,
                StringTunings = track.StringTunings.ToList(), Capo = track.Capo,
            };
            if (track.HasNotation)
                for (var bar = first; bar <= last; bar++)
                {
                    var measure = bar < track.Measures.Count ? ProjectService.CloneMeasure(track.Measures[bar]) : new MeasureModel();
                    foreach (var cell in measure.Cells.Concat(measure.Voice2Cells)) cell.Lyrics = "";
                    copy.Bars.Add(measure);
                }
            if (span is { } range && !tabsOnly)
                foreach (var clip in track.AudioClips.Where(c => c.Overlaps(range.From, range.To)))
                    if (Part(clip, range.From, range.To) is { } part) copy.Clips.Add(part);
            snapshot.Tracks.Add(copy);
        }
        return snapshot;
    }

    // The part of the clip inside [from, to): trimmed at a cut edge (the fade on a cut edge goes), positioned relative to the range start. Null when nothing audible is left.
    private static RangeClipPart? Part(AudioClip clip, double from, double to)
    {
        var start = Math.Max(clip.StartSec, from);
        var end = Math.Min(clip.EndSec, to);
        if (end - start <= Eps) return null;
        var speed = Math.Clamp(clip.Speed, 0.25, 4);
        var part = clip.Clone();
        part.Id = Guid.NewGuid().ToString("N");
        part.StartSec = 0;
        if (clip.StartSec < from - Eps) { part.OffsetSec = clip.OffsetSec + (from - clip.StartSec) * speed; part.FadeInSec = 0; }
        part.SourceLengthSec = (end - start) * speed;
        if (clip.EndSec > to + Eps) part.FadeOutSec = 0;
        return new RangeClipPart { OffsetSec = start - from, Clip = part };
    }
}
