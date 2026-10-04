using TabForge.Models;
using TabForge.Playback;

namespace TabForge.Services;

/// <summary>What a bar-range command does to the selected bars.</summary>
public enum BarRangeAction { Clear, Remove, InsertBefore, InsertAfter }

// Owns: the model half of the timeline's bar-range commands: clear the bars (leave a gap), remove them (close the gap), insert an empty gap
//     before or after them, for every track or for one track; clips follow the bars (shifted, cut at an edge or removed).
// Does not own: the undo step (DocumentEdits), the prompt, the selection afterwards (BarRangeFlow).
// Tests: TestBarRangeGaps.
/// <summary>
/// Whole-track or one-track bar-range edits. Every track keeps the same bar count. Clips are positioned in seconds of song time, so a closed gap moves the
/// clips after the range earlier by the range's duration, removes the clips inside it and cuts the clips that cross an edge; an inserted gap moves the clips
/// that start at or after the insertion point later by the gap's duration.
/// </summary>
public static class BarRangeGaps
{
    private const double Eps = 0.0005;

    /// <summary>The song time [From, To) in seconds of bars <paramref name="start"/>..<paramref name="end"/> (first pass), or null when a bar is not on the timeline.</summary>
    public static (double From, double To)? Span(SongProject project, int start, int end)
    {
        var bars = new Dictionary<int, ScoreBar>();
        foreach (var bar in MidiTimelineBuilder.Build(project, new PlaybackOptions { RepeatExpansion = true, RespectMuteSolo = false, SkipClips = true }).Bars)
            bars.TryAdd(bar.Bar, bar);
        return bars.TryGetValue(start, out var first) && bars.TryGetValue(end, out var last) ? (first.StartMs / 1000.0, last.EndMs / 1000.0) : null;
    }

    /// <summary>The clips (per track name) that the range touches; with <paramref name="track"/> >= 0 only that track's. Null when none.</summary>
    public static IReadOnlyList<(string Track, int Clips)>? ClipsUnder(SongProject project, int start, int end, int track)
    {
        if (ClipDeleteImpact.Find(project, new[] { (start, end) }) is not { } all) return null;
        if (track < 0) return all;
        var name = project.Tracks[track].Name;
        var mine = all.Where(a => a.Track == name).ToList();
        return mine.Count == 0 ? null : mine;
    }

    private static MeasureModel Blank(MeasureModel? template) => new()
    {
        TimeSigNum = template?.TimeSigNum, TimeSigDenom = template?.TimeSigDenom, KeySignature = template?.KeySignature,
        KeySignatureMinor = template?.KeySignatureMinor, Clef = template?.Clef ?? Clefs.Guitar,
    };

    private static bool HasNotes(MeasureModel m) => m.Cells.Any(c => c.Notes.Count > 0) || m.Voice2Cells.Any(c => c.Notes.Count > 0);

    /// <summary>Empties bars start..end (notes, rests, beats) on every track or on <paramref name="track"/>; bar count, signatures, sections and clips stay.</summary>
    public static bool Clear(SongProject project, int start, int end, int track, bool fillRests)
    {
        var changed = false;
        for (var t = 0; t < project.Tracks.Count; t++)
        {
            if (track >= 0 && t != track) continue;
            var measures = project.Tracks[t].Measures;
            for (var b = start; b <= end && b < measures.Count; b++)
            {
                var m = measures[b];
                m.Cells = Enumerable.Range(0, 16).Select(_ => new TabCell()).ToList();
                m.Voice2Cells = new();
                m.SimileOneBar = m.SimileTwoBar = false;
                changed = true;
            }
            if (fillRests) BarFill.FillBars(project, t, start, end);
        }
        return changed;
    }

    /// <summary>
    /// Removes bars start..end and closes the gap. All tracks: sections, markers and clips follow (the old-to-new bar map is returned, always keeping one bar).
    /// One track: that track's later bars move left and empty bars fill its end; null map. Null when nothing changed.
    /// </summary>
    public static (int[]? Map, bool ClipsChanged)? Remove(SongProject project, int start, int end, int track)
    {
        if (track < 0)
        {
            var count = Math.Min(end - start + 1, BarRangeEditor.MaxMeasures(project) - 1);
            if (count <= 0) return null;
            end = start + count - 1;
            var span = Span(project, start, end);
            var map = BarRangeEditor.Remove(project, start, end);
            if (map is null) return null;
            var clips = false;
            if (span is { } s) foreach (var t in project.Tracks) clips |= CloseClips(t, s.From, s.To);
            return (map, clips);
        }
        var target = project.Tracks[track];
        if (start >= target.Measures.Count) return null;
        var spanOne = Span(project, start, Math.Min(end, BarRangeEditor.MaxMeasures(project) - 1));
        var before = target.Measures.Count;
        var last = target.Measures[^1];
        target.Measures.RemoveRange(start, Math.Min(end - start + 1, before - start));
        while (target.Measures.Count < before) target.Measures.Add(Blank(last));
        BarRangeEditor.Renumber(target);
        return (null, spanOne is { } o && CloseClips(target, o.From, o.To));
    }

    /// <summary>Inserts an empty gap as long as bars start..end before them (<paramref name="before"/>) or after them; the map is returned.</summary>
    public static (int[] Map, int At, int Count)? InsertGap(SongProject project, int start, int end, bool before, int track, bool fillRests)
    {
        var barCount = BarRangeEditor.MaxMeasures(project);
        if (start < 0 || start >= barCount) return null;
        end = Math.Min(end, barCount - 1);
        var count = end - start + 1;
        var at = before ? start : end + 1;
        var span = Span(project, start, end);
        var point = Span(project, Math.Min(at, barCount - 1), Math.Min(at, barCount - 1)) is { } p ? (at < barCount ? p.From : p.To) : double.MaxValue;
        var blanks = project.Tracks.Select(t => Enumerable.Range(0, count).Select(i => Blank(start + i < t.Measures.Count ? t.Measures[start + i] : null)).ToList()).ToList();
        int[] map;
        if (track < 0)
        {
            map = BarRangeEditor.Insert(project, at, blanks);
            if (span is { } s) foreach (var t in project.Tracks) ShiftClips(t, point, s.To - s.From);
        }
        else
        {
            var target = project.Tracks[track];
            while (target.Measures.Count < barCount) target.Measures.Add(new MeasureModel());
            target.Measures.InsertRange(at, blanks[track]);
            for (var i = 0; i < count && target.Measures.Count > barCount && !HasNotes(target.Measures[^1]); i++) target.Measures.RemoveAt(target.Measures.Count - 1);
            var newMax = BarRangeEditor.MaxMeasures(project);
            foreach (var t in project.Tracks)
            {
                var last = t.Measures.Count > 0 ? t.Measures[^1] : null;
                while (t.Measures.Count < newMax) t.Measures.Add(Blank(last));
                BarRangeEditor.Renumber(t);
            }
            map = Enumerable.Range(0, barCount).Select(b => b < at ? b : b + (newMax - barCount)).ToArray();
            if (newMax == barCount) map = Enumerable.Range(0, barCount).ToArray();   // the bars did not grow: the other tracks did not move
            if (span is { } s) ShiftClips(target, point, s.To - s.From);
        }
        if (fillRests)
            for (var t = 0; t < project.Tracks.Count; t++)
                if (track < 0 || t == track) BarFill.FillBars(project, t, at, at + count - 1);
        return (map, at, count);
    }

    private static void ShiftClips(TrackModel track, double point, double by)
    {
        foreach (var clip in track.AudioClips) if (clip.StartSec >= point - Eps) clip.StartSec += by;
    }

    /// <summary>Closes the song time [s, e) on one track's clips: later clips move earlier, inner clips go, edge clips are cut. True when any clip changed.</summary>
    private static bool CloseClips(TrackModel track, double s, double e)
    {
        var d = e - s;
        var changed = false;
        foreach (var clip in track.AudioClips.ToList())
        {
            if (clip.EndSec <= s + Eps) continue;
            changed = true;
            if (clip.StartSec >= e - Eps) { clip.StartSec -= d; continue; }
            bool leftIn = clip.StartSec < s - Eps, rightOut = clip.EndSec > e + Eps;
            var speed = Math.Clamp(clip.Speed, 0.25, 4);
            if (!leftIn && !rightOut) { track.AudioClips.Remove(clip); continue; }
            if (leftIn && !rightOut) { clip.SourceLengthSec = (s - clip.StartSec) * speed; clip.FadeOutSec = 0; continue; }
            var cut = (e - clip.StartSec) * speed;
            if (!leftIn) { clip.OffsetSec += cut; clip.SourceLengthSec -= cut; clip.StartSec = s; clip.FadeInSec = 0; continue; }
            var second = clip.Clone();   // the clip spans the whole range: both ends stay, joined at the closed gap
            second.StartSec = s; second.OffsetSec = clip.OffsetSec + cut; second.SourceLengthSec = clip.SourceLengthSec - cut; second.FadeInSec = 0;
            clip.SourceLengthSec = (s - clip.StartSec) * speed; clip.FadeOutSec = 0;
            track.AudioClips.Insert(track.AudioClips.IndexOf(clip) + 1, second);
        }
        return changed;
    }
}
