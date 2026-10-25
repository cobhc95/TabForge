using System;
using System.Linq;
using TabForge.Documents;
using TabForge.Services;

namespace TabForge.Avalonia.Platform;

// Owns: the Band view's row counts per card: the live preview while a card's bottom edge is dragged, the one undo step that stores the count with the
//   song (SongProject.BandPaneRows), and applying the stored counts after a fit, an undo or a track change. Split from BandView.cs.
// Does not own: the rules (BandRowRules), the card's window of systems (BandTrackPanel) or the fit (BandCardFit).
// Tests: TabForge.Avalonia.Tests.BandRowsTests.
public sealed partial class BandView
{
    private const string RowsEditLabel = "Band rows";

    // The row count the dragged card had when the drag began (null: no drag); the release stores the count only if it changed.
    private int? _dragFromRows;

    // The stored counts of the shown cards, to tell an edit or an undo that moved one.
    private int? _rowsSignature;

    // While the edge is dragged the card shows the whole rows that fit the pointer's height; the song is not touched until the release.
    private void PreviewRows(BandTrackPanel panel, double height)
    {
        _dragFromRows ??= panel.SystemCount;
        panel.SystemCount = panel.SystemsForHeight(height);
        panel.ShowSystemsAround(_followedBar);
    }

    private void EndRowsDrag(BandTrackPanel panel)
    {
        var from = _dragFromRows;
        _dragFromRows = null;
        if (from is { } before && before != panel.SystemCount) StoreRows(panel, panel.SystemCount);
    }

    // The right-click menu's pick: the card shows the count at once and the song keeps it (one undo step).
    private void SetRows(BandTrackPanel panel, int rows)
    {
        rows = BandRowRules.Clamp(rows);
        if (rows == panel.SystemCount) return;
        panel.SystemCount = rows;
        panel.ShowSystemsAround(_followedBar);
        StoreRows(panel, rows);
    }

    private void StoreRows(BandTrackPanel panel, int rows)
    {
        if (panel.TrackIndex >= _document.Project.Tracks.Count) return;   // the card's track went with an edit
        var id = _document.Project.Tracks[panel.TrackIndex].Id;
        DocumentEdits.Run(_document, project => BandRowRules.Set(project, id, rows), invalidatesTimeline: false, label: RowsEditLabel);
    }

    private void ClearRows() => DocumentEdits.Run(_document, BandRowRules.Clear, invalidatesTimeline: false, label: RowsEditLabel);

    // Cards with a stored count show it; the fit has already given every card its default.
    private void ApplySavedRows()
    {
        foreach (var index in _current.Shown)
        {
            if (index >= _document.Project.Tracks.Count || !_current.Panels.TryGetValue(index, out var panel)) continue;
            if (BandRowRules.RowsOf(_document.Project, _document.Project.Tracks[index].Id) is { } rows) panel.SystemCount = rows;
        }
    }

    private static int RowsSignature(SongDocument song)
    {
        var hash = new HashCode();
        foreach (var track in song.Project.Tracks) hash.Add(BandRowRules.RowsOf(song.Project, track.Id));
        return hash.ToHashCode();
    }

    // An edit or an undo may have moved a stored count: the cards fit again, which applies the counts the song now holds.
    private void FollowRows()
    {
        var rows = RowsSignature(_document);
        if (rows == _rowsSignature) return;
        _rowsSignature = rows;
        FitCards();
        SlideCards();
    }
}
