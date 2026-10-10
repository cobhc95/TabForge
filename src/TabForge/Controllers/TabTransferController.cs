using System.Windows;
using TabForge.Documents;
using TabForge.Services;
using TabForge.Shell;

namespace TabForge.Controllers;

/// <summary>What the tab transfer needs from its window: the songs it shows, its tab strip, and the window-level actions (open, drag, close).</summary>
internal interface ITabTransferHost
{
    /// <summary>The songs open in this window.</summary>
    DocumentManager Documents { get; }
    /// <summary>Shown and not closed: the only state in which this window takes a song or lends its tab strip as a drop target.</summary>
    bool IsOpen { get; }
    bool IsEnabled { get; }
    Rect TabStripScreenRect { get; }
    double DpiScaleY { get; }
    int TabInsertIndexAt(Point screen);
    /// <summary>Whole-title-row accent treatment that marks this window as the drop target of a held tear-off.</summary>
    void SetAttachHighlight(bool highlighted);
    bool TryGetCursor(out Point screen);
    Point ScreenToDip(Point screen);
    /// <summary>Where a window opened from the tab menu goes: next to this window's pointer.</summary>
    Point NewWindowPositionNearPointer();
    /// <summary>Stores the shown song's view state (zoom, typed boxes) on it, so it leaves this window complete.</summary>
    void CaptureDocumentState();
    /// <summary>Shows the song the manager now reports as active.</summary>
    void ShowActiveDocument();
    /// <summary>Shows <paramref name="session"/> (just inserted), with the tab strip focused on it.</summary>
    void ActivateAdopted(DocumentSession session);
    /// <summary>The last song left: the "last tab closed" setting decides between closing the window and a blank tab.</summary>
    void LastDocumentLeft();
    void CloseWindow();
    void SetStatus(string text);
    /// <summary>Applies the preferred score view to a song restored from a drop payload.</summary>
    void PrepareRestoredDocument(DocumentSession session);
    /// <summary>A new top-level window that starts with <paramref name="session"/> (Normal state, at <paramref name="dipPosition"/> when given), shown and activated.</summary>
    TabTransferController OpenWindowWith(DocumentSession session, Point? dipPosition);
    /// <summary>
    /// Hands this window to the native caption loop while the button is held (restoring it to Normal under the pointer first), calling
    /// <paramref name="moved"/> on every move; false when no button is held (headless runs, a click without drag) and nothing was started.
    /// </summary>
    bool DragWindowWithHeldPointer(Point screen, Action moved);
}

// Owns: one window's side of moving a tab between windows: tear-off to a new window, the sole-tab window drag, merging into another window,
//     the attach-target highlight and tab drops. Hands a song over explicitly: released from the source (state captured, removed, next tab
//     shown) and adopted by the target (inserted and shown, which syncs the engine and approvals for it); the song object itself never changes.
// Does not own: the song, its undo history, dirty state and playback (the DocumentSession travels whole), the tab strip control, the native
//     window drag, and which windows exist (TabWindowRegistry).
// Tests: TestWindowLifetime, TestRepeatedTearOffAndMergeReleasesWindows.
/// <summary>
/// Tab transfer for one window. The attach target is held only while a tear-off is in flight and is cleared by <see cref="Dispose"/>, which
/// also leaves the registry; a window that closed meanwhile refuses a hand-over (<see cref="TryAdopt"/>), so a song is never handed to a dead window.
/// </summary>
internal sealed class TabTransferController : ITabTransferTarget, IDisposable
{
    private readonly ITabTransferHost _host;
    private ITabTransferTarget? _attachTarget;
    private int _attachTargetIndex = -1;
    private bool _disposed;

    public TabTransferController(ITabTransferHost host) => _host = host;

    /// <summary>The window being highlighted as the drop target of the tear-off in flight (self-test).</summary>
    internal ITabTransferTarget? AttachTarget => _attachTarget;

    /// <summary>Makes this window a candidate drop target of other windows' held tear-offs (once it is loaded).</summary>
    public void Register()
    {
        if (!_disposed) TabWindowRegistry.Register(this);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ClearAttachTarget();
        TabWindowRegistry.Unregister(this);
    }

    // ---------- the source side ----------

    /// <summary>Releases the song at <paramref name="index"/> from this window: its state is captured, it leaves the window, and the window shows what is left.</summary>
    private DocumentSession? ReleaseDocument(int index)
    {
        _host.CaptureDocumentState();
        var session = _host.Documents.Detach(index);
        if (session is null) return null;
        if (_host.Documents.Documents.Count == 0) _host.LastDocumentLeft();
        else _host.ShowActiveDocument();
        return session;
    }

    /// <summary>Opens the song in a brand-new top-level window (the tab menu's "Move to new window").</summary>
    public void DetachToNewWindow(int index)
    {
        if (index < 0 || index >= _host.Documents.Documents.Count) return;
        var session = ReleaseDocument(index);
        if (session is null) return;
        _host.OpenWindowWith(session, _host.NewWindowPositionNearPointer());
        _host.SetStatus($"Moved {session.DisplayName} to a new window");
    }

    /// <summary>
    /// Held tear-off: hand the live session over, place a real top-level window under the held pointer, then hand movement to the native caption
    /// loop so Aero Snap and monitor-edge behaviour stay with Windows. A window's only tab moves the window instead.
    /// </summary>
    public void DetachHeldToNewWindow(int index, Point screenPoint)
    {
        if (index < 0 || index >= _host.Documents.Documents.Count) return;
        if (BrowserTabDragPolicy.TearOffAction(_host.Documents.Documents.Count) == TabTearOffAction.MoveWindow)
        {
            ContinueHeldDrag(screenPoint);
            return;
        }
        var session = ReleaseDocument(index);
        if (session is null) return;
        var drop = _host.ScreenToDip(screenPoint);
        _host.OpenWindowWith(session, new Point(drop.X - 120, drop.Y - 22)).ContinueHeldDrag(screenPoint);
    }

    /// <summary>
    /// The window follows the held pointer (native caption loop, so snapping still works). Released over another window's tab strip, its only
    /// tab merges there and this window closes.
    /// </summary>
    public void ContinueHeldDrag(Point screenPoint)
    {
        if (_host.DragWindowWithHeldPointer(screenPoint, UpdateAttachTargetFromCursor)) CompleteHeldTearOff();
    }

    /// <summary>Moves this window's only song into <paramref name="target"/> at <paramref name="index"/> and closes this window; false (nothing moved) when it cannot.</summary>
    public bool MergeSoleDocumentInto(ITabTransferTarget target, int index)
    {
        if (ReferenceEquals(target, this) || _host.Documents.Documents.Count != 1 || !target.IsOpen) return false;
        _host.CaptureDocumentState();
        var session = _host.Documents.Detach(0);
        if (session is null) return false;
        if (!target.TryAdopt(session, Math.Max(0, index)))
        {
            _host.Documents.Insert(session, 0);   // the target went away after all: the song stays here, and its tab is shown again
            _host.ShowActiveDocument();
            return false;
        }
        _host.CloseWindow();
        return true;
    }

    // ---------- the held tear-off's drop target ----------

    private void UpdateAttachTargetFromCursor()
    {
        if (_disposed || !_host.TryGetCursor(out var cursor)) return;
        var next = TabWindowRegistry.FindTarget(this, cursor);
        if (!ReferenceEquals(next, _attachTarget))
        {
            _attachTarget?.SetAttachHighlight(false);
            _attachTarget = next;
            _attachTarget?.SetAttachHighlight(true);
        }
        _attachTargetIndex = _attachTarget?.AttachInsertIndexAt(cursor) ?? -1;
    }

    private void CompleteHeldTearOff()
    {
        if (!_host.TryGetCursor(out var cursor)) cursor = new Point(0, 0);
        UpdateAttachTargetFromCursor();
        var target = _attachTarget;
        var index = _attachTargetIndex;
        ClearAttachTarget();
        if (target is null || !target.IsOpen || !target.CanAcceptAttachAt(cursor)) return;
        MergeSoleDocumentInto(target, index);
    }

    private void ClearAttachTarget()
    {
        _attachTarget?.SetAttachHighlight(false);
        _attachTarget = null;
        _attachTargetIndex = -1;
    }

    // ---------- the target side (ITabTransferTarget) ----------

    public bool IsOpen => !_disposed && _host.IsOpen;

    public bool CanAcceptAttachAt(Point screenPoint)
    {
        if (!IsOpen || !_host.IsEnabled) return false;
        var strip = _host.TabStripScreenRect;
        var scale = Math.Max(1.0, _host.DpiScaleY);
        var horizontal = Math.Max(24, 28 * scale);
        var top = Math.Max(4, 6 * scale);
        var bottom = Math.Max(56, 78 * scale);
        var zone = new Rect(strip.Left - horizontal, strip.Top - top,
            strip.Width + horizontal * 2, strip.Height + top + bottom);
        return zone.Contains(screenPoint);
    }

    public int AttachInsertIndexAt(Point screenPoint) => _host.TabInsertIndexAt(screenPoint);

    public void SetAttachHighlight(bool highlighted) => _host.SetAttachHighlight(highlighted);

    /// <summary>Adopts a song handed over by another window (or dropped on the tab strip); false when this window has closed.</summary>
    public bool TryAdopt(DocumentSession session, int index)
    {
        if (!IsOpen) return false;
        _host.CaptureDocumentState();
        _host.Documents.Insert(session, Math.Clamp(index, 0, _host.Documents.Documents.Count));
        _host.ActivateAdopted(session);
        _host.SetStatus($"Opened {session.DisplayName} here");
        return true;
    }

    /// <summary>Replaces the freshly-created blank tab of a new window with the song handed over from another window.</summary>
    public void AdoptAsOnlyDocument(DocumentSession session)
    {
        while (_host.Documents.Documents.Count > 0)
            _host.Documents.Detach(0)?.DisposePlayback();
        _host.Documents.Insert(session, 0);
        _host.ActivateAdopted(session);
    }

    /// <summary>A tab dropped on the window outside its tab strip (the strip handles its own drops): the song merges in at the end.</summary>
    public void DropTab(DragEventArgs e)
    {
        e.Handled = true;
        e.Effects = DragDropEffects.Move;
        var index = _host.Documents.Documents.Count;

        if (TabDragService.Current is { } incoming)
        {
            if (_host.Documents.IndexOf(incoming.Session) >= 0) return;   // our own tab: the strip handled it
            if (TryAdopt(incoming.Session, index)) incoming.Consumed = true;
            return;
        }

        var json = e.Data.GetData(TabDragService.Format) as string;
        if (string.IsNullOrEmpty(json)) return;
        try
        {
            var project = ProjectService.Restore(json);
            var session = DocumentSession.FromProject(project, null);
            _host.PrepareRestoredDocument(session);
            session.Project.IsDirty = true;
            TryAdopt(session, index);
        }
        catch
        {
            Services.Trace.Error(Services.Trace.Ui, "tab drop: corrupt payload ignored"); // Corrupt payload: ignore the drop rather than crashing.
        }
    }
}
