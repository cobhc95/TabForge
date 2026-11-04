using System.Text;

namespace TabForge.Models;

// Owns: the partial-capo rules shared by the model, the editor, the fretboard and the score header: the fret range and count,
//     which string a mask covers, the effective capo per string, and the refit after a string-count change.
// Does not own: the track's model (TrackModel), the note shift (TrackSetup), the drawing or the save format.
// Tests: TabForge.Avalonia.Tests.PartialCapoTests.
public static class PartialCapoRules
{
    /// <summary>Most partial capos one track keeps (the Track properties card's Add limit).</summary>
    public const int MaxCount = 4;

    /// <summary>Partial-capo fret range, in absolute frets like the full capo.</summary>
    public const int MinFret = 0, MaxFret = 24;

    /// <summary>True when the partial at fret <paramref name="fret"/> with <paramref name="mask"/> covers string <paramref name="stringIndex"/>.</summary>
    public static bool Covers(int fret, int mask, int stringIndex) =>
        fret > 0 && stringIndex >= 0 && stringIndex < 31 && (mask & (1 << stringIndex)) != 0;

    /// <summary>
    /// The capo a string sounds behind: the higher of the full capo and every partial capo covering the string.
    /// A partial at fret 0/off, below the full capo, or with no bits adds nothing.
    /// </summary>
    public static int EffectiveCapo(int fullCapo, IReadOnlyList<PartialCapo>? partials, int stringIndex)
    {
        var capo = Math.Max(0, fullCapo);
        if (partials is null) return capo;
        for (var i = 0; i < partials.Count; i++)
        {
            var partial = partials[i];
            if (partial is not null && partial.Fret > capo && Covers(partial.Fret, partial.StringMask, stringIndex)) capo = partial.Fret;
        }
        return capo;
    }

    /// <summary>The mask with the bits of strings that no longer exist cleared (a bit for a removed string never comes back).</summary>
    public static int Refit(int mask, int stringCount) =>
        stringCount >= 31 ? mask : mask & ((1 << Math.Max(0, stringCount)) - 1);

    /// <summary>A deep copy of the list, or null when there is none.</summary>
    public static List<PartialCapo>? Copy(IReadOnlyList<PartialCapo>? partials) =>
        partials is { Count: > 0 } ? partials.Select(p => p.Clone()).ToList() : null;

    /// <summary>
    /// The list as the model keeps it: at most <see cref="MaxCount"/>, frets clamped, masks refit to the strings this track has,
    /// and off/empty entries dropped. Null when nothing is left, so a track without partials saves no field at all.
    /// </summary>
    public static List<PartialCapo>? Normalize(IReadOnlyList<PartialCapo>? partials, int stringCount)
    {
        if (partials is null) return null;
        List<PartialCapo>? kept = null;
        for (var i = 0; i < partials.Count && (kept?.Count ?? 0) < MaxCount; i++)
        {
            var partial = partials[i];
            if (partial is null) continue;
            var fret = Math.Clamp(partial.Fret, MinFret, MaxFret);
            var mask = Refit(partial.StringMask, stringCount);
            if (fret <= 0 || mask == 0) continue;
            (kept ??= new()).Add(new PartialCapo { Fret = fret, StringMask = mask });
        }
        return kept;
    }

    /// <summary>True when both lists hold the same partials in the same order (null and empty are the same).</summary>
    public static bool Same(IReadOnlyList<PartialCapo>? a, IReadOnlyList<PartialCapo>? b)
    {
        var aCount = a?.Count ?? 0;
        var bCount = b?.Count ?? 0;
        if (aCount != bCount) return false;
        for (var i = 0; i < aCount; i++)
            if (!a![i].SameAs(b![i])) return false;
        return true;
    }

    /// <summary>The score header's capo line: <c>"Capo 2, partial 5 (1-3)"</c>, <c>"Capo 2"</c>, <c>"Partial 5 (1-3)"</c>, or empty.</summary>
    public static string CapoLine(int fullCapo, IReadOnlyList<PartialCapo>? partials)
    {
        var described = Describe(partials);
        if (Math.Max(0, fullCapo) > 0)
            return described.Length == 0 ? $"Capo {fullCapo}" : $"Capo {fullCapo}, {described}";
        if (described.Length == 0) return "";
        return char.ToUpperInvariant(described[0]) + described[1..];
    }

    /// <summary>True when the track needs a capo line above the staff (a full capo or at least one effective partial).</summary>
    public static bool HasLine(int fullCapo, IReadOnlyList<PartialCapo>? partials)
    {
        if (fullCapo > 0) return true;
        if (partials is null) return false;
        for (var i = 0; i < partials.Count; i++)
            if (partials[i] is { Fret: > 0, StringMask: not 0 }) return true;
        return false;
    }

    /// <summary>The score header's partials, e.g. <c>"partial 5 (1-3)"</c>, joined by <c>", "</c>; empty when there are none.</summary>
    public static string Describe(IReadOnlyList<PartialCapo>? partials)
    {
        if (partials is null || partials.Count == 0) return "";
        var text = new StringBuilder();
        for (var i = 0; i < partials.Count; i++)
        {
            if (text.Length > 0) text.Append(", ");
            var partial = partials[i];
            text.Append("partial ").Append(partial.Fret).Append(" (");
            AppendStrings(text, partial.StringMask);
            text.Append(')');
        }
        return text.ToString();
    }

    // 1-based string numbers, contiguous runs joined with a dash: 0b000111 -> "1-3".
    private static void AppendStrings(StringBuilder text, int mask)
    {
        var start = -1;
        for (var s = 0; s <= 31; s++)
        {
            var set = s < 31 && (mask & (1 << s)) != 0;
            if (set && start < 0) start = s;
            else if (set) continue;
            else if (start >= 0)
            {
                if (text.Length > 0 && text[^1] != '(') text.Append(',');
                text.Append(start + 1);
                if (s - 1 > start) text.Append('-').Append(s);
                start = -1;
            }
        }
    }
}
