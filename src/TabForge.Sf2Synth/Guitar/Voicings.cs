using TabForge.Sf2Synth.Dsp;
using static TabForge.Sf2Synth.Dsp.EqKind;

namespace TabForge.Sf2Synth.Guitar;

// Owns: every sound value of the guitar layer, per GM program 24-37: the string and the rig behind it.
// Does not own: the DSP reading them (PluckedString, GuitarBus, StageAmp, CabinetImpulse).
// Tests: GuitarLayerTests.

internal enum CabKind { None, Guitar, Bass }

// T60 (s) at RefHz and 1 kHz (log-log between); Darkness = loop low-pass pole; pick and pickup in mm from the bridge (pickup 0 =
// acoustic, no comb); stiffness comes from StringPhysics; PluckHz = pluck low-pass at full velocity.
internal sealed record StringSpec(double RefHz, double T60Ref, double T60At1k, double Darkness, double NoiseMix, double PickMm,
    double PickupMm, double ReleaseT60, int MaxStrings, double PluckHz, bool Harmonic = false, bool Bass = false);

// One tube stage: gain, grid bias (asymmetry), coupling high-pass and plate low-pass (Hz).
internal readonly record struct StageSpec(float Gain, float Bias, double HighPassHz, double LowPassHz);

// Pickup = the DI's magnetic resonance (inductance into cable capacitance), before any amp. Level is calibrated so each program
// matches the GeneralUser GS guitar of the same program at velocity 100 (GeneralUserBankTests).
internal sealed record RigSpec(float Drive, EqSpec[] PreEq, StageSpec[] Stages, float Sag, EqSpec[] Tone, CabKind Cab, float Level)
{
    public EqSpec[] Pickup { get; init; } = [];
}

internal sealed record Voicing(StringSpec String, RigSpec Rig);

internal static class Voicings
{
    public const int First = 24, Last = 37, MaxBands = 4, MaxStages = 4;
    public static bool Models(int program) => program is >= First and <= Last;

    private static StringSpec Gtr(double t60, double t1k, double dark, double noise, double pickMm, double pickupMm, double rel, double pluckHz, bool harmonic = false)
        => new(82, t60, t1k, dark, noise, pickMm, pickupMm, rel, 6, pluckHz, harmonic);
    private static StringSpec Bass(double t60, double t1k, double dark, double noise, double pickMm, double pickupMm, double rel, double pluckHz)
        => new(41, t60, t1k, dark, noise, pickMm, pickupMm, rel, 5, pluckHz, Bass: true);

    private static readonly StageSpec[] CleanStages = [new(1.2f, 0.1f, 15, 12000), new(1.0f, 0f, 15, 15000)];
    private static readonly StageSpec[] CrunchStages = [new(6f, 0.2f, 25, 6000), new(3f, -0.1f, 25, 7000), new(1.3f, 0f, 20, 9000)];
    private static readonly StageSpec[] LeadStages = [new(12f, 0.25f, 25, 7000), new(8f, -0.2f, 30, 6000), new(6f, 0.15f, 30, 5500), new(1.5f, 0f, 20, 9000)];
    private static readonly StageSpec[] BassStages = [new(1.5f, 0.05f, 15, 8000)];

    private static RigSpec Body(params EqSpec[] eq) => new(1, eq, [], 0, [], CabKind.None, 0.9f);
    private static RigSpec Clean(params EqSpec[] tone) => new(1.5f, [new EqSpec(HighPass, 70, 0.7)], CleanStages, 0.05f, tone, CabKind.Guitar, 0.55f) { Pickup = SingleCoil };
    private static RigSpec BassRig(params EqSpec[] tone) => new(1.2f, [new EqSpec(HighPass, 30, 0.7)], BassStages, 0.05f, tone, CabKind.Bass, 0.8f) { Pickup = BassPickup };

    private static readonly EqSpec[] SingleCoil = [new(LowPass, 4500, 1.8)], Humbucker = [new(LowPass, 3300, 2.2)], BassPickup = [new(LowPass, 5000, 1.3)];
    private static readonly EqSpec GuitarBodyLow = new(Peak, 100, 2, 5), GuitarTop = new(Peak, 220, 1.5, 3), Hp60 = new(HighPass, 60, 0.7);

    private static readonly Voicing[] Table =
    [
        new(Gtr(3.5, 1.0, 0.35, 0.12, 117, 0, 0.15, 3000), Body(Hp60, GuitarBodyLow, GuitarTop, new(HighShelf, 4000, 0.7, -4))),     // 24 nylon
        new(Gtr(5.0, 1.5, 0.18, 0.30, 97, 0, 0.18, 6000), Body(Hp60, GuitarBodyLow, GuitarTop, new(Peak, 3000, 1, 2)) with { Level = 0.68f }),             // 25 steel
        new(Gtr(4.0, 1.2, 0.30, 0.15, 130, 162, 0.12, 3500), Clean(new(LowShelf, 200, 0.7, 2), new(HighShelf, 2000, 0.7, -6)) with { Level = 0.36f }),    // 26 jazz
        new(Gtr(5.0, 1.5, 0.12, 0.30, 84, 110, 0.12, 7000), Clean(new EqSpec(HighShelf, 2500, 0.7, 3)) with { Level = 0.27f }),                           // 27 clean
        new(Gtr(0.25, 0.12, 0.45, 0.20, 65, 78, 0.06, 2500), Clean(new EqSpec(LowShelf, 150, 0.7, 3)) with { Level = 1.3f }),                           // 28 muted
        new(Gtr(6.0, 2.0, 0.12, 0.35, 78, 45, 0.10, 7000),                                                                        // 29 overdrive
            new RigSpec(1, [new(HighPass, 90, 0.7), new(Peak, 750, 0.9, 5)], CrunchStages, 0.10f, [new(HighShelf, 3000, 0.7, 1)], CabKind.Guitar, 0.126f) { Pickup = Humbucker }),
        new(Gtr(7.0, 2.5, 0.10, 0.40, 71, 42, 0.10, 8000),                                                                        // 30 distortion
            new RigSpec(1, [new(HighPass, 110, 0.7), new(Peak, 900, 0.8, 4)], LeadStages, 0.15f,
                [new(LowShelf, 100, 0.7, 2), new(Peak, 650, 0.9, -5), new(HighShelf, 3000, 0.7, 2)], CabKind.Guitar, 0.057f) { Pickup = Humbucker }),
        new(Gtr(4.0, 2.5, 0.05, 0, 324, 110, 0.15, 4000, harmonic: true), Clean(new EqSpec(HighShelf, 2500, 0.7, 2)) with { Level = 0.164f }),               // 31 harmonics
        new(Bass(2.5, 0.6, 0.45, 0.10, 173, 0, 0.15, 1200), Body(new(HighPass, 30, 0.7), new(Peak, 80, 1.5, 4), new(HighShelf, 2000, 0.7, -6)) with { Level = 2.7f }), // 32 upright
        new(Bass(5.0, 1.0, 0.30, 0.15, 173, 173, 0.12, 2500), BassRig(new EqSpec(Peak, 800, 1, 1)) with { Level = 0.56f }),                                // 33 finger
        new(Bass(5.0, 1.2, 0.20, 0.35, 104, 130, 0.10, 5000), BassRig(new EqSpec(HighShelf, 1500, 0.7, 4)) with { Level = 0.45f }),                        // 34 pick
        new(Bass(4.0, 0.8, 0.35, 0.10, 216, 173, 0.15, 2000), BassRig(new(Peak, 1000, 0.7, 3), new(HighShelf, 2500, 0.7, -4)) with { Level = 0.62f }),    // 35 fretless
        new(Bass(3.0, 1.0, 0.15, 0.50, 69, 104, 0.10, 8000), BassRig(new(Peak, 500, 0.8, -6), new(HighShelf, 2500, 0.7, 6)) with { Level = 1.25f }),     // 36 slap 1
        new(Bass(3.0, 1.0, 0.15, 0.50, 69, 104, 0.10, 8000), BassRig(new(Peak, 500, 0.8, -4), new(HighShelf, 2500, 0.7, 4)) with { Level = 0.56f }),     // 37 slap 2
    ];

    public static Voicing Of(int program) => Table[Math.Clamp(program, First, Last) - First];
}
