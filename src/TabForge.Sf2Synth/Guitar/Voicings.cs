using TabForge.Sf2Synth.Dsp;
using static TabForge.Sf2Synth.Dsp.EqKind;

namespace TabForge.Sf2Synth.Guitar;

// Owns: every sound value of the guitar layer, per GM program 24-37: the string and the rig behind it.
// Does not own: the DSP reading them (PluckedString, GuitarBus, StageAmp, BodyModes, Compressor, CabinetImpulse); the spec types (VoicingSpecs).
// Tests: GuitarLayerTests, GeneralUserBankTests.
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
    private static readonly CompSpec CleanComp = new(-6, 1.6f, 8, 150, 0), BassComp = new(-20, 3.5f, 3, 90, 0), SlapComp = new(-20, 3.5f, 12, 90, 0);

    // Hz, Q, gain over the direct path: Helmholtz air, top, back, then higher plate modes.
    private static readonly BodySpec Nylon = new(0.6f, [new(98, 14, 1.5f), new(195, 18, 1.3f), new(235, 20, 0.6f), new(390, 22, 0.5f), new(550, 24, 0.35f), new(780, 25, 0.25f), new(1050, 25, 0.15f)]);
    private static readonly BodySpec Steel = new(0.6f, [new(102, 15, 1.2f), new(210, 20, 1.5f), new(300, 20, 0.9f), new(420, 25, 0.6f), new(620, 25, 0.5f), new(900, 28, 0.4f), new(1500, 25, 0.35f), new(2600, 20, 0.3f)]);
    private static readonly BodySpec Upright = new(0.5f, [new(60, 10, 1.6f), new(105, 14, 1.4f), new(150, 15, 0.8f), new(230, 18, 0.6f), new(400, 20, 0.4f), new(700, 20, 0.3f)]);

    private static readonly EqSpec[] SingleCoil = [new(LowPass, 5000, 2.2)], NeckHumbucker = [new(LowPass, 2800, 1.6)], Humbucker = [new(LowPass, 3300, 2.2)],
        PBass = [new(LowPass, 2500, 1.5)], JBass = [new(LowPass, 5000, 1.3)];
    private static readonly EqSpec Hp60 = new(HighPass, 60, 0.7);

    private static RigSpec Body(BodySpec body, params EqSpec[] eq) => new(1, eq, [], 0, [], CabKind.None, 0.9f) { Body = body };
    private static RigSpec Clean(params EqSpec[] tone) => new(0.6f, [new EqSpec(HighPass, 70, 0.7)], CleanStages, 0.05f, tone, CabKind.Room, 0.55f) { Pickup = SingleCoil, Comp = CleanComp };
    private static RigSpec BassRig(EqSpec[] pickup, params EqSpec[] tone) => new(1.2f, [new EqSpec(HighPass, 30, 0.7)], BassStages, 0.05f, tone, CabKind.Bass, 0.8f) { Pickup = pickup, Comp = BassComp };

    private static readonly Voicing[] Table =
    [
        new(Gtr(6.0, 1.6, 0.25, 0.10, 117, 0, 0.15, 3500) with { Vel = 1.67, Tap = new(0.25f, 1500, 0.7, 4), Click = 0.3f },                      // 24 nylon
            Body(Nylon, Hp60, new(HighShelf, 4000, 0.7, -4)) with { Level = 1.40f }),
        new(Gtr(7.0, 2.2, 0.18, 0.30, 97, 0, 0.18, 6000) with { Vel = 1.47, Tap = new(0.25f, 3500, 0.8, 3) },                                     // 25 steel
            Body(Steel, Hp60, new(Peak, 3000, 1, 2)) with { Level = 1.125f }),
        new(Gtr(6.0, 1.8, 0.30, 0.15, 130, 155, 0.12, 3500) with { Vel = 1.85, Pickup2Mm = 173, Tap = new(0.1f, 2500, 0.7, 2) },                  // 26 jazz
            Clean(new(LowShelf, 200, 0.7, 2), new(HighShelf, 2000, 0.7, -6)) with { Level = 1.06f, Pickup = NeckHumbucker }),
        new(Gtr(8.0, 2.5, 0.12, 0.30, 84, 110, 0.12, 7000) with { Vel = 1.85, Tap = new(0.15f, 4000, 0.8, 2) },                                   // 27 clean
            Clean(new EqSpec(HighShelf, 2500, 0.7, 3)) with { Level = 0.845f }),
        new(Gtr(1.2, 0.2, 0.45, 0.20, 65, 78, 0.06, 2500) with { Vel = 2 }, Clean(new EqSpec(LowShelf, 150, 0.7, 3)) with { Level = 2.56f }), // 28 muted
        new(Gtr(6.0, 2.0, 0.12, 0.35, 78, 45, 0.10, 7000),                                                                                      // 29 overdrive
            new RigSpec(1, [new(HighPass, 90, 0.7), new(Peak, 750, 0.9, 5)], CrunchStages, 0.10f, [new(HighShelf, 3000, 0.7, 1)], CabKind.Guitar, 0.126f) { Pickup = Humbucker }),
        new(Gtr(7.0, 2.5, 0.10, 0.40, 71, 42, 0.10, 8000),                                                                                      // 30 distortion
            new RigSpec(1, [new(HighPass, 110, 0.7), new(Peak, 900, 0.8, 4)], LeadStages, 0.15f,
                [new(LowShelf, 100, 0.7, 2), new(Peak, 650, 0.9, -5), new(HighShelf, 3000, 0.7, 2)], CabKind.Guitar, 0.057f) { Pickup = Humbucker }),
        new(Gtr(4.0, 2.5, 0.05, 0, 324, 110, 0.15, 4000, harmonic: true) with { Vel = 2 }, Clean(new EqSpec(HighShelf, 2500, 0.7, 2)) with { Level = 0.54f }), // 31 harmonics
        new(Bass(3.5, 0.8, 0.50, 0.10, 173, 0, 0.15, 1200) with { Vel = 1.75, Tap = new(2.5f, 90, 1.2, 25) },                                    // 32 upright
            Body(Upright, new(HighPass, 30, 0.7), new(HighShelf, 2000, 0.7, -6)) with { Level = 3.03f }),
        new(Bass(8.0, 1.5, 0.45, 0.15, 173, 150, 0.12, 2500) with { Vel = 2, Tap = new(0.4f, 700, 0.8, 6), Click = 0.3f },                    // 33 finger (P)
            BassRig(PBass, new EqSpec(Peak, 800, 1, 1)) with { Level = 0.72f }),
        new(Bass(8.0, 1.8, 0.20, 0.35, 104, 150, 0.10, 5000) with { Vel = 2, Tap = new(0.3f, 3000, 0.8, 3), Click = 2 },                      // 34 pick (P)
            BassRig(PBass, new EqSpec(HighShelf, 1500, 0.7, 4)) with { Level = 0.49f }),
        new(Bass(6.0, 1.2, 0.45, 0.10, 216, 125, 0.15, 2000) with { Vel = 1.8, Pickup2Mm = 45, Tap = new(0.3f, 600, 0.8, 6), Mwah = 8, Glide = 0.12 }, // 35 fretless (J)
            BassRig(JBass, new(Peak, 1000, 0.7, 3), new(HighShelf, 2500, 0.7, -4)) with { Level = 1.10f }),
        new(Bass(5.0, 1.5, 0.15, 0.20, 250, 125, 0.10, 8000) with { Vel = 2, Pickup2Mm = 45, Slap = true, Tap = new(1.5f, 120, 1.0, 10), Click = 4 }, // 36 slap 1 (thumb)
            BassRig(JBass, new(Peak, 500, 0.8, -6), new(HighShelf, 2500, 0.7, 6)) with { Level = 1.86f, Comp = SlapComp }),
        new(Bass(5.0, 1.5, 0.15, 0.20, 200, 125, 0.10, 8000) with { Vel = 2.1, Pickup2Mm = 45, Slap = true, Tap = new(0.4f, 2200, 1.2, 6), Click = 5 }, // 37 slap 2 (pop)
            BassRig(JBass, new(Peak, 500, 0.8, -4), new(HighShelf, 2500, 0.7, 4)) with { Level = 0.81f, Comp = SlapComp }),
    ];

    public static Voicing Of(int program) => Table[Math.Clamp(program, First, Last) - First];
}
