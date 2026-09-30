namespace TabForge.AudioEngine.Audio;

/// <summary>
/// Where a recorded take starts on the song timeline (RT-09). The player hears the song after the engine's output delay, and their
/// playing reaches the input after the input latency (device-reported when available); the user's recording offset corrects what is
/// left (a manual input offset): positive = takes sounded late, so they move earlier.
/// </summary>
public static class TakeAlignment
{
    public const double MaxOffsetMs = 1000;

    /// <param name="songSecNow">Song time being sent now (the scheduler's clock).</param>
    /// <param name="outputDelaySec">The engine's fixed output delay (what the player hears lags the clock by this).</param>
    /// <param name="inputLatencyMs">Capture latency of the input.</param>
    /// <param name="userOffsetMs">Settings > Audio &amp; VST > Recording offset.</param>
    public static double StartSec(double songSecNow, double outputDelaySec, double inputLatencyMs, double userOffsetMs) =>
        Math.Max(0, songSecNow - outputDelaySec - inputLatencyMs / 1000.0 - Math.Clamp(userOffsetMs, -MaxOffsetMs, MaxOffsetMs) / 1000.0);
}
