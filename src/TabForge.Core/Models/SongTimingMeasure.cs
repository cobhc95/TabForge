namespace TabForge.Models;

// Owns: one song's measured timing as the timing cache keeps it: the performed end, one more bar's length, and every bar in the order it is performed.
// Does not own: the measuring (SongExtent.Timing), the cache key and its ceiling (SongExtentMeasureCache), the bar-to-seconds rules (AudioClipTimelineRules).

/// <summary>A bar as the song performs it: its source bar index and its place on the performed clock, in song milliseconds (repeats and jumps applied).
/// <paramref name="EndingMask"/> is the alternate-ending mask of the source bar (bit n = pass n+1, 0 = not an ending), as PlaybackOrder reads it.</summary>
public readonly record struct PerformedBar(int Bar, double StartMs, double EndMs, int EndingMask = 0);

/// <summary>The measured timing of one song.</summary>
/// <param name="EndSec">The end of the song as performed, in seconds.</param>
/// <param name="BarSec">The length of one more bar (last bar's time signature, tempo in effect at the end).</param>
/// <param name="Performed">Every performed bar, in playing order.</param>
public sealed record SongTimingMeasure(double EndSec, double BarSec, PerformedBar[] Performed);
