namespace TabForge.Presenters;

// Owns: one note as the piano roll edits it (start and length in quarter-note beats on the piano roll's own clock, pitch, velocity, mute), the
//       piano roll's tools and the two kinds of edit (the tab prompt asks only for the first kind).
// Does not own: where a note is stored (PianoRollClipSource, PianoRollTabSource) or the clock (PianoRollTimeMap).
// Tests: TabForge.Avalonia.Tests.PianoRollEditTests.

/// <summary>A piano-roll note: <paramref name="Start"/> and <paramref name="Length"/> in quarter-note beats from the piano roll's first bar.
/// <paramref name="Voice"/> is the tab voice (0 or 1) of a tab track's note; <paramref name="Channel"/> a MIDI note's own channel (-1: the track's).</summary>
public readonly record struct PianoRollNote(double Start, double Length, int Pitch, int Velocity, bool Muted = false, int Voice = 0, int Channel = -1)
{
    /// <summary>Where the note stops, in beats.</summary>
    public double End => Start + Length;
}

/// <summary>The piano roll's tools (keys 1 to 6 while it has focus).</summary>
public enum PianoRollTool
{
    Select,
    Draw,
    Erase,
    Split,
    Mute,
    Glue,
    /// <summary>Retired 26 Oct (tombstone): no button or key picks it any more; Alt-drag a note or drag its velocity bar instead.</summary>
    Velocity,
    /// <summary>The Select tool (V; the arrow): selects, moves and resizes notes and writes nothing. <see cref="Select"/> stays the Write tool (the pencil); the old values keep their numbers.</summary>
    Pointer,
}

/// <summary>What an edit changes: <see cref="Notes"/> (pitch, timing, length, add, delete: rewrites a tab track's tab, so it asks once per visit) or
/// <see cref="Expression"/> (velocity, mute: never asks).</summary>
public enum PianoRollEditKind
{
    Notes,
    Expression,
}
