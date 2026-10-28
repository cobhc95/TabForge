namespace TabForge.AudioEngine.Builtin.Compressor;

// Owns: the static gain computer of the compressor: output level for an input level with threshold, ratio and a soft knee. The audio path and the
//   transfer curve picture of the editor call the same function.
// Does not own: the timing (attack and release, CompressorEffect.Process.cs), the drawing (TransferCurveView in the app).
// Tests: BuiltinDynamicsTests.
public static class CompressorCurve
{
    /// <summary>The output level in dB for an input level in dB: unchanged below the knee, <c>threshold + over / ratio</c> above it, a quadratic blend inside it.</summary>
    public static double OutputDb(double inDb, double thresholdDb, double ratio, double kneeDb)
    {
        var over = inDb - thresholdDb;
        if (2 * over < -kneeDb) return inDb;
        if (kneeDb > 0 && 2 * Math.Abs(over) <= kneeDb)
        {
            var t = over + kneeDb / 2;
            return inDb + (1 / ratio - 1) * t * t / (2 * kneeDb);
        }
        return thresholdDb + over / ratio;
    }

    /// <summary>The gain reduction in dB (0 or more) for an input level in dB.</summary>
    public static double ReductionDb(double inDb, double thresholdDb, double ratio, double kneeDb) =>
        Math.Max(0, inDb - OutputDb(inDb, thresholdDb, ratio, kneeDb));
}
