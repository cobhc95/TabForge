namespace TabForge.Presenters;

// Owns: the ring of a string in the track properties window: the sound port, the vibrate curve and the one-at-a-time repeat state.
// Does not own: the sound itself (the host supplies an IStringRinger through the track's sound), the drawing or the timers (the window).
// Tests: TabForge.Avalonia.Tests.TrackTuningDraftTests.

/// <summary>Sounds one open string through the track's own sound. The window never waits on it: Ring returns at once.</summary>
public interface IStringRinger
{
    /// <summary>Starts one ring of this sounding pitch (a note on that fades by itself); never blocks.</summary>
    void Ring(int midi);

    /// <summary>Cuts a ring still sounding.</summary>
    void Silence();
}

public static class StringVibration
{
    public const double RingMs = 1600, DecayMs = 450, FrequencyHz = 11, PeakDips = 1.6;

    /// <summary>The string's sideways offset in device-independent pixels, <paramref name="elapsedMs"/> after the ring began: a sine fading out, zero before and after.</summary>
    public static double Offset(double elapsedMs, double peak = PeakDips) =>
        elapsedMs is < 0 or >= RingMs ? 0 : peak * Math.Exp(-elapsedMs / DecayMs) * Math.Sin(2 * Math.PI * FrequencyHz * elapsedMs / 1000);
}

/// <summary>Which string repeats its ring (the speaker toggle): at most one.</summary>
public sealed class StringRingLoop
{
    /// <summary>Milliseconds between the repeated rings.</summary>
    public const int RepeatMs = 1800;

    public int? Looping { get; private set; }

    /// <summary>Flips the string's speaker: returns true when it now repeats. Turning one on turns the previous one off.</summary>
    public bool Toggle(int index)
    {
        Looping = Looping == index ? null : index;
        return Looping == index;
    }

    public void Stop() => Looping = null;
}
