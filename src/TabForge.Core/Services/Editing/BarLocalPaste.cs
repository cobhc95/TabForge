using TabForge.Models;

namespace TabForge.Services;

// Owns: placing a beats clip inside one bar: no bar-line split, no ties added, the following bars untouched. A bar the beats do not fit stays overfull
//     (the editor shows it red); a selection pastes over its own cells, or over the whole bar when asked.
// Does not own: choosing the placement (EditCommands.PasteBeats), the writing of a bar grid (BarGrid.WriteBar), the red bar look (MusicTime.AnalyzeBar).
// Tests: no dedicated test class.
public static class BarLocalPaste
{
    private const double Eps = 1e-6;

    /// <summary>What a placement did: the first pasted beat's cell (the cursor goes there), and whether the written bar is longer than its time signature.</summary>
    public sealed record Result(PlacementResult Placement, int CursorCell, bool Overfull);

    /// <summary>
    /// Writes <paramref name="run"/> into the bar of <paramref name="target"/>. Without a selection, <see cref="BeatPasteMode.Replace"/> overwrites the span the clip
    /// covers from the cursor beat; with a selection it overwrites only the selected beats, or the whole bar (<see cref="BeatPasteMode.ReplaceBar"/>: every bar
    /// the selection touches is emptied and the clip goes into the first). The clip is never cut to fit.
    /// </summary>
    public static Result Replace(SongProject p, PasteTarget target, int voice, BeatRun run, BeatPasteMode mode)
    {
        var track = p.Tracks[target.TrackIndex];
        var bar = target.Bar;
        var lastBar = target.EndBar >= bar ? Math.Min(target.EndBar, track.Measures.Count - 1) : bar;
        var slots = MusicTime.BarSlots(p, bar);
        var measure = track.Measures[bar];
        var cells = measure.CellsForVoice(voice);
        var onsets = BarGrid.Onsets(cells);
        var cursor = target.Cell >= 0 && target.Cell < cells.Count ? onsets[target.Cell] : Math.Max(0, target.Cell);
        var at = mode == BeatPasteMode.ReplaceBar ? 0 : cursor;
        var length = Math.Max(run.LengthSlots, run.Beats.Max(b => b.OffsetSlots + MusicTime.CellSlots(b.Cell)));

        // The end of what is overwritten in the first bar (slots): the clip's span, the selected beats, or the whole bar.
        double cutEnd;
        if (mode == BeatPasteMode.ReplaceBar) cutEnd = double.MaxValue;
        else if (target.EndBar < 0) cutEnd = at + length;
        else if (lastBar > bar) cutEnd = double.MaxValue;
        else cutEnd = SelectionEnd(cells, onsets, target.EndCell);

        var kept = new List<(double Pos, TabCell Cell)>();
        for (var i = 0; i < cells.Count; i++)
        {
            if (!MusicTime.IsBeat(cells[i])) continue;
            var pos = onsets[i];
            if (pos >= at - Eps && pos < cutEnd - Eps) continue;
            kept.Add((pos >= cutEnd - Eps ? pos + Math.Max(0, at + length - cutEnd) : pos, cells[i]));   // what follows a too-short selection moves after the clip
        }
        ShortenTail(kept, at);
        AddClip(kept, run, at);
        if (kept.Count > InputLimits.MaxCellsPerMeasure) return Refused(bar);
        Write(measure, voice, kept, slots);
        // A selection across bars: the bars after the first lose their selected beats.
        for (var b = bar + 1; target.EndBar >= 0 && b <= lastBar; b++)
            ClearSelected(p, track, b, voice, b == lastBar && mode != BeatPasteMode.ReplaceBar ? target.EndCell : int.MaxValue);
        return Finish(kept, slots, bar, lastBar, at);
    }

    /// <summary>
    /// Whether a normal beats paste stays inside its bar: always for a selection and for a few notes (less than a bar). Material of a bar or more flows on into the
    /// following bars as long as no beat has to be cut by a bar line (that would need a tie); otherwise it stays in the bar, which then shows red.
    /// </summary>
    public static bool Applies(SongProject p, PasteTarget target, double anchor, BeatRun run, bool onSelection)
    {
        if (onSelection) return true;
        var length = Math.Max(run.LengthSlots, run.Beats.Max(b => b.OffsetSlots + MusicTime.CellSlots(b.Cell)));
        if (length < BarGrid.SlotsOf(p, target.Bar) - Eps) return true;
        foreach (var beat in run.Beats)
        {
            var start = anchor + beat.OffsetSlots;
            var (bar, inBar) = BarGrid.Locate(p, start);
            if (inBar + MusicTime.CellSlots(beat.Cell) > BarGrid.SlotsOf(p, bar) + Eps) return true;
        }
        return false;
    }

    /// <summary>The placement of a normal (not Paste Special) beats paste: Insert only when the choice asked for it on a selection, otherwise an overwrite.</summary>
    public static Result For(SongProject p, PasteTarget target, int voice, BeatRun run, BeatPasteMode mode, bool onSelection) =>
        onSelection && mode == BeatPasteMode.Insert ? Insert(p, target, voice, run) : Replace(p, target, voice, run, mode);

    /// <summary>The clip pushed in before the cursor beat of the bar (the beats from there on move later); the bar may overfill.</summary>
    public static Result Insert(SongProject p, PasteTarget target, int voice, BeatRun run)
    {
        var track = p.Tracks[target.TrackIndex];
        var measure = track.Measures[target.Bar];
        var cells = measure.CellsForVoice(voice);
        var onsets = BarGrid.Onsets(cells);
        var at = target.Cell >= 0 && target.Cell < cells.Count ? onsets[target.Cell] : Math.Max(0, target.Cell);
        var length = Math.Max(run.LengthSlots, run.Beats.Max(b => b.OffsetSlots + MusicTime.CellSlots(b.Cell)));
        var kept = new List<(double Pos, TabCell Cell)>();
        for (var i = 0; i < cells.Count; i++)
            if (MusicTime.IsBeat(cells[i])) kept.Add((onsets[i] >= at - Eps ? onsets[i] + length : onsets[i], cells[i]));
        ShortenTail(kept, at);
        AddClip(kept, run, at);
        if (kept.Count > InputLimits.MaxCellsPerMeasure) return Refused(target.Bar);
        var slots = MusicTime.BarSlots(p, target.Bar);
        Write(measure, voice, kept, slots);
        return Finish(kept, slots, target.Bar, target.Bar, at);
    }

    /// <summary>
    /// Where an overfull bar's beats end, when the paste cursor is at or past that end; null otherwise. As GP5 (quiet run j07, session 15
    /// step 56), a paste there goes on in the same bar instead of starting the next bar after the overflow's rests.
    /// </summary>
    public static double? OverfullEnd(SongProject p, PasteTarget target, int voice)
    {
        var measures = p.Tracks[target.TrackIndex].Measures;
        if (target.Bar < 0 || target.Bar >= measures.Count) return null;
        var cells = measures[target.Bar].CellsForVoice(voice);
        var onsets = BarGrid.Onsets(cells);
        var end = 0.0;
        for (var i = 0; i < cells.Count; i++)
            if (MusicTime.IsBeat(cells[i])) end = Math.Max(end, onsets[i] + MusicTime.HeldSlots(cells[i]));
        var at = target.Cell >= 0 && target.Cell < cells.Count ? onsets[target.Cell] : target.Cell;
        return end > MusicTime.BarSlots(p, target.Bar) + 1e-6 && at >= end - 1e-6 ? end : null;
    }

    /// <summary>Appends a beat run to one voice of a bar at slot <paramref name="end"/> (the bar grows; nothing moves to the next bar).</summary>
    public static PlacementResult AppendInBar(SongProject p, PasteTarget target, int voice, double end, BeatRun run)
    {
        var measure = p.Tracks[target.TrackIndex].Measures[target.Bar];
        var cells = measure.CellsForVoice(voice);
        var onsets = BarGrid.Onsets(cells);
        var beats = cells.Select((c, i) => (Pos: onsets[i], Cell: c)).Where(x => MusicTime.IsBeat(x.Cell)).ToList();
        foreach (var b in run.Beats)
        {
            var cell = b.Cell.Clone();
            cell.RhythmicPosition = null;
            beats.Add((end + b.OffsetSlots, cell));
        }
        if (beats.Count > InputLimits.MaxCellsPerMeasure) return PlacementResult.Refused($"Bar {target.Bar + 1} would hold too many beats.");
        var written = BarGrid.WriteBar(beats, MusicTime.BarSlots(p, target.Bar));
        if (voice == 0) measure.Cells = written; else measure.Voice2Cells = written;
        return new PlacementResult(true, null, target.Bar, target.Bar, 0, 0, 0);
    }

    /// <summary>True when a voice of the bar holds beats running past the bar's length (the bar shows red).</summary>
    public static bool IsOverfull(SongProject p, int trackIndex, int bar, int voice)
    {
        var cells = p.Tracks[trackIndex].Measures[bar].CellsForVoice(voice);
        var onsets = BarGrid.Onsets(cells);
        return Enumerable.Range(0, cells.Count).Any(i => MusicTime.IsBeat(cells[i]) && onsets[i] + MusicTime.HeldSlots(cells[i]) > MusicTime.BarSlots(p, bar) + Eps);
    }

    /// <summary>
    /// One copied bar pasted onto part of a bar (a selection inside one bar that is not the whole bar) is pasted as that bar's beats, so the selection decides
    /// where it lands; every other clip comes back unchanged.
    /// </summary>
    public static ScoreClip AsBeatsOnPartialSelection(SongProject p, ScoreClip clip, PasteTarget target)
    {
        if (clip.Kind != ScoreClipKind.Bars || clip.BarCount != 1 || clip.Tracks.Count != 1 || target.EndBar != target.Bar || target.Bar >= p.Tracks[target.TrackIndex].Measures.Count) return clip;
        if (target.Cell <= 0 && target.EndCell >= MusicTime.BarSlots(p, target.Bar) - 1) return clip;
        var source = clip.Tracks[0];
        var cells = source.Bars[0].Cells;
        var onsets = BarGrid.Onsets(cells);
        var track = new ScoreClipTrack
        {
            SourceTrackId = source.SourceTrackId, SourceTrackIndex = source.SourceTrackIndex, Name = source.Name, InstrumentName = source.InstrumentName,
            TrackKind = source.TrackKind, IsDrums = source.IsDrums, MidiChannel = source.MidiChannel, StringTunings = source.StringTunings.ToList(), Capo = source.Capo,
            NumberOfFrets = source.NumberOfFrets, DrumMapPreset = source.DrumMapPreset, Voice = 0, LengthSlots = clip.BarSlots(0)
        };
        for (var i = 0; i < cells.Count; i++)
        {
            if (!MusicTime.IsBeat(cells[i])) continue;
            var cell = cells[i].Clone();
            cell.RhythmicPosition = null;
            track.Events.Add(new ScoreClipEvent { OffsetSlots = onsets[i], Cell = cell });
        }
        if (track.Events.Count == 0) return clip;
        var beats = new ScoreClip
        {
            Format = clip.Format, Version = clip.Version, Kind = ScoreClipKind.Beats, SourceSongId = clip.SourceSongId, SourceTrackCount = clip.SourceTrackCount,
            SongTimeSignatureNumerator = clip.SongTimeSignatureNumerator, SongTimeSignatureDenominator = clip.SongTimeSignatureDenominator,
            SongKeySignature = clip.SongKeySignature, SongKeySignatureMinor = clip.SongKeySignatureMinor, StartTempo = clip.StartTempo
        };
        beats.Tracks.Add(track);
        return beats;
    }

    private static void AddClip(List<(double Pos, TabCell Cell)> into, BeatRun run, double at)
    {
        foreach (var beat in run.Beats.OrderBy(b => b.OffsetSlots))
        {
            if (!MusicTime.IsBeat(beat.Cell)) continue;
            var cell = beat.Cell.Clone();
            cell.RhythmicPosition = null;
            into.Add((at + beat.OffsetSlots, cell));
        }
    }

    private static Result Refused(int bar) => new(PlacementResult.Refused($"Bar {bar + 1} would hold too many beats."), -1, false);

    private static Result Finish(List<(double Pos, TabCell Cell)> beats, int slots, int firstBar, int lastBar, double at)
    {
        var end = beats.Count == 0 ? 0 : beats.Max(b => b.Pos + MusicTime.CellSlots(b.Cell));
        var first = beats.Where(b => b.Pos >= at - Eps).OrderBy(b => b.Pos).Select(b => ((double Pos, TabCell Cell)?)b).FirstOrDefault();
        return new Result(new PlacementResult(true, null, firstBar, lastBar, 0, 0, 0), first is { } f ? (int)Math.Floor(f.Pos + Eps) : -1, end > slots + Eps);
    }

    private static void Write(MeasureModel measure, int voice, List<(double Pos, TabCell Cell)> beats, int slots)
    {
        var written = BarGrid.WriteBar(beats, slots);
        if (voice == 0) measure.Cells = written; else measure.Voice2Cells = written;
    }

    /// <summary>Slot after the last selected beat of the end bar (the cell's own length, at least one slot).</summary>
    private static double SelectionEnd(IReadOnlyList<TabCell> cells, double[] onsets, int endCell)
    {
        if (endCell < 0 || endCell >= cells.Count) return Math.Max(0, endCell) + 1;
        return onsets[endCell] + (MusicTime.IsBeat(cells[endCell]) ? Math.Max(1, MusicTime.HeldSlots(cells[endCell])) : 1);
    }

    private static void ClearSelected(SongProject p, TrackModel track, int bar, int voice, int endCell)
    {
        var measure = track.Measures[bar];
        var cells = measure.CellsForVoice(voice);
        var onsets = BarGrid.Onsets(cells);
        var end = endCell == int.MaxValue ? double.MaxValue : SelectionEnd(cells, onsets, endCell);
        var kept = new List<(double Pos, TabCell Cell)>();
        for (var i = 0; i < cells.Count; i++)
            if (MusicTime.IsBeat(cells[i]) && onsets[i] >= end - Eps) kept.Add((onsets[i], cells[i]));
        Write(measure, voice, kept, MusicTime.BarSlots(p, bar));
    }

    /// <summary>A beat that rings into the pasted span is shortened to end where the span starts, so the pasted beats start on time.</summary>
    private static void ShortenTail(List<(double Pos, TabCell Cell)> beats, double at)
    {
        for (var i = 0; i < beats.Count; i++)
        {
            var (pos, cell) = beats[i];
            var room = at - pos;
            if (pos >= at - Eps || pos + MusicTime.CellSlots(cell) <= at + Eps) continue;
            var parts = BarGrid.Decompose(room);
            if (parts.Count == 0) continue;
            var copy = cell.Clone();
            (copy.DurationDenominator, copy.Dots) = parts[0];
            copy.IsTriplet = false; copy.TupletNumerator = 0; copy.TupletDenominator = 0;
            beats[i] = (pos, copy);
        }
    }

}
