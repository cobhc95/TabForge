using TabForge.Models;

// Owns: the clip gesture rules that need no toolkit types: which gesture a press on a clip starts (body, trim edges, fade handles), the
//       drag threshold, the nearest snap candidate, the lane and zone under a dragged clip, the notation-row drop rule, restoring a
//       cancelled trim, and the waveform's visible span and peak range per pixel column. Every method takes plain numbers and
//       allocates nothing.
// Does not own: pointer capture, hit lists, drawing or snapping targets (TrackTimeline, ClipGestureController); the clip edit itself
//       (DocumentEdits.Run via ClipEditController).
// Tests: no dedicated test class.
namespace TabForge.Presenters;

/// <summary>What a clip edit gesture is doing.</summary>
public enum ClipGesture { Move, TrimStart, TrimEnd, FadeIn, FadeOut }

public static class ClipGesturePresenter
{
    /// <summary>The gesture a press at <paramref name="x"/> starts on a clip drawn from <paramref name="left"/> to <paramref name="right"/>: an edge grip (at most a third of the clip) trims, the body moves.</summary>
    public static ClipGesture BodyGesture(double x, double left, double right, double edgeGrip)
    {
        var width = right - left;
        if (!(width > 0)) return ClipGesture.Move;
        // On a clip too narrow for two full grips, each grip takes one third: the middle third always stays a move.
        var grip = width < 3 * edgeGrip ? width / 3 : edgeGrip;
        return x <= left + grip ? ClipGesture.TrimStart : x >= right - grip ? ClipGesture.TrimEnd : ClipGesture.Move;
    }

    /// <summary>True when a press at y is within the fade handle band (<paramref name="laneTopPlusPad"/> is the clip's top edge).</summary>
    public static bool InFadeBand(double y, double laneTopPlusPad, double handleSize) => y >= laneTopPlusPad - 2 && y <= laneTopPlusPad + handleSize + 3;

    /// <summary>The fade handle under x (within one handle size of the nearer handle), or null. The fade-in wins a tie.</summary>
    public static ClipGesture? FadeHandleAt(double x, double fadeInX, double fadeOutX, double handleSize)
    {
        var nearIn = Math.Abs(x - fadeInX);
        var nearOut = Math.Abs(x - fadeOutX);
        if (Math.Min(nearIn, nearOut) > handleSize) return null;
        return nearIn <= nearOut ? ClipGesture.FadeIn : ClipGesture.FadeOut;
    }

    /// <summary>True when the pointer is still inside the click jitter around the press point (the gesture has not started).</summary>
    public static bool WithinClickJitter(double dx, double dy, double thresholdX, double thresholdY) => Math.Abs(dx) < thresholdX && Math.Abs(dy) < thresholdY;

    /// <summary>Keeps the nearest snap candidate within its limit: a candidate at <paramref name="distancePx"/> from the pointer replaces the best when it is within <paramref name="limitPx"/> and strictly nearer.</summary>
    public static void ConsiderSnap(ref double best, ref double bestDistancePx, double candidate, double distancePx, double limitPx)
    {
        if (distancePx <= limitPx && distancePx < bestDistancePx) { best = candidate; bestDistancePx = distancePx; }
    }

    /// <summary>The zone under a dragged clip's centre: -1 above the lanes (the notation row), else the lane index clamped to the last lane. <paramref name="lane"/> is 0 in the notation row.</summary>
    public static int MoveZone(double offsetFromFirstLane, int lanes, double laneHeight, out int lane)
    {
        if (offsetFromFirstLane < 0) { lane = 0; return -1; }
        lane = Math.Min((int)(offsetFromFirstLane / laneHeight), Math.Max(0, lanes - 1));
        return lane;
    }

    /// <summary>A MIDI clip on a track's notation row is written into the score: the row is above the lanes, belongs to a track with lanes that is not an audio track, and the pointer is within the notation height (a group header above does not count).</summary>
    public static bool OnNotationRow(double offsetFromFirstLane, bool clipIsMidi, int lanes, bool trackIsAudio, double notationHeight) =>
        offsetFromFirstLane < 0 && clipIsMidi && lanes > 0 && !trackIsAudio && offsetFromFirstLane >= -notationHeight;

    /// <summary>Where a moved clip starts: its start at the press plus the drag, never before the song's start.</summary>
    public static double MovedStart(double originStart, double deltaSec) => Math.Max(0, originStart + deltaSec);

    /// <summary>Puts a cancelled trim or fade drag back: the clip's start, offset, length and fades as they were at the press.</summary>
    public static void RestoreOrigin(AudioClip clip, double start, double offset, double length, double fadeIn, double fadeOut)
    {
        clip.StartSec = start;
        clip.OffsetSec = offset;
        clip.SourceLengthSec = length;
        clip.FadeInSec = fadeIn;
        clip.FadeOutSec = fadeOut;
    }

    /// <summary>
    /// The pixel columns of a clip's waveform to build: the clip box clipped to the song width and, with a known viewport, to one
    /// viewport either side of the visible span. <see cref="WaveSpan.RebuildFrom"/> and <see cref="WaveSpan.RebuildTo"/> are the
    /// visible edges beyond which the cached outline is stale (infinite when that side of the clip is fully built).
    /// </summary>
    public static WaveSpan WaveSpanOf(double boxX, double boxRight, double songWidth, double viewFrom, double viewWidth)
    {
        var from = Math.Floor(Math.Max(boxX, 0));
        var to = Math.Min(boxRight, songWidth);
        var rebuildFrom = double.NaN; var rebuildTo = double.NaN;
        if (viewWidth > 0)
        {
            from = Math.Max(from, Math.Floor(viewFrom - viewWidth));
            to = Math.Min(to, Math.Ceiling(viewFrom + 2 * viewWidth));
            rebuildFrom = from <= Math.Max(boxX, 0) ? double.NegativeInfinity : from + viewWidth;
            rebuildTo = to >= Math.Min(boxRight, songWidth) ? double.PositiveInfinity : to - viewWidth;
        }
        return new WaveSpan(from, to, rebuildFrom, rebuildTo);
    }

    /// <summary>The peak index range [First, End) covering pixel column <paramref name="x"/> of a clip box (a looping clip repeats the media past its end).</summary>
    public static (int First, int End) PeakRange(double x, double boxX, double boxWidth, double sourceLengthSec, double offsetSec,
        bool loops, double periodSec, double secondsPerPeak, int peakCount)
    {
        var columnStart = (x - boxX) / boxWidth * sourceLengthSec;
        var fileFrom = columnStart;
        var fileTo = (x + 1 - boxX) / boxWidth * sourceLengthSec;
        if (loops)
        {
            fileFrom %= periodSec;
            fileTo = fileFrom + (fileTo - columnStart);
        }
        fileFrom += offsetSec; fileTo += offsetSec;
        var first = Math.Max(0, (int)(fileFrom / secondsPerPeak));
        var end = Math.Min(peakCount, Math.Max(first + 1, (int)Math.Ceiling(fileTo / secondsPerPeak)));
        return (first, end);
    }
}

/// <summary>The columns of a waveform to build, and the visible edges that make the built outline stale (NaN: no viewport known).</summary>
public readonly record struct WaveSpan(double From, double To, double RebuildFrom, double RebuildTo);
