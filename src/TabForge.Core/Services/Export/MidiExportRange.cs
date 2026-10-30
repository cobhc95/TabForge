using TabForge.Models;
using TabForge.Playback;
using TabForge.Presenters;
using TabForge.Rendering;

namespace TabForge.Services;

// Owns: resolving the Export MIDI dialog's choices to a track set and a song range, with the same
//   performed-order mapping Render uses (RenderSpecBuilder.Bounds, RenderSnip for the white selection).
// Does not own: the range rules (RenderSpecBuilder, RenderBarRange, RenderDialogPresenter), the file encoding (MidiExportService)
//   or the settings (MidiExportSettings).
// Tests: MidiExportRangeTests.
/// <summary>A resolved export: the performed timeline the range refers to and the tracks and song milliseconds to write.</summary>
public sealed record MidiExportSlice(IReadOnlySet<int> Tracks, double StartMs, double EndMs);

/// <summary>Turns the Export MIDI dialog's choices into a <see cref="MidiExportSlice"/>, exactly like Render maps its bounds.</summary>
public static class MidiExportRange
{
    /// <summary>The dialog's bounds index (0 whole song, 1 selected bars, 2 bar range, 3 time range) as a <see cref="RenderBounds"/>.</summary>
    public static RenderBounds ToBounds(int dialogBounds) => dialogBounds switch
    {
        1 => RenderBounds.Bars,
        2 => RenderBounds.CustomBars,
        3 => RenderBounds.Custom,
        _ => RenderBounds.Song,
    };

    /// <summary>Whether the dialog's bounds index can be picked (a selection needs a selection, a bar range needs bars).</summary>
    public static bool IsEnabled(int dialogBounds, bool hasSelection, int barCount) => RenderDialogPresenter.IsBoundsEnabled(
        ToBounds(dialogBounds) switch { RenderBounds.Bars => 2, RenderBounds.CustomBars => 4, RenderBounds.Custom => 3, _ => 0 },
        hasSelection, 1, barCount);

    /// <summary>The dialog's bounds index to show on load, moved to whole song when the song cannot use it.</summary>
    public static int BoundsOnLoad(int savedBounds, bool hasSelection, int barCount)
    {
        var bounds = Math.Clamp(savedBounds, 0, 3);
        return IsEnabled(bounds, hasSelection, barCount) ? bounds : 0;
    }

    /// <summary>A time typed in a dialog box: mm:ss (or m:ss) or plain seconds; a comma counts as the decimal point, text that is not a number reads as 0.</summary>
    public static double ParseSeconds(string text)
    {
        text = (text ?? "").Trim();
        var colon = text.IndexOf(':');
        if (colon >= 0 && double.TryParse(text.Substring(0, colon).Replace(',', '.'),
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var minutes)
            && RenderDialogPresenter.ParseNumber(text.Substring(colon + 1)) is var seconds && seconds >= 0)
            return Math.Max(0, minutes * 60 + seconds);
        return Math.Max(0, RenderDialogPresenter.ParseNumber(text));
    }

    /// <summary>Seconds shown in a dialog box as m:ss.</summary>
    public static string FormatSeconds(double seconds)
    {
        seconds = Math.Max(0, seconds);
        var whole = (int)Math.Floor(seconds);
        return $"{whole / 60}:{whole % 60:D2}";
    }

    /// <summary>The track indexes to write: the chosen names (audio never), in score order; null names means every non-audio track.</summary>
    public static HashSet<int> TrackIndexes(SongProject project, IEnumerable<string>? names)
    {
        var wanted = names?.ToHashSet(StringComparer.Ordinal);
        var indexes = new HashSet<int>();
        for (var i = 0; i < project.Tracks.Count; i++)
            if (!project.Tracks[i].IsAudio && (wanted is null || wanted.Contains(project.Tracks[i].Name))) indexes.Add(i);
        return indexes;
    }

    /// <summary>
    /// The performed timeline the range refers to and the slice to write. Selected bars use the white selection
    /// exactly like Render (the snipped copy compiled again); every other range maps source bars to performed
    /// passes through <see cref="RenderSpecBuilder.Bounds"/>, so repeats play as Render plays them.
    /// </summary>
    public static (ScoreTimeline Timeline, MidiExportSlice Slice) Resolve(SongProject project, MidiExportSettings settings,
        SelectionSnapshot? snip, (int StartBar, int StartCell, int EndBar, int EndCell)? selection = null)
    {
        var timeline = RenderSpecBuilder.Compile(project);
        var mode = ToBounds(settings.Bounds);
        var startBar = selection?.StartBar ?? 0; var startCell = selection?.StartCell ?? 0;
        var endBar = selection?.EndBar ?? 0; var endCell = selection?.EndCell ?? -1;
        if (mode == RenderBounds.CustomBars)
        {
            // The dialog's 1-based bars, like Render's custom-bars choice (same check through RenderBarRange).
            var last = Math.Max(0, BarRangeEditor.MaxMeasures(project) - 1);
            startBar = Math.Clamp(settings.CustomFromBar - 1, 0, last); startCell = 0;
            endBar = Math.Clamp(settings.CustomToBar - 1, 0, last); endCell = -1;
        }
        if (mode == RenderBounds.Bars && RenderSnip.Any(snip) && RenderSnip.Range(timeline, project, snip!) is { } snipRange)
        {
            var snipped = RenderSnip.Build(project, snip!);
            timeline = RenderSpecBuilder.Compile(snipped.Project);
            return (timeline, new MidiExportSlice(TrackIndexes(project, settings.TrackNames), snipRange.StartMs, snipRange.EndMs));
        }
        var (startMs, endMs) = RenderSpecBuilder.Bounds(timeline, mode, startBar, startCell, endBar, endCell,
            settings.CustomStartSec, settings.CustomEndSec);
        return (timeline, new MidiExportSlice(TrackIndexes(project, settings.TrackNames), startMs, endMs));
    }
}
