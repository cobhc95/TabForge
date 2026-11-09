namespace TabForge.Sf2Synth.Humanize;

// Owns: every value of the humanizer: the evidence thresholds, the pulse search, and per instrument family the velocity, timing
//   and voice amounts (velocities in MIDI steps, times in ms, cents, fractions).
// Does not own: the code reading them (Evidence, Phrase, Nuance, Humanizer).
// Tests: HumanizerTests.
public sealed record Profile(double Accent, double Strong, double Off, double Arch, double Stroke, double JitterSlow, double JitterFast,
    double FastBias, double Drift, double Tilt, double Chug, double StrumMinMs, double StrumMaxMs, double LazyMs,
    double DetuneCents, double Bright, double Pick, double Release, double VibCents);

public static class HumanTable
{
    // Evidence: a window of recent notes; flat = few distinct velocities, small spread, long identical runs; grid = inter-onset
    // intervals in exact simple ratios (within 0.15 ms, which human timing almost never hits).
    public const int Window = 16, MinNotes = 6;
    public const double StdLo = 1.5, StdHi = 5, DistinctLo = 2.5, DistinctHi = 6, RunLo = 4, RunHi = 10;
    public const double SameMs = 0.15, GridLo = 0.4, GridHi = 0.8, Follow = 0.35, Snap = 0.03;

    // Pulse: candidates 0.12-1.5 s, preferred near 0.5 s (one octave wide); on-pulse within 12 %; a phrase restarts after a gap.
    public const double PulseMin = 0.12, PulseMax = 1.5, PulsePref = 0.5, OnPulse = 0.12, PhraseGap = 0.6, ArchPulses = 8;
    public const double FastIoi = 0.09, SlowIoi = 0.25, DownOnly = 0.3, ShortRatio = 0.7;

    // Limits: velocity change, the shortest a delayed note keeps, detune fall, vibrato onset and ramp, brightness of SF2 voices.
    public const int MaxDelta = 12;
    public const double MinKeepMs = 40, DetuneSeconds = 0.15, DetuneUntil = 1, VibOnset = 0.4, VibRamp = 0.3, VibLo = 4.5, VibHi = 6;
    public const double FcCentsPerBright = 1500;

    public static readonly Profile Plucked = new(Accent: 6, Strong: 3, Off: -2, Arch: 5, Stroke: 3, JitterSlow: 5, JitterFast: 2,
        FastBias: -4, Drift: 3, Tilt: 3, Chug: 0.5, StrumMinMs: 4, StrumMaxMs: 15, LazyMs: 6,
        DetuneCents: 3, Bright: 0.04, Pick: 0.08, Release: 0.15, VibCents: 5);

    public static readonly Profile Bowed = Plucked with { Stroke = 2, Tilt = 0, StrumMinMs = 2, StrumMaxMs = 6, LazyMs = 4, Pick = 0, VibCents = 0 };

    public static double MaxDelayMs => Math.Max(Plucked.StrumMaxMs + Plucked.LazyMs, Bowed.StrumMaxMs + Bowed.LazyMs);

    /// <summary>The family of a GM program; null = left untouched (structure for other families later).</summary>
    public static Profile? Of(int program) => program switch { >= 24 and <= 39 => Plucked, >= 40 and <= 51 => Bowed, _ => null };

    public static double Smooth(double lo, double hi, double x)
    {
        var t = Math.Clamp((x - lo) / (hi - lo), 0, 1);
        return t * t * (3 - 2 * t);
    }
}
