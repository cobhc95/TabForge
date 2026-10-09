using TabForge.Models;

namespace TabForge.Services;

// Owns: the model half of commands reached from both a menu and a key, one behaviour per command (partial files: notes, paste,
//     paste special).
// Does not own: the editor's selection, drawing and menus.
// Tests: TestEditCommands, TestEditorCopyPaste.
/// <summary>
/// The model half of the commands that both a menu item and a keyboard shortcut reach. There is exactly one
/// behaviour per command: <c>TabEditorControl</c> calls these (capturing one undo step through its
/// <c>EditStarting</c> event) and the menu handlers call the editor, never the model directly.
/// Pure model code: no UI, no undo capture.
/// </summary>
public static partial class EditCommands
{
    /// <summary>Whether <paramref name="cursor"/> is a real slot of <paramref name="cells"/> with an earlier written beat before it.</summary>
    public static bool CanCopyLastBeat(IList<TabCell> cells, int cursor)
        => cursor > 0 && cursor < cells.Count && FindPreviousBeat(cells, cursor) is not null;

    /// <summary>
    /// Repeat beat: copies the nearest earlier beat of the same voice onto the cursor slot. Never clamps the target,
    /// never touches other slots, and does nothing (false) when the cursor is outside the bar or no earlier beat exists.
    /// </summary>
    public static bool CopyLastBeat(IList<TabCell> cells, int cursor)
    {
        if (cursor <= 0 || cursor >= cells.Count) return false;
        var source = FindPreviousBeat(cells, cursor);
        if (source is null) return false;
        var dest = cells[cursor];
        var clone = source.Clone();
        dest.Notes = clone.Notes;
        dest.DurationDenominator = clone.DurationDenominator;
        dest.Dots = clone.Dots;
        dest.IsTriplet = clone.IsTriplet;
        dest.TupletNumerator = clone.TupletNumerator;
        dest.TupletDenominator = clone.TupletDenominator;
        dest.RhythmicPosition = null;
        dest.ChordName = clone.ChordName;
        dest.IsRest = false;
        return true;
    }

    private static TabCell? FindPreviousBeat(IList<TabCell> cells, int cursor)
    {
        for (var i = Math.Min(cursor, cells.Count) - 1; i >= 0; i--)
            if (cells[i].Notes.Count > 0) return cells[i];
        return null;
    }

    /// <summary>Sets the dot count on every cell (the caller has already checked <c>CanSetDots</c>).</summary>
    public static void ApplyDots(IEnumerable<TabCell> cells, int dots)
    {
        dots = Math.Clamp(dots, 0, 2);
        foreach (var cell in cells) cell.Dots = dots;
    }

    /// <summary>Sets the written duration on every cell (the caller has already checked <c>CanSetDuration</c>).</summary>
    public static void ApplyDuration(IEnumerable<TabCell> cells, int denominator)
    {
        foreach (var cell in cells) cell.DurationDenominator = denominator;
    }

    /// <summary>
    /// Lays the beats of a plain bar end to end from its first beat by their written lengths, so a changed duration moves the beats after it
    /// (GP5 closes up instead of leaving gaps). Rests after the last note go unless <paramref name="keep"/> holds them (the rest fill refills the end).
    /// False, and nothing changes, for a bar with tuplets or free positions.
    /// </summary>
    public static bool CloseUp(List<TabCell> cells, int slots, IReadOnlySet<TabCell> keep)
    {
        if (!BarFill.IsPlain(cells)) return false;
        var beats = cells.Where(c => c.Notes.Count > 0 || c.IsRest || c.HasAnnotation).ToList();
        if (beats.Count == 0) return false;
        var start = cells.IndexOf(beats[0]);
        var last = beats.FindLastIndex(c => !c.IsRest || c.HasAnnotation || keep.Contains(c));
        beats.RemoveRange(last + 1, beats.Count - last - 1);
        cells.Clear();
        foreach (var beat in beats)
        {
            while (cells.Count < start) cells.Add(new TabCell());
            cells.Add(beat);
            start += Math.Max(1, MusicTime.CellSlotsRounded(beat));
        }
        while (cells.Count < Math.Max(slots, start)) cells.Add(new TabCell());
        return true;
    }

    /// <summary>Whether toggling the triplet should turn it on: off only when every cell is already a tuplet.</summary>
    public static bool NextTriplet(IReadOnlyCollection<TabCell> cells)
        => !cells.All(cell => cell.IsTriplet || cell.TupletNumerator > 0);

    /// <summary>Turns the triplet (3:2) on or off on one cell; an existing custom tuplet is kept when turning on.</summary>
    public static void ApplyTriplet(TabCell cell, bool triplet)
    {
        cell.IsTriplet = triplet;
        if (!triplet) { cell.TupletNumerator = 0; cell.TupletDenominator = 0; }
        else if (cell.TupletNumerator == 0) { cell.TupletNumerator = 3; cell.TupletDenominator = 2; }
    }

    /// <summary>
    /// Times the beats of a bar's voice from cell <paramref name="from"/> on one after another, each where the one before ends, as GP5 times
    /// them after a beat's length changed (triplet taken off the middle of a group: quiet GP5 b05 / b06). Cells keep their indices; a beat whose
    /// cell the onset rule already puts there loses its written position.
    /// </summary>
    public static void RetimeFrom(IReadOnlyList<TabCell> cells, int from)
    {
        static bool IsBeat(TabCell c) => c.Notes.Count > 0 || c.IsRest || c.HasAnnotation;
        var onsets = BarGrid.Onsets(cells);
        double? end = null;   // where the beat before ends; the first beat keeps its onset
        for (var i = 0; i < Math.Min(from, cells.Count); i++) if (IsBeat(cells[i])) end = onsets[i] + MusicTime.CellSlots(cells[i]);
        for (var i = Math.Max(0, from); i < cells.Count; i++)
        {
            if (!IsBeat(cells[i])) continue;
            var pos = end ?? onsets[i];
            cells[i].RhythmicPosition = Math.Abs(Math.Max(i, end ?? 0) - pos) < 1e-6 ? null : pos;
            end = pos + MusicTime.CellSlots(cells[i]);
        }
    }

    /// <summary>Staccato toggle over a selection: on unless every beat already has it.</summary>
    public static void ToggleStaccato(IReadOnlyList<TabCell> cells)
    {
        var value = !cells.All(cell => cell.Staccato);
        foreach (var cell in cells) cell.Staccato = value;
    }

    /// <summary>A bar's repeat marks are a song-level property: the bar is changed in every track.</summary>
    private static bool UpdateBar(SongProject project, int bar, Action<MeasureModel> update)
    {
        if (bar < 0) return false;
        var updated = false;
        foreach (var track in project.Tracks)
        {
            if (bar >= track.Measures.Count) continue;
            update(track.Measures[bar]);
            updated = true;
        }
        return updated;
    }

    private static MeasureModel? Bar(SongProject project, int trackIndex, int bar)
        => trackIndex >= 0 && trackIndex < project.Tracks.Count && bar >= 0 && bar < project.Tracks[trackIndex].Measures.Count
            ? project.Tracks[trackIndex].Measures[bar] : null;

    /// <summary>Toggles the repeat start; the state is read from the selected track's bar and applied to every track.</summary>
    public static bool ToggleRepeatOpen(SongProject project, int trackIndex, int bar, out bool enabled)
    {
        enabled = false;
        var selected = Bar(project, trackIndex, bar);
        if (selected is null) return false;
        var next = !selected.RepeatStart;
        enabled = next;
        return UpdateBar(project, bar, m => m.RepeatStart = next);
    }

    /// <summary>
    /// Toggles the repeat end. Turning it on uses <paramref name="count"/> (2-99) or, when none is given, the bar's
    /// existing count (at least 2). Turning it off keeps the count so toggling back restores it.
    /// </summary>
    public static bool ToggleRepeatClose(SongProject project, int trackIndex, int bar, int? count, out bool enabled)
    {
        enabled = false;
        var selected = Bar(project, trackIndex, bar);
        if (selected is null) return false;
        var next = !selected.RepeatEnd;
        enabled = next;
        var times = Math.Clamp(count ?? Math.Max(2, selected.RepeatCount), 2, TabForge.Playback.PlaybackOrder.MaxRepeats);
        return UpdateBar(project, bar, m =>
        {
            m.RepeatEnd = next;
            if (next) m.RepeatCount = times;
        });
    }

    /// <summary>Clears every beat of the voice (notes, rest and tie flags); the bar keeps its length and rhythm grid.</summary>
    public static bool EmptyBar(IList<TabCell> cells)
    {
        var changed = false;
        foreach (var cell in cells)
        {
            if (cell.Notes.Count == 0 && !cell.IsRest && !cell.IsTied) continue;
            cell.Notes.Clear(); cell.IsRest = false; cell.IsTied = false;
            changed = true;
        }
        return changed;
    }
}
