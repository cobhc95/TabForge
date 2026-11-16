namespace TabForge;

// Owns: the one MIDI data-byte maximum (127), its clamp and its 0..1 fraction, shared by notes, velocities, controllers, programs and mixer levels.
// Does not own: other 127 meanings (fret caps, colours); those keep their own named limits.
// Tests: covered by every MIDI clamp that uses it (TabForge.Core.Tests, TabForge.AudioEngine.Tests).

/// <summary>The largest MIDI 7-bit data value.</summary>
public static class MidiLimits
{
    /// <summary>The largest MIDI note, velocity, controller value, program or mixer level.</summary>
    public const int Max = 127;

    /// <summary>The value held to a MIDI data byte, 0..127 (a note, velocity, controller value, program or mixer level). Use this instead of <c>Math.Clamp(v, 0, 127)</c>.</summary>
    public static int Clamp(int value) => Math.Clamp(value, 0, Max);

    /// <summary>The same clamp for a computed (not yet rounded) value; the caller rounds or casts.</summary>
    public static double Clamp(double value) => Math.Clamp(value, 0, Max);

    /// <summary>A note velocity held to 1..127: 0 would be a note-off, so a sounding note never goes below 1.</summary>
    public static int ClampVelocity(int velocity) => Math.Clamp(velocity, 1, Max);

    /// <summary>A sounding velocity (1..127, clamped first) as a share of full scale: 127 is 1.0, 1 is about 0.008.</summary>
    public static double VelocityFraction(int velocity) => ClampVelocity(velocity) / (double)Max;

    /// <summary>The value as a share of full scale, 0..1 (clamped first): velocity 127 is 1.0, 64 is about 0.504.</summary>
    public static double Fraction(int value) => Clamp(value) / (double)Max;
}
