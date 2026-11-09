using TabForge.Sf2Synth.Dsp;

namespace TabForge.Sf2Synth.Guitar;

// Owns: the shapes the voicing table is written in: string, attack tap, amp stage, rig, voicing, cabinet kind.
// Does not own: the values (Voicings), the DSP reading them.
// Tests: GuitarLayerTests.

internal enum CabKind { None, Guitar, Bass, Room }

// T60 (s) at RefHz and 1 kHz (log-log between); Darkness = loop low-pass pole; pick and pickup in mm from the bridge (pickup 0 =
// acoustic, no comb; Pickup2Mm = second coil or pickup); stiffness comes from StringPhysics; PluckHz = pluck low-pass at full
// velocity; Vel = velocity exponent of the pluck; Click = pick-click scale; Tap = attack noise; Slap = struck, not plucked;
// Mwah = fretless resonance at Mwah × f0; Glide = every legato step slides this long (fretless).
internal sealed record StringSpec(double RefHz, double T60Ref, double T60At1k, double Darkness, double NoiseMix, double PickMm,
    double PickupMm, double ReleaseT60, int MaxStrings, double PluckHz, bool Harmonic = false, bool Bass = false)
{
    public double Pickup2Mm { get; init; }
    public double Vel { get; init; } = 1.4;
    public float Click { get; init; } = 1;
    public TapSpec Tap { get; init; }
    public bool Slap { get; init; }
    public double Mwah { get; init; }
    public double Glide { get; init; }
}

// Attack noise through a band-pass: amount (× pluck amplitude × velocity), centre Hz, Q, decay ms.
internal readonly record struct TapSpec(float Amount, double Hz, double Q, double Ms);

// One tube stage: gain, grid bias (asymmetry), coupling high-pass and plate low-pass (Hz).
internal readonly record struct StageSpec(float Gain, float Bias, double HighPassHz, double LowPassHz);

// Pickup = the DI's magnetic resonance (inductance into cable capacitance), before any amp. Level is calibrated so each program
// matches the GeneralUser GS guitar of the same program at velocity 100 (GeneralUserBankTests). Body = acoustic modes after the
// bridge; Comp = compressor ahead of the built-in amp.
internal sealed record RigSpec(float Drive, EqSpec[] PreEq, StageSpec[] Stages, float Sag, EqSpec[] Tone, CabKind Cab, float Level)
{
    public EqSpec[] Pickup { get; init; } = [];
    public BodySpec? Body { get; init; }
    public CompSpec? Comp { get; init; }
}

internal sealed record Voicing(StringSpec String, RigSpec Rig);
