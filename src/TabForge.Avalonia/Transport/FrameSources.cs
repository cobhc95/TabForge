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

/// <summary>Frames in step with the screen refresh (60, 120 or 144 Hz), capped by the Follow frame-rate setting; falls back to a timer without a window.</summary>
public sealed class AnimationFrameSource : IFrameSource
{
    /// <summary>A cap at or above this (or at 0) means no cap: every screen frame.</summary>
    public const int UncappedFps = 240;
    private const double MillisecondsPerSecond = 1000;
    // A frame arriving within this share of the cap interval still counts, so a 60 Hz cap on a 60 Hz screen does not skip every other frame.
    private const double CapIntervalTolerance = 0.9;

    private readonly Func<TopLevel?> _window;
    private readonly Func<int> _maxFps;
    private readonly TimerFrameSource _fallback;
    private Action<TimeSpan> _onAnimationFrame;
    private int _generation;   // each Start owns one chain of frame requests; a frame still pending from before a Stop ends instead of making a second chain
    private bool _animating;
    private double _lastFrameMs = double.NegativeInfinity;

    /// <param name="window">The window whose refresh drives the frames; null falls back to the timer.</param>
    /// <param name="maxFps">The live frame-rate cap (the Follow setting); 0 or <see cref="UncappedFps"/> and up is the screen rate.</param>
    public AnimationFrameSource(IUiThread ui, Func<TopLevel?> window, Func<int> maxFps)
    {
        _window = window;
        _maxFps = maxFps;
        _fallback = new TimerFrameSource(ui);
        _fallback.Frame += () => Frame?.Invoke();
        _onAnimationFrame = _ => { };
    }

    public event Action? Frame;
    public bool IsRunning => _animating || _fallback.IsRunning;

    public void Start()
    {
        if (IsRunning) return;
        if (_window() is { } top)
        {
            _animating = true;
            var generation = ++_generation;
            _onAnimationFrame = time => OnAnimationFrame(time, generation);
            _lastFrameMs = double.NegativeInfinity;
            top.RequestAnimationFrame(_onAnimationFrame);
        }
        else _fallback.Start();
    }

    public void Stop()
    {
        _animating = false;
        _generation++;
        _fallback.Stop();
    }

    private void OnAnimationFrame(TimeSpan time, int generation)
    {
        if (!_animating || generation != _generation) return;
        var cap = _maxFps();
        var now = time.TotalMilliseconds;
        var capped = cap > 0 && cap < UncappedFps && now - _lastFrameMs < MillisecondsPerSecond / cap * CapIntervalTolerance;
        if (!capped)
        {
            _lastFrameMs = now;
            Frame?.Invoke();
        }
        // The frame handler may have stopped the source (the song ended); ask for the next frame only while still running.
        if (_animating && _window() is { } top) top.RequestAnimationFrame(_onAnimationFrame);
        else _animating = false;
    }
}
