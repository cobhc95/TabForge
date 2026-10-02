using TabForge.Models;
using TabForge.Playback;
using TempoMath = TabForge.Audio.Contracts.TempoMath;

namespace TabForge.Services;

/// <summary>What <see cref="SongExtent.EnsureCovers"/> did.</summary>
public readonly record struct ExtentResult(int BarsAdded, bool Capped);

// Owns: keeping the song at least as long as the material on its timeline.
// Does not own: the clip edits that trigger it.
// Tests: TestSongExtent.
/// <summary>
/// Keeps the song at least as long as the material on its timeline: when a clip (dropped, imported, moved, pasted or recorded) ends after
/// the last bar, whole empty bars are appended to every track. The song never shrinks by itself.
/// </summary>
public static class SongExtent
{
    /// <summary>The end of the song in seconds as performed, and the length of one more bar (last bar's time signature, tempo in effect at the end).</summary>
    public static (double EndSec, double BarSec) Measure(SongProject project)
    {
        var timeline = MidiTimelineBuilder.Build(project, new PlaybackOptions { RepeatExpansion = true, RespectMuteSolo = false, SkipClips = true });
        var endSec = timeline.Bars.Count == 0 ? 0 : timeline.Bars[^1].EndMs / 1000;
        var tempo = timeline.Bars.Count == 0 ? project.Tempo : timeline.Bars[^1].Tempo;
        if (tempo <= 0) tempo = project.Tempo > 0 ? project.Tempo : 120;
        var last = project.Tracks.Select(t => t.Measures.LastOrDefault()).FirstOrDefault(m => m is not null);
        var num = last?.TimeSigNum ?? project.TimeSignatureNumerator;
        var den = last?.TimeSigDenom ?? project.TimeSignatureDenominator;
        if (num <= 0) num = 4;
        if (den <= 0) den = 4;
        return (endSec, TempoMath.BeatsToSeconds(num * (4.0 / den), tempo));
    }

    /// <summary>Appends enough bars (up to the song-length cap) that the song reaches <paramref name="endSec"/>; does nothing when it already does.</summary>
    public static ExtentResult EnsureCovers(SongProject project, double endSec)
    {
        if (project.Tracks.Count == 0 || double.IsNaN(endSec) || endSec <= 0) return default;
        var (songEnd, barSec) = Measure(project);
        if (endSec <= songEnd + 1e-6 || barSec <= 0) return default;
        var wanted = (int)Math.Ceiling((endSec - songEnd) / barSec - 1e-9);
        var bars = BarRangeEditor.MaxMeasures(project);
        var room = Math.Min(InputLimits.MaxMeasuresPerTrack - bars, InputLimits.MaxTotalMeasures / project.Tracks.Count - bars);
        var add = Math.Max(0, Math.Min(wanted, room));
        if (add > 0) BarGrid.AppendBars(project, add);
        return new ExtentResult(add, add < wanted);
    }

    /// <summary>The status-bar note for a result ("" when nothing was added).</summary>
    public static string Describe(ExtentResult r) =>
        r.BarsAdded == 0 && !r.Capped ? "" :
        r.Capped ? $" (added {r.BarsAdded} bars; the song is at its length limit, so the end of the material lies beyond the last bar)" :
        $" (added {r.BarsAdded} bar{(r.BarsAdded == 1 ? "" : "s")} at the end so the song covers it)";
}
