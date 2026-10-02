using System.Windows;
using System.Windows.Threading;
using TabForge.Shell;
using TabForge.Views;

namespace TabForge;

// MainWindow, lifetime (R1): what this window attaches to objects that outlive it, and the one place that undoes it.
//
// Rules: (1) anything attached to a static or shared object (a static event, the shared audio engine client, the shared settings store,
// a registration) goes through Subscribe / _lifetime.Add, so its detach is recorded next to its attach; (2) the detach runs once, from
// Closed (a cancelled close raises no Closed, so a window that stays open keeps every attachment); (3) work another thread or object
// queues for this window goes through PostIfOpen, so it never touches a window that has closed meanwhile.
public partial class MainWindow : IDiscardPromptHost
{
    /// <summary>Everything attached to a longer-lived object; undone once, newest first, when the window has really closed.</summary>
    private readonly OwnedSubscriptions _lifetime = new();

    /// <summary>True from the moment the window has really closed (Closed raised): queued callbacks check it before touching controls.</summary>
    private bool _isClosed;

    /// <summary>Attaches <paramref name="handler"/> to a longer-lived event and records the matching detach.</summary>
    private void Subscribe<THandler>(Action<THandler> attach, Action<THandler> detach, THandler handler)
    {
        attach(handler);
        _lifetime.Add(() => detach(handler));
    }

    /// <summary>
    /// Queues UI work that an event of a longer-lived object (or another thread) raised for this window. If the window has closed by the time
    /// the dispatcher gets to it, the work is dropped: no control of a closed window is touched and nothing runs on its documents.
    /// </summary>
    private void PostIfOpen(Action work, DispatcherPriority priority = DispatcherPriority.Normal) =>
        Dispatcher.BeginInvoke(priority, new Action(() => { if (!_isClosed) work(); }));

    /// <summary>The window has really closed (Closed): stop what runs on its behalf and undo every attachment to longer-lived objects. Idempotent.</summary>
    private void ReleaseWindowResources()
    {
        if (_isClosed) return;
        // A metronome change still waiting for its debounced save is saved now (stopping the timer below would otherwise drop it).
        _transport.FlushPendingSave();
        _isClosed = true;
        _playbackView.Dispose();
        _follow.Halt();
        // The documents still in this window are closed for good (a tab moved to another window left _documents when it moved, and is
        // not touched here): their playback stops and their engine chains are unloaded now, not parked.
        foreach (var session in _documents.Documents.ToArray()) session.DisposePlayback();
        _recorder?.Release();
        _resizeBorderFrame?.Dispose();
        _captionButtonFrame?.Dispose();
        _tabTransfer.Dispose();   // clears the attach highlight and leaves the registry
        _lifetime.Dispose();
        // A UI Automation client can keep automation peers of this window's controls alive: they must not lead back to the window.
        // After the other Closed handlers have run: the controls (and this window) let go of their handlers and of the song.
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            ChildEventRelease.Release(this);
            Editor.ReleaseDocument();
            _documents.ReleaseAll();
        }));
    }

    /// <summary>Attachments still held (self-test: zero once the window has closed).</summary>
    internal int AttachmentCount => _lifetime.Count + _engineSync.AttachmentCount;

    // IDiscardPromptHost: the "discard unapplied changes?" question follows the window that opened the dialog.
    bool IDiscardPromptHost.WarnOnDiscard => _settings.General.ConfirmDiscardSettingsChanges;

    void IDiscardPromptHost.DisableDiscardWarning()
    {
        if (_isClosed) return;
        _settings.General.ConfirmDiscardSettingsChanges = false;
        SaveSettings();
    }
}
