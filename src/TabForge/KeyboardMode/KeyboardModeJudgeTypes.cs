namespace TabForge.KeyboardMode;

// Owns: the plain data of the Keyboard mode judge: tolerance windows, an expected note, a played event and the result of one expected note. Times are song seconds.
// Does not own: the matching rules (KeyboardModeJudge), the totals (KeyboardModeScore) or where the notes and events come from.
// Tests: TestKeyboardModeJudge.

/// <summary>How an expected note was played. A hit within the perfect window is Perfect, a hit within the good window is Good (early or late by the sign of its offset).</summary>
public enum KeyboardModeGrade { Pending, Perfect, Good, Miss }

/// <summary>A note the player should play: MIDI key, onset and length in song seconds.</summary>
public readonly record struct KeyboardModeExpected(int Midi, double OnsetSec, double DurationSec);

/// <summary>One key event of the player. <see cref="TimeSec"/> is song time after latency compensation (<see cref="KeyboardModeJudge.Compensate"/>); NaN means the clock could not tell (paused or stopped).</summary>
public readonly record struct KeyboardModePlayed(int Midi, bool On, double TimeSec);

/// <summary>The windows and thresholds of the judge. Values are in milliseconds unless the name says otherwise.</summary>
public sealed record KeyboardModeTolerances
{
    /// <summary>A press within this distance of the onset is Perfect (inclusive).</summary>
    public double PerfectMs { get; init; } = 60;
    /// <summary>A press within this distance is a hit (Good); farther is no hit (inclusive).</summary>
    public double GoodMs { get; init; } = 150;
    /// <summary>Expected notes whose onsets lie within this of the chord's first note are one chord.</summary>
    public double ChordMs { get; init; } = 40;
    /// <summary>A note counts as sustained when the key is held this fraction of its length.</summary>
    public double SustainFraction { get; init; } = 0.6;
    /// <summary>Notes shorter than this are never judged on sustain (a key cannot be held for them).</summary>
    public double MinSustainMs { get; init; } = 120;

    public static KeyboardModeTolerances Default { get; } = new();
    /// <summary>Strict (60 / 150 ms), Normal (100 / 220 ms) or Relaxed (150 / 300 ms, early and late alike); anything else is Relaxed.</summary>
    public static KeyboardModeTolerances Of(string? name) => name switch
    {
        KeyboardModeSettings.TimingStrict => Default,
        KeyboardModeSettings.TimingNormal => Normal,
        _ => Relaxed,
    };
    private static readonly KeyboardModeTolerances Normal = new() { PerfectMs = 100, GoodMs = 220 };
    private static readonly KeyboardModeTolerances Relaxed = new() { PerfectMs = 150, GoodMs = 300 };
}

/// <summary>The outcome of one expected note. Mutable only inside the judge.</summary>
public sealed class KeyboardModeResult
{
    internal KeyboardModeResult(int index, KeyboardModeExpected expected, int group) { Index = index; Expected = expected; Group = group; }

    /// <summary>Position in the judge's onset-sorted list.</summary>
    public int Index { get; }
    public KeyboardModeExpected Expected { get; }
    /// <summary>Which chord (group of notes within the chord window) the note belongs to; a single note is its own group.</summary>
    public int Group { get; }
    public KeyboardModeGrade Grade { get; internal set; } = KeyboardModeGrade.Pending;
    /// <summary>Played onset minus expected onset in ms (negative is early); NaN on a miss.</summary>
    public double OffsetMs { get; internal set; } = double.NaN;
    public double PlayedOnSec { get; internal set; } = double.NaN;
    /// <summary>How long the key was held, in seconds; NaN until released (or the judge finished).</summary>
    public double HeldSec { get; internal set; } = double.NaN;
    /// <summary>Null until the key is released; then whether it was held long enough (always true for a short note).</summary>
    public bool? Sustained { get; internal set; }

    public bool IsResolved => Grade != KeyboardModeGrade.Pending;
    public bool IsHit => Grade is KeyboardModeGrade.Perfect or KeyboardModeGrade.Good;
    public bool IsEarly => Grade == KeyboardModeGrade.Good && OffsetMs < 0;
    public bool IsLate => Grade == KeyboardModeGrade.Good && OffsetMs > 0;
}
