using TabForge.Models;
using TabForge.Platform.Theme;

namespace TabForge.Services;

// Owns: turning conductor-track marker/cue texts into song sections on MIDI import.
// Does not own: reading the file (SmfReader) or the score notes (MidiToSong).
// Tests: MidiSectionImportTests (Import/).
/// <summary>Song sections from a MIDI file's conductor-track markers.</summary>
internal static class MidiSectionImport
{
    /// <summary>
    /// Turns the conductor-track (track 0) marker (0x06) and cue-point (0x07) texts that do not start
    /// with TF1 into sections: name = text, start = the bar containing the tick, each running to the
    /// next marker. Empty names are skipped, duplicate names get " (2)", colours cycle the app palette.
    /// Legacy TF1 technique markers never become sections. Nothing is added when the file has none.
    /// </summary>
    public static void ApplySections(SongProject project, SmfFile smf, List<(long Start, long Length)> bars)
    {
        if (smf.Tracks.Count == 0 || bars.Count == 0) return;
        var events = smf.Tracks[0].SectionEvents;
        if (events.Count == 0) return;
        var ordered = events.OrderBy(e => e.Tick).ToList();
        var usedNames = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (tick, raw) in ordered)
        {
            var name = (raw ?? "").Trim();
            if (name.Length == 0) continue;
            if (name.Length > InputLimits.MaxTitleLength) name = name[..InputLimits.MaxTitleLength];
            // Duplicate names get " (2)", then " (3)".
            if (usedNames.TryGetValue(name, out var seen))
            {
                seen++;
                usedNames[name] = seen;
                name = $"{name} ({seen})";
            }
            else usedNames[name] = 1;
            var bar = BarOf(bars, tick);
            if (project.Markers.Count >= InputLimits.MaxMarkers) break;
            project.Markers.Add(new MarkerModel
            {
                MeasureIndex = bar,
                Title = name,
                ColorHex = NamedColourSwatches.Sections[project.Markers.Count % NamedColourSwatches.Sections.Count].Hex,
            });
        }
    }

    private static int BarOf(List<(long Start, long Length)> bars, long tick)
    {
        var bar = 0;
        for (var i = 0; i < bars.Count; i++)
        {
            if (bars[i].Start <= tick) bar = i;
            else break;
        }
        return Math.Clamp(bar, 0, bars.Count - 1);
    }
}
