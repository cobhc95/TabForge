namespace TabForge.Audio;

/// <summary>The input level scale used by recording and live monitoring.</summary>
public static class InputMeterScale
{
    public const double MeterFloorDb = -60, MeterCeilingDb = 0;

    /// <summary>A linear peak in dB; <see cref="MeterFloorDb"/> for silence (and for NaN).</summary>
    public static double PeakToDb(double peak) => peak > 0 ? Math.Max(MeterFloorDb, 20 * Math.Log10(peak)) : MeterFloorDb;

    /// <summary>Where a dB value sits on the meter, 0 (floor) to 1 (ceiling).</summary>
    public static double MeterFraction(double db) => Math.Clamp((db - MeterFloorDb) / (MeterCeilingDb - MeterFloorDb), 0, 1);
}
