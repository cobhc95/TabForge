using System;
using System.Collections.Generic;
using System.Linq;

namespace TabForge.Models;

// Owns: one song's timeline bar highlight: a rectangle of tab tracks x written bars (a clicked cell, a shift-click block or a dragged block), the corner Shift extends from,
//   and the snapshot kept with an undo step.
// Does not own: the selection or the loop (SelectionModel; the highlight never changes them), where the highlight draws (TimelineView), what edits do with it (HighlightEdits).
// Tests: TimelineBarHighlightModelTests.

/// <summary>A highlight stored with an undo step: its two corners (the anchor and the corner the keys and Shift move).</summary>
public readonly record struct BarHighlightSnapshot(bool Has, int AnchorTrack, int AnchorBar, int ActiveTrack, int ActiveBar, HighlightCells Extras = default);

/// <summary>The single cells a highlight holds besides its rectangle (Ctrl+click), as (track, bar) pairs packed in order; compares by content so an unchanged highlight records no undo step.</summary>
public readonly struct HighlightCells : IEquatable<HighlightCells>
{
    public HighlightCells(long[] items) => Items = items;
    public long[]? Items { get; }
    public bool Equals(HighlightCells other) => (Items ?? Array.Empty<long>()).AsSpan().SequenceEqual(other.Items ?? Array.Empty<long>());
    public override bool Equals(object? obj) => obj is HighlightCells other && Equals(other);
    public override int GetHashCode() => Items is { Length: > 0 } items ? (int)items[0] ^ items.Length : 0;
    public static long Pack(int track, int bar) => ((long)track << 32) | (uint)bar;
}

/// <summary>
/// The grey cell highlight of the arrangement timeline: tab tracks (by index) x written bars. It is a view state of the song, kept while another tab shows and never saved in the file.
/// A change raises <see cref="Changed"/>; recording it as an undo step is the caller's job (DocumentEdits.RecordHighlight). UI thread only.
/// </summary>
public sealed class BarHighlight
{
    /// <summary>The score/model selection a timeline selection is about to clear; folded into the next highlight-move undo step. UI thread only.</summary>
    public SelectionSnapshot? PendingSelection { get; set; }
    public bool Has { get; private set; }
    public int AnchorTrack { get; private set; } = -1;
    public int AnchorBar { get; private set; } = -1;
    public int ActiveTrack { get; private set; } = -1;
    public int ActiveBar { get; private set; } = -1;

    public int FirstTrack => Math.Min(AnchorTrack, ActiveTrack);
    public int LastTrack => Math.Max(AnchorTrack, ActiveTrack);
    public int FirstBar => Math.Min(AnchorBar, ActiveBar);
    public int LastBar => Math.Max(AnchorBar, ActiveBar);

    /// <summary>The number of highlighted bars (columns).</summary>
    public int BarCount => Has ? LastBar - FirstBar + 1 : 0;

    /// <summary>True when the cell (<paramref name="track"/>, <paramref name="bar"/>) lies inside the highlighted block.</summary>
    public bool Contains(int track, int bar) => Has && (InBlock(track, bar) || ExtraSet().Contains((track, bar)));

    private bool InBlock(int track, int bar) => track >= FirstTrack && track <= LastTrack && bar >= FirstBar && bar <= LastBar;

    private readonly List<(int Track, int Bar)> _extras = new();
    private HashSet<(int Track, int Bar)>? _extraSet;   // lookup mirror of _extras, rebuilt on demand after any change

    private HashSet<(int Track, int Bar)> ExtraSet() => _extraSet ??= new HashSet<(int Track, int Bar)>(_extras);

    /// <summary>The single cells outside the rectangle (Ctrl+click and Ctrl+box); empty in the common case.</summary>
    public IReadOnlyList<(int Track, int Bar)> Extras => _extras;

    /// <summary>The highlighted bars on <paramref name="track"/> as sorted, merged (first, last) runs, for any highlight (block or scattered); empty when none is on the track.</summary>
    public IReadOnlyList<(int First, int Last)> ScatteredBars(int track)
    {
        var runs = new List<(int First, int Last)>();
        if (!Has) return runs;
        var bars = new SortedSet<int>();
        if (track >= FirstTrack && track <= LastTrack) for (var b = FirstBar; b <= LastBar; b++) bars.Add(b);
        foreach (var (t, b) in _extras) if (t == track) bars.Add(b);
        foreach (var bar in bars)
        {
            if (runs.Count > 0 && runs[^1].Last == bar - 1) runs[^1] = (runs[^1].First, bar);
            else runs.Add((bar, bar));
        }
        return runs;
    }

    /// <summary>The number of cells outside the rectangle.</summary>
    public int ExtraCount => _extras.Count;

    /// <summary>Ctrl+click: the cell joins the highlight, or leaves it when it is in. Leaving a rectangle turns it into single cells (the first remaining cell becomes the rectangle); a set that is a full rectangle again is stored as the rectangle with no extras, so Copy and Cut see it whole.</summary>
    public bool Toggle(int track, int bar)
    {
        if (!Has) return Set(track, bar);
        var before = Snapshot();
        var cells = new List<(int Track, int Bar)>();
        for (var t = FirstTrack; t <= LastTrack; t++) for (var b = FirstBar; b <= LastBar; b++) cells.Add((t, b));
        cells.AddRange(_extras);
        if (!cells.Remove((track, bar))) cells.Add((track, bar));
        return Rebuild(cells, before);
    }

    /// <summary>Ctrl+box: the tab cells of the block between the two corners join the highlight (the existing cells stay).</summary>
    public bool AddBlock(int track1, int bar1, int track2, int bar2, Func<int, bool> isTabTrack)
    {
        if (!Has) return SetBlock(track1, bar1, track2, bar2);
        var before = Snapshot();
        var added = false;
        for (var t = Math.Min(track1, track2); t <= Math.Max(track1, track2); t++)
            for (var b = Math.Min(bar1, bar2); b <= Math.Max(bar1, bar2); b++)
                if (isTabTrack(t) && !Contains(t, b)) { _extras.Add((t, b)); ExtraSet().Add((t, b)); added = true; }
        if (!added) return false;
        var cells = new List<(int Track, int Bar)>();
        for (var t = FirstTrack; t <= LastTrack; t++) for (var b = FirstBar; b <= LastBar; b++) cells.Add((t, b));
        cells.AddRange(_extras);
        return Rebuild(cells, before);
    }

    private bool Rebuild(List<(int Track, int Bar)> cells, BarHighlightSnapshot before)
    {
        _extras.Clear();
        _extraSet = null;
        int minT = int.MaxValue, maxT = -1, minB = int.MaxValue, maxB = -1;
        foreach (var (t, b) in cells) { minT = Math.Min(minT, t); maxT = Math.Max(maxT, t); minB = Math.Min(minB, b); maxB = Math.Max(maxB, b); }
        if (cells.Count == 0) { Apply(false, -1, -1, -1, -1); }
        else if (cells.Count == (maxT - minT + 1) * (maxB - minB + 1) && new HashSet<(int, int)>(cells).Count == cells.Count)
            (Has, AnchorTrack, AnchorBar, ActiveTrack, ActiveBar) = (true, minT, minB, maxT, maxB);   // a full rectangle: stored whole, no extras
        else
        {
            (Has, AnchorTrack, AnchorBar, ActiveTrack, ActiveBar) = (true, cells[0].Track, cells[0].Bar, cells[0].Track, cells[0].Bar);
            for (var i = 1; i < cells.Count; i++) _extras.Add(cells[i]);
        }
        if (before == Snapshot()) return false;
        Changed?.Invoke();
        return true;
    }

    /// <summary>Raised after every real change.</summary>
    public event Action? Changed;

    /// <summary>Highlights the one cell, which becomes the anchor.</summary>
    public bool Set(int track, int bar) => Apply(true, track, bar, track, bar);

    /// <summary>Shift+click: the block between the anchor and the cell (no highlight yet: the cell alone).</summary>
    public bool ExtendTo(int track, int bar) => Has ? Apply(true, AnchorTrack, AnchorBar, track, bar) : Set(track, bar);

    /// <summary>A dragged block: from the cell where the drag started to the cell under the pointer.</summary>
    public bool SetBlock(int anchorTrack, int anchorBar, int track, int bar) => Apply(true, anchorTrack, anchorBar, track, bar);

    public bool Clear() => Apply(false, -1, -1, -1, -1);

    /// <summary>The cells outside the rectangle, as the undo step stores them.</summary>
    private HighlightCells ExtraKey() => _extras.Count == 0 ? default : new HighlightCells(_extras.Select(c => HighlightCells.Pack(c.Track, c.Bar)).ToArray());

    /// <summary>
    /// An arrow key: the active corner moves by one bar and/or to the next tab track in the direction, staying inside the song. Plain: the highlight becomes the one cell there;
    /// <paramref name="extend"/> (Shift): the anchor stays and the block grows or shrinks.
    /// </summary>
    public bool Move(int trackStep, int barStep, bool extend, int trackCount, int barCount, Func<int, bool> isTabTrack)
    {
        if (!Has || trackCount <= 0 || barCount <= 0) return false;
        var bar = Math.Clamp(ActiveBar + barStep, 0, barCount - 1);
        var track = ActiveTrack;
        if (trackStep != 0)
            for (var next = ActiveTrack + trackStep; next >= 0 && next < trackCount; next += trackStep)
                if (isTabTrack(next)) { track = next; break; }
        return extend ? Apply(true, AnchorTrack, AnchorBar, track, bar) : Set(track, bar);
    }

    /// <summary>Keeps the highlight inside a song of <paramref name="trackCount"/> tracks and <paramref name="barCount"/> bars (an edit removed some); it is cleared when the song has none.</summary>
    public bool ClampTo(int trackCount, int barCount)
    {
        if (!Has) return false;
        if (trackCount <= 0 || barCount <= 0) return Clear();
        var dropped = _extras.RemoveAll(c => c.Track >= trackCount || c.Bar >= barCount) > 0;
        _extraSet = null;
        if (dropped) Changed?.Invoke();
        return dropped | Apply(true, Math.Clamp(AnchorTrack, 0, trackCount - 1), Math.Clamp(AnchorBar, 0, barCount - 1), Math.Clamp(ActiveTrack, 0, trackCount - 1), Math.Clamp(ActiveBar, 0, barCount - 1), keepExtras: true);
    }

    /// <summary>
    /// Follows an edit: <paramref name="trackMap"/> and <paramref name="barMap"/> give each old index its new one (-1 = gone, null = unchanged). A block keeps the tracks and bars that survive
    /// (it shrinks when some went) and is cleared when none did; a bar insert shifts it, a track reorder moves it with its tracks.
    /// </summary>
    public bool Remap(int[]? trackMap, int[]? barMap)
    {
        if (!Has) return false;
        if (Surviving(FirstTrack, LastTrack, trackMap) is not var (firstTrack, lastTrack) || Surviving(FirstBar, LastBar, barMap) is not var (firstBar, lastBar)) return Clear();
        var (anchorTrack, activeTrack) = AnchorTrack <= ActiveTrack ? (firstTrack, lastTrack) : (lastTrack, firstTrack);
        var (anchorBar, activeBar) = AnchorBar <= ActiveBar ? (firstBar, lastBar) : (lastBar, firstBar);
        var hadExtras = _extras.Count > 0;
        _extraSet = null;
        for (var i = _extras.Count - 1; i >= 0; i--)
        {
            var (track, bar) = _extras[i];
            var (newTrack, newBar) = (trackMap is null ? track : track < trackMap.Length ? trackMap[track] : -1, barMap is null ? bar : bar < barMap.Length ? barMap[bar] : -1);
            if (newTrack < 0 || newBar < 0) _extras.RemoveAt(i); else _extras[i] = (newTrack, newBar);
        }
        var moved = Apply(true, anchorTrack, anchorBar, activeTrack, activeBar, keepExtras: true);
        if (!moved && hadExtras) Changed?.Invoke();   // the single cells may have moved with the rectangle unchanged
        return moved || hadExtras;
    }

    // The smallest and largest new index among the old indexes first..last that still exist; null when none do.
    private static (int First, int Last)? Surviving(int first, int last, int[]? map)
    {
        if (map is null) return (first, last);
        int? low = null, high = null;
        for (var old = first; old <= last && old < map.Length; old++)
        {
            if (old < 0 || map[old] < 0) continue;
            low = Math.Min(low ?? map[old], map[old]);
            high = Math.Max(high ?? map[old], map[old]);
        }
        return low is { } l && high is { } h ? (l, h) : null;
    }

    public BarHighlightSnapshot Snapshot() => new(Has, AnchorTrack, AnchorBar, ActiveTrack, ActiveBar, ExtraKey());

    /// <summary>Shows <paramref name="snapshot"/> again (undo or redo), kept inside the restored song.</summary>
    public void Restore(BarHighlightSnapshot snapshot, int trackCount, int barCount)
    {
        Apply(snapshot.Has, snapshot.AnchorTrack, snapshot.AnchorBar, snapshot.ActiveTrack, snapshot.ActiveBar);
        if (snapshot.Has && snapshot.Extras.Items is { } packed) { foreach (var cell in packed) _extras.Add(((int)(cell >> 32), (int)(cell & 0xFFFFFFFF))); _extraSet = null; }
        ClampTo(trackCount, barCount);
    }

    private bool Apply(bool has, int anchorTrack, int anchorBar, int activeTrack, int activeBar, bool keepExtras = false)
    {
        var droppedExtras = false;
        if (!keepExtras && _extras.Count > 0) { _extras.Clear(); _extraSet = null; droppedExtras = true; }
        if (has && (anchorTrack < 0 || anchorBar < 0 || activeTrack < 0 || activeBar < 0)) has = false;
        if (!has) (anchorTrack, anchorBar, activeTrack, activeBar) = (-1, -1, -1, -1);
        if (has == Has && anchorTrack == AnchorTrack && anchorBar == AnchorBar && activeTrack == ActiveTrack && activeBar == ActiveBar)
        {
            if (droppedExtras) Changed?.Invoke();
            return droppedExtras;
        }
        (Has, AnchorTrack, AnchorBar, ActiveTrack, ActiveBar) = (has, anchorTrack, anchorBar, activeTrack, activeBar);
        Changed?.Invoke();
        return true;
    }
}
