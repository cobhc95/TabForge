using System;
using System.Collections.Generic;
using TabForge.Platform;

namespace TabForge.Avalonia.Platform;

// Owns: the one 33 ms meter tick the armed-row levels and the tuner share: a single UI timer while anyone listens, none when nobody does.
//   One instance per app (AppServices); a listener that never unsubscribes is released by Dispose.
// Does not own: the polls themselves (ArmedRowLayer, TunerWindow) or the interval (UiTiming.SharedMeterTickMs).
// Tests: no dedicated test class.
// Threads: UI thread only.
public sealed class SharedMeterTick : IDisposable
{
    private readonly HashSet<Action> _listeners = new();
    private IUiTimer? _timer;

    /// <summary>How many ticks are subscribed now.</summary>
    public int ListenerCount => _listeners.Count;

    /// <summary>Calls <paramref name="tick"/> about every 33 ms until <see cref="Unsubscribe"/>.</summary>
    public void Subscribe(Action tick)
    {
        _timer ??= Create();
        if (_listeners.Add(tick)) _timer.Start();
    }

    /// <summary>Stops calling a subscribed tick; the timer stops with the last listener.</summary>
    public void Unsubscribe(Action tick)
    {
        if (_listeners.Remove(tick) && _listeners.Count == 0) _timer?.Stop();
    }

    /// <summary>Drops every listener and stops the timer.</summary>
    public void Dispose()
    {
        _listeners.Clear();
        _timer?.Stop();
    }

    private Action[] _snapshot = Array.Empty<Action>();   // reused every tick: a listener may unsubscribe while the others run, so they are copied first, without allocating
    private bool _ticking;

    private void Tick()
    {
        if (_ticking) return;   // a listener that runs a nested message loop (a modal window) must not start a second pass over the shared copy
        _ticking = true;
        var count = _listeners.Count;
        try
        {
            if (_snapshot.Length < count) _snapshot = new Action[count];
            _listeners.CopyTo(_snapshot);
            for (var index = 0; index < count; index++) _snapshot[index]();
        }
        finally
        {
            Array.Clear(_snapshot, 0, count);   // also when a listener throws: no stale listener stays reachable
            _ticking = false;
        }
    }

    private IUiTimer Create()
    {
        var timer = AvaloniaUiThread.ForApplication().CreateTimer(TimeSpan.FromMilliseconds(UiTiming.SharedMeterTickMs), UiPriority.Render);
        timer.Tick += (_, _) => Tick();
        return timer;
    }
}
