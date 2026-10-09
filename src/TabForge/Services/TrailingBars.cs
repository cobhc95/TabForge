using TabForge.Models;
using TabForge.Playback;

namespace TabForge.Services;

// Owns: finding and removing the empty bars at the end of a song after clips shrink.
// Does not own: the undo step or the setting (ClipEditController), the clip edits themselves.
// Tests: TestTrimEmptyBars.
/// <summary>
/// The song's end follows its last item: a bar at the end stays when any track has notation in it, it carries a repeat, ending, section name, tempo
/// change, direction or mix-table point, a section marker starts in or runs over it, or an audio or MIDI clip reaches into it.
/// </summary>
internal static class TrailingBars
{
    /// <summary>True when bar <paramref name="bar"/> holds notation or bar-level form on any track.</summary>
    public static bool HoldsContent(SongProject project, int bar) =>
        project.Tracks.Any(t => bar < t.Measures.Count && MeasureHoldsContent(t.Measures[bar]));

    private static bool MeasureHoldsContent(MeasureModel m) =>
        m.RepeatStart || m.RepeatEnd || m.AlternateEnding != 0 || m.AlternateEndingMask != 0 || m.SectionName.Length > 0 || m.TempoChange is not null ||
        m.MidBarTempos is { Count: > 0 } || m.Directions.Length > 0 || m.IsDoubleBar || m.Anacrusis ||
        Voice(m.Cells) || Voice(m.Voice2Cells);

    private static bool Voice(List<TabCell> cells) => cells.Any(c => c.Notes.Count > 0 || c.Fermata || c.Mix is { IsEmpty: false });

    /// <summary>The bar count the song needs: everything up to the last bar with content, a section or a clip. At least 1.</summary>
    public static int NeededBars(SongProject project)
    {
        var count = BarRangeEditor.MaxMeasures(project);
        var keep = 1;
        for (var bar = count - 1; bar >= 0; bar--)
            if (HoldsContent(project, bar)) { keep = bar + 1; break; }
        foreach (var marker in project.Markers) keep = Math.Max(keep, marker.MeasureIndex + Math.Max(1, marker.LengthBars ?? 1));
        var clipEnd = SongExtent.ClipsEnd(project);
        if (clipEnd > 0 && keep < count)
        {
            var timeline = MidiTimelineBuilder.Build(project, new PlaybackOptions { RepeatExpansion = true, RespectMuteSolo = false, SkipClips = true });
            // Bars after the last one with content do not repeat, so each starts once; the clip must end by the start of the first dropped bar.
            for (var bar = count - 1; bar >= keep; bar--)
            {
                var entry = timeline.Bars.LastOrDefault(b => b.Bar == bar);
                if (entry.StartMs / 1000 < clipEnd - 1e-6) { keep = bar + 1; break; }
            }
        }
        return Math.Min(count, keep);
    }

    /// <summary>Removes the empty bars at the end; returns how many went (0 when none).</summary>
    public static int Trim(SongProject project)
    {
        var count = BarRangeEditor.MaxMeasures(project);
        var keep = NeededBars(project);
        if (keep >= count) return 0;
        BarRangeEditor.Remove(project, keep, count - 1);
        project.MarkTimelineChanged();
        return count - BarRangeEditor.MaxMeasures(project);
    }
}
