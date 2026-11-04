using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using TabForge.Platform;

namespace TabForge.Avalonia.Platform;

// Owns: starting/stopping an attached control's poll timer on its own and its ancestors' visibility and window minimization.
// Does not own: the timer interval, the request protocol or the control. No idle wake-and-return ticks while hidden.
// Thread: UI thread. Ancestor subscriptions exist only while attached; reparenting rebuilds them.
// Tests: VisibilityPollingTests.
internal sealed class VisibilityPolling
{
    private readonly Control _owner;
    private readonly IUiTimer _timer;
    private readonly Action _resumed;
    private readonly List<Visual> _ancestors = new();

    internal VisibilityPolling(Control owner, IUiTimer timer, Action resumed)
    {
        (_owner, _timer, _resumed) = (owner, timer, resumed);
        owner.AttachedToVisualTree += (_, _) => Attach();
        owner.DetachedFromVisualTree += (_, _) => Detach();
        if (owner.GetVisualRoot() is not null) Attach();
    }

    private void Attach()
    {
        Detach();
        for (Visual? visual = _owner; visual is not null; visual = visual.GetVisualParent())
        {
            _ancestors.Add(visual);
            visual.PropertyChanged += Changed;
        }
        Refresh();
    }

    private void Detach()
    {
        _timer.Stop();
        foreach (var visual in _ancestors) visual.PropertyChanged -= Changed;
        _ancestors.Clear();
    }

    private void Changed(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Visual.IsVisibleProperty || e.Property == Window.WindowStateProperty) Refresh();
    }

    private void Refresh()
    {
        var visible = _ancestors.Count > 0;
        foreach (var visual in _ancestors)
            if (!visual.IsVisible || visual is Window { WindowState: WindowState.Minimized }) { visible = false; break; }
        if (!visible) { _timer.Stop(); return; }
        if (_timer.IsEnabled) return;
        _resumed();
        _timer.Start();
    }
}
