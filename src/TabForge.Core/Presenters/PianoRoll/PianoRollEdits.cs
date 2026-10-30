namespace TabForge.Presenters;

// Owns: the piano roll's note edits as pure functions on a note list and the selected indexes: move and copy (time and pitch), resize from either
//       edge, velocity (set, shift, scale, ramp), mute, delete, split, add, duplicate, paste and quantize (destructive, with strength and
//       starts / ends). Each returns the new list and which of its notes are selected, so the caller commits it as one undo step.
// Does not own: storing notes (the sources), the undo step and the tab prompt (PianoRollPresenter), the snap (PianoRollSnap).
// Tests: TabForge.Avalonia.Tests.PianoRollEditTests.

/// <summary>An edit's outcome: the whole note list after it, and the indexes of the notes selected in it.</summary>
public sealed record PianoRollEditResult(List<PianoRollNote> Notes, List<int> Selected);

public static partial class PianoRollEdits
{
    /// <summary>The shortest note an edit leaves (a 128th note, in beats).</summary>
    public const double MinLength = 1.0 / 32;

    public static int ClampPitch(int pitch) => Math.Clamp(pitch, 0, MidiLimits.Max);

    public static int ClampVelocity(int velocity) => Math.Clamp(velocity, 1, MidiLimits.Max);

    private static List<int> Valid(IReadOnlyList<PianoRollNote> notes, IReadOnlyCollection<int> selected) =>
        selected.Where(i => i >= 0 && i < notes.Count).Distinct().ToList();

    private static PianoRollEditResult Map(IReadOnlyList<PianoRollNote> notes, IReadOnlyCollection<int> selected, Func<PianoRollNote, PianoRollNote> change)
    {
        var list = notes.ToList();
        var picked = Valid(notes, selected);
        foreach (var i in picked) list[i] = change(list[i]);
        return new PianoRollEditResult(list, picked);
    }

    /// <summary>Moves the selected notes by <paramref name="beats"/> and <paramref name="semitones"/> (none before beat 0, all pitches in MIDI range);
    /// with <paramref name="copy"/> the moved notes are new ones and the originals stay.</summary>
    public static PianoRollEditResult Move(IReadOnlyList<PianoRollNote> notes, IReadOnlyCollection<int> selected, double beats, int semitones, bool copy = false)
    {
        var picked = Valid(notes, selected);
        if (picked.Count == 0) return new PianoRollEditResult(notes.ToList(), picked);
        var dt = Math.Max(beats, -picked.Min(i => notes[i].Start));
        var dp = Math.Clamp(semitones, -picked.Min(i => notes[i].Pitch), MidiLimits.Max - picked.Max(i => notes[i].Pitch));
        PianoRollNote Moved(PianoRollNote n) => n with { Start = n.Start + dt, Pitch = n.Pitch + dp };
        if (!copy) return Map(notes, picked, Moved);
        var list = notes.ToList();
        var added = new List<int>();
        foreach (var i in picked)
        {
            added.Add(list.Count);
            list.Add(Moved(notes[i]));
        }
        return new PianoRollEditResult(list, added);
    }

    /// <summary>Moves the end (or, with <paramref name="start"/>, the start) of every selected note by <paramref name="beats"/>; no note shorter than
    /// <see cref="MinLength"/>, none before beat 0.</summary>
    public static PianoRollEditResult Resize(IReadOnlyList<PianoRollNote> notes, IReadOnlyCollection<int> selected, double beats, bool start = false) =>
        Map(notes, selected, n => start ? ResizeStart(n, beats) : n with { Length = Math.Max(MinLength, n.Length + beats) });

    private static PianoRollNote ResizeStart(PianoRollNote n, double beats)
    {
        var newStart = Math.Clamp(n.Start + beats, 0, n.End - MinLength);
        return n with { Start = newStart, Length = n.End - newStart };
    }

    /// <summary>Sets every selected note's length to <paramref name="length"/> beats.</summary>
    public static PianoRollEditResult SetLength(IReadOnlyList<PianoRollNote> notes, IReadOnlyCollection<int> selected, double length) =>
        Map(notes, selected, n => n with { Length = Math.Max(MinLength, length) });

    public static PianoRollEditResult SetVelocity(IReadOnlyList<PianoRollNote> notes, IReadOnlyCollection<int> selected, int velocity) =>
        Map(notes, selected, n => n with { Velocity = ClampVelocity(velocity) });

    public static PianoRollEditResult ShiftVelocity(IReadOnlyList<PianoRollNote> notes, IReadOnlyCollection<int> selected, int delta) =>
        Map(notes, selected, n => n with { Velocity = ClampVelocity(n.Velocity + delta) });

    public static PianoRollEditResult ScaleVelocity(IReadOnlyList<PianoRollNote> notes, IReadOnlyCollection<int> selected, double percent) =>
        Map(notes, selected, n => n with { Velocity = ClampVelocity((int)Math.Round(n.Velocity * percent / 100)) });

    /// <summary>Velocities from <paramref name="first"/> at the earliest selected note to <paramref name="last"/> at the latest, in a straight line over time.</summary>
    public static PianoRollEditResult Ramp(IReadOnlyList<PianoRollNote> notes, IReadOnlyCollection<int> selected, int first, int last)
    {
        var picked = Valid(notes, selected);
        if (picked.Count == 0) return new PianoRollEditResult(notes.ToList(), picked);
        var from = picked.Min(i => notes[i].Start);
        var span = picked.Max(i => notes[i].Start) - from;
        return Map(notes, picked, n => n with { Velocity = ClampVelocity((int)Math.Round(first + (last - first) * (span > 0 ? (n.Start - from) / span : 0))) });
    }

    /// <summary>Mutes the selected notes, or unmutes them all when every one is muted already.</summary>
    public static PianoRollEditResult ToggleMute(IReadOnlyList<PianoRollNote> notes, IReadOnlyCollection<int> selected)
    {
        var picked = Valid(notes, selected);
        var mute = picked.Any(i => !notes[i].Muted);
        return Map(notes, picked, n => n with { Muted = mute });
    }

    public static PianoRollEditResult SetMuted(IReadOnlyList<PianoRollNote> notes, IReadOnlyCollection<int> selected, bool muted) =>
        Map(notes, selected, n => n with { Muted = muted });

    public static PianoRollEditResult Delete(IReadOnlyList<PianoRollNote> notes, IReadOnlyCollection<int> selected)
    {
        var drop = selected.ToHashSet();
        return new PianoRollEditResult(notes.Where((_, i) => !drop.Contains(i)).ToList(), new List<int>());
    }

    /// <summary>Cuts note <paramref name="index"/> at <paramref name="at"/> into two (nothing when the cut is not inside it); both halves are selected.</summary>
    public static PianoRollEditResult Split(IReadOnlyList<PianoRollNote> notes, int index, double at)
    {
        var list = notes.ToList();
        if (index < 0 || index >= list.Count) return new PianoRollEditResult(list, new List<int>());
        var n = list[index];
        if (at <= n.Start + MinLength / 2 || at >= n.End - MinLength / 2) return new PianoRollEditResult(list, new List<int> { index });
        list[index] = n with { Length = at - n.Start };
        list.Add(n with { Start = at, Length = n.End - at });
        return new PianoRollEditResult(list, new List<int> { index, list.Count - 1 });
    }

    public static PianoRollEditResult Add(IReadOnlyList<PianoRollNote> notes, PianoRollNote note)
    {
        var list = notes.ToList();
        list.Add(note with { Start = Math.Max(0, note.Start), Length = Math.Max(MinLength, note.Length), Pitch = ClampPitch(note.Pitch), Velocity = ClampVelocity(note.Velocity) });
        return new PianoRollEditResult(list, new List<int> { list.Count - 1 });
    }

    /// <summary>Copies of the selected notes right after the selection (shifted by its span); the copies are selected.</summary>
    public static PianoRollEditResult Duplicate(IReadOnlyList<PianoRollNote> notes, IReadOnlyCollection<int> selected)
    {
        var picked = Valid(notes, selected);
        if (picked.Count == 0) return new PianoRollEditResult(notes.ToList(), picked);
        var span = picked.Max(i => notes[i].End) - picked.Min(i => notes[i].Start);
        return Move(notes, picked, span, 0, copy: true);
    }

    /// <summary>The clipboard form of the selected notes: their starts made relative to the earliest one.</summary>
    public static List<PianoRollNote> Copy(IReadOnlyList<PianoRollNote> notes, IReadOnlyCollection<int> selected)
    {
        var picked = Valid(notes, selected).Select(i => notes[i]).ToList();
        if (picked.Count == 0) return picked;
        var from = picked.Min(n => n.Start);
        return picked.Select(n => n with { Start = n.Start - from }).ToList();
    }

    /// <summary>Adds <paramref name="clip"/> (relative starts) at beat <paramref name="at"/>; the pasted notes are selected.</summary>
    public static PianoRollEditResult Paste(IReadOnlyList<PianoRollNote> notes, IReadOnlyList<PianoRollNote> clip, double at)
    {
        var list = notes.ToList();
        var added = new List<int>();
        foreach (var n in clip)
        {
            added.Add(list.Count);
            list.Add(n with { Start = Math.Max(0, at) + n.Start });
        }
        return new PianoRollEditResult(list, added);
    }

    /// <summary>
    /// Quantizes the selected notes (every note when none is selected) to <paramref name="grid"/>: each start moves <paramref name="strength"/>
    /// (0 to 1) of the way to its nearest grid line, keeping its length; with <paramref name="ends"/> each end moves the same share towards its own
    /// nearest line. A note never ends up shorter than <see cref="MinLength"/>. Destructive: the original times are not kept.
    /// </summary>
    public static PianoRollEditResult Quantize(IReadOnlyList<PianoRollNote> notes, IReadOnlyCollection<int> selected, PianoRollTimeMap map, string grid,
        double strength, bool starts, bool ends, double swing = 0)
    {
        var all = selected.Count == 0;
        var picked = all ? Enumerable.Range(0, notes.Count).ToList() : Valid(notes, selected);
        var share = Math.Clamp(strength, 0, 1);
        var result = Map(notes, picked, n => QuantizeOne(n, map, grid, share, starts, ends, swing));
        return all ? result with { Selected = new List<int>() } : result;
    }

    private static PianoRollNote QuantizeOne(PianoRollNote n, PianoRollTimeMap map, string grid, double share, bool starts, bool ends, double swing)
    {
        var start = starts ? n.Start + (map.NearestGrid(n.Start, grid, swing) - n.Start) * share : n.Start;
        var end = start + n.Length;
        if (ends) end += (map.NearestGrid(end, grid, swing) - end) * share;
        return n with { Start = Math.Max(0, start), Length = Math.Max(MinLength, end - start) };
    }

    /// <summary>Flips each listed note's mute on its own (a paint gesture: mixed notes each toggle once).</summary>
    public static PianoRollEditResult FlipMute(IReadOnlyList<PianoRollNote> notes, IReadOnlyCollection<int> selected)
    {
        var list = notes.ToList();
        foreach (var i in Valid(notes, selected)) list[i] = list[i] with { Muted = !list[i].Muted };
        return new PianoRollEditResult(list, Valid(notes, selected));
    }

    /// <summary>Cuts each listed note at its beat (nothing for cuts outside their note); one step for a split-line drag.</summary>
    public static PianoRollEditResult SplitMany(IReadOnlyList<PianoRollNote> notes, IEnumerable<(int Index, double At)> cuts)
    {
        var list = notes.ToList();
        var chosen = new List<int>();
        foreach (var (index, at) in cuts.OrderBy(c => c.Index))
        {
            if (index < 0 || index >= list.Count) continue;
            var n = list[index];
            if (at <= n.Start + MinLength / 2 || at >= n.End - MinLength / 2) continue;
            list[index] = n with { Length = at - n.Start };
            chosen.Add(index);
            chosen.Add(list.Count);
            list.Add(n with { Start = at, Length = n.End - at });
        }
        return new PianoRollEditResult(list, chosen);
    }

    /// <summary>The indexes of the notes whose rectangle meets the box from beat <paramref name="from"/> to <paramref name="to"/> and pitch
    /// <paramref name="low"/> to <paramref name="high"/> (both inclusive).</summary>
    public static List<int> InBox(IReadOnlyList<PianoRollNote> notes, double from, double to, int low, int high)
    {
        var list = new List<int>();
        for (var i = 0; i < notes.Count; i++)
            if (notes[i].Pitch >= low && notes[i].Pitch <= high && notes[i].Start < to && notes[i].End > from) list.Add(i);
        return list;
    }

    /// <summary>Snaps every selected note to the nearest pitch of <paramref name="scale"/> (ties up with <paramref name="direction"/> >= 0);
    /// a null scale keeps every pitch.</summary>
    public static PianoRollEditResult SnapToScale(IReadOnlyList<PianoRollNote> notes, IReadOnlyCollection<int> selected, IReadOnlySet<int>? scale, int direction = 0) =>
        Map(notes, selected, n => n with { Pitch = PianoRollScaleLock.Snap(n.Pitch, scale, direction) });

    /// <summary>Moves every selected note by <paramref name="steps"/> scale degrees of <paramref name="scale"/> (semitones with no scale).</summary>
    public static PianoRollEditResult TransposeSteps(IReadOnlyList<PianoRollNote> notes, IReadOnlyCollection<int> selected, IReadOnlySet<int>? scale, int steps) =>
        Map(notes, selected, n => n with { Pitch = PianoRollScaleLock.Step(n.Pitch, scale, steps) });
}
