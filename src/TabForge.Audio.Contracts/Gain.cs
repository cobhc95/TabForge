namespace TabForge.Audio.Contracts;

/// <summary>
/// Decibels and linear amplitude. The overload follows the argument type (a float stays in single precision, a double in double),
/// so each call gives exactly the value the inline expression it names gives. Clamps and floors (for example a silence floor
/// before <see cref="ToDb"/>) belong to the caller. No allocation: safe on the audio thread.
/// </summary>
public static class Gain
{
    /// <summary>Linear amplitude of <paramref name="db"/> decibels (10^(db/20)), in single precision.</summary>
    public static float FromDb(float db) => MathF.Pow(10, db / 20);

    /// <summary>Linear amplitude of <paramref name="db"/> decibels (10^(db/20)), in double precision.</summary>
    public static double FromDb(double db) => Math.Pow(10, db / 20);

    /// <summary>Decibels of a linear amplitude (20 log10); a float argument is widened first. Zero gives -infinity, a negative value NaN.</summary>
    public static double ToDb(double linear) => 20 * Math.Log10(linear);
}
