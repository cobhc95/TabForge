using TabForge.Models;

namespace TabForge.Services;

/// <summary>
/// Time and key signatures per master bar.
/// <para>
/// A bar's <see cref="MeasureModel.TimeSigNum"/> / <see cref="MeasureModel.TimeSigDenom"/> / <see cref="MeasureModel.KeySignature"/> /
/// <see cref="MeasureModel.KeySignatureMinor"/> are optional overrides; a bar without one uses the song's own signature
/// (<see cref="SongProject.TimeSignatureNumerator"/> and friends, which are bar 1's). Everything that reads a bar (score drawing,
/// playback, bar-fill check, exports) resolves a signature this way, and a signature is drawn only where it differs from the
/// previous bar.
/// </para>
/// <para>
/// Setting a signature at bar N is therefore written out for every bar it applies to, so that rendering and playback agree and no
/// later bar silently falls back to the song's value: it covers bar N and the bars after it up to (not including) the next bar
/// whose signature is different, or only bar N when asked. Time signatures equal to the song's keep no override of their own
/// (the shape an imported file has; bar N always holds its value as the visible change point). Keys are written on every bar of
/// the run, as an imported file has them, because the exports carry a key forward from the last bar that has one. Every track
/// gets the same values. Changing bar 1 also changes the song's own signature; bars after the changed run that were using it
/// keep their old value.
/// </para>
/// <para>
/// Engineering decisions (2026-10-01): a bar without a key of its own (a one-bar change, or a song saved before keys carried forward)
/// is in the song's key everywhere, the exports included; saved songs are not rewritten on load. With a bar range selected, a
/// signature change applies to exactly the selected bars. A duplicated bar never repeats the section label or the pickup mark.
/// </para>
/// Pure model code: no UI, no undo (the caller wraps it in one transaction).
/// </summary>
public static class BarSignatures
{
    public static (int Num, int Den) TimeAt(SongProject project, int bar)
    {
        var measure = MusicTime.BarOf(project, bar);
        return (measure?.TimeSigNum ?? project.TimeSignatureNumerator, measure?.TimeSigDenom ?? project.TimeSignatureDenominator);
    }

    public static (int Key, bool Minor) KeyAt(SongProject project, int bar)
    {
        var measure = MusicTime.BarOf(project, bar);
        return (measure?.KeySignature ?? project.KeySignature, measure?.KeySignatureMinor ?? project.KeySignatureMinor);
    }

    /// <summary>The last bar of the run that starts at <paramref name="bar"/> and keeps its time signature (the next change follows it).</summary>
    public static int TimeRunEnd(SongProject project, int bar)
    {
        var count = BarRangeEditor.MaxMeasures(project);
        var value = TimeAt(project, bar);
        var last = bar;
        while (last + 1 < count && TimeAt(project, last + 1) == value) last++;
        return last;
    }

    /// <summary>The last bar of the run that starts at <paramref name="bar"/> and keeps its key signature.</summary>
    public static int KeyRunEnd(SongProject project, int bar)
    {
        var count = BarRangeEditor.MaxMeasures(project);
        var value = KeyAt(project, bar);
        var last = bar;
        while (last + 1 < count && KeyAt(project, last + 1) == value) last++;
        return last;
    }

    /// <summary>
    /// Sets the time signature from <paramref name="bar"/> up to the next change (or on that bar only). Returns the last bar changed,
    /// or -1 when there is no such bar.
    /// </summary>
    public static int SetTime(SongProject project, int bar, int numerator, int denominator, bool untilNextChange = true)
    {
        var count = BarRangeEditor.MaxMeasures(project);
        if (bar < 0 || bar >= count) return -1;
        var last = untilNextChange ? TimeRunEnd(project, bar) : bar;
        var oldSong = (project.TimeSignatureNumerator, project.TimeSignatureDenominator);
        if (bar == 0)
        {
            project.TimeSignatureNumerator = numerator;
            project.TimeSignatureDenominator = denominator;
            // The song's signature moved: later bars that relied on it keep what they had.
            if (oldSong != (numerator, denominator))
                for (var b = last + 1; b < count; b++)
                    ForBar(project, b, m => { m.TimeSigNum ??= oldSong.Item1; m.TimeSigDenom ??= oldSong.Item2; });
        }
        var song = (project.TimeSignatureNumerator, project.TimeSignatureDenominator);
        for (var b = bar; b <= last; b++)
        {
            var explicitValue = b == bar || song != (numerator, denominator);
            ForBar(project, b, m =>
            {
                m.TimeSigNum = explicitValue ? numerator : null;
                m.TimeSigDenom = explicitValue ? denominator : null;
            });
        }
        PadCells(project, bar, last);
        return last;
    }

    /// <summary>Sets the key signature from <paramref name="bar"/> up to the next change (or on that bar only); see <see cref="SetTime"/>.</summary>
    public static int SetKey(SongProject project, int bar, int signature, bool minor, bool untilNextChange = true)
    {
        var count = BarRangeEditor.MaxMeasures(project);
        if (bar < 0 || bar >= count) return -1;
        var last = untilNextChange ? KeyRunEnd(project, bar) : bar;
        var oldSong = (project.KeySignature, project.KeySignatureMinor);
        if (bar == 0)
        {
            project.KeySignature = signature;
            project.KeySignatureMinor = minor;
            if (oldSong != (signature, minor))
                for (var b = last + 1; b < count; b++)
                    ForBar(project, b, m => { m.KeySignature ??= oldSong.Item1; m.KeySignatureMinor ??= oldSong.Item2; });
        }
        // Keys are always written out (an imported file has a key on every bar too): the exports carry a bar's key forward
        // from the last bar that has one, so a bar back in the song's key must say so itself.
        for (var b = bar; b <= last; b++)
            ForBar(project, b, m => { m.KeySignature = signature; m.KeySignatureMinor = minor; });
        return last;
    }

    /// <summary>Sets the time signature on exactly bars [first, last] (a selected bar range); the bars after it keep theirs. Returns the last bar changed or -1.</summary>
    public static int SetTimeRange(SongProject project, int first, int last, int numerator, int denominator)
    {
        last = Math.Min(last, BarRangeEditor.MaxMeasures(project) - 1);
        if (first < 0 || first > last) return -1;
        for (var b = first; b <= last; b++) SetTime(project, b, numerator, denominator, untilNextChange: false);
        return last;
    }

    /// <summary>Sets the key signature on exactly bars [first, last] (a selected bar range); see <see cref="SetTimeRange"/>.</summary>
    public static int SetKeyRange(SongProject project, int first, int last, int signature, bool minor)
    {
        last = Math.Min(last, BarRangeEditor.MaxMeasures(project) - 1);
        if (first < 0 || first > last) return -1;
        for (var b = first; b <= last; b++) SetKey(project, b, signature, minor, untilNextChange: false);
        return last;
    }

    /// <summary>
    /// The song's own time signature (Project settings): every bar without an override of its own follows it, as before.
    /// Bar 1 is kept in step.
    /// </summary>
    public static void SetSongTime(SongProject project, int numerator, int denominator)
    {
        project.TimeSignatureNumerator = numerator;
        project.TimeSignatureDenominator = denominator;
        ForBar(project, 0, m => { m.TimeSigNum = numerator; m.TimeSigDenom = denominator; });
        PadCells(project, 0, BarRangeEditor.MaxMeasures(project) - 1);
    }

    /// <summary>The song's own key signature (Project settings); see <see cref="SetSongTime"/>.</summary>
    public static void SetSongKey(SongProject project, int signature, bool minor)
    {
        project.KeySignature = signature;
        project.KeySignatureMinor = minor;
        ForBar(project, 0, m => { m.KeySignature = signature; m.KeySignatureMinor = minor; });
    }

    /// <summary>
    /// A bar that got longer has to offer the extra beats to the editor: its empty beat slots are topped up to the new length.
    /// Written content is never touched (a bar that no longer fits is flagged by the bar check).
    /// </summary>
    private static void PadCells(SongProject project, int first, int last)
    {
        for (var b = first; b <= last; b++)
        {
            var slots = MusicTime.BarSlots(project, b);
            foreach (var track in project.Tracks)
            {
                if (b >= track.Measures.Count) continue;
                var measure = track.Measures[b];
                while (measure.Cells.Count < slots) measure.Cells.Add(new TabCell());
                if (measure.Voice2Cells.Count > 0) while (measure.Voice2Cells.Count < slots) measure.Voice2Cells.Add(new TabCell());
            }
        }
    }

    private static void ForBar(SongProject project, int bar, Action<MeasureModel> edit)
    {
        foreach (var track in project.Tracks)
            if (bar < track.Measures.Count) edit(track.Measures[bar]);
    }
}
