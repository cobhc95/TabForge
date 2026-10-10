namespace TabForge.KeyboardMode;

// Owns: the plain data of the note stream: one note (lane, MIDI key, time, length, hand, finger) and the source contract the falling-notes view reads.
// Does not own: how a source is built from a score (KeyboardNoteSource), which notes are in view (KeyboardModeWindow) or any drawing.
// Tests: TestKeyboardModeNoteStream.

/// <summary>One note of the stream. Times are timeline milliseconds, already scaled by the playback speed and laid out through repeats and tempo changes.</summary>
public sealed class KeyboardModeNote
{
    /// <summary>When the note's column sits (every note of one beat shares it, so a chord is a column).</summary>
    public double OnsetMs { get; init; }
    public double DurationMs { get; init; }
    /// <summary>The lane (the key minus the source's lowest key).</summary>
    public int StringIndex { get; init; }
    /// <summary>The MIDI key.</summary>
    public int Fret { get; init; }
    /// <summary>Played by the left hand (KeyboardHands decides).</summary>
    public bool LeftHand { get; init; }
    /// <summary>The finger written in the score, 1 (thumb) to 5 (little); 0 when none is written.</summary>
    public int Finger { get; init; }
    public double EndMs => OnsetMs + DurationMs;
}

/// <summary>Where the falling-notes view gets its notes.</summary>
public interface IKeyboardModeNoteSource
{
    /// <summary>How many lanes the view draws (one per key); lane 0 is the lowest key.</summary>
    int LaneCount { get; }
    /// <summary>The name written at the left of each lane (a C marks its octave).</summary>
    IReadOnlyList<string> LaneLabels { get; }
    /// <summary>The notes in onset order.</summary>
    IReadOnlyList<KeyboardModeNote> Notes { get; }
    /// <summary>The longest note length, so a window can reach back for a note that began earlier and still sounds.</summary>
    double MaxDurationMs { get; }
}

/// <summary>The loop of the playing song in timeline milliseconds.</summary>
public readonly record struct KeyboardModeLoop(double StartMs, double EndMs)
{
    public double Length => EndMs - StartMs;
    public bool IsUsable => Length >= 50;
}

/// <summary>A note placed on the unrolled timeline: with a loop, the passes after the first sit one loop length further on each.</summary>
public readonly record struct KeyboardModePlaced(KeyboardModeNote Note, double VirtualMs);
