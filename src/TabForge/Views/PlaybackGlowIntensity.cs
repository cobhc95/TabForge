namespace TabForge.Views;

/// <summary>Shared opacity scaling and presets for playback duration shading.</summary>
internal static class PlaybackGlowIntensity
{
    internal static readonly int[] PresetPercentages = { 0, 10, 20, 30, 50, 75, 100 };

    /// <summary>Scales a layer's existing alpha linearly; zero is always fully transparent.</summary>
    internal static byte ScaleAlpha(byte baseAlpha, double intensity)
    {
        if (baseAlpha == 0 || !double.IsFinite(intensity)) return 0;
        intensity = Math.Clamp(intensity, 0, 1);
        return (byte)Math.Clamp(Math.Round(baseAlpha * intensity, MidpointRounding.AwayFromZero), 0, 255);
    }
}
