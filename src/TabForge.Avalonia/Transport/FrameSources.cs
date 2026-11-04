using System;
using Avalonia;
using Avalonia.Controls;
using TabForge.Avalonia.Platform;
using TabForge.Platform;

namespace TabForge.Avalonia.Transport;

// Owns: the per-frame tick that drives the playhead clock: the port (IFrameSource), a timer-driven source (tests, no window) and the
//     display-synced source (TopLevel.RequestAnimationFrame, one callback per screen refresh, requested only while running).
// Does not own: what a frame does (AvaloniaPlaybackSession), the position maths (PlayheadClock).
// Tests: TabForge.Avalonia.Tests.PlayheadClockTests.
// Threads: UI only.

/// <summary>Calls <see cref="Frame"/> once per display frame while running; nothing runs while stopped.</summary>
public interface IFrameSource
{
    event Action? Frame;
    bool IsRunning { get; }
    void Start();
    void Stop();
}

/// <summary>Frames from a UI timer at Render priority; the fallback when no window shows the song.</summary>
public sealed class TimerFrameSource : IFrameSource
{
    /// <summary>The timer's period: about 125 Hz, above the 120 fps target.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(UiTiming.PlayheadFallbackFrameMs);

    private readonly IUiTimer _timer;

    public TimerFrameSource(IUiThread ui)
    {
        _timer = ui.CreateTimer(Interval, UiPriority.Render);
        _timer.Tick += (_, _) => Frame?.Invoke();
    }

    public event Action? Frame;
    public bool IsRunning => _timer.IsEnabled;
    public void Start() => _timer.Start();
    public void Stop() => _timer.Stop();
}

/// <summary>One tick per screen refresh, without a software cap; falls back to a timer only while no window is available.</summary>
public sealed class AnimationFrameSource : IFrameSource
{
    private readonly Func<Action<TimeSpan>, bool> _requestFrame;
    private readonly IFrameSource _fallback;
    private Action<TimeSpan> _onAnimationFrame;
    private int _generation;   // each Start owns one chain of frame requests; a frame still pending from before a Stop ends instead of making a second chain
    private bool _running;

    /// <param name="window">The window whose refresh drives the frames; null falls back to the timer.</param>
    public AnimationFrameSource(IUiThread ui, Func<TopLevel?> window)
        : this(new TimerFrameSource(ui), callback =>
        {
            if (window() is not { } top) return false;
            top.RequestAnimationFrame(callback);
            return true;
        })
    {
    }

    // The request port lets tests drive display timestamps without a real monitor. Requests always complete asynchronously.
    internal AnimationFrameSource(IFrameSource fallback, Func<Action<TimeSpan>, bool> requestFrame)
    {
        _requestFrame = requestFrame;
        _fallback = fallback;
        _fallback.Frame += OnFallbackFrame;
        _onAnimationFrame = _ => { };
    }

    public event Action? Frame;
    public bool IsRunning => _running;

    public void Start()
    {
        if (IsRunning) return;
        _running = true;
        var generation = ++_generation;
        _onAnimationFrame = time => OnAnimationFrame(time, generation);
        if (!_requestFrame(_onAnimationFrame)) _fallback.Start();
    }

    public void Stop()
    {
        _running = false;
        _generation++;
        _fallback.Stop();
    }

    private void OnFallbackFrame()
    {
        if (!_running) return;
        var generation = _generation;
        Frame?.Invoke();
        // A window may attach after playback starts. Do not stay on an unsynchronised timer for the rest of the song.
        if (_running && generation == _generation && _requestFrame(_onAnimationFrame)) _fallback.Stop();
    }

    private void OnAnimationFrame(TimeSpan time, int generation)
    {
        if (!_running || generation != _generation) return;
        Frame?.Invoke();
        // A handler may stop or restart playback. The old chain must never enqueue a second callback for the new run.
        if (!_running || generation != _generation) return;
        if (!_requestFrame(_onAnimationFrame)) _fallback.Start();
    }
}
