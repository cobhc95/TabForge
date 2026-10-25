using System;
using System.Diagnostics;

namespace TabForge.Avalonia.Platform;

// Owns: the frame-rate ceiling of the score's Smooth follow: it says whether enough time has passed since the last allowed glide step.
// Does not own: the glide itself (ScorePreviewView) or the cap value (FollowSettings.MaxFps).
// Tests: TabForge.Avalonia.Tests.FollowSettingsTests.
// Thread: UI only. Ceiling: one instance per score view; it holds one timestamp.
internal sealed class FollowFrameGate
{
    private const double MillisecondsPerSecond = 1000;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _lastAllowedMs = double.NegativeInfinity;

    /// <summary>True when at least 1000 / <paramref name="maxFps"/> ms have passed since the last true; that instant becomes the new last.</summary>
    public bool Allow(int maxFps)
    {
        var now = _clock.Elapsed.TotalMilliseconds;
        if (now - _lastAllowedMs < MillisecondsPerSecond / Math.Max(1, maxFps)) return false;
        _lastAllowedMs = now;
        return true;
    }
}
