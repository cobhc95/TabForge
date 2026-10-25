using System;
using TabForge.Platform;

namespace TabForge.Avalonia.Platform;

// Owns: the shared coalescing of slider drags: a ValueChanged storm (one event per pointer move) becomes at most one heavy
//   change per settings-preview interval with a trailing run, plus one flush when the drag ends.
// Does not own: the label or the stored value (the caller updates those at once), the heavy change itself (the caller's
//   callback: engine push, menu checks, settings save) or any slider template.
// Threads: UI thread only (the trailing timer ticks there).
// Tests: TabForge.Avalonia.Tests.TransportSettingsPopupTests (a drag's changes are coalesced, the final value flushes).
/// <summary>Coalesces one slider's drag so every slider stays smooth: the label and the stored value follow each move,
/// while the heavy change runs at most every 100 ms with a trailing run, and once more when the drag ends.</summary>
internal sealed class SliderChangeCoalescer
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(UiTiming.SettingsPreviewIntervalMs);

    private readonly Action _changed;
    private readonly IUiTimer _trailer;
    private long _lastRunTicks;
    private bool _armed;

    internal SliderChangeCoalescer(Action changed)
    {
        _changed = changed;
        _trailer = AvaloniaUiThread.ForApplication().CreateTimer(Interval, UiPriority.Background);
        _trailer.Tick += (_, _) =>
        {
            _trailer.Stop();
            _armed = false;
            _lastRunTicks = Environment.TickCount64;
            _changed();
        };
    }

    /// <summary>A slider value changed (the caller already updated the label and the stored value): runs the heavy change at
    /// once when the last run is older than the preview interval, else arms one trailing run for the interval's end.</summary>
    internal void SliderChanged()
    {
        var now = Environment.TickCount64;
        if (now - _lastRunTicks >= UiTiming.SettingsPreviewIntervalMs)
        {
            _lastRunTicks = now;
            _changed();
            return;
        }
        if (_armed) return;
        _armed = true;
        _trailer.Start();
    }

    /// <summary>Drops a trailing change that is armed (the slider went away before it ran).</summary>
    internal void Cancel()
    {
        _trailer.Stop();
        _armed = false;
    }

    /// <summary>The drag ended (pointer released): runs the trailing change now when one is armed, so the final value always lands.</summary>
    internal void Flush()
    {
        if (!_armed) return;
        _trailer.Stop();
        _armed = false;
        _lastRunTicks = Environment.TickCount64;
        _changed();
    }
}
