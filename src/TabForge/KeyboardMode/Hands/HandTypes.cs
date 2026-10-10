namespace TabForge.KeyboardMode.Hands;

// Owns: the hand-assignment value types: the hand, a note as the assigner sees it, and the tuning constants of the cost model with their fitted defaults.
// Does not own: the assignment itself (HandAssigner) or where the notes come from (KeyboardNoteSource).
// Tests: TestKeyboardHands, TestKeyboardHandsAccuracy.

public enum Hand { Left, Right }

/// <summary>One note: MIDI pitch, onset and length in milliseconds.</summary>
public readonly record struct HandNote(int Pitch, long StartMs, long DurationMs);

/// <summary>
/// The cost model's constants (pitches in semitones, times in milliseconds). The defaults were fitted by coordinate descent
/// against the evaluation set (HandEvalSet in the self-tests, described in docs/KEYBOARD_HANDS.md); change them, that page and the accuracy test together.
/// </summary>
public sealed record HandAssignerOptions
{
    public static HandAssignerOptions Default { get; } = new();

    /// <summary>Notes starting within this of the first note of a group are one chord.</summary>
    public long ChordToleranceMs { get; init; } = 30;
    /// <summary>A chord in one hand wider than this costs ChordSpanWeight per semitone squared plus ChordStretchWeight per semitone.</summary>
    public int ChordSpan { get; init; } = 12;
    public double ChordSpanWeight { get; init; } = 4;
    public double ChordStretchWeight { get; init; } = 0.5;
    /// <summary>More notes than this in one hand at once cost 100 each.</summary>
    public int MaxNotesPerHand { get; init; } = 5;
    /// <summary>A hand still holding its last chord: the held and new notes wider than this cost HeldWeight per semitone squared (a busy hand passes notes to the other).</summary>
    public int HeldSpan { get; init; } = 2;
    public double HeldWeight { get; init; } = 2;
    /// <summary>The hand's last two chords that started within RecentMs and the new notes wider than RecentSpan cost RecentWeight per semitone (fast figures stay within a hand's reach).</summary>
    public long RecentMs { get; init; } = 400;
    public int RecentSpan { get; init; } = 12;
    public double RecentWeight { get; init; } = 2;
    /// <summary>A move of the hand's centre beyond MoveFree semitones costs MoveWeight per semitone, scaled by MoveTauMs / (MoveTauMs + time since the hand's last onset).</summary>
    public int MoveFree { get; init; } = 12;
    public double MoveWeight { get; init; } = 0.1;
    public double MoveTauMs { get; init; } = 100;
    /// <summary>Register prior per note: left above LeftTop and right below RightBottom cost RegisterWeight per octave; a gentle RegisterSlope per octave from middle C breaks ties.</summary>
    public double RegisterWeight { get; init; } = 1;
    public int LeftTop { get; init; } = 64;
    public int RightBottom { get; init; } = 60;
    public double RegisterSlope { get; init; } = 0.1;
    /// <summary>The left hand's centre above the right's costs CrossWeight per semitone.</summary>
    public double CrossWeight { get; init; } = 3;
    /// <summary>A note within LineMax semitones of the nearest note of the previous chord (less than LineWindowMs before) but in the other hand costs up to LineWeight.</summary>
    public int LineMax { get; init; } = 3;
    public double LineWeight { get; init; } = 2;
    public long LineWindowMs { get; init; } = 1600;
    /// <summary>Where the hands start: C3 and C5.</summary>
    public int LeftHome { get; init; } = 48;
    public int RightHome { get; init; } = 72;
}
