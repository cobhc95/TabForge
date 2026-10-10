namespace TabForge.KeyboardMode;

/// <summary>Which hand's notes the player practises: both, one with the other faded, or one with the other hidden.</summary>
public enum KeyboardHandsFilter { Both, Left, Right, LeftOnly, RightOnly }

// Owns: which hand plays each note of a source (the one call site of the hand-assignment engine, HandAssigner, run once per note source build; docs/KEYBOARD_HANDS.md) and the hands filter: which notes are judged,
//   which are faded and which are hidden.
// Does not own: the notes (KeyboardNoteSource), the colours (KeyboardColours) or the saved choice (KeyboardModeSettings.Hands).
// Tests: TestKeyboardModeKeyboardSource.
public static class KeyboardHands
{
    /// <summary>Middle C: a key held while nothing is due shows the right hand's colour from here up.</summary>
    public const int MiddleC = 60;
    /// <summary>The opacity of the other hand's notes when one hand is practised.</summary>
    public const double FadedOpacity = 0.28;

    public static readonly string[] FilterNames = { "Both hands", "Left hand", "Right hand", "Left hand only", "Right hand only" };

    /// <summary>True for each note played by the left hand (HandAssigner). <paramref name="notes"/> are (MIDI key, onset ms, length ms) in onset order.</summary>
    public static bool[] Assign(IReadOnlyList<(int Midi, double OnsetMs, double DurationMs)> notes)
    {
        var input = new Hands.HandNote[notes.Count];
        for (var i = 0; i < notes.Count; i++) input[i] = new Hands.HandNote(notes[i].Midi, (long)Math.Round(notes[i].OnsetMs), (long)Math.Round(notes[i].DurationMs));
        var hands = Hands.HandAssigner.Assign(input);
        var left = new bool[notes.Count];
        for (var i = 0; i < notes.Count; i++) left[i] = hands[i] == Hands.Hand.Left;
        return left;
    }

    public static KeyboardHandsFilter Parse(string? name)
    {
        var i = Array.IndexOf(FilterNames, name);
        return i < 0 ? KeyboardHandsFilter.Both : (KeyboardHandsFilter)i;
    }

    public static string NameOf(KeyboardHandsFilter filter) => FilterNames[(int)filter];

    /// <summary>True when the filter practises this hand (its notes are judged and awaited).</summary>
    public static bool Practises(KeyboardHandsFilter filter, bool leftHand) => filter switch
    {
        KeyboardHandsFilter.Left or KeyboardHandsFilter.LeftOnly => leftHand,
        KeyboardHandsFilter.Right or KeyboardHandsFilter.RightOnly => !leftHand,
        _ => true,
    };

    /// <summary>How a note of this hand shows: 1 in full, <see cref="FadedOpacity"/> faded, 0 hidden.</summary>
    public static double OpacityOf(KeyboardHandsFilter filter, bool leftHand) =>
        Practises(filter, leftHand) ? 1 : filter is KeyboardHandsFilter.LeftOnly or KeyboardHandsFilter.RightOnly ? 0 : FadedOpacity;
}
