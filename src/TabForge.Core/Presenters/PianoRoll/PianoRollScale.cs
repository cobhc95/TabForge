using TabForge.Services;

namespace TabForge.Presenters;

// Owns: the piano roll's scale and row rules: which pitch classes the Scale highlight setting ("A Minor Pentatonic") holds, and which pitches the
//       rows show from top to bottom with the fold (None: every pitch; Scale: the pitches in the scale; Used: the pitches the notes use), and the
//       note colour modes.
// Does not own: the setting (EditingSettings.ScaleHighlight, the scale table MusicTheoryService.Scales) or the drawing (the app's PianoRollSurface).
// Tests: TabForge.Avalonia.Tests.PianoRollEditTests.
public static class PianoRollScale
{
    public const string FoldNone = "None", FoldScale = "Scale", FoldUsed = "Used";
    public const int MinUsedFold = 3;
    public static IReadOnlyList<string> Folds{ get; } = new[] { FoldNone, FoldScale, FoldUsed };
    public static IReadOnlyList<string> ColourModes { get; } = new[] { "Track", "Velocity", "Lane", "Pitch", "Channel" };

    /// <summary>The pitch classes (0 = C) of a highlight such as "A Minor Pentatonic"; null when it names no scale.</summary>
    public static IReadOnlySet<int>? PitchClasses(string? highlight)
    {
        if (string.IsNullOrWhiteSpace(highlight)) return null;
        var space = highlight.IndexOf(' ');
        if (space <= 0) return null;
        var root = Array.FindIndex(MusicTheoryService.NoteNames, n => n.Equals(highlight[..space], StringComparison.OrdinalIgnoreCase));
        if (root < 0 || !MusicTheoryService.Scales.TryGetValue(highlight[(space + 1)..], out var intervals)) return null;
        return intervals.Select(i => (root + i) % 12).ToHashSet();
    }

    /// <summary>The pitches the rows show, highest first: every pitch, the scale's pitches, or the pitches the notes use (every pitch when that leaves none).</summary>
    public static IReadOnlyList<int> Rows(string fold, IReadOnlySet<int>? scale, IEnumerable<PianoRollNote> notes)
    {
        IEnumerable<int> pitches = Enumerable.Range(0, 128);
        if (fold == FoldScale && scale is not null) pitches = pitches.Where(p => scale.Contains(p % 12));
        else if (fold == FoldUsed)
        {
            var used = notes.Select(n => n.Pitch).ToHashSet();
            // One or two used pitches would leave a blank strip nothing can be drawn on: show an octave either side of them instead.
            if (used.Count >= MinUsedFold) pitches = pitches.Where(used.Contains);
            else if (used.Count > 0) pitches = pitches.Where(p => p >= used.Min() - 12 && p <= used.Max() + 12);
        }
        return pitches.OrderByDescending(p => p).ToList();
    }
}
