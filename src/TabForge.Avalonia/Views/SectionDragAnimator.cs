using System;
using System.Diagnostics;
using TabForge.Avalonia.Platform;
using TabForge.Platform;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

/// <summary>
/// Owns: the eased x of every section strip while one is dragged (the others slide to their new slots) and the settle of the dropped column;
/// one frame timer that runs only while something moves. With animation off every position jumps at once.
/// Does not own: the drag gesture (TimelineInputController), drawing (SectionDragVisual, TimelineView), the easing rule (SectionDragPresenter).
/// Tests: TimelineSectionDragTests. Thread: UI thread.
/// </summary>
internal sealed class SectionDragAnimator
{
    /// <summary>Interval of the frame timer while sections move.</summary>
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(UiTiming.SectionDragFrameMs);

    private readonly Action _redraw;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private readonly Func<TimeSpan> _clock;
    private readonly IUiTimer _timer;
    private readonly EventHandler _onTick;
    private bool _subscribed;
    private TimeSpan _lastFrame;
    private double _settleTarget;
    private Action? _settled;

    /// <param name="redraw">Repaints the timeline after each frame.</param>
    /// <param name="clock">The time each frame is measured at; the time since construction when null. Tests pass a manual clock.</param>
    /// <param name="timer">The frame timer; a render-priority dispatcher timer when null. Tests pass one they fire themselves, so no real time reaches the animation.</param>
    public SectionDragAnimator(Action redraw, Func<TimeSpan>? clock = null, IUiTimer? timer = null)
    {
        _redraw = redraw;
        _clock = clock ?? (() => _stopwatch.Elapsed);
        _timer = timer ?? TabForge.Avalonia.Platform.AvaloniaUiThread.ForApplication().CreateTimer(FrameInterval, UiPriority.Render);
        _onTick = (_, _) => Advance(_clock());
        _timer.Stop();
    }

    /// <summary>False: every position jumps to its target at once.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The x each section slot is drawn at now.</summary>
    public double[] Shown { get; private set; } = Array.Empty<double>();

    /// <summary>The x each section slot comes to rest at.</summary>
    public double[] Targets { get; private set; } = Array.Empty<double>();

    /// <summary>The x of the dropped column while it settles.</summary>
    public double SettleX { get; private set; }

    public bool Settling { get; private set; }

    public void Begin(double[] startX)
    {
        Shown = (double[])startX.Clone();
        Targets = (double[])startX.Clone();
    }

    public void SetTargets(double[] targets)
    {
        Array.Copy(targets, Targets, Targets.Length);
        if (!Enabled) { Array.Copy(Targets, Shown, Shown.Length); _redraw(); return; }
        StartTimer();
    }

    /// <summary>Slides the dropped column from <paramref name="from"/> to <paramref name="to"/>, then calls <paramref name="settled"/>; false (nothing started, <paramref name="settled"/> not called) with animation off or when it is already there.</summary>
    public bool BeginSettle(double from, double to, Action settled)
    {
        if (!Enabled || Math.Abs(from - to) <= SectionDragPresenter.SnapDistance) return false;
        SettleX = from;
        _settleTarget = to;
        Settling = true;
        _settled = settled;
        StartTimer();
        return true;
    }

    /// <summary>Stops the frame timer and unhooks it, so a detached view is not repainted; the next move hooks it again.</summary>
    public void Stop()
    {
        _timer.Stop();
        Unhook();
        Settling = false;
        _settled = null;
    }

    /// <summary>One frame at <paramref name="now"/>; true while anything still moves.</summary>
    public bool Advance(TimeSpan now)
    {
        var share = SectionDragPresenter.EaseShare(SectionDragPresenter.FrameSeconds(now, _lastFrame) * UiMotion.Speed);
        _lastFrame = now;
        var moving = false;
        for (var i = 0; i < Shown.Length; i++)
        {
            Shown[i] = SectionDragPresenter.Step(Shown[i], Targets[i], share, out var arrived);
            moving |= !arrived;
        }
        Action? finished = null;
        if (Settling)
        {
            SettleX = SectionDragPresenter.Step(SettleX, _settleTarget, share, out var arrived);
            if (arrived) { Settling = false; finished = _settled; _settled = null; }
            else moving = true;
        }
        if (!moving) { _timer.Stop(); Unhook(); }
        _redraw();
        finished?.Invoke();
        return moving;
    }

    private void StartTimer()
    {
        if (_timer.IsEnabled) return;
        _lastFrame = TimeSpan.Zero;
        if (!_subscribed) { _timer.Tick += _onTick; _subscribed = true; }
        _timer.Start();
    }

    private void Unhook()
    {
        if (!_subscribed) return;
        _timer.Tick -= _onTick;
        _subscribed = false;
    }
}
