using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;

namespace TabForge.Avalonia.Platform;

// Owns: carrying the X thread hover / cancel events to the UI thread for the drop preview: only the latest point is kept per UI turn (the UI never queues
//   a backlog), a cancel always lands after the hovers before it, and the last hover result feeds the next XdndStatus reply.
// Does not own: what the preview draws (MainWindowAudioDrop / MainWindowFileDrop) or the X protocol (XdndTarget, X11FileDrop).
// Tests: none (needs the UI dispatcher); the coalescing is small and checked by the trace lines.
/// <summary>Posts hover and clear to the UI thread, coalesced. Hover/Cancel are called from the X thread.</summary>
internal sealed class X11DragPreview
{
    private readonly Window _window;
    private readonly Func<IReadOnlyList<string>, Point, bool> _hover;
    private readonly Action _clear;
    private readonly object _gate = new();
    private (IReadOnlyList<string> Paths, int X, int Y)? _latest;
    private bool _clearRequested, _queued, _active;
    private int _updates;
    private volatile bool _accepting = true;

    public X11DragPreview(Window window, Func<IReadOnlyList<string>, Point, bool> hover, Action clear)
    {
        _window = window;
        _hover = hover;
        _clear = clear;
    }

    /// <summary>Whether the window would take the drag at the last previewed point (the status reply of the next XdndPosition).</summary>
    public bool Accepting => _accepting;

    public void Hover(IReadOnlyList<string> paths, int x, int y)
    {
        lock (_gate)
        {
            _latest = (paths, x, y);
            _clearRequested = false;
            if (_queued) return;
            _queued = true;
        }
        AvaloniaUiThread.ForApplication().Post(Run);
    }

    public void Cancel()
    {
        lock (_gate)
        {
            _latest = null;
            _clearRequested = true;
            if (_queued) return;
            _queued = true;
        }
        AvaloniaUiThread.ForApplication().Post(Run);
    }

    private void Run()
    {
        (IReadOnlyList<string> Paths, int X, int Y)? latest;
        bool clear;
        lock (_gate)
        {
            latest = _latest;
            clear = _clearRequested;
            _latest = null;
            _clearRequested = false;
            _queued = false;
        }
        try
        {
            if (latest is { } hover)
            {
                var scale = _window.RenderScaling <= 0 ? 1 : _window.RenderScaling;
                var point = new Point(hover.X / scale, hover.Y / scale);
                if (!_active)
                {
                    _active = true;
                    _updates = 0;
                    X11FileDrop.Log($"preview start: {hover.Paths.Count} file(s) at ({point.X:0},{point.Y:0})");
                }
                _accepting = _hover(hover.Paths, point);
                if (++_updates % 10 == 0) X11FileDrop.Log($"preview update #{_updates} at ({point.X:0},{point.Y:0}) accepting={_accepting}");
            }
            else if (clear && _active)
            {
                _active = false;
                _accepting = true;
                _clear();
                X11FileDrop.Log($"preview clear after {_updates} update(s)");
            }
        }
        catch (Exception ex) { X11FileDrop.Log($"preview failed: {ex.GetType().Name}: {ex.Message}"); }
    }
}
