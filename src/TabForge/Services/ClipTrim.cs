using TabForge.Models;

namespace TabForge.Services;

// Owns: the arithmetic of trimming a clip's edges from the state it had when the drag began.
// Does not own: the pointer, snapping and redraw (ClipGestureController) or the song growing to hold the clip (SongExtent).
// Tests: TestClipEdgesAndLoops.
/// <summary>
/// Non-destructive edge trims. The end edge has no upper limit (past the media it loops, see <see cref="ClipLoop"/>); the start edge keeps the
/// audio where it is: the offset grows by what is cut and the clip's start moves by the same amount.
/// </summary>
public static class ClipTrim
{
    /// <summary>The shortest a clip may be trimmed to, in seconds of source.</summary>
    public const double MinSourceSec = 0.05;
    /// <summary>The longest a clip may be, in seconds of source (the project validator's limit).</summary>
    public const double MaxSourceSec = 86_400;

    /// <summary>Moves the end so the clip ends at <paramref name="endSec"/> on the timeline.</summary>
    public static void TrimEnd(AudioClip clip, double originStart, double endSec) =>
        clip.SourceLengthSec = Math.Clamp((endSec - originStart) * Math.Clamp(clip.Speed, 0.25, 4), MinSourceSec, MaxSourceSec);

    /// <summary>Moves the start to <paramref name="startSec"/> (from the clip as it was: start, offset, source length); the audio stays in place.</summary>
    public static void TrimStart(AudioClip clip, (double Start, double Offset, double Length) origin, double startSec)
    {
        var speed = Math.Clamp(clip.Speed, 0.25, 4);
        var fileDelta = Math.Clamp((startSec - origin.Start) * speed, -origin.Offset, origin.Length - MinSourceSec);
        if (origin.Start + fileDelta / speed < 0) fileDelta = -origin.Start * speed;
        clip.OffsetSec = origin.Offset + fileDelta;
        clip.SourceLengthSec = origin.Length - fileDelta;
        clip.StartSec = origin.Start + fileDelta / speed;
    }
}
