using TabForge.Models;

namespace TabForge.Services;

/// <summary>One playing part of a clip: where it starts on the timeline, where in the source it reads from and how much of the source it plays.</summary>
public readonly record struct ClipPiece(double StartSec, double OffsetSec, double SourceLengthSec);

// Owns: the loop rule of a clip (a clip longer than its source repeats the source from the clip's offset) and the pieces it plays as.
// Does not own: the trim gestures (ClipGestureController), playback of the pieces (engine sync, MIDI compiler) or drawing (TrackTimeline.Waveform).
// Tests: TestClipEdgesAndLoops.
/// <summary>
/// A clip edge can be dragged past the end of its source: the part from the clip's offset to the end of the media then repeats inside the clip.
/// A clip that is not longer than its media is one piece, so everything that plays or draws clips stays on the same path.
/// </summary>
public static class ClipLoop
{
    /// <summary>The most pieces one clip plays as (a guard against a tiny source in a very long clip).</summary>
    public const int MaxPieces = 512;

    /// <summary>The media's length in seconds of the source: the file, a MIDI clip's own length, or (unknown) the part the clip held when it was made.</summary>
    public static double MediaLengthSec(AudioClip clip)
    {
        if (clip.FileLengthSec > 0) return clip.FileLengthSec;
        var end = clip.OffsetSec + clip.SourceLengthSec;
        if (clip.Notes is { } notes) foreach (var n in notes) end = Math.Max(end, n.StartSec + n.LengthSec);
        return end;
    }

    /// <summary>How much source one loop pass plays (media from the offset to its end).</summary>
    public static double PeriodSec(AudioClip clip) => Math.Max(0.05, MediaLengthSec(clip) - clip.OffsetSec);

    public static bool Loops(AudioClip clip) => clip.SourceLengthSec > PeriodSec(clip) + 1e-6;

    /// <summary>The clip as the parts it plays: one piece, or one per loop pass (the last one cut short).</summary>
    public static IReadOnlyList<ClipPiece> Pieces(AudioClip clip)
    {
        if (!Loops(clip)) return new[] { new ClipPiece(clip.StartSec, clip.OffsetSec, clip.SourceLengthSec) };
        var period = PeriodSec(clip);
        var speed = Math.Clamp(clip.Speed, 0.25, 4);
        var pieces = new List<ClipPiece>();
        for (var done = 0.0; done < clip.SourceLengthSec - 1e-6 && pieces.Count < MaxPieces; done += period)
            pieces.Add(new ClipPiece(clip.StartSec + done / speed, clip.OffsetSec, Math.Min(period, clip.SourceLengthSec - done)));
        return pieces;
    }
}
