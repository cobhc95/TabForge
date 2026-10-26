using TabForge.Services;

namespace TabForge.Presenters;

// Owns: the piano roll's second set of note edits: glue (a note with the next one of its pitch, or the selected notes of each pitch into one),
//       legato (each note stretched or trimmed to the next note's start), randomised velocities, humanised timing (both repeatable with a seed),
//       and chord stamps (a drawn note becomes a chord from the shared chord table).
// Does not own: the first set (PianoRollEdits.cs), the commit (PianoRollPresenter) or the chord table (MusicTheoryService.Chords).
// Tests: TabForge.Avalonia.Tests.PianoRollEditTests.
public static partial class PianoRollEdits
{
    /// <summary>The chord stamps offered (MusicTheoryService.Chords names); "Off" draws single notes.</summary>
    public static IReadOnlyList<string> ChordNames { get; } = new[] { "Off" }.Concat(MusicTheoryService.Chords.Keys).ToList();

    /// <summary>Glues note <paramref name="index"/> with the next note of the same pitch (and voice): one note from the first start to the later end.</summary>
    public static PianoRollEditResult GlueAt(IReadOnlyList<PianoRollNote> notes, int index)
    {
        if (index < 0 || index >= notes.Count) return new PianoRollEditResult(notes.ToList(), new List<int>());
        var note = notes[index];
        var next = Enumerable.Range(0, notes.Count)
            .Where(i => i != index && notes[i].Pitch == note.Pitch && notes[i].Voice == note.Voice && notes[i].Start >= note.Start)
            .OrderBy(i => notes[i].Start).DefaultIfEmpty(-1).First();
        return next < 0 ? new PianoRollEditResult(notes.ToList(), new List<int> { index }) : Join(notes, new[] { index, next });
    }

    /// <summary>Glues the selected notes of each pitch (and voice) into one note per pitch; a pitch with one selected note is left alone.</summary>
    public static PianoRollEditResult Glue(IReadOnlyList<PianoRollNote> notes, IReadOnlyCollection<int> selected)
    {
        var picked = Valid(notes, selected);
        var groups = picked.GroupBy(i => (notes[i].Pitch, notes[i].Voice)).Where(g => g.Count() > 1).ToList();
        if (groups.Count == 0) return new PianoRollEditResult(notes.ToList(), picked);
        var drop = groups.SelectMany(g => g).ToHashSet();
        var list = new List<PianoRollNote>();
        var chosen = new List<int>();
        for (var i = 0; i < notes.Count; i++)
        {
            if (drop.Contains(i)) continue;
            if (picked.Contains(i)) chosen.Add(list.Count);
            list.Add(notes[i]);
        }
        foreach (var group in groups)
        {
            var start = group.Min(i => notes[i].Start);
            var end = group.Max(i => notes[i].End);
            chosen.Add(list.Count);
            list.Add(notes[group.OrderBy(i => notes[i].Start).First()] with { Start = start, Length = end - start });
        }
        return new PianoRollEditResult(list, chosen);
    }

    private static PianoRollEditResult Join(IReadOnlyList<PianoRollNote> notes, IReadOnlyCollection<int> indexes)
    {
        var start = indexes.Min(i => notes[i].Start);
        var end = indexes.Max(i => notes[i].End);
        var first = indexes.OrderBy(i => notes[i].Start).First();
        var list = notes.Where((_, i) => !indexes.Contains(i) || i == first).ToList();
        var at = list.IndexOf(notes[first]);
        list[at] = notes[first] with { Start = start, Length = end - start };
        return new PianoRollEditResult(list, new List<int> { at });
    }

    /// <summary>Legato: each selected note (every note with none selected) ends where the next later note of its voice starts; the last keeps its length.</summary>
    public static PianoRollEditResult Legato(IReadOnlyList<PianoRollNote> notes, IReadOnlyCollection<int> selected)
    {
        var all = selected.Count == 0;
        var picked = all ? Enumerable.Range(0, notes.Count).ToList() : Valid(notes, selected);
        var result = Map(notes, picked, n =>
        {
            var next = notes.Where(o => o.Voice == n.Voice && o.Start > n.Start + MinLength / 2).Select(o => o.Start).DefaultIfEmpty(double.NaN).Min();
            return double.IsNaN(next) ? n : n with { Length = Math.Max(MinLength, next - n.Start) };
        });
        return all ? result with { Selected = new List<int>() } : result;
    }

    /// <summary>Each selected velocity moved by a random amount up to ±<paramref name="amount"/> (repeatable with <paramref name="seed"/>).</summary>
    public static PianoRollEditResult RandomiseVelocity(IReadOnlyList<PianoRollNote> notes, IReadOnlyCollection<int> selected, int amount, int seed)
    {
        var random = new Random(seed);
        var spread = Math.Abs(amount);
        return Map(notes, selected.OrderBy(i => i).ToList(), n => n with { Velocity = ClampVelocity(n.Velocity + random.Next(-spread, spread + 1)) });
    }

    /// <summary>Each selected note's start moved by a random amount up to ±<paramref name="beats"/>, its end kept (repeatable with <paramref name="seed"/>).</summary>
    public static PianoRollEditResult Humanise(IReadOnlyList<PianoRollNote> notes, IReadOnlyCollection<int> selected, double beats, int seed)
    {
        var random = new Random(seed);
        var spread = Math.Abs(beats);
        return Map(notes, selected.OrderBy(i => i).ToList(), n =>
        {
            var start = Math.Clamp(n.Start + (random.NextDouble() * 2 - 1) * spread, 0, n.End - MinLength);
            return n with { Start = start, Length = n.End - start };
        });
    }

    /// <summary>The notes a chord stamp draws for a root note: the root and the chord's other tones above it (just the root for "Off" or an unknown name).</summary>
    public static List<PianoRollNote> Chord(PianoRollNote root, string chord)
    {
        if (!MusicTheoryService.Chords.TryGetValue(chord, out var intervals)) return new List<PianoRollNote> { root };
        return intervals.Select(i => root with { Pitch = ClampPitch(root.Pitch + i) }).Distinct().ToList();
    }

    /// <summary>Adds several notes at once (a chord stamp); they are selected.</summary>
    public static PianoRollEditResult AddMany(IReadOnlyList<PianoRollNote> notes, IEnumerable<PianoRollNote> added)
    {
        var result = new PianoRollEditResult(notes.ToList(), new List<int>());
        foreach (var note in added)
        {
            var one = Add(result.Notes, note);
            result = new PianoRollEditResult(one.Notes, result.Selected.Append(one.Selected[0]).ToList());
        }
        return result;
    }
}
