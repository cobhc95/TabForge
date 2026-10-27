namespace TabForge.Services;

// Owns: whether one view's playhead follow may scroll now: the master switch (FollowSettings.FollowPlayback) and the pause a hand scroll starts,
//   which ends ResumeAfterSeconds after the last hand scroll.
// Does not own: the settings values (FollowSettings), the follow maths (FollowScrollPolicy), the scrolling itself (the score and timeline views).
// Tests: TabForge.Avalonia.Tests.FollowPlaybackTests.
// Threads: UI thread only.

/// <summary>The hand-scroll pause of one view's follow; each view owns one and asks it once per frame before it scrolls.</summary>
public sealed class FollowHold
{
    private long _pausedAt;
    private bool _paused;

    /// <summary>The clock in milliseconds; tests replace it.</summary>
    public Func<long> Now { get; set; } = () => Environment.TickCount64;

    /// <summary>A hand scroll: the follow stands still until <see cref="FollowSettings.ResumeAfterSeconds"/> after this (the last) one.</summary>
    public void Pause()
    {
        _pausedAt = Now();
        _paused = true;
    }

    /// <summary>Ends the pause at once (playback starts, or the reader places the cursor).</summary>
    public void Release() => _paused = false;

    /// <summary>True when the follow may scroll now: it is switched on and no hand scroll is still holding it back.</summary>
    public bool Allows(FollowSettings follow)
    {
        if (!follow.FollowPlayback) return false;
        if (!_paused) return true;
        if (Now() - _pausedAt < follow.ResumeAfterSeconds * 1000L) return false;
        _paused = false;
        return true;
    }
}
