namespace TabForge.Services;

// Owns: splitting a recording take into the lane segments (song start, file offset, length) it lands as.
// Does not own: capture and the clip drawing.
// Tests: TestClips.
/// <summary>Loop recording: one continuous recording cut into the passes (takes) of the loop.</summary>
public static class RecordingPasses
{
    /// <summary>
    /// A recording that began at <paramref name="startSec"/> (song time) and lasted <paramref name="lengthSec"/>, made while
    /// looping [loop.Start, loop.End): the first pass runs from the start to the loop end, every later pass covers the whole loop.
    /// Each pass is (where it sits on the song timeline, where it starts in the recorded file, its length).
    /// </summary>
    public static List<(double SongStart, double FileOffset, double Length)> Split(double startSec, double lengthSec, (double Start, double End)? loop)
    {
        var result = new List<(double, double, double)>();
        if (loop is not { } range || startSec >= range.End - 0.01 || range.End - range.Start < 0.05)
        {
            result.Add((startSec, 0, lengthSec));
            return result;
        }
        var loopLength = range.End - range.Start;
        var first = Math.Min(lengthSec, range.End - startSec);
        result.Add((startSec, 0, first));
        for (var offset = first; offset < lengthSec - 0.05; offset += loopLength)
            result.Add((range.Start, offset, Math.Min(loopLength, lengthSec - offset)));
        return result;
    }
}
