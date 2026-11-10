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
    private static readonly StageSpec[] BassStages = [new(1.2f, 0.06f, 5, 6000), new(1.15f, 0.01f, 5, 7500)];
    private static readonly CompSpec CleanComp = new(-6, 1.6f, 8, 150, 0);
    private static readonly CompSpec BassComp = new(-16, 2.5f, 2, 90, 0) { SplitHz = 180 };
    private static readonly CompSpec SlapComp = new(-16, 2.5f, 6, 120, 0) { SplitHz = 180, SubAttackMs = 4, SubReleaseMs = 220 };

    // Hz, Q, gain over the direct path: Helmholtz air, top, back, then higher plate modes.
    private static readonly BodySpec Nylon = new(0.72f, [new(98, 14, 1.5f), new(195, 18, 1.3f), new(235, 20, 0.6f), new(390, 22, 0.5f), new(550, 24, 0.35f), new(780, 25, 0.25f), new(1050, 25, 0.15f)]);
    private static readonly BodySpec Steel = new(0.72f, [new(102, 15, 1.2f), new(210, 20, 1.5f), new(300, 20, 0.9f), new(420, 25, 0.6f), new(620, 25, 0.5f), new(900, 28, 0.4f), new(1500, 25, 0.35f), new(2600, 20, 0.3f)]);
    // Upright body: air/f-hole energy at 60/110 Hz and top resonance at 215 Hz, broad and wooden under the string.
    private static readonly BodySpec Upright = new(0.65f, [new(60, 7, 1.4f), new(110, 8, 1.1f), new(155, 9, 0.6f), new(215, 11, 0.9f), new(360, 12, 0.5f), new(600, 14, 0.3f)]);
    // Electric bass body: lower and broader than the upright (wood thump under the low strings).
    private static readonly BodySpec BassBody = new(1f, [new(48, 7, 0.7f), new(92, 9, 0.9f), new(155, 11, 0.6f), new(240, 13, 0.35f), new(380, 15, 0.2f)]);

    private static readonly EqSpec[] SingleCoil = [new(LowPass, 5000, 2.2)], NeckHumbucker = [new(LowPass, 2800, 1.6)],
        PBass = [new(Peak, 1300, 0.8, 2.5), new(LowPass, 2300, 0.7)], JBass = [new(Peak, 2700, 1.0, 4), new(LowPass, 4400, 0.9)];
    private static readonly EqSpec Hp60 = new(HighPass, 38, 0.7);

    // 27/29/30 DI fitted to a measured real guitar DI (status doc): low cut 135 Hz, pickup resonance 900 Hz (+13.5 dB, Q 1) under a
    // 2.07 kHz roll-off; a dark loop (highs: T60 0.8 s at 2 kHz), softer pluck, two-coil bridge humbucker.
    private static readonly EqSpec[] MeasuredDi = [new(HighPass, 135, 0.7), new(Peak, 900, 1, 13.5), new(LowPass, 2070, 0.4)];
    private static readonly StringSpec MeasuredString = Gtr(5.0, 1.4, 0.55, 0.25, 78, 45, 0.12, 3500) with { Pickup2Mm = 63, Vel = 1.85 };

    // Captured amp output EQ, fitted to the real amp recording of the measured take (status doc): low mids up, the 830 Hz bump and
    // upper mids down, the capture's fizz above 6 kHz down.
    private static readonly EqSpec[] CapturedEq = [new(Peak, 210, 0.9, 4), new(Peak, 830, 1.1, -6), new(Peak, 2550, 1, -3.5), new(HighShelf, 5700, 0.7, -10)];

    private static RigSpec Body(BodySpec body, params EqSpec[] eq) => new(1, eq, [], 0, [], CabKind.None, 0.9f) { Body = body };
    private static RigSpec Clean(params EqSpec[] tone) => new(0.6f, [new EqSpec(HighPass, 45, 0.7)], CleanStages, 0.05f, tone, CabKind.Room, 0.55f) { Pickup = SingleCoil, Comp = CleanComp };
    private static RigSpec BassRig(EqSpec[] pickup, params EqSpec[] tone) => new(1.2f, [new EqSpec(LowShelf, 85, 0.7, 4.5)], BassStages, 0.05f, tone, CabKind.Bass, 0.8f) { Pickup = pickup, Comp = BassComp, Body = BassBody };

    private static readonly Voicing[] Table =
    [
        new(Gtr(6.5, 1.1, 0.35, 0.10, 117, 0, 0.28, 3500) with { Vel = 1.67, Drop = 7, DropMs = 30, ReleaseDarken = 0.25, Tap = new(0.25f, 1500, 0.7, 4), Click = 0.3f },                      // 24 nylon
            Body(Nylon, Hp60, new(HighShelf, 4000, 0.7, -4)) with { Level = 1.36f }),
        new(Gtr(7.5, 1.4, 0.28, 0.30, 97, 0, 0.30, 6000) with { Vel = 1.47, Drop = 7, DropMs = 30, ReleaseDarken = 0.25, Tap = new(0.25f, 3500, 0.8, 3) },                                     // 25 steel
            Body(Steel, Hp60, new(Peak, 3000, 1, 2)) with { Level = 1.125f }),
        new(Gtr(5.0, 1.3, 0.40, 0.15, 130, 155, 0.18, 3500) with { Vel = 1.85, Pickup2Mm = 173, Drop = 7, DropMs = 14, Tap = new(0.1f, 2500, 0.7, 2) },                  // 26 jazz
            Clean(new(LowShelf, 200, 0.7, 2), new(HighShelf, 2000, 0.7, -6)) with { Level = 1.06f, Pickup = NeckHumbucker }),
        new(MeasuredString with { Tap = new(0.15f, 4000, 0.8, 2) },                                 // 27 clean
            Clean(new EqSpec(HighShelf, 2500, 0.7, 3)) with { Level = 2.08f, Pickup = MeasuredDi }),
        new(Gtr(1.2, 0.2, 0.45, 0.20, 65, 78, 0.06, 2500) with { Vel = 2, Drop = 0 }, Clean(new EqSpec(LowShelf, 150, 0.7, 3)) with { Level = 2.56f }), // 28 muted
        new(MeasuredString,                                                                                                                     // 29 overdrive
            new RigSpec(1, [new(HighPass, 90, 0.7), new(Peak, 750, 0.9, 5)], CrunchStages, 0.10f, [new(HighShelf, 3000, 0.7, 1)], CabKind.Guitar, 0.126f) { Pickup = MeasuredDi, Captured = new(0.30f, 0.94f, CapturedEq) }),
        new(MeasuredString,                                                                                                                     // 30 distortion
            new RigSpec(1, [new(HighPass, 110, 0.7), new(Peak, 900, 0.8, 4)], LeadStages, 0.15f,
                [new(LowShelf, 100, 0.7, 2), new(Peak, 650, 0.9, -5), new(HighShelf, 3000, 0.7, 2)], CabKind.Guitar, 0.057f) { Pickup = MeasuredDi, Captured = new(0.30f, 0.73f, CapturedEq) }),
        new(Gtr(3.5, 1.5, 0.05, 0, 324, 110, 0.20, 4000, harmonic: true) with { Vel = 2, Drop = 6, DropMs = 18 }, Clean(new EqSpec(HighShelf, 2500, 0.7, 2)) with { Level = 0.54f }), // 31 harmonics
        new(Bass(4.5, 1.0, 0.35, 0.16, 173, 0, 0.30, 1800) with { Vel = 1.75, Drop = 9, DropMs = 28, ReleaseDarken = 0.25, Tap = new(1.5f, 300, 1.0, 20), TapVel = 1.2, Click = 0.3f },                            // 32 upright
            Body(Upright, new(HighPass, 18, 0.7), new(LowShelf, 100, 0.7, 3), new(HighShelf, 2500, 0.7, -5)) with { Level = 2.00f }),
        new(Bass(5.5, 1.0, 0.50, 0.15, 173, 150, 0.25, 2500) with { Vel = 2, Drop = 7, DropMs = 22, Tap = new(0.45f, 650, 0.7, 6), TapVel = 1.3, Click = 0.4f, ClickVel = 1.2 },     // 33 finger (P)
            BassRig(PBass, new EqSpec(Peak, 800, 1, 1)) with { Level = 0.68f }),
        new(Bass(5.5, 1.1, 0.30, 0.35, 104, 150, 0.20, 5000) with { Vel = 2, Drop = 7, DropMs = 18, Tap = new(0.5f, 2800, 0.9, 3), TapVel = 1.6, Click = 2.5f, ClickVel = 1.6 },   // 34 pick (P)
            BassRig(PBass, new EqSpec(HighShelf, 1500, 0.7, 4)) with { Level = 0.47f }),
        new(Bass(4.5, 0.9, 0.50, 0.10, 216, 125, 0.25, 2000) with { Vel = 1.8, Pickup2Mm = 45, Drop = 7, DropMs = 20, Tap = new(0.35f, 550, 0.7, 6), TapVel = 1.2, Click = 0.5f, ClickVel = 1.2, Mwah = 8, Glide = 0.12 }, // 35 fretless (J)
            BassRig(JBass, new(Peak, 1000, 0.7, 3), new(HighShelf, 2500, 0.7, -4)) with { Level = 0.90f }),
        new(Bass(4.0, 1.0, 0.25, 0.20, 250, 125, 0.20, 8000) with { Vel = 2, Pickup2Mm = 45, Slap = true, Drop = 5, DropMs = 18, Tap = new(2f, 110, 0.6, 18), TapVel = 1.2, Click = 4.5f, ClickVel = 1.1 },   // 36 slap 1 (thumb)
            BassRig(JBass, new(Peak, 500, 0.8, -6), new(HighShelf, 2500, 0.7, 6)) with { Level = 1.49f, Comp = SlapComp }),
        new(Bass(4.0, 1.0, 0.25, 0.20, 200, 125, 0.20, 8000) with { Vel = 2.1, Pickup2Mm = 45, Slap = true, Drop = 8, DropMs = 16, Tap = new(0.7f, 2400, 1.4, 4), TapVel = 1.8, Click = 6f, ClickVel = 1.5 },   // 37 slap 2 (pop)
            BassRig(JBass, new(Peak, 500, 0.8, -4), new(HighShelf, 2500, 0.7, 4)) with { Level = 0.68f, Comp = SlapComp }),
    ];

    public static Voicing Of(int program) => Table[Math.Clamp(program, First, Last) - First];
}
