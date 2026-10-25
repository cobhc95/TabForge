using System;
using System.Diagnostics;
using Avalonia.Input;
using TabForge.Platform;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Avalonia.Platform;

// Owns: the hover, press and confirm-flash state of a section button, shared by the timeline's section blocks and the Sections panel rows: the
//     section under the pointer (its hover shade), how deep a press is (1 while held, easing back to 0 over the release time) and
//     how strong the confirm flash is after a jump (1 at the jump, fading to 0 over the flash time). Keyed by the section's marker. Each owner
//     holds its own instance and draws or scales its button from the values.
// Does not own: drawing or scaling the block or row, the jump itself (SectionLinks), the pointer gestures (TimelineInputController, SectionsPanelView).
// Tests: TabForge.Avalonia.Tests.SectionButtonFeedbackTests.
// Threads: UI thread only. The frame timer runs only while a release or a flash is still fading, and calls the owner's redraw on each frame.
public sealed class SectionButtonFeedback
{
    /// <summary>How long a released press takes to ease back to rest, in ms (UiMotion scales it; zero with reduced animations).</summary>
    public const double ReleaseMilliseconds = 80;

    /// <summary>How long the confirm flash takes to fade after a jump, in ms (UiMotion scales it).</summary>
    public const double FlashMilliseconds = 200;

    /// <summary>The scale a pressed panel row is drawn at.</summary>
    public const double PressedScale = 0.97;

    /// <summary>How far a pressed timeline block is inset on each side, in DIPs.</summary>
    public const double PressedInsetDips = 1;

    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(UiTiming.SectionFeedbackFrameMs);

    private readonly Action _redraw;
    private Func<TimeSpan> _clock;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private readonly IUiTimer _timer;
    private MarkerModel? _pressed;
    private MarkerModel? _releasing;
    private TimeSpan _releasedAt;
    private MarkerModel? _flashing;
    private TimeSpan _flashedAt;

    /// <param name="redraw">Repaints or re-applies the owner's button look after each change and each animation frame.</param>
    /// <param name="uiThread">The UI thread the frame timer runs on.</param>
    /// <param name="clock">The time the states are measured at; the time since construction when null. Tests pass a manual clock.</param>
    public SectionButtonFeedback(Action redraw, IUiThread uiThread, Func<TimeSpan>? clock = null)
    {
        _redraw = redraw;
        _clock = clock ?? (() => _stopwatch.Elapsed);
        _timer = uiThread.CreateTimer(FrameInterval, UiPriority.Render);
        _timer.Tick += (_, _) => Tick();
        _timer.Stop();
    }

    /// <summary>Replaces the clock the states are measured at (a test drives time by hand); call before any press or flash.</summary>
    internal void UseClock(Func<TimeSpan> clock) => _clock = clock;

    /// <summary>The section under the pointer, or null.</summary>
    public MarkerModel? Hovered { get; private set; }

    /// <summary>Sets the section under the pointer (null for none) and redraws when it changed.</summary>
    public void SetHovered(MarkerModel? section)
    {
        if (ReferenceEquals(section, Hovered)) return;
        Hovered = section;
        _redraw();
    }

    /// <summary>The pointer presses <paramref name="section"/>: its depth is 1 until the press is released.</summary>
    public void Press(MarkerModel section)
    {
        _pressed = section;
        _releasing = null;
        _redraw();
    }

    /// <summary>Ends the press: the depth eases back to 0. Returns the section that was held, or null when none was.</summary>
    public MarkerModel? Release()
    {
        var held = _pressed;
        if (held is null) return null;
        _pressed = null;
        _releasing = held;
        _releasedAt = _clock();
        StartTimer();
        _redraw();
        return held;
    }

    /// <summary>How deep <paramref name="section"/> is pressed: 1 while held, easing out to 0 after the release; 0 at rest.</summary>
    public double PressDepth(MarkerModel section)
    {
        if (ReferenceEquals(section, _pressed)) return 1;
        if (!ReferenceEquals(section, _releasing)) return 0;
        var progress = Progress(_releasedAt, ReleaseMilliseconds);
        return 1 - EaseOut(progress);
    }

    /// <summary>Starts the confirm flash on <paramref name="section"/> (the jump landed on it).</summary>
    public void Flash(MarkerModel section)
    {
        _flashing = section;
        _flashedAt = _clock();
        StartTimer();
        _redraw();
    }

    /// <summary>The confirm flash strength of <paramref name="section"/>: 1 at the jump, fading to 0; 0 when it is not flashing.</summary>
    public double FlashAlpha(MarkerModel section)
    {
        if (!ReferenceEquals(section, _flashing)) return 0;
        var progress = Progress(_flashedAt, FlashMilliseconds);
        return 1 - EaseOut(progress);
    }

    // Fraction of an animation done at the current clock: 1 when it has no length (reduced animations), else clamped to 0..1.
    private double Progress(TimeSpan startedAt, double baseMilliseconds)
    {
        var duration = UiMotion.DurationMilliseconds(baseMilliseconds);
        if (duration <= 0) return 1;
        return Math.Clamp((_clock() - startedAt).TotalMilliseconds / duration, 0, 1);
    }

    private static double EaseOut(double t) => 1 - (1 - t) * (1 - t);

    private void StartTimer()
    {
        if (!_timer.IsEnabled) _timer.Start();
    }

    // Clears the releases and flashes that have finished, repaints, and stops the timer when nothing is still fading.
    private void Tick()
    {
        if (_releasing is not null && Progress(_releasedAt, ReleaseMilliseconds) >= 1) _releasing = null;
        if (_flashing is not null && Progress(_flashedAt, FlashMilliseconds) >= 1) _flashing = null;
        if (_releasing is null && _flashing is null) _timer.Stop();
        _redraw();
    }
}
