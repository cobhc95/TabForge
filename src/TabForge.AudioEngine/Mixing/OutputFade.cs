using System.Diagnostics;

namespace TabForge.AudioEngine.Mixing;

/// <summary>The last gain before the audio device: a short linear ramp to silence before the output is stopped and back up after it starts.</summary>
/// <remarks>
/// Owns: the ramp gain of one mixer's output, the "silent block delivered" report, and the bounded wait of the control thread.
/// Does not own: stopping or opening the device (EngineSession), mixer levels, mute / solo or plug-in state (never touched here).
/// Tests: TestOutputFade.
/// </remarks>
public sealed class OutputFade
{
    /// <summary>Ramp length in milliseconds.</summary>
    public const int RampMs = 30;
    /// <summary>The longest the control thread waits for silence before it stops the device anyway (longer for a large device block: three blocks and the ramp).</summary>
    public const int WaitCapMs = 150;
    /// <summary>No callback for this long while waiting (or two device blocks, if that is longer): the device is not running and the wait ends.</summary>
    public const int StallMs = 60;
    /// <summary>The longest the silent output keeps running so the audio the driver still holds plays out.</summary>
    public const int MaxDrainMs = 300;

    private readonly int _rate;
    private readonly float _step;      // gain change per frame
    private volatile int _frames;      // frames of the latest callback (the device's block size as it really is)
    private float _gain = 1f;          // audio thread only
    private int _hold;                 // audio thread only: silent callbacks still to deliver before the ramp up
    private volatile float _target = 1f;
    private volatile bool _restart;    // control thread asks for gain 0 + one silent callback, then the ramp up
    private volatile int _callbacks;   // callbacks seen off the fast path (the wait's liveness signal)
    private volatile int _silent;      // whole callbacks delivered at gain 0 since FadeOut

    public OutputFade(int sampleRate) { _rate = Math.Max(1, sampleRate); _step = 1000f / (RampMs * _rate); }

    /// <summary>True once a whole callback of silence went to the device after <see cref="FadeOut"/>.</summary>
    public bool IsSilent => _silent > 0;

    /// <summary>Control thread: ramp to silence from the next callback on.</summary>
    public void FadeOut() { _silent = 0; _restart = false; _target = 0f; }

    /// <summary>Control thread, before the device starts: the first callback is silent, then the gain ramps up to 1.</summary>
    public void FadeIn() { _target = 1f; _restart = true; }

    /// <summary>
    /// Control thread: <see cref="FadeOut"/>, then waits until silence was delivered. Returns false when it gave up
    /// (cap reached, or no callback is running); the caller stops the device either way.
    /// </summary>
    public bool FadeOutAndWait()
    {
        var blockMs = (int)(_frames * 1000L / _rate);
        var capMs = Math.Max(WaitCapMs, 3 * blockMs + RampMs);
        var stallMs = Math.Max(StallMs, 2 * blockMs);
        FadeOut();
        var started = Stopwatch.GetTimestamp();
        var seen = _callbacks; var seenAt = started;
        while (!IsSilent)
        {
            var now = Stopwatch.GetTimestamp();
            if (_callbacks != seen) { seen = _callbacks; seenAt = now; }
            if (Stopwatch.GetElapsedTime(started, now).TotalMilliseconds >= capMs) return false;
            if (Stopwatch.GetElapsedTime(seenAt, now).TotalMilliseconds >= stallMs) return false;
            Thread.Sleep(1);
        }
        return true;
    }

    /// <summary>
    /// Control thread, before the device stops: <see cref="FadeOutAndWait"/>, then the silent output keeps running for the device's
    /// latency (at most <see cref="MaxDrainMs"/>) so the sound the driver still holds is played, not cut. No drain when the wait gave up.
    /// </summary>
    public bool FadeOutAndDrain(int latencyMs)
    {
        if (!FadeOutAndWait()) return false;
        Thread.Sleep(Math.Clamp(latencyMs, 0, MaxDrainMs));
        return true;
    }

    /// <summary>Audio thread: scales one callback's interleaved stereo block in place. At rest (gain 1, target 1) it returns at once.</summary>
    public void Apply(float[] buffer, int offset, int frames)
    {
        _frames = frames;
        var target = _target;
        if (_gain == 1f && target == 1f && !_restart) return;
        _callbacks++;
        if (_restart) { _restart = false; _gain = 0f; _hold = 1; }
        var g = _gain;
        if (g == 0f && (target == 0f || _hold > 0))
        {
            Array.Clear(buffer, offset, frames * 2);
            if (target == 0f) { if (_silent == 0) _silent = 1; }
            else _hold--;
            return;
        }
        var step = target > g ? _step : -_step;
        var i = 0;
        for (; i < frames; i++)
        {
            g += step;
            if (g <= 0f) { g = 0f; break; }
            if (g >= 1f) { g = 1f; break; }
            buffer[offset + 2 * i] *= g; buffer[offset + 2 * i + 1] *= g;
        }
        // The ramp ended inside this block: the rest is silence (gain 0) or left untouched (gain 1).
        if (g == 0f && i < frames) Array.Clear(buffer, offset + 2 * i, (frames - i) * 2);
        _gain = g;
    }
}
