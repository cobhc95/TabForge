using System;
using System.Collections.Generic;
using System.Linq;
using TabForge.Models;

namespace TabForge.Avalonia.Platform;

// Owns: one song's Band cards as the Band view keeps them while the song is open: the tracks whose cards show, in song order; which of those
//   fretboards are on; the selected card; and the card panels, so each song's staffs and their playback binding stay with the song.
// Does not own: the view that shows them and the rules for a default (BandView), a card (BandTrackPanel), the song (SongDocument).
// Tests: TabForge.Avalonia.Tests.BandCardTests.
// Threads: UI thread only. One instance per open song, held by BandView in a ConditionalWeakTable keyed by the song (so a closed song drops out).
internal sealed class BandSongCards
{
    // The ids of the song's tracks in the order the indices below refer to; Reconcile moves the indices when the order changes.
    private List<Guid> _trackIds;

    public BandSongCards(IEnumerable<int> defaultTracks, IReadOnlyList<TrackModel> tracks)
    {
        _trackIds = tracks.Select(track => track.Id).ToList();
        ResetTo(defaultTracks);
    }

    /// <summary>The track indices whose cards show, in song order.</summary>
    public List<int> Shown { get; } = new();

    /// <summary>The tracks whose card shows its fretboard.</summary>
    public HashSet<int> FretboardsOn { get; } = new();

    /// <summary>The card panels made so far, by track index (a hidden track keeps its panel).</summary>
    public Dictionary<int, BandTrackPanel> Panels { get; } = new();

    /// <summary>The selected card's track index, or -1 when no card shows.</summary>
    public int Selected { get; set; } = -1;

    /// <summary>
    /// Follows the song's tracks by id after a track was deleted, added or moved: every kept index moves to its track's new place, a deleted track's
    /// card and fretboard flag go. A card whose track moved is dropped (it was built for the old index) and comes back built again.
    /// Returns the dropped cards so the caller can unbind them.
    /// </summary>
    public List<BandTrackPanel> Reconcile(IReadOnlyList<TrackModel> tracks)
    {
        var dropped = new List<BandTrackPanel>();
        var ids = tracks.Select(track => track.Id).ToList();
        if (ids.SequenceEqual(_trackIds)) return dropped;
        int Moved(int old) => old >= 0 && old < _trackIds.Count ? ids.IndexOf(_trackIds[old]) : -1;
        var shown = Shown.Select(Moved).Where(index => index >= 0).Order().ToList();
        var selected = Moved(Selected);
        var fretboards = FretboardsOn.Select(Moved).Where(index => index >= 0).ToList();
        foreach (var (old, panel) in Panels.ToList())
        {
            if (Moved(old) == old) continue;
            dropped.Add(panel);
            Panels.Remove(old);
        }
        Shown.Clear();
        Shown.AddRange(shown);
        FretboardsOn.Clear();
        FretboardsOn.UnionWith(fretboards);
        Selected = selected >= 0 ? selected : Shown.Count > 0 ? Shown[0] : -1;
        _trackIds = ids;
        return dropped;
    }

    /// <summary>Shows the given tracks, the first one's fretboard on and the first one selected; every other fretboard off.</summary>
    public void ResetTo(IEnumerable<int> defaultTracks)
    {
        Shown.Clear();
        Shown.AddRange(defaultTracks);
        FretboardsOn.Clear();
        FretboardsOn.UnionWith(Shown.Take(1));
        Selected = Shown.Count > 0 ? Shown[0] : -1;
    }
}
