using TabForge.Sf2Synth.Dsp;
using static TabForge.Sf2Synth.Dsp.EqKind;

namespace TabForge.Sf2Synth.Guitar;

// Owns: every sound value of the guitar layer, per GM program 24-37: the string and the rig behind it.
// Does not own: the DSP reading them (PluckedString, GuitarBus, StageAmp, CabinetImpulse).
// Tests: GuitarLayerTests.

internal enum CabKind { None, Guitar, Bass }

// T60 (s) at RefHz and 1 kHz (log-log between); Darkness = loop low-pass pole; positions = share of string length from the bridge
// (pickup 0 = acoustic, no comb); Stiffness = dispersion all-pass coefficient (<0: partials run sharp); PluckHz = pluck low-pass at full velocity.
internal sealed record StringSpec(double RefHz, double T60Ref, double T60At1k, double Darkness, double NoiseMix, double PickPos,
    double PickupPos, double Stiffness, double ReleaseT60, int MaxStrings, double PluckHz, bool Harmonic = false);

// One tube stage: gain, grid bias (asymmetry), coupling high-pass and plate low-pass (Hz).
internal readonly record struct StageSpec(float Gain, float Bias, double HighPassHz, double LowPassHz);

internal sealed record RigSpec(float Drive, EqSpec[] PreEq, StageSpec[] Stages, float Sag, EqSpec[] Tone, CabKind Cab, float Level);

internal sealed record Voicing(StringSpec String, RigSpec Rig);

internal static class Voicings
{
    public const int First = 24, Last = 37, MaxBands = 4, MaxStages = 4;
    public static bool Models(int program) => program is >= First and <= Last;

    private static StringSpec Gtr(double t60, double t1k, double dark, double noise, double pick, double pickup, double stiff, double rel, double pluckHz, bool harmonic = false)
        => new(82, t60, t1k, dark, noise, pick, pickup, stiff, rel, 6, pluckHz, harmonic);
    private static StringSpec Bass(double t60, double t1k, double dark, double noise, double pick, double pickup, double stiff, double rel, double pluckHz)
        => new(41, t60, t1k, dark, noise, pick, pickup, stiff, rel, 5, pluckHz);

    private static readonly StageSpec[] CleanStages = [new(1.2f, 0.1f, 15, 12000), new(1.0f, 0f, 15, 15000)];
    private static readonly StageSpec[] CrunchStages = [new(6f, 0.2f, 25, 6000), new(3f, -0.1f, 25, 7000), new(1.3f, 0f, 20, 9000)];
    private static readonly StageSpec[] LeadStages = [new(12f, 0.25f, 25, 7000), new(8f, -0.2f, 30, 6000), new(6f, 0.15f, 30, 5500), new(1.5f, 0f, 20, 9000)];
    private static readonly StageSpec[] BassStages = [new(1.5f, 0.05f, 15, 8000)];

    private static RigSpec Body(params EqSpec[] eq) => new(1, eq, [], 0, [], CabKind.None, 0.9f);
    private static RigSpec Clean(params EqSpec[] tone) => new(1.5f, [new EqSpec(HighPass, 70, 0.7)], CleanStages, 0.05f, tone, CabKind.Guitar, 0.55f);
    private static RigSpec BassRig(params EqSpec[] tone) => new(1.2f, [new EqSpec(HighPass, 30, 0.7)], BassStages, 0.05f, tone, CabKind.Bass, 0.8f);

    private static readonly EqSpec GuitarBodyLow = new(Peak, 100, 2, 5), GuitarTop = new(Peak, 220, 1.5, 3), Hp60 = new(HighPass, 60, 0.7);

    private static readonly Voicing[] Table =
    [
        new(Gtr(3.5, 1.0, 0.35, 0.12, 0.18, 0, -0.05, 0.15, 3000), Body(Hp60, GuitarBodyLow, GuitarTop, new(HighShelf, 4000, 0.7, -4))),     // 24 nylon
        new(Gtr(5.0, 1.5, 0.18, 0.30, 0.15, 0, -0.08, 0.18, 6000), Body(Hp60, GuitarBodyLow, GuitarTop, new(Peak, 3000, 1, 2))),             // 25 steel
        new(Gtr(4.0, 1.2, 0.30, 0.15, 0.20, 0.25, -0.06, 0.12, 3500), Clean(new(LowShelf, 200, 0.7, 2), new(HighShelf, 2000, 0.7, -6))),    // 26 jazz
        new(Gtr(5.0, 1.5, 0.12, 0.30, 0.13, 0.17, -0.08, 0.12, 7000), Clean(new EqSpec(HighShelf, 2500, 0.7, 3))),                           // 27 clean
        new(Gtr(0.25, 0.12, 0.45, 0.20, 0.10, 0.12, -0.06, 0.06, 2500), Clean(new EqSpec(LowShelf, 150, 0.7, 3))),                           // 28 muted
        new(Gtr(6.0, 2.0, 0.12, 0.35, 0.12, 0.10, -0.08, 0.10, 7000),                                                                        // 29 overdrive
            new RigSpec(1, [new(HighPass, 90, 0.7), new(Peak, 750, 0.9, 5)], CrunchStages, 0.10f, [new(HighShelf, 3000, 0.7, 1)], CabKind.Guitar, 0.30f)),
        new(Gtr(7.0, 2.5, 0.10, 0.40, 0.11, 0.09, -0.10, 0.10, 8000),                                                                        // 30 distortion
            new RigSpec(1, [new(HighPass, 110, 0.7), new(Peak, 900, 0.8, 4)], LeadStages, 0.15f,
                [new(LowShelf, 100, 0.7, 2), new(Peak, 650, 0.9, -5), new(HighShelf, 3000, 0.7, 2)], CabKind.Guitar, 0.25f)),
        new(Gtr(4.0, 2.5, 0.05, 0, 0.5, 0.17, -0.05, 0.15, 4000, harmonic: true), Clean(new EqSpec(HighShelf, 2500, 0.7, 2))),               // 31 harmonics
        new(Bass(2.5, 0.6, 0.45, 0.10, 0.20, 0, -0.15, 0.15, 1200), Body(new(HighPass, 30, 0.7), new(Peak, 80, 1.5, 4), new(HighShelf, 2000, 0.7, -6))), // 32 upright
        new(Bass(5.0, 1.0, 0.30, 0.15, 0.20, 0.20, -0.20, 0.12, 2500), BassRig(new EqSpec(Peak, 800, 1, 1))),                                // 33 finger
        new(Bass(5.0, 1.2, 0.20, 0.35, 0.12, 0.15, -0.20, 0.10, 5000), BassRig(new EqSpec(HighShelf, 1500, 0.7, 4))),                        // 34 pick
        new(Bass(4.0, 0.8, 0.35, 0.10, 0.25, 0.20, -0.15, 0.15, 2000), BassRig(new(Peak, 1000, 0.7, 3), new(HighShelf, 2500, 0.7, -4))),    // 35 fretless
        new(Bass(3.0, 1.0, 0.15, 0.50, 0.08, 0.12, -0.20, 0.10, 8000), BassRig(new(Peak, 500, 0.8, -6), new(HighShelf, 2500, 0.7, 6))),     // 36 slap 1
        new(Bass(3.0, 1.0, 0.15, 0.50, 0.08, 0.12, -0.20, 0.10, 8000), BassRig(new(Peak, 500, 0.8, -4), new(HighShelf, 2500, 0.7, 4))),     // 37 slap 2
    ];

    public static Voicing Of(int program) => Table[Math.Clamp(program, First, Last) - First];
}
