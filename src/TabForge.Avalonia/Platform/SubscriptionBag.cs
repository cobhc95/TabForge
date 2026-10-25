using System;
using System.Collections.Generic;

namespace TabForge.Avalonia.Platform;

// Owns: the undo actions of a window's event subscriptions, run once when the window closes, so nothing it subscribed to keeps it alive.
// Does not own: the subscriptions themselves or the window's lifetime.
// Tests: no dedicated test class.
// Threads: UI thread only.
public sealed class SubscriptionBag : IDisposable
{
    private List<Action>? _undo = new();

    /// <summary>Runs <paramref name="undo"/> on <see cref="Dispose"/>; run at once when the bag is already disposed.</summary>
    public void Add(Action undo)
    {
        if (_undo is null) { undo(); return; }
        _undo.Add(undo);
    }

    /// <summary>Undoes every subscription, latest first; one that throws is logged and the rest still run; a second call does nothing.</summary>
    public void Dispose()
    {
        if (_undo is not { } undo) return;
        _undo = null;
        for (var i = undo.Count - 1; i >= 0; i--)
        {
            try { undo[i](); }
            catch (Exception ex) { TabForge.Services.Trace.Error(TabForge.Services.Trace.Ui, "subscription undo failed: " + ex.GetType().Name + ": " + ex.Message); }   // one failure must not leave the rest subscribed
        }
    }
}
