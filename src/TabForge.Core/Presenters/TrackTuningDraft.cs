using TabForge.Models;
using TabForge.Services;

namespace TabForge.Presenters;

// Owns: the working strings of the track properties window: the tuning per string (high to low) and where each string came from in the track,
//     so adding, removing, retuning and presets are one edit list that OK writes as one change (TrackSetup.ApplyTuning plus the note moves).
// Does not own: the window, the retune rule (TrackSetup.ApplyTuning), the presets (TuningPresetMenu) or the undo step (DocumentEdits.Run in the window).
// Tests: TabForge.Avalonia.Tests.TrackTuningDraftTests.
// Threads: UI thread only.

/// <summary>The notes on strings a tuning draft removes, and the ones no remaining string can play at any fret (their pitch must change).</summary>
public sealed record TuningRemovalImpact(int Total, IReadOnlyList<TabNote> UnplayableNotes)
{
    public int Unplayable => UnplayableNotes.Count;
}

/// <summary>
/// The strings being edited. <see cref="Origin"/> holds, per working string, its index in the track's strings when OK is pressed (-1 for a string added here).
/// A string removed here is an original index missing from <see cref="Origin"/>; its notes are moved against the final working tunings to the nearest string that
/// can play the same pitch, else to the nearest playable pitch, never dropped.
/// </summary>
public sealed class TrackTuningDraft
{
    private readonly List<int> _tunings = new(), _origin = new();

    public TrackTuningDraft(IEnumerable<int> tunings) => Reset(tunings);

    public IReadOnlyList<int> Tunings => _tunings;
    public IReadOnlyList<int> Origin => _origin;
    public int Count => _tunings.Count;

    /// <summary>Starts again from the track's strings (nothing added, removed or moved).</summary>
    public void Reset(IEnumerable<int> tunings)
    {
        _tunings.Clear();
        _tunings.AddRange(tunings);
        _origin.Clear();
        _origin.AddRange(Enumerable.Range(0, _tunings.Count));
    }

    /// <summary>An independent copy of the working strings, so a structural edit can be judged before it replaces the open draft.</summary>
    public TrackTuningDraft Clone()
    {
        var clone = new TrackTuningDraft(_tunings);
        clone._origin.Clear();
        clone._origin.AddRange(_origin);
        return clone;
    }

    public bool CanAdd => Count < TrackPropertiesPresenter.MostStrings;
    public bool CanRemove => Count > TrackPropertiesPresenter.FewestStrings;

    /// <summary>Adds a thicker string a fourth below the lowest.</summary>
    public void AddLow()
    {
        if (!CanAdd) return;
        _tunings.Add(Math.Max(TrackPropertiesPresenter.LowestNote, _tunings.Count == 0 ? 40 : _tunings[^1] - 5));
        _origin.Add(-1);
    }

    /// <summary>Adds a string a fourth above the highest; every existing string moves one place down the list.</summary>
    public void AddHigh()
    {
        if (!CanAdd) return;
        _tunings.Insert(0, Math.Min(TrackPropertiesPresenter.HighestNote, _tunings.Count == 0 ? 64 : _tunings[0] + 5));
        _origin.Insert(0, -1);
    }

    public void RemoveLow()
    {
        if (!CanRemove) return;
        _tunings.RemoveAt(Count - 1);
        _origin.RemoveAt(_origin.Count - 1);
    }

    public void RemoveHigh()
    {
        if (!CanRemove) return;
        _tunings.RemoveAt(0);
        _origin.RemoveAt(0);
    }

    /// <summary>Grows or shrinks to <paramref name="count"/> (3 to 10) at the low end.</summary>
    public void ResizeTo(int count)
    {
        count = Math.Clamp(count, TrackPropertiesPresenter.FewestStrings, TrackPropertiesPresenter.MostStrings);
        while (Count < count) AddLow();
        while (Count > count) RemoveLow();
    }

    /// <summary>The working slots a resize to <paramref name="count"/> would drop (the lowest ones).</summary>
    public IReadOnlyList<int> SlotsDroppedBy(int count) => Enumerable.Range(Math.Max(0, count), Math.Max(0, Count - count)).ToList();

    public void Step(int index, int delta) => _tunings[index] = TrackPropertiesPresenter.Step(_tunings[index], delta);

    public void StepAll(int delta)
    {
        for (var i = 0; i < _tunings.Count; i++) Step(i, delta);
    }

    public void Set(int index, int midi) => _tunings[index] = Math.Clamp(midi, TrackPropertiesPresenter.LowestNote, TrackPropertiesPresenter.HighestNote);

    /// <summary>Takes a preset's strings; a preset with another string count resizes at the low end first.</summary>
    public void Apply(IReadOnlyList<int> highToLow)
    {
        ResizeTo(highToLow.Count);
        for (var i = 0; i < Count; i++) _tunings[i] = highToLow[i];
    }

    /// <summary>The notes on the strings in these working slots (strings added here have none): how many, and how many of them no remaining string can play at any fret,
    /// so their pitch would change to the nearest playable one. Playability is judged against the final working tunings, not the track's old ones.</summary>
    public (int Total, int Unplayable) NotesAtStake(TrackModel track, IEnumerable<int> slots)
    {
        var impact = ImpactAtSlots(track, slots);
        return (impact.Total, impact.Unplayable);
    }

    private TuningRemovalImpact ImpactAtSlots(TrackModel track, IEnumerable<int> slots)
    {
        var dropped = slots.Where(s => s >= 0 && s < _origin.Count).ToHashSet();
        var droppedStrings = dropped.Select(s => _origin[s]).Where(o => o >= 0).ToHashSet();
        var kept = _tunings.Where((_, i) => !dropped.Contains(i)).ToList();
        return Impact(track, droppedStrings, kept);
    }

    /// <summary>The notes on these strings of the track as it is now, judged against this draft's final working tunings.</summary>
    public TuningRemovalImpact NotesAtStakeForOrigins(TrackModel track, IEnumerable<int> originalStrings) =>
        Impact(track, originalStrings.Where(o => o >= 0).ToHashSet(), _tunings);

    /// <summary>Every note on a string this draft removes (an original index missing from <see cref="Origin"/>), judged against the final working tunings.</summary>
    public TuningRemovalImpact RemovedNotesAtStake(TrackModel track)
    {
        var keptOrigins = _origin.Where(o => o >= 0).ToHashSet();
        return NotesAtStakeForOrigins(track, Enumerable.Range(0, track.StringTunings.Count).Where(i => !keptOrigins.Contains(i)));
    }

    private static TuningRemovalImpact Impact(TrackModel track, HashSet<int> removedStrings, List<int> keptTunings)
    {
        if (removedStrings.Count == 0) return new TuningRemovalImpact(0, Array.Empty<TabNote>());
        int total = 0;
        var unplayable = new List<TabNote>();
        foreach (var note in AllNotes(track).Where(n => removedStrings.Contains(n.StringIndex)))
        {
            total++;
            var playable = false;
            for (var s = 0; s < keptTunings.Count && !playable; s++)
                playable = IsPlayable(track, note.MidiValue - keptTunings[s] - track.EffectiveCapo(s));
            if (!playable) unplayable.Add(note);
        }
        return new TuningRemovalImpact(total, unplayable);
    }

    // Each working string's tuning as the track has it now (a string added here takes its working tuning).
    private List<int> BeforeTunings(TrackModel track) =>
        _tunings.Select((t, i) => _origin[i] >= 0 && _origin[i] < track.StringTunings.Count ? track.StringTunings[_origin[i]] : t).ToList();

    private static bool IsPlayable(TrackModel track, int fret) => fret >= 0 && fret <= track.NumberOfFrets;

    /// <summary>Writes the strings to the track: notes of removed strings move to the nearest remaining string that plays the same pitch in the final working tunings
    /// (else to the nearest playable pitch), every note follows its string to its new place, then the tuning is applied (keep frets or keep pitches).</summary>
    public void Commit(TrackModel track, bool keepFrets)
    {
        var old = track.StringTunings;
        var final = _tunings.ToList();
        var before = BeforeTunings(track);
        if (_origin.SequenceEqual(Enumerable.Range(0, old.Count)) && before.SequenceEqual(final)) return;

        var removed = new List<(TabNote Note, int SourceString, int Midi)>();
        foreach (var note in AllNotes(track))
        {
            if (note.StringIndex < 0) continue;
            var slot = _origin.IndexOf(note.StringIndex);
            if (slot >= 0) { note.StringIndex = slot; continue; }
            removed.Add((note, note.StringIndex, note.MidiValue));
            note.StringIndex = -1;   // ApplyTuning must not touch a note whose string is gone
        }

        track.StringTunings = before;
        TrackSetup.ApplyTuning(track, final, keepFrets);
        if (!keepFrets) PreservePitchOrNearest(track, final);
        foreach (var (note, source, midi) in removed) MoveToFinal(track, note, source, midi, final);
    }

    // keepFrets=false: a pitch outside this string's frets cannot stay written on a stale fret; use the nearest playable fret and let the pitch follow it.
    private static void PreservePitchOrNearest(TrackModel track, List<int> tunings)
    {
        foreach (var note in AllNotes(track))
        {
            if (note.StringIndex < 0 || note.StringIndex >= tunings.Count) continue;
            var fret = note.MidiValue - tunings[note.StringIndex] - track.EffectiveCapo(note.StringIndex);
            if (IsPlayable(track, fret)) { note.Fret = fret; continue; }
            note.Fret = Math.Clamp(fret, 0, track.NumberOfFrets);
            var pitch = Math.Clamp(tunings[note.StringIndex] + track.EffectiveCapo(note.StringIndex) + note.Fret, 0, MidiLimits.Max);
            ShiftTargets(note, pitch - note.MidiValue);
            note.MidiValue = pitch;
        }
    }

    // Against the FINAL working tunings: the string closest by original position where the old pitch is a playable fret; when none is, the closest string takes the
    // nearest fret and the pitch follows it (the one pitch rule: tuning + capo + fret). A pitch that had to change carries its slide/trill target by the same interval.
    private void MoveToFinal(TrackModel track, TabNote note, int from, int midi, List<int> tunings)
    {
        int best = 0, bestFret = 0;
        var bestScore = double.MaxValue;
        for (var j = 0; j < tunings.Count; j++)
        {
            var fret = midi - tunings[j] - track.EffectiveCapo(j);
            var score = Math.Abs((_origin[j] >= 0 ? _origin[j] : j) - from) + (IsPlayable(track, fret) ? 0 : 1000) + Math.Abs(fret) * 0.001;
            if (score < bestScore) { bestScore = score; best = j; bestFret = fret; }
        }
        note.StringIndex = best;
        note.Fret = Math.Clamp(bestFret, 0, track.NumberOfFrets);
        var pitch = Math.Clamp(tunings[best] + track.EffectiveCapo(best) + note.Fret, 0, MidiLimits.Max);
        ShiftTargets(note, pitch - midi);
        note.MidiValue = pitch;
    }

    private static void ShiftTargets(TabNote note, int delta)
    {
        if (delta == 0) return;
        if (note.SlideTargetMidi > 0) note.SlideTargetMidi = Math.Clamp(note.SlideTargetMidi + delta, 0, MidiLimits.Max);
        if (note.TrillTargetMidi > 0) note.TrillTargetMidi = Math.Clamp(note.TrillTargetMidi + delta, 0, MidiLimits.Max);
    }

    private static IEnumerable<TabNote> AllNotes(TrackModel track) =>
        track.Measures.SelectMany(m => m.Cells.Concat(m.Voice2Cells)).SelectMany(c => c.Notes);
}
