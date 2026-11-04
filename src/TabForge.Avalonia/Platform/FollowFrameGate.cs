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
    private const int ScreenRateFps = 240;
    private const double CapIntervalTolerance = 0.9;
    private readonly Func<double> _nowMs;
    private double _lastAllowedMs = double.NegativeInfinity;

    public FollowFrameGate(Func<double>? nowMs = null)
    {
        var clock = Stopwatch.StartNew();
        _nowMs = nowMs ?? (() => clock.Elapsed.TotalMilliseconds);
    }

    /// <summary>Allows the configured scrolling rate with jitter tolerance; 240 (the maximum setting) follows every display frame.</summary>
    public bool Allow(int maxFps)
    {
        var now = _nowMs();
        if (maxFps > 0 && maxFps < ScreenRateFps && now - _lastAllowedMs < MillisecondsPerSecond / maxFps * CapIntervalTolerance) return false;
        _lastAllowedMs = now;
        return true;
    }
}
