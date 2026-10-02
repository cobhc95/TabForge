namespace TabForge.Shell;

/// <summary>
/// The attachments one owner (a main window) made to objects that outlive it: static or shared events, registrations, hooks. Each is added
/// together with the action that undoes it; <see cref="Dispose"/> runs them once, newest first, when the owner has really finished closing
/// (never in a cancellable Closing: a cancelled close leaves every attachment in place). Disposing again does nothing, and an attachment
/// added after disposal is undone at once, so a late callback cannot re-attach a closed owner.
/// </summary>
internal sealed class OwnedSubscriptions : IDisposable
{
    private readonly object _gate = new();
    private List<Action>? _undo = new();

    public bool IsDisposed { get { lock (_gate) return _undo is null; } }

    /// <summary>Number of attachments still held (self-test: it returns to zero when the owner closed).</summary>
    public int Count { get { lock (_gate) return _undo?.Count ?? 0; } }

    /// <summary>Records <paramref name="undo"/>; if the owner is already disposed it runs immediately.</summary>
    public void Add(Action undo)
    {
        ArgumentNullException.ThrowIfNull(undo);
        lock (_gate)
        {
            if (_undo is { } list) { list.Add(undo); return; }
        }
        undo();
    }

    public void Add(IDisposable resource) => Add(resource.Dispose);

    /// <summary>Attaches (<paramref name="attach"/> runs now) and records the matching detach.</summary>
    public void Attach(Action attach, Action detach)
    {
        attach();
        Add(detach);
    }

    public void Dispose()
    {
        List<Action>? run;
        lock (_gate) { run = _undo; _undo = null; }
        if (run is null) return;
        for (var i = run.Count - 1; i >= 0; i--) run[i]();
    }
}
