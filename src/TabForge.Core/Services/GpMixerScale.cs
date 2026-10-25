namespace TabForge.Services;

// Owns: the scale between mixer values and the fractions a Guitar Pro 7/8 file stores for a track.
// Does not own: the mixer and the file writing.
// Tests: no dedicated test class.
/// <summary>
/// The scale between TabForge's mixer values (volume and pan, 0..127) and the fractions a Guitar Pro 7/8 file stores for a track
/// (the ChannelStrip parameters of the gpif, 0..1; pan 0.5 = centre). alphaTab's own model keeps only the 0..16 step (a floor), which
/// is why TabForge uses the patched TabForge.AlphaTab (<c>PlaybackInformation.VolumeFraction</c>/<c>BalanceFraction</c>, patch 0002).
/// One divisor for both directions, so every value 0..127 survives a clean .gp round trip exactly and the centre (64) is exactly 0.5.
/// </summary>
internal static class GpMixerScale
{
    private const double Steps = 128.0;

    /// <summary>The gpif fraction for a mixer value (0..127 → 0..0.9921875).</summary>
    public static double ToFraction(int mixerValue) => Math.Clamp(mixerValue, 0, MidiLimits.Max) / Steps;

    /// <summary>The mixer value (0..127) for a gpif fraction; a fraction of 1.0 is the maximum, 127.</summary>
    public static int FromFraction(double fraction) => Math.Min(Scale(fraction, Steps), 127);

    private const double MixSteps = 16.0;

    /// <summary>The gpif fraction for a mix-table step (0..16, as <c>MixChange.Volume</c>; a pan step is -8..8 plus 8).</summary>
    public static double StepToFraction(int step) => Math.Clamp(step, 0, 16) / MixSteps;

    /// <summary>The mix-table step (0..16) for a gpif fraction.</summary>
    public static int FractionToStep(double fraction) => Math.Min(Scale(fraction, MixSteps), 16);

    /// <summary>The mix-table step (0..16) for a mixer value (0..127): the same rounding as <see cref="FractionToStep"/> of <see cref="ToFraction"/>, so the writer, reader and exporter agree.</summary>
    public static int MixerToStep(int mixerValue) => FractionToStep(ToFraction(mixerValue));

    /// <summary>The fraction clamped to 0..1 first (so an infinite or huge value cannot overflow the cast; NaN counts as 0), then scaled and rounded half away from zero.</summary>
    private static int Scale(double fraction, double steps) => double.IsNaN(fraction) ? 0 : (int)Math.Round(Math.Clamp(fraction, 0, 1) * steps, MidpointRounding.AwayFromZero);
}
