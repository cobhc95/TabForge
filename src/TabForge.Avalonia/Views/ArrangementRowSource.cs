using System;
using Avalonia.Threading;
using TabForge.Avalonia.Platform;
using TabForge.Documents;
using TabForge.Models;

namespace TabForge.Avalonia.Views;

// Owns: the arrangement's reload of the shown song's tracks. While the arrangement is attached it watches the DOCUMENT (a DocumentWatch): each timeline mark (the Add track strip,
//   the Track menu, the arm key) and each replaced song (undo, redo) re-reads the current project's tracks into the timeline and its header.
//   A reload that changed the row count or the base content height raises RowsCountChanged, so the window re-fits the track list.
// Does not own: the rows and their geometry (TimelineView, TrackHeaderColumn), the track list fit (the window's subscriber), and when the arrangement
//   attaches or shows another song (ArrangementView calls Watch and Unwatch).
// Tests: TabForge.Avalonia.Tests.TrackListCountFitTests, TabForge.Avalonia.Tests.ArrangementViewTests.
// Threads: UI thread only.
public sealed class ArrangementRowSource
{
    private readonly TimelineView _timeline;
    private readonly TrackHeaderColumn _header;

    // The document watched now; null while the arrangement is detached. Key: the document; ceiling: one; thread: UI only.
    private readonly DocumentWatch _watch = new();
    private IEditableDocument? _watched;

    public ArrangementRowSource(TimelineView timeline, TrackHeaderColumn header)
    {
        _timeline = timeline;
        _header = header;
        _watch.Marked = project =>
        {
            // The mark an undo or redo raises right after replacing the song finds the rows already reloaded.
            if (ReferenceEquals(project, _reloadedByReplace)) return;
            Reload(project);
        };
        _watch.ProjectChanged = (_, current) =>
        {
            Reload(current);
            _reloadedByReplace = current;
            // A replace that is not an undo or redo gets no Restored; the next turn clears the flag.
            Dispatcher.UIThread.Post(() => { if (ReferenceEquals(_reloadedByReplace, current)) _reloadedByReplace = null; });
        };
        _watch.Restored = () => _reloadedByReplace = null;
    }

    // The song the last replace reloaded, until the undo or redo that replaced it finishes. Key: one song; ceiling: one; thread: UI only.
    private SongProject? _reloadedByReplace;

    /// <summary>Raised when a reload changed the row count or the base content height (a track or group row added or removed, or an armed sub-row).</summary>
    public event Action? RowsCountChanged;

    /// <summary>Watches <paramref name="document"/> (its marks and its song being replaced) until <see cref="Unwatch"/>; watching the document already watched changes nothing.</summary>
    public void Watch(IEditableDocument document)
    {
        if (ReferenceEquals(_watched, document)) return;
        _watched = document;
        _watch.Bind(document);
    }

    /// <summary>Stops watching; a closed or hidden arrangement stops listening.</summary>
    public void Unwatch()
    {
        _watch.Dispose();
        _watched = null;
    }

    /// <summary>Re-reads the tracks of <paramref name="project"/> into the timeline and its header.</summary>
    public void Reload(SongProject project)
    {
        var rowsBefore = _timeline.RowCount;
        var heightBefore = _timeline.BaseContentHeight;
        _timeline.Reload(project);
        _header.Reload(project);
        if (_timeline.RowCount != rowsBefore || _timeline.BaseContentHeight != heightBefore) RowsCountChanged?.Invoke();
    }
}
