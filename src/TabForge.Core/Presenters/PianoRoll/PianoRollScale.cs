using TabForge.Models;
using TabForge.Services;

namespace TabForge.Presenters;

// Owns: the piano roll's scale and row rules: which pitch classes the Scale highlight setting ("A Minor Pentatonic") holds, and which pitches the
//       rows show from top to bottom with the fold (None: every pitch; Scale: the pitches in the scale; Used: the pitches the notes use), the fold
//       each song keeps (SongProject.PianoRollRows: All pitches unless the song chose another), and the note colour modes.
//       Also the scale popup's choice: follow the setting, Auto-detect from the shown notes, or a manual pick; and the cheap
//       detection the Auto mode re-runs after each reload (never in pointer-move handlers).
// Does not own: the setting (EditingSettings.ScaleHighlight, the scale table MusicTheoryService.Scales) or the drawing (the app's PianoRollSurface).
// Tests: TabForge.Avalonia.Tests.PianoRollEditTests, PianoRollScaleDetectTests.
public static class PianoRollScale
{
    public const string FoldNone = "None", FoldScale = "Scale", FoldUsed = "Used";
    public const string Auto = "Auto";
    public const int MinUsedFold = 3;
    public static IReadOnlyList<string> Folds { get; } = new[] { FoldNone, FoldScale, FoldUsed };
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

    /// <summary>The pitches the rows show, highest first: every pitch, the scale's pitches, or the pitches the notes use (every pitch when that leaves none).
    /// An unknown fold or a Scale fold with no scale shows every pitch, so a missing scale never blanks the grid.</summary>
    public static IReadOnlyList<int> Rows(string? fold, IReadOnlySet<int>? scale, IEnumerable<PianoRollNote> notes)
    {
        IEnumerable<int> pitches = Enumerable.Range(0, 128);
        if (string.Equals(fold, FoldScale, StringComparison.OrdinalIgnoreCase) && scale is not null) pitches = pitches.Where(p => scale.Contains(p % 12));
        else if (string.Equals(fold, FoldUsed, StringComparison.OrdinalIgnoreCase))
        {
            var used = notes.Select(n => n.Pitch).ToHashSet();
            // One or two used pitches would leave a blank strip nothing can be drawn on: show an octave either side of them instead.
            if (used.Count >= MinUsedFold) pitches = pitches.Where(used.Contains);
            else if (used.Count > 0) pitches = pitches.Where(p => p >= used.Min() - 12 && p <= used.Max() + 12);
        }
        return pitches.OrderByDescending(p => p).ToList();
    }

    /// <summary>The SHOW ROWS fold the song chose: Scale or Used; every other value (none stored, an old file, a damaged one) is All pitches.</summary>
    public static string RowsOf(SongProject? project) => project?.PianoRollRows switch { FoldScale => FoldScale, FoldUsed => FoldUsed, _ => FoldNone };

    /// <summary>Stores the song's SHOW ROWS fold. All pitches is not written (null), so a song that never chose one saves exactly as before.
    /// False when the song already holds that choice.</summary>
    public static bool StoreRows(SongProject project, string? fold)
    {
        var value = fold is FoldScale or FoldUsed ? fold : null;
        if (project.PianoRollRows == value) return false;
        project.PianoRollRows = value;
        return true;
    }

    /// <summary>The stored popup choice: null follows the setting, "Auto" detects, else a "Root Scale" pick; anything else follows the setting.</summary>
    public static string? NormalizePick(string? pick)
    {
        if (string.IsNullOrWhiteSpace(pick)) return null;
        if (pick.Trim().Equals(Auto, StringComparison.OrdinalIgnoreCase)) return Auto;
        return PitchClasses(pick.Trim()) is null ? null : pick.Trim();
    }

    /// <summary>True when the choice is Auto-detect.</summary>
    public static bool IsAuto(string? pick) => pick?.Trim().Equals(Auto, StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>The detected scales of the shown notes, best first (at most 3); empty with no notes. Cheap (one pass over the notes plus the
    /// ranking over the default families), so the view re-runs it after each reload/commit only, never in a pointer-move handler.</summary>
    public static IReadOnlyList<ScaleMatch> DetectNotes(IEnumerable<PianoRollNote> notes, bool preferCommon = true)
    {
        var histogram = HistogramOf(notes);
        return histogram.NoteCount == 0 ? Array.Empty<ScaleMatch>() : SectionScaleDetector.Rank(histogram, ScaleFamilies.ScalesOf(ScaleFamilies.DefaultOn), preferCommon).Take(3).ToList();
    }

    /// <summary>The pitch-class weights of the shown notes (length, velocity, muted halved); empty with no notes.</summary>
    public static PitchHistogram HistogramOf(IEnumerable<PianoRollNote> notes)
    {
        var histogram = new PitchHistogram();
        foreach (var note in notes)
        {
            if (note.Pitch < 0 || note.Pitch > 127) continue;
            var weight = Math.Max(0.0625, note.Length) * Math.Clamp(note.Velocity, 1, 127) / 127.0 * (note.Muted ? 0.5 : 1);
            histogram.Add(note.Pitch, weight);
        }
        return histogram;
    }

    /// <summary>The highlight the roll follows: the manual pick, the Auto detection (the setting when nothing detected), else the setting.</summary>
    public static string? EffectiveHighlight(string? settingsHighlight, string? pick, IReadOnlyList<ScaleMatch> detected)
    {
        if (IsAuto(pick)) return detected.Count > 0 ? detected[0].Highlight : settingsHighlight;
        if (!string.IsNullOrWhiteSpace(pick) && PitchClasses(pick) is not null) return pick!.Trim();
        return settingsHighlight;
    }

    /// <summary>The button wording for a highlight: "C major", with " (auto)" when Auto detected it; "No scale" when it names none.</summary>
    public static string DisplayLabel(string? highlight, bool auto)
    {
        var parsed = ScaleReadoutEngine.Parse(highlight);
        if (parsed is null) return "No scale";
        var text = $"{ScaleFamilies.NoteName(parsed.Root, false)} {ScaleFamilies.Wording(parsed.Scale)}";
        return auto ? text + " (auto)" : text;
    }
}
