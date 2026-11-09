namespace TabForge.Sf2Synth.Humanize;

// Owns: every value of the humanizer (docs/specs/HUMAN_FEEL_SPEC.md): evidence thresholds, pulse search, the level parts (dB),
//   timing parts (ms), strum, note lengths and voice amounts, the three feel styles and the instrument families.
// Does not own: the code reading them (Evidence, Phrase, Strum, Noise, Nuance, Humanizer).
// Tests: HumanizerTests.

/// <summary>A feel preset: scale of level (KDb) and timing (KMs) parts, ghost rate, upstroke dB, strum ms, look-ahead ms, bias ms (+ = late).</summary>
public sealed record Style(double KDb, double KMs, double Ghost, double UpDb, double StrumMs, double LookMs, double BiasMs);

/// <summary>An instrument family: strum factor (0 = none), timing and level scale, own bias, finger alternation dB, voice amounts.</summary>
public sealed record Family(double Strum, double TimeK, double LevelK, double BiasMs, double AltDb,
    double DetuneCents, double Bright, double Pick, double Release, double VibCents);

public static class HumanTable
{
    // Evidence over 16 notes: velocity human = distinct AND (spread OR short runs); timing human = few intervals in exact ratios.
    public const int Window = 16, MinNotes = 6;
    public const double StdLo = 3, StdHi = 6, DistinctLo = 4, DistinctHi = 7, RunLo = 3, RunHi = 6;
    public const double SameMs = 0.1, GridLo = 0.3, GridHi = 0.6, Follow = 0.5, Snap = 0.03;

    // Pulse 0.12-1.5 s, preferred near 0.5 s; a bar = 16 slots of a quarter pulse; a phrase restarts after a gap.
    public const double PulseMin = 0.12, PulseMax = 1.5, PulsePref = 0.5, UnitTol = 0.03, PhraseGap = 0.6, ShortRatio = 0.7;
    public const int Slots = 16, PinkStages = 9;

    // Level (dB): metric accent, bar pattern (unit draw x PatDb, kept bar to bar with PatDbRho), arch, 1/f, white, speed, ceiling.
    public const double DownDb = 2.5, BeatDb = 1, EighthDb = -1.5, SixteenthDb = -3;
    public const double PatDb = 2.8, PatDbRho = 0.95, ArchDb = 1.75, ArchBars = 4, PinkDb = 1.6, PinkDbSlope = 1.2, WhiteDb = 1.1;
    public const double FastIoi = 0.09, SlowIoi = 0.25, GapIoi = 0.4, FastDb = -2, GapDb = 1;
    public const double KneeDb = 4, Ratio = 3, CapDb = 10, GhostDb = -12, ChugDb = 0.8, BiasDbPerMs = -0.125;

    // Timing (ms): bar pattern, 1/f, anticorrelated jitter, tempo drift (fraction, bounded error), note-length change.
    public const double PatMs = 8, PatMsRho = 0.9, AntiPat = 0.6, PinkMs = 5, PinkMsSlope = 1, JitMs = 6.5, AntiJit = 0.6;
    public const double FastJitIoi = 0.12, FastJit = 0.7, ChugJit = 0.8;
    public const double TempoStep = 0.0006, TempoMax = 0.01, TempoSec = 8, DriftMs = 20, DriftSec = 2;
    public const double LenFrac = 0.12, LenMaxMs = 30, MinKeepMs = 40;

    // Strum: spread x dynamics (pp / mf / ff in dB re 127), up x, at most a share of the interval; gaps first..last, jitter, cap.
    public const double PpDb = -24, MfDb = -6, FfDb = 0, PpX = 1.4, FfX = 0.7, UpX = 0.8, IoiFrac = 0.3, StrumCapMs = 100;
    public const double BiasStrumPerMs = 0.025, GapFirst = 1.3, GapLast = 0.8, GapJit = 0.2, GapMaxMs = 39;
    public const double FirstDb = 1, LastDb = -1, StringDb = 1, UpLowDb = -6, MissChance = 0.3, MissDb = -20, InnerChance = 0.08, InnerDb = -8;

    // Voice: detune fall, vibrato onset and ramp, brightness of SF2 voices.
    public const double DetuneSeconds = 0.15, DetuneUntil = 1, VibOnset = 0.4, VibRamp = 0.3, VibLo = 4.5, VibHi = 6, FcCentsPerBright = 1500;

    /// <summary>Tight, Natural, Loose (index = <see cref="Feel"/>).</summary>
    public static readonly Style[] Styles =
    [
        new(KDb: 0.62, KMs: 0.58, Ghost: 0.04, UpDb: -0.5, StrumMs: 14, LookMs: 20, BiasMs: -3),
        new(KDb: 1, KMs: 1, Ghost: 0.08, UpDb: -1, StrumMs: 25, LookMs: 35, BiasMs: 0),
        new(KDb: 1.65, KMs: 1.65, Ghost: 0.15, UpDb: -2, StrumMs: 45, LookMs: 50, BiasMs: 8),
    ];

    public static readonly Family Guitar = new(Strum: 1, TimeK: 1, LevelK: 1, BiasMs: 0, AltDb: 0,
        DetuneCents: 3, Bright: 0.04, Pick: 0.08, Release: 0.15, VibCents: 5);
    public static readonly Family Nylon = Guitar with { Strum = 1.5, LevelK = 1.2 };
    public static readonly Family Bass = Guitar with { Strum = 0, TimeK = 0.8, BiasMs = 3, AltDb = 0.5 };
    public static readonly Family Bowed = Guitar with { Strum = 0.25, Pick = 0, VibCents = 0 };

    public static double MaxLookMs => Styles.Max(s => s.LookMs);

    /// <summary>The family of a GM program; null = left untouched.</summary>
    public static Family? Of(int program) => program switch
    {
        24 => Nylon, >= 25 and <= 31 => Guitar, >= 32 and <= 39 => Bass, >= 40 and <= 51 => Bowed, _ => null,
    };

    public static double Smooth(double lo, double hi, double x)
    {
        var t = Math.Clamp((x - lo) / (hi - lo), 0, 1);
        return t * t * (3 - 2 * t);
    }
}
