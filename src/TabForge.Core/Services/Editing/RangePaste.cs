using TabForge.Models;

namespace TabForge.Services;

// Owns: pasting a RangeSnapshot into a song (pure model code, the caller wraps it in one undo step): where each track goes (PasteMapper), Overwrite / Merge / Insert, which parts
//   (tabs, clips, both), what happens to clips that overlap (trim them away, or a new take lane), the repeat count, growing the song (ClipAutoBars) and the notices
//   (skipped tracks, a time signature that differs, cells that do not fit, notes with no string).
// Does not own: the snapshot (RangeSnapshot, RangeCapture), the track mapping (PasteMapper), bar surgery (BarRangeGaps.InsertGap), the take-lane model (ClipLanes), the undo step,
//   the selection afterwards and the question (the caller).
// Tests: TimelinePasteTests.

/// <summary>How the pasted bars meet the bars already there: <see cref="Overwrite"/> replaces them, <see cref="Merge"/> only fills empty spots, <see cref="Insert"/> pushes everything later to the right.</summary>
public enum RangePasteMode { Overwrite, Merge, Insert }

/// <summary>Which parts of the snapshot are pasted.</summary>
public enum RangePasteWhat { TabsAndClips, Tabs, Clips }

/// <summary>What a pasted clip does to an existing clip it overlaps: <see cref="Trim"/> cuts the overlapped part away, <see cref="NewTakeLane"/> keeps both (the newest plays).</summary>
public enum RangeClipOverlap { Trim, NewTakeLane }

/// <summary>The choices of one range paste. Ctrl+V uses the defaults.</summary>
public sealed record RangePasteOptions(RangePasteMode Mode = RangePasteMode.Overwrite, RangePasteWhat What = RangePasteWhat.TabsAndClips,
    RangeClipOverlap Overlap = RangeClipOverlap.Trim, int Repeat = 1)
{
    public const int MaxRepeat = 64;

    /// <summary>The same choices with the repeat count in 1..64 and any unknown mode back to its default (a damaged settings file).</summary>
    public RangePasteOptions Normalized() => this with
    {
        Mode = Enum.IsDefined(Mode) ? Mode : RangePasteMode.Overwrite,
        What = Enum.IsDefined(What) ? What : RangePasteWhat.TabsAndClips,
        Overlap = Enum.IsDefined(Overlap) ? Overlap : RangeClipOverlap.Trim,
        Repeat = Math.Clamp(Repeat, 1, MaxRepeat),
    };
}

/// <summary>What one paste did: whether the song changed, the bars it filled (first..last, written bars), the notices, and the counts behind them.</summary>
public sealed record RangePasteResult(bool Changed, int FirstBar, int LastBar, IReadOnlyList<string> Notices, int CutCells = 0, int SignatureMismatches = 0)
{
    /// <summary>All notices as one line for the top bar, or null.</summary>
    public string? Notice => Notices.Count == 0 ? null : string.Join("; ", Notices);
}

public static class RangePaste
{
    private const double Eps = 0.0005;

    /// <summary>
    /// Pastes <paramref name="snapshot"/> from bar <paramref name="atBar"/>, its first track on <paramref name="startTrack"/>, <see cref="RangePasteOptions.Repeat"/> copies back to back.
    /// The target bars keep their own time signature, key and tempo; only their notes change. Nothing is changed when no track maps.
    /// </summary>
    public static RangePasteResult Apply(SongProject project, RangeSnapshot snapshot, int atBar, int startTrack, RangePasteOptions options)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(snapshot);
        options = (options ?? new RangePasteOptions()).Normalized();
        var notices = new List<string>();
        atBar = Math.Max(0, atBar);
        var none = new RangePasteResult(false, atBar, atBar, notices);
        if (project.Tracks.Count == 0) return none;
        var mapping = PasteMapper.Map(snapshot, project.Tracks, startTrack);
        if (mapping.Notice is { } skipped) notices.Add(skipped);
        var tabs = options.What != RangePasteWhat.Clips;
        var clips = options.What != RangePasteWhat.Tabs;
        var tabPairs = mapping.Pairs.Where(p => tabs && snapshot.Tracks[p.Source].Kind != TrackKind.Audio).ToList();
        var clipPairs = mapping.Pairs.Where(p => clips && snapshot.Tracks[p.Source].Clips.Count > 0).ToList();
        if (tabPairs.Count == 0 && clipPairs.Count == 0)
        {
            if (notices.Count == 0) notices.Add("Nothing to paste here");
            return none;
        }

        var count = snapshot.BarCount;
        var total = count * options.Repeat;
        var before = BarRangeEditor.MaxMeasures(project);
        var room = Math.Min(InputLimits.MaxMeasuresPerTrack, InputLimits.MaxTotalMeasures / project.Tracks.Count);
        var insert = options.Mode == RangePasteMode.Insert && atBar < before;
        if (insert && Math.Max(before, atBar + total) + total > room)
        {
            notices.Add("The song would get too long");
            return none;
        }
        var grown = ClipAutoBars.GrowToBars(project, atBar + total);
        if (insert) InsertGap(project, atBar, total, grown.Added);
        var available = BarRangeEditor.MaxMeasures(project);
        if (grown.EndsInJump) notices.Add(ClipAutoBars.JumpNotice);
        var last = Math.Min(atBar + total, available) - 1;
        if (last < atBar) return none;
        if (last < atBar + total - 1) notices.Add($"Only {last - atBar + 1} of {total} bars fit in the song");

        var cut = 0;
        var mismatches = 0;
        if (tabPairs.Count > 0)
        {
            for (var bar = atBar; bar <= last; bar++)
                if (!SameMeter(snapshot.Bars[(bar - atBar) % count], BarSignatures.TimeAt(project, bar))) mismatches++;
            var noString = 0;
            foreach (var pair in tabPairs)
            {
                var source = snapshot.Tracks[pair.Source];
                var target = project.Tracks[pair.Target];
                for (var bar = atBar; bar <= last && bar < target.Measures.Count; bar++)
                    PasteBar(project, target, bar, source.Bars[(bar - atBar) % count], options.Mode, ref cut, ref noString);
            }
            if (mismatches > 0) notices.Add($"{mismatches} {(mismatches == 1 ? "bar has" : "bars have")} another time signature: kept the one of the song");
            if (cut > 0) notices.Add($"{cut} {(cut == 1 ? "beat" : "beats")} did not fit the shorter bars and {(cut == 1 ? "was" : "were")} cut");
            if (noString > 0) notices.Add($"{noString} {(noString == 1 ? "note" : "notes")} skipped: the track has fewer strings");
        }
        if (clipPairs.Count > 0) PlaceClips(project, snapshot, clipPairs, atBar, last, options, notices);

        project.MarkTimelineChanged();
        return new RangePasteResult(true, atBar, last, notices, cut, mismatches);
    }

    // Opens a gap of total bars before atBar on every track (later bars and clips move right). Bars that were only added to give the gap a template trail the song again: they go once nothing reaches them.
    private static void InsertGap(SongProject project, int atBar, int total, int added)
    {
        // A clip that spans the insertion point is split there first: its right part moves right with the gap (the DAW rule), its left part stays.
        if (BarRangeGaps.Span(project, atBar, atBar) is { } point)
            foreach (var track in project.Tracks)
                foreach (var clip in track.AudioClips.Where(c => c.StartSec < point.From - Eps && c.EndSec > point.From + Eps).ToList())
                    ClipSplitGlue.Split(track, clip, point.From);
        BarRangeGaps.InsertGap(project, atBar, atBar + total - 1, before: true, track: -1, fillRests: false);
        var bars = BarRangeEditor.MaxMeasures(project);
        if (project.AutoBars.EndBars == bars - total) project.AutoBars = project.AutoBars with { EndBars = bars };
        if (added > 0) ClipAutoBars.Shrink(project);
    }

    private static bool SameMeter(RangeSnapshotBar source, (int Num, int Den) target) =>
        MusicTime.BarSlots(source.Numerator, source.Denominator) == MusicTime.BarSlots(target.Num, target.Den);

    private static bool IsEmpty(TabCell cell) => cell.Notes.Count == 0 && !cell.HasAnnotation;

    // Puts the notes of one source bar into the target bar: the target keeps its own meter, key and marks. Beats that start after the end of a shorter target bar are cut.
    private static void PasteBar(SongProject project, TrackModel target, int bar, MeasureModel source, RangePasteMode mode, ref int cut, ref int noString)
    {
        var measure = target.Measures[bar];
        var copy = ProjectService.CloneMeasure(source);
        var slots = MusicTime.BarSlots(project, bar);
        foreach (var cells in new[] { copy.Cells, copy.Voice2Cells })
            foreach (var (index, cell, start) in MusicTime.BeatOnsets(cells).ToList())
            {
                if (start >= slots - 1e-6) { if (!IsEmpty(cell) || cell.IsRest) cut++; cells[index] = new TabCell(); continue; }
                noString += cell.Notes.RemoveAll(n => n.StringIndex >= target.StringTunings.Count);
            }
        if (mode == RangePasteMode.Merge)
        {
            Merge(measure.Cells, copy.Cells);
            if (copy.Voice2Cells.Count > 0) Merge(measure.CellsForVoice(1, create: true), copy.Voice2Cells);
        }
        else
        {
            measure.Cells = Resized(copy.Cells, measure.Cells.Count);
            measure.Voice2Cells = copy.Voice2Cells;
        }
        measure.SimileOneBar = measure.SimileTwoBar = false;
    }

    private static List<TabCell> Resized(List<TabCell> cells, int length)
    {
        while (cells.Count > length && IsEmpty(cells[^1]) && !cells[^1].IsRest) cells.RemoveAt(cells.Count - 1);
        while (cells.Count < length) cells.Add(new TabCell());
        return cells;
    }

    // Existing beats win: a pasted beat only lands in a spot that has no notes or marks (a rest counts as empty).
    private static void Merge(List<TabCell> target, List<TabCell> pasted)
    {
        for (var i = 0; i < pasted.Count; i++)
        {
            if (IsEmpty(pasted[i]) && !pasted[i].IsRest) continue;
            while (target.Count <= i) target.Add(new TabCell());
            if (IsEmpty(target[i])) target[i] = pasted[i];
        }
    }

    private static void PlaceClips(SongProject project, RangeSnapshot snapshot, List<TrackPairing> pairs, int atBar, int last, RangePasteOptions options, List<string> notices)
    {
        var count = snapshot.BarCount;
        var skipped = 0;
        for (var copy = 0; copy < options.Repeat; copy++)
        {
            var first = atBar + copy * count;
            if (first > last) break;
            if (BarRangeGaps.Span(project, first, first) is not { } span) continue;
            foreach (var pair in pairs)
            {
                var track = project.Tracks[pair.Target];
                foreach (var part in snapshot.Tracks[pair.Source].Clips)
                {
                    var clip = part.Clip.Clone();
                    clip.Id = Guid.NewGuid().ToString("N");
                    clip.TakeGroupId = null;
                    clip.TakeGroupKind = default;
                    clip.TakeGroupPass = 0;
                    clip.StartSec = span.From + part.OffsetSec;
                    if (options.Mode == RangePasteMode.Merge)
                    {
                        if (track.AudioClips.Any(c => c.Lane == clip.Lane && c.Overlaps(clip.StartSec, clip.EndSec))) { skipped++; continue; }
                    }
                    else if (options.Overlap == RangeClipOverlap.NewTakeLane)
                    {
                        PlaceOnNewLane(track, clip);
                        continue;
                    }
                    else CutOut(track, clip.Lane, clip.StartSec, clip.EndSec);
                    ClipLanes.Ensure(track, clip.Lane + 1);
                    track.AudioClips.Add(clip);
                }
            }
        }
        if (skipped > 0) notices.Add($"{skipped} {(skipped == 1 ? "clip" : "clips")} skipped: the spot is not empty");
    }

    // The take-lane rule of a recording: the clip takes the first lane that is free under it; when that is not the first lane, or it overlaps a clip of its kind, its lane plays and the older takes grey out.
    private static void PlaceOnNewLane(TrackModel track, AudioClip clip)
    {
        clip.Lane = ClipLanes.FreeLane(track, clip.StartSec, clip.EndSec);
        var newTake = clip.Lane > 0 || track.AudioClips.Any(c => c.IsMidi == clip.IsMidi && c.Overlaps(clip.StartSec, clip.EndSec));
        ClipLanes.Ensure(track, clip.Lane + 1);
        track.AudioClips.Add(clip);
        if (newTake) ClipLanes.PlayNewTake(track, clip.Lane, clip.IsMidi);
    }

    // Removes song time [from, to) from the clips of one lane: a clip inside goes, a clip across an edge is cut there, a clip across both is split and its middle goes.
    private static void CutOut(TrackModel track, int lane, double from, double to)
    {
        foreach (var clip in track.AudioClips.Where(c => c.Lane == lane && c.Overlaps(from + Eps, to - Eps)).ToList())
        {
            bool leftIn = clip.StartSec < from - Eps, rightOut = clip.EndSec > to + Eps;
            var speed = Math.Clamp(clip.Speed, 0.25, 4);
            if (!leftIn && !rightOut) track.AudioClips.Remove(clip);
            else if (leftIn && !rightOut) { clip.SourceLengthSec = (from - clip.StartSec) * speed; clip.FadeOutSec = 0; }
            else if (!leftIn)
            {
                var cutSource = (to - clip.StartSec) * speed;
                clip.OffsetSec += cutSource; clip.SourceLengthSec -= cutSource; clip.StartSec = to; clip.FadeInSec = 0;
            }
            else
            {
                ClipSplitGlue.Split(track, clip, to);
                clip.SourceLengthSec = (from - clip.StartSec) * speed; clip.FadeOutSec = 0;
            }
        }
    }
}
