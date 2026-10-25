using System;

namespace TabForge.Platform;

// Owns: the one "coalesce to next frame" utility: many requests within a UI pass become one queued action carrying the latest state.
// Does not own: the action itself (the caller's engine sync, repaint or save) or the thread (the IUiThread it is given).
// Tests: TabForge.Avalonia.Tests.NextFrameCoalescerTests, LiveMixPushTests, UiResponsivenessGuardTests (the guard names this type in its failure messages).
// Threads: UI thread only. The queued action runs on the thread the IUiThread posts to.

/// <summary>Runs <c>action</c> once per UI pass however often <see cref="Request"/> is called; use it in slider, resize, pointer and frame handlers instead of syncing the engine or saving there.</summary>
public sealed class NextFrameCoalescer
{
    private readonly IUiThread _ui;
    private readonly Action _action;
    private readonly UiPriority _priority;
    private bool _queued;
    private int _generation;

    public NextFrameCoalescer(IUiThread ui, Action action, UiPriority priority = UiPriority.Render)
    {
        _ui = ui;
        _action = action;
        _priority = priority;
    }

    /// <summary>Queues one run for the next UI pass; a request made while one is queued adds nothing.</summary>
    public void Request()
    {
        if (_queued) return;
        _queued = true;
        var generation = _generation;
        try { _ui.Post(() => { if (generation != _generation) return; _queued = false; _action(); }, _priority); }
        catch { _queued = false; throw; }   // a refused post must not leave later requests swallowed
    }

    /// <summary>Drops the queued run (its owner is gone) and frees the next request. A run the UI thread dropped by itself (shutdown) is freed the same way.</summary>
    public void Cancel()
    {
        _generation++;
        _queued = false;
    }
}
