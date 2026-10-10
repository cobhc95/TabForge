using TabForge.Documents;
using TabForge.Models;

namespace TabForge.Controllers;

/// <summary>What <see cref="ArrangementGestureState"/> needs from its window.</summary>
public interface IArrangementGestureHost
{
    /// <summary>The document the arrangement shows (read at each gesture event).</summary>
    DocumentSession Document { get; }
}

// Owns: the arrangement gestures' pending undo state (track edit, mix edit, track drag, section drag/resize), the section
//   highlighted while playing, and the track count the timeline height was last fitted to.
// Does not own: what a gesture edits (the controllers), the views, and the document (the host names the active one).
// Tests: TestArrangementGestureState; the window paths by TestClipAndSectionEdits, TestDocumentOperations, TestInteractions.
/// <summary>Holds the half-finished undo steps of arrangement gestures between their start and end events.</summary>
public sealed class ArrangementGestureState
{
    private readonly IArrangementGestureHost _host;
    private UndoSnapshot? _sectionSnapshot;
    private UndoSnapshot? _trackSnapshot;
    private UndoController.UndoTransaction? _mixTransaction;
    private bool _mixChanged;
    private UndoController.UndoTransaction? _trackEditTransaction;
    private int _fittedTrackCount = -1;

    public ArrangementGestureState(IArrangementGestureHost host) => _host = host;

    /// <summary>The section the playhead is in (null when not playing).</summary>
    public MarkerModel? PlayingSection { get; private set; }
    /// <summary>True while the section list selection follows the playing section.</summary>
    public bool SyncingPlayingSelection { get; private set; }

    /// <summary>Opens an undo step, runs <paramref name="apply"/>, and keeps the step for <see cref="CompleteTrackEdit"/> when it changed something.</summary>
    public void BeginTrackEdit(Func<bool> apply)
    {
        var document = _host.Document;
        var transaction = document.Undo.BeginTransaction(document.Project);
        if (apply()) _trackEditTransaction = transaction;
        else document.Undo.Cancel(transaction);
    }

    /// <summary>Stores the pending track-edit step, if any.</summary>
    public void CompleteTrackEdit()
    {
        if (_trackEditTransaction is not { } transaction) return;
        Commit(_host.Document, transaction);
        _trackEditTransaction = null;
    }

    public void MixEditStarting()
    {
        var document = _host.Document;
        _mixTransaction ??= document.Undo.BeginTransaction(document.Project);
    }

    public void MixChanged()
    {
        if (_mixTransaction is not null) _mixChanged = true;
    }

    public void MixEditEnded()
    {
        if (_mixTransaction is not { } transaction) return;
        var document = _host.Document;
        if (_mixChanged) Commit(document, transaction);
        else document.Undo.Cancel(transaction);
        _mixTransaction = null;
        _mixChanged = false;
    }

    public void TrackDragStarted()
    {
        var document = _host.Document;
        _trackSnapshot = document.Undo.Snapshot(document.Project);
    }

    /// <summary>The state taken when the track drag started; cleared by the read.</summary>
    public UndoSnapshot? TakeTrackSnapshot()
    {
        var snapshot = _trackSnapshot;
        _trackSnapshot = null;
        return snapshot;
    }

    /// <summary>A section drag or resize starts: the state is taken before the first change.</summary>
    public void SectionDragStarted()
    {
        var document = _host.Document;
        _sectionSnapshot = document.Undo.Snapshot(document.Project);
    }

    public void SectionDragCancelled() => _sectionSnapshot = null;

    /// <summary>The state taken when the section drag or resize started; cleared by the read.</summary>
    public UndoSnapshot? TakeSectionSnapshot()
    {
        var snapshot = _sectionSnapshot;
        _sectionSnapshot = null;
        return snapshot;
    }

    /// <summary>Records the playing section. <paramref name="select"/> runs with the section flag raised so the list's own handlers ignore it; true returns when it changed.</summary>
    public bool ShowPlayingSection(MarkerModel? current, bool force, Action<MarkerModel> select)
    {
        var changed = !ReferenceEquals(current, PlayingSection);
        if (!changed && !force) return false;
        PlayingSection = current;
        if (current is not null)
        {
            SyncingPlayingSelection = true;
            try { select(current); }
            finally { SyncingPlayingSelection = false; }
        }
        return changed;
    }

    /// <summary>True when the track count differs from the one the timeline height was last fitted to (then remembers it).</summary>
    public bool TrackCountChanged(int count)
    {
        if (count == _fittedTrackCount) return false;
        _fittedTrackCount = count;
        return true;
    }

    private static void Commit(DocumentSession document, UndoController.UndoTransaction transaction)
    {
        var capture = document.Undo.Commit(transaction);
        if (capture.Stored) document.Playback.RememberBarMapping(capture.Snapshot);
    }
}
