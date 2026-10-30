using System;
using TabForge.Documents;
using TabForge.Models;

namespace TabForge.Avalonia.Platform;

// Owns: the Band view's song following: the document listener (marks and replaced songs, so undo keeps the cards on the
//   current song), the track-following reconcile, the song-changed refresh and the bar signature the shared widths key on.
// Does not own: the cards (BandTrackPanel), the watching attach/detach (BandView) or the shared widths (ShareBars/FitCards).
// Tests: TabForge.Avalonia.Tests.BandViewTests, TabForge.Avalonia.Tests.BandCardTests.
public sealed partial class BandView
{
    // The attached view's listener on the DOCUMENT (marks and replaced songs), so an undo that swaps the project keeps it drawing the current one.
    private readonly DocumentWatch _watch = new();

    /// <summary>
    /// An edit (every edit marks the song's timeline). A card keeps its bars where they are while a note is edited in place, so the shared bar
    /// widths and the card fit are measured again only when the song's bars changed (a bar or a track came or went); otherwise the cards repaint
    /// the same layout, which shows the new notes without moving the tab around them.
    /// </summary>
    private void OnProjectMarked(SongProject project) => SongChanged();

    // The document's song was replaced (undo, redo): the cards' probes take the current song even when its bars match the old one (a rename-only undo).
    private void OnProjectReplaced(SongProject old, SongProject current) => TakeCurrentSong();

    // An undo or redo finished (also when the song object stayed): the probes take the current song again.
    private void OnRestored() => TakeCurrentSong();

    private void TakeCurrentSong()
    {
        _barSignature = null;
        SongChanged();
    }

    private void OnSongChanged(object? sender, EventArgs e)
    {
        SongChanged();
    }

    // A track was deleted, added or moved: every kept card follows its track by id, a card of a deleted track goes (an index kept across the edit would point at another track or past the end).
    private void FollowTracks()
    {
        var dropped = _current.Reconcile(_document.Project.Tracks);
        if (dropped.Count == 0) return;
        foreach (var panel in dropped)
        {
            panel.BindPlayback(null);
            _unbindStaff?.Invoke(panel.Staff);
        }
        if (_current.Shown.Count == 0 && _document.Project.Tracks.Count > 0) _current.ResetTo(DefaultTracks(_document));
        Refresh();
    }

    private void SongChanged()
    {
        FollowTracks();
        FollowRows();
        var bars = BarSignature(_document);
        if (bars != _barSignature)
        {
            _barSignature = bars;
            foreach (var panel in _current.Panels.Values)
                panel.Staff.Probe.ReplaceSong(_document.Project);
            ShareBars();
            RefreshInstrumentCursors();
            return;
        }
        foreach (var panel in _current.Panels.Values)
        {
            panel.Staff.RepaintSongChanged();
            panel.RefreshInstrumentCursor();
        }
    }

    // An edit may have added or removed the notes under a cursor: every card's fretboard re-lights.
    private void RefreshInstrumentCursors()
    {
        foreach (var panel in _current.Panels.Values) panel.RefreshInstrumentCursor();
    }

    // The song's bars as the cards lay them out (one count per track): a bar added, deleted or a track added changes it.
    private int? _barSignature;

    private static int BarSignature(SongDocument song)
    {
        var hash = new HashCode();
        foreach (var track in song.Project.Tracks) hash.Add(track.Measures.Count);
        return hash.ToHashCode();
    }
}
