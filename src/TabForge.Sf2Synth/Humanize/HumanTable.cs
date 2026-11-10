namespace TabForge.Sf2Synth.Humanize;

// Owns: every value of the humanizer (docs/HUMAN_FEEL_SPEC.md): window, evidence thresholds, the structural level parts (dB),
//   the timing parts (ms), the fast-run model, note lengths, strum, voice amounts, the three feel styles and the instrument families
//   (guitars/basses/bowed plus drums: per-kit-piece velocity ceilings and the backbeat/crash rules).
// Does not own: the code reading them (Humanizer, Dynamics, Timing, Evidence, Strum, Noise, Nuance).
// Tests: HumanizerTests.

/// <summary>A feel preset: scale of level (KDb) and timing (KMs) parts, strum ms, bias ms (+ = late), share of smoothly odd notes.</summary>
public sealed record Style(double KDb, double KMs, double StrumMs, double BiasMs, double OddRate);

/// <summary>An instrument family: strum factor (0 = none), timing and level scale, own bias, finger alternation dB, voice amounts.</summary>
public sealed record Family(double Strum, double TimeK, double LevelK, double BiasMs, double AltDb,
    double DetuneCents, double Bright, double Pick, double Release, double VibCents);

public static class HumanTable
{
    // Window in bars around the bar being played (features never look further), and the motif grid (positions per beat).
    public const int Before = 2, After = 4, Grid = 48;

    // Evidence over the window: velocity human = distinct AND (spread OR short runs); timing human = few onsets on the written grid.
    public const int MinNotes = 6, FullWindow = 16;
    public const double StdLo = 3, StdHi = 6, DistinctLo = 4, DistinctHi = 7, RunLo = 3, RunHi = 6, GridMs = 0.5, GridLo = 0.3, GridHi = 0.6;

    // Level (dB), all structural: meter, syncopation, length, melodic contour, phrase arch, energy, hand, run group, motif memory.
    public const double DownDb = 6, MidDb = 3.5, BeatDb = 2.5, EighthDb = -3.5, SixteenthDb = -6, RunMeterK = 0.35, SyncDb = 3;
    public const double LenDbPerOct = 1, PeakDb = 2.4, RiseDb = 1.8, ArchDb = 3, ArchBars = 4, EnergyDb = 1.2, EnergyRef = 2;
    public const double CrossDb = -0.5, UpDb = -1.4, GroupDb = 2.4, MotifDb = 1.8, LegatoDb = 1.8, SoftK = 0.5, ChugK = 0.5;
    // Residue: the only free part, slow and smooth (octaves of value noise over bars), bounded to +-ResidueDb.
    public const double ResidueDb = 1, KneeDb = 8, Ratio = 3, CapDb = 12;
    // Headroom: a flat loud input is lowered so an accent of this many dB still fits under velocity 127 (peaks above, not only dips).
    public const double HeadDb = 9;

    // Timing (ms): wander (slow, bars 1..8), micro (smooth, 1/8..1/2 bar), jitter, motif; fast runs below FastMs.
    public const double WanderMs = 6, MicroMs = 4, JitMs = 3, MotifMs = 3, FastMs = 250, ShiftCapMs = 30, IoiShare = 0.3;
    public const double GroupMs = -2, UpMs = 1.5, CrossMs = 1, CrossStepMs = 1.5, RushMs = 3, LegatoMs = 2, OddMs = 9;

    // Note lengths: change of the length (share, capped), shortest kept, gap before the same key again.
    public const double LenFrac = 0.12, LenMaxMs = 15, MinKeepMs = 20, RetrigMs = 9;

    // Strum: spread x dynamics (pp / mf / ff in dB re 127), up x, at most a share of the interval; gaps first..last, jitter, cap.
    public const double PpDb = -24, MfDb = -6, FfDb = 0, PpX = 1.4, FfX = 0.7, UpX = 0.8, IoiFrac = 0.3, StrumCapMs = 100;
    public const double BiasStrumPerMs = 0.025, GapFirst = 1.3, GapLast = 0.8, GapJit = 0.2, GapMaxMs = 39;
    public const double FirstDb = 2, LastDb = -2, StringDb = 1, UpLowDb = -6, MissChance = 0.3, MissDb = -20, InnerChance = 0.08, InnerDb = -8;

    // Drum family (channel 9, GM bank 128): velocity ceilings per kit piece (crash always below snare), scaled by Feel KDb.
    //   Snare 2&4 boost, fill backbeat, ghost cut, crash phrase fatigue, double-crash and choke rules, all in dB/ms at Natural.
    public const int DrumProgram = 128, DrumSnareCeil = 118, DrumCrashCeil = 95, DrumKickCeil = 110, DrumHatCeil = 100, DrumHatOffCeil = 85, DrumTomCeil = 105;
    public const double DrumSnareBackbeatLoDb = 4, DrumSnareBackbeatHiDb = 7, DrumSnareFillDb = 3, DrumGhostCutDb = -6, DrumGhostVel = 45;
    public const double DrumCrashBar1Db = 2, DrumCrashFatigueDb = -1.5, DrumCrashChokeMs = 15, DrumDoubleCutDb = -5, DrumDoubleGapMs = 80, DrumDoubleLooseMs = -12;
    public const double DrumBarAccentDb = 1.5, DrumOpenLenMs = 2, DrumOpenMs = 250, DrumChokeK = 0.5;
    public const double DetuneSeconds = 0.15, DetuneUntil = 1, VibOnset = 0.4, VibRamp = 0.3, VibLo = 4.5, VibHi = 6, FcCentsPerBright = 1500;

    /// <summary>Tight, Natural, Loose (index = <see cref="Feel"/>).</summary>
    public static readonly Style[] Styles =
    [
        new(KDb: 0.65, KMs: 0.6, StrumMs: 14, BiasMs: -2, OddRate: 0.005),
        new(KDb: 1, KMs: 1, StrumMs: 25, BiasMs: 0, OddRate: 0.015),
        new(KDb: 1.5, KMs: 1.5, StrumMs: 45, BiasMs: 8, OddRate: 0.02),
    ];

    public static readonly Family Guitar = new(Strum: 1, TimeK: 1, LevelK: 1, BiasMs: 0, AltDb: 0,
        DetuneCents: 3, Bright: 0.04, Pick: 0.08, Release: 0.15, VibCents: 5);
    public static readonly Family Nylon = Guitar with { Strum = 1.5, LevelK = 1.2 };
    public static readonly Family Bass = Guitar with { Strum = 0, TimeK = 0.8, BiasMs = 3, AltDb = 0.5 };
    public static readonly Family Bowed = Guitar with { Strum = 0.25, Pick = 0, VibCents = 0 };
    public static readonly Family Drums = Guitar with { Strum = 0, TimeK = 0.7, LevelK = 1, BiasMs = 0, AltDb = 0, DetuneCents = 0, Bright = 0, Pick = 0, Release = 0, VibCents = 0 };

    /// <summary>The family of a GM program; null = left untouched. Drums are <see cref="DrumProgram"/> (bank 128, channel 9).</summary>
    public static Family? Of(int program) => program switch
    {
        >= 128 => Drums, 24 => Nylon, >= 25 and <= 31 => Guitar, >= 32 and <= 39 => Bass, >= 40 and <= 51 => Bowed, _ => null,
    };

    public static double Smooth(double lo, double hi, double x)
    {
        var t = Math.Clamp((x - lo) / (hi - lo), 0, 1);
        return t * t * (3 - 2 * t);
    }
}
