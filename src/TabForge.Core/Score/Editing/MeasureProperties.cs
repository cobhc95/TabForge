using TabForge.Models;

namespace TabForge.ScoreEditing;

// Owns: the bar-level property edits shared by the palette, the menus and the timeline: toggles of a bar flag, the swing cycle, simile
//   marks (one track only), line breaks, the tempo change and the directions, each applied to the same bar of every track.
// Does not own: undo (DocumentEdits.Run wraps these), signatures (BarSignatures) or bar insertion and removal (BarStructureEdits).
// Tests: no dedicated test class.
/// <summary>Bar flag edits on a project; every method returns whether a bar changed.</summary>
public static class MeasureProperties
{
    /// <summary>The bar of the given track, or null when either index is outside the song.</summary>
    public static MeasureModel? Get(SongProject project, int trackIndex, int bar) =>
        trackIndex >= 0 && trackIndex < project.Tracks.Count && bar >= 0 && bar < project.Tracks[trackIndex].Measures.Count
            ? project.Tracks[trackIndex].Measures[bar]
            : null;

    /// <summary>Applies <paramref name="update"/> to bar <paramref name="bar"/> of every track that has it.</summary>
    public static bool UpdateAtBar(SongProject project, int bar, Action<MeasureModel> update)
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

    public static bool TryToggle(SongProject project, int trackIndex, int bar, Func<MeasureModel, bool> get, Action<MeasureModel, bool> set, out bool value)
    {
        var selected = Get(project, trackIndex, bar);
        if (selected is null)
        {
            value = false;
            return false;
        }
        var next = !get(selected);
        value = next;
        return UpdateAtBar(project, bar, measure => set(measure, next));
    }

    /// <summary>Straight, eighth swing, sixteenth swing, straight again; <paramref name="next"/> is the new kind.</summary>
    public static bool TryCycleTripletFeel(SongProject project, int trackIndex, int bar, out string next)
    {
        var selected = Get(project, trackIndex, bar);
        if (selected is null)
        {
            next = TripletFeels.None;
            return false;
        }
        var nextValue = TripletFeels.Effective(selected) switch
        {
            TripletFeels.None => TripletFeels.Eighth,
            TripletFeels.Eighth => TripletFeels.Sixteenth,
            _ => TripletFeels.None
        };
        next = nextValue;
        return UpdateAtBar(project, bar, measure =>
        {
            measure.TripletFeelKind = nextValue;
            measure.TripletFeel = nextValue != TripletFeels.None;
        });
    }

    /// <summary>Moves the cursor bar's clef to the next one in <see cref="Clefs.Cycle"/> (wrapping); <paramref name="clef"/> is the new clef.</summary>
    public static bool TryCycleClef(SongProject project, int trackIndex, int bar, out string clef)
    {
        var selected = Get(project, trackIndex, bar);
        if (selected is null)
        {
            clef = "";
            return false;
        }
        var order = Clefs.Cycle;
        var next = order[(Array.IndexOf(order, selected.Clef) + 1 + order.Length) % order.Length];
        clef = next;
        return UpdateAtBar(project, bar, measure => measure.Clef = next);
    }

    /// <summary>Toggles the one-bar (1) or two-bar (2) repeat mark on the given track only; the first bars of the song have nothing to repeat.</summary>
    public static bool TryToggleSimile(SongProject project, int trackIndex, int bar, int barCount, out bool enabled)
    {
        var selected = Get(project, trackIndex, bar);
        if (selected is null || barCount is not (1 or 2) || bar < barCount)
        {
            enabled = false;
            return false;
        }
        var next = !(barCount == 1 ? selected.SimileOneBar : selected.SimileTwoBar);
        enabled = next;
        selected.SimileOneBar = next && barCount == 1;   // a simile mark belongs to one track's bar, not the song
        selected.SimileTwoBar = next && barCount == 2;
        return true;
    }

    /// <summary>Toggles the forced (<paramref name="force"/>) or the prevented system break before the bar.</summary>
    public static bool TryToggleLineBreak(SongProject project, int trackIndex, int bar, bool force, out bool enabled)
    {
        var selected = Get(project, trackIndex, bar);
        if (selected is null)
        {
            enabled = false;
            return false;
        }
        var next = force ? !selected.ForceLineBreak : !selected.PreventLineBreak;
        enabled = next;
        return UpdateAtBar(project, bar, measure =>
        {
            if (force) measure.ForceLineBreak = next;
            else measure.PreventLineBreak = next;
        });
    }

    public static bool SetTempoChange(SongProject project, int bar, int tempo) => UpdateAtBar(project, bar, measure => measure.TempoChange = tempo);

    public static bool SetDirections(SongProject project, int bar, string directions, int alternateEnding) =>
        UpdateAtBar(project, bar, measure =>
        {
            measure.Directions = directions;
            measure.AlternateEnding = alternateEnding;
            measure.AlternateEndingMask = 0;
        });
}
