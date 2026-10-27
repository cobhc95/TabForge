using System;
using System.Collections.Generic;
using System.IO;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: the landing a file drop previews on the arrangement timeline (the track or a new track, the lane, the span from the
//   measured lengths or the one-bar estimate, one ghost clip per file, the block's label and the "Bar 17.3 · 0:32.4" readout), so the timeline draws where the files would land before the drop.
// Does not own: the drag events and the measuring (MainWindowAudioDrop), the drawing (TimelineAudioClips), or the landed edit (AudioDropPlanner).
// Tests: TabForge.Avalonia.Tests.AudioDropPreviewTests.
public sealed record AudioFileDropPreview(TrackModel? Track, int Lane, double StartSec, double EndSec, bool Estimated, string Label, bool NewTrack,
    IReadOnlyList<AudioClip>? Parts = null, string Readout = "");

/// <summary>Plans the landing an audio-file drag previews: the same target and lane rule as the landed plan, with an estimated span until the files are measured.</summary>
internal static class AudioDropPreviewPlanner
{
    /// <summary>Short files still preview a visible block.</summary>
    private const double MinPreviewSeconds = 0.05;

    /// <summary>
    /// The preview for <paramref name="files"/> at <paramref name="startSec"/>: measured files at their real lengths, unmeasured ones
    /// (length 0 or less) at one bar there. Null when the target takes no drop.
    /// </summary>
    public static AudioFileDropPreview? Plan(SongProject song, AudioDropTarget target, double startSec, IReadOnlyList<(string File, double LengthSec)> files)
    {
        if (target.Kind == AudioDropTargetKind.None) return null;
        if (target.Kind == AudioDropTargetKind.ExistingTrack && target.Track is null) return null;
        var start = Normalise(startSec);
        var estimated = false;
        var at = start;
        // One ghost clip per file, one after another from the start, as the landed plan places them (never added to the song: the timeline folds and draws them).
        var parts = new List<AudioClip>(files.Count);
        foreach (var file in files)
        {
            var length = file.LengthSec > 0 ? file.LengthSec : EstimateBar(song, start);
            if (file.LengthSec <= 0) estimated = true;
            length = Math.Max(MinPreviewSeconds, length);
            parts.Add(new AudioClip { Name = Path.GetFileNameWithoutExtension(file.File) ?? "", StartSec = at, SourceLengthSec = length });
            at += length;
        }
        var lane = target.Kind == AudioDropTargetKind.NewTrack
            ? 0
            : target.Track!.AudioClips.Count == 0 ? 0 : ClipLanes.Count(target.Track);
        var label = files.Count == 1 ? Path.GetFileNameWithoutExtension(files[0].File) : $"{files.Count} files";
        return new AudioFileDropPreview(target.Track, lane, start, Math.Max(start + MinPreviewSeconds, at), estimated, label ?? "",
            target.Kind == AudioDropTargetKind.NewTrack, parts, AudioDropLanding.Readout(song, start));
    }

    /// <summary>The preview's length line: the real span, or "length on drop" while any file is still estimated.</summary>
    public static string Detail(AudioFileDropPreview preview) =>
        preview.Estimated ? "length on drop" : LengthText(preview.EndSec - preview.StartSec);

    /// <summary>How long a span reads on the preview block.</summary>
    public static string LengthText(double seconds) =>
        seconds < 60 ? $"{seconds:0.0} s" : $"{(int)(seconds / 60)}:{seconds % 60:00.0}";

    private static double EstimateBar(SongProject song, double startSec)
    {
        var (bar, _) = AudioClipTimelineRules.BarAtSeconds(song, startSec);
        if (bar >= AudioClipTimelineRules.BarCount(song)) return MinPreviewSeconds;
        return Math.Max(MinPreviewSeconds,
            AudioClipTimelineRules.SecondsAtBar(song, bar + 1, 0) - AudioClipTimelineRules.SecondsAtBar(song, bar, 0));
    }

    private static double Normalise(double sec) => double.IsNaN(sec) || double.IsInfinity(sec) || sec < 0 ? 0 : sec;
}
