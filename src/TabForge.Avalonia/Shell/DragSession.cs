using System;
using System.Collections.Generic;
using Avalonia.Input;

namespace TabForge.Avalonia.Shell;

// Owns: the state of one tab drag, shared by the document tab flow and the dock pane flow: which tab is dragged, the Escape
//   cancel (it holds until the pointer is released, so the rest of that press does not drag again) and the drop check that
//   only a running, uncancelled drag may act on the drop.
// Does not own: the moves, drops and ghosts (the flows), the press gesture (the tab strips).
// Tests: TabForge.Avalonia.Tests.TabTearOffTests (Escape and cancel-then-drop for both flows). UI thread only.
internal sealed class DragSession<T>
{
    private readonly Action _hideVisuals;
    private T _dragged = default!;
    private bool _cancelled;

    /// <param name="hideVisuals">Hides the flow's drop preview and ghost when a drag stops.</param>
    public DragSession(Action hideVisuals) => _hideVisuals = hideVisuals;

    /// <summary>True while a tab drag runs.</summary>
    public bool IsDragging { get; private set; }

    /// <summary>The tab being dragged; meaningful only while <see cref="IsDragging"/> is true.</summary>
    public T Dragged => _dragged;

    /// <summary>Records that <paramref name="item"/> is dragged; false when Escape cancelled this press's drag.</summary>
    public bool TryFollow(T item)
    {
        if (_cancelled) return false;
        _dragged = item;
        IsDragging = true;
        return true;
    }

    /// <summary>Ends the drag at release; true only when <paramref name="item"/> was the uncancelled dragged tab.</summary>
    public bool Drop(T item)
    {
        var dragging = IsDragging && EqualityComparer<T>.Default.Equals(_dragged, item) && !_cancelled;
        Cancel();
        _cancelled = false;
        return dragging;
    }

    public void Cancel()
    {
        _cancelled = _cancelled || IsDragging;
        IsDragging = false;
        _hideVisuals();
    }

    /// <summary>Escape cancels a running drag; registered as a tunnelling key handler.</summary>
    public void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || !IsDragging) return;
        Cancel();
        e.Handled = true;
    }
}
