// Owns: the File > Render dialog's logic without UI types: its option lists, the format and sample-rate rules (MP3 takes 44.1 / 48 kHz only),
//       which bounds choices can be picked, the custom bar range on load, the hint and tooltip texts, and the output file names a render would write.
// Does not own: the dialog and its controls (Views/RenderWindow.cs), the render (RenderJob), or the output folder check (RenderWindow.RenderAsync, through FilePathPolicy.OutputDirectory).
// Tests: no dedicated test class.
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Presenters;

/// <summary>One output file of a render: the track it holds (null for the master mix) and its file name, without the folder.</summary>
public sealed record RenderFileName(TrackModel? Track, string FileName);

/// <summary>The File > Render dialog's option lists, rules and file names. Static: the dialog keeps its state.</summary>
public static class RenderDialogPresenter
{
    /// <summary>The sample rates in combo order; 0 is the engine rate.</summary>
    public static IReadOnlyList<int> SampleRates { get; } = [0, 44100, 48000, 88200, 96000];

    /// <summary>The MP3 bit rates; the format combo lists them after the three WAV formats.</summary>
    public static IReadOnlyList<int> Mp3Kbps { get; } = [128, 160, 192, 320];

    public static IReadOnlyList<string> FormatNames { get; } =
        ["WAV 16-bit", "WAV 24-bit", "WAV 32-bit float", "MP3 128 kbps", "MP3 160 kbps", "MP3 192 kbps", "MP3 320 kbps"];

    public static IReadOnlyList<string> SourceNames { get; } = ["Master mix", "Stems: checked tracks", "Stems: all tracks", "Master mix + stems (all tracks)"];

    public static IReadOnlyList<string> TailNames { get; } = ["Off", "Fixed", "Auto (until silent)"];

    public static IReadOnlyList<string> ThreadNames { get; } = ["Threads: auto", "Threads: 1"];

    /// <summary>The bounds choices in dialog order: entire song, time selection, selected bars, custom seconds, custom bars, custom sections.</summary>
    public static IReadOnlyList<string> BoundsNames { get; } = ["Entire song", "Time selection", "Selected bars", "Custom range (seconds)", "Custom bars", "Custom sections"];

    /// <summary>True for the MP3 formats, which start at format index 3.</summary>
    public static bool IsMp3(int formatIndex) => formatIndex >= 3;

    /// <summary>
    /// The format index the dialog shows for a saved format (0 to 2 are the WAV formats; 3 is MP3 at the saved bit rate).
    /// Without the MP3 encoder an MP3 choice falls back to WAV 24-bit.
    /// </summary>
    public static int FormatIndex(int savedFormat, int savedKbps, bool mp3Available)
    {
        var index = savedFormat == 3 ? 3 + Math.Max(0, Mp3Kbps.ToList().IndexOf(savedKbps)) : Math.Clamp(savedFormat, 0, 2);
        return !mp3Available && IsMp3(index) ? 1 : index;
    }

    /// <summary>The format to save for a format index, and the MP3 bit rate when the index is an MP3 one (null keeps the saved rate).</summary>
    public static (int Format, int? Mp3Kbps) SavedFormat(int formatIndex) =>
        IsMp3(formatIndex) ? (3, Mp3Kbps[formatIndex - 3]) : (formatIndex, null);

    /// <summary>MP3 takes the engine rate, 44.1 kHz or 48 kHz only; WAV takes every rate.</summary>
    public static bool IsSampleRateAllowed(int formatIndex, int sampleRate) => !IsMp3(formatIndex) || sampleRate is 0 or 44100 or 48000;

    /// <summary>The rate combo index to show after a format change: a rate the format cannot use falls back to the engine rate (index 0).</summary>
    public static int RateIndexAfterFormat(int formatIndex, int rateIndex) =>
        IsMp3(formatIndex) && !IsSampleRateAllowed(formatIndex, SampleRates[Math.Max(0, rateIndex)]) ? 0 : rateIndex;

    /// <summary>Whether a bounds choice can be picked: a selection needs a selection, custom bars need bars and custom sections need sections.</summary>
    public static bool IsBoundsEnabled(int bounds, bool hasSelection, int sectionCount, int barCount) => bounds switch
    {
        1 or 2 => hasSelection,
        4 => barCount > 0,
        5 => sectionCount > 0,
        _ => true,
    };

    /// <summary>The saved bounds choice, moved to "Entire song" when the song cannot use it.</summary>
    public static int BoundsOnLoad(int savedBounds, bool hasSelection, int sectionCount, int barCount)
    {
        var bounds = Math.Clamp(savedBounds, 0, 5);
        return IsBoundsEnabled(bounds, hasSelection, sectionCount, barCount) ? bounds : 0;
    }

    /// <summary>The custom bar range shown on load: a saved bar (1-based) is kept inside the song; an unset one (below 1) loads as
    /// the first bar (from) or the last bar (to).</summary>
    public static (int From, int To) BarRangeOnLoad(int savedFrom, int savedTo, int barCount)
    {
        var last = Math.Max(1, barCount);
        return (savedFrom >= 1 ? Math.Min(savedFrom, last) : 1, savedTo >= 1 ? Math.Min(savedTo, last) : last);
    }

    /// <summary>The hint under the bounds: the problem with the chosen bars or sections, else the note that custom sections need sections.</summary>
    public static string HintText(string? problem, int sectionCount) =>
        problem ?? (sectionCount == 0 ? "Custom sections needs sections: add them in the Sections pane." : "");

    /// <summary>The tooltip of the selection choices when there is no selection, else none.</summary>
    public static string? SelectionTooltip(bool hasSelection) => hasSelection ? null : "Select bars in the score first.";

    /// <summary>The tooltip of the custom sections choice when the song has no sections, else none.</summary>
    public static string? SectionsTooltip(int sectionCount) => sectionCount == 0 ? "The song has no sections yet." : null;

    /// <summary>
    /// The output file names a render writes, in order: the master mix (source 0 or 3), then one stem per track of the source
    /// (1 = the checked tracks, 2 and 3 = all tracks). The pattern expands per name, a name is never repeated in the folder or in the render
    /// (a number is added), and a stem gets its track name when the pattern has no $track wildcard.
    /// </summary>
    public static IReadOnlyList<RenderFileName> PlanFiles(string pattern, string directory, string? projectTitle, double tempo, DateTime now,
        string extension, int source, IList<TrackModel> projectTracks, IList<TrackModel> checkedTracks)
    {
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string Name(string track, int number)
        {
            var name = RenderNaming.Expand(pattern, projectTitle ?? "song", track, number, now, tempo) + extension;
            name = RenderNaming.Unique(directory, name, reserved);
            reserved.Add(name);
            return name;
        }
        var files = new List<RenderFileName>();
        if (source is 0 or 3) files.Add(new RenderFileName(null, Name("master", 0)));
        if (source == 0) return files;
        foreach (var track in source == 1 ? checkedTracks : projectTracks)
        {
            var name = Name(track.Name, projectTracks.IndexOf(track) + 1);
            if (!pattern.Contains("$track", StringComparison.Ordinal))
            {
                reserved.Remove(name);
                name = RenderNaming.Unique(directory, Path.GetFileNameWithoutExtension(name) + "_" + RenderNaming.Sanitize(track.Name) + extension, reserved);
                reserved.Add(name);
            }
            files.Add(new RenderFileName(track, name));
        }
        return files;
    }

    /// <summary>A number typed in a dialog box: a comma counts as the decimal point, and text that is not a number reads as 0.</summary>
    public static double ParseNumber(string text) =>
        double.TryParse(text.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : 0;
}
