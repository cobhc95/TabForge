namespace TabForge.Services;

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
    public static double ToFraction(int mixerValue) => Math.Clamp(mixerValue, 0, 127) / Steps;

    /// <summary>The mixer value (0..127) for a gpif fraction; a fraction of 1.0 is the maximum, 127.</summary>
    public static int FromFraction(double fraction) => Math.Clamp((int)Math.Round(fraction * Steps, MidpointRounding.AwayFromZero), 0, 127);
}
