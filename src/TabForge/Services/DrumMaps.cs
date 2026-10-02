using TabForge.Models;

namespace TabForge.Services;

/// <summary>
/// How one GM percussion sound is shown: TAB line and label, staff position and notehead.
/// StaffStep counts half-spaces down from the top staff line (0 = top line F5, 7 = F4 space, negative = above).
/// </summary>
public sealed class DrumMapEntry
{
    public int Midi { get; set; }
    public int TabLine { get; set; }
    public string Label { get; set; } = "";
    public int StaffStep { get; set; } = 3;
    /// <summary>"x" (cymbals, hi-hats), "normal" (drums), "circle" (open hi-hat), "diamond" (bells).</summary>
    public string Head { get; set; } = "normal";
    public DrumMapEntry Clone() => (DrumMapEntry)MemberwiseClone();
}

// Owns: the drum maps that name drum notes per kit and notation style.
// Does not own: drum playback and the notation drawing.
// Tests: TestDrumEntryAndQuickAddBars, TestInstrumentChoiceStrings.
/// <summary>
/// Drum notation presets, modelled on how the common programs show percussion:
/// - Guitar Pro 5: TAB shows the GM note number on a line per kit group.
/// - Guitar Pro 6/7: drum-kit notation; TAB lines carry articulation abbreviations (BD, SD, HH...).
/// - Drum tab (lines): one line per instrument (CC, HH, SD, T1, T2, FT, BD) with x / o marks, like
///   classic ASCII drum tabs and many web tab sites.
/// - Custom: the user's own table, starting from any preset.
/// All presets use standard percussion-staff positions (kick F4 space, snare C5, hi-hat G5 x, crash A5 x,
/// ride F5 x, toms E5/D5/B4/A4/G4), as in GP6/7, MuseScore and PAS notation.
/// </summary>
public static class DrumMaps
{
    public const string GuitarPro5 = "Guitar Pro 5";
    public const string GuitarPro6 = ".gp6/.gp7";
    public const string DrumTab = "Drum tab (lines)";
    public const string Custom = "Custom";
    public static readonly string[] Presets = { GuitarPro5, GuitarPro6, DrumTab, Custom };

    /// <summary>Display label of a stored preset name (the stored value never changes).</summary>
    public static string DisplayName(string preset) => preset == GuitarPro5 ? ".gp5 file drum map" : preset == GuitarPro6 ? ".gp6/.gp7 file drum map" : preset;

    // (midi, staffStep, head, abbreviation, kit group)  group: 0 cymbal, 1 hihat, 2 high tom, 3 snare, 4 low tom, 5 kick, 6 other
    private static readonly Dictionary<int, (int Step, string Head, string Abbrev, int Group)> Standard = new()
    {
        [35] = (7, "normal", "BD", 5), [36] = (7, "normal", "BD", 5),
        [37] = (3, "x", "SS", 3), [38] = (3, "normal", "SD", 3), [39] = (3, "x", "CL", 3), [40] = (3, "normal", "SD", 3),
        [41] = (6, "normal", "FT", 4), [43] = (5, "normal", "FT", 4), [45] = (4, "normal", "T3", 4), [47] = (2, "normal", "T2", 4),
        [48] = (1, "normal", "T1", 2), [50] = (1, "normal", "T1", 2),
        [42] = (-1, "x", "HH", 1), [44] = (9, "x", "PH", 1), [46] = (-1, "circle", "OH", 1),
        [49] = (-2, "x", "CC", 0), [57] = (-2, "x", "CC", 0), [52] = (-3, "x", "CH", 0), [55] = (-2, "x", "SP", 0),
        [51] = (0, "x", "RC", 0), [59] = (0, "x", "RC", 0), [53] = (0, "diamond", "RB", 0),
        [54] = (4, "x", "TB", 6), [56] = (1, "x", "CB", 6), [69] = (4, "x", "CA", 6), [70] = (4, "x", "MA", 6),
        [75] = (4, "x", "CV", 6), [76] = (4, "x", "WB", 6), [77] = (5, "x", "WB", 6),
    };

    private static (int Step, string Head, string Abbrev, int Group) Info(int midi) =>
        Standard.TryGetValue(midi, out var s) ? s : (4, "x", midi.ToString(), 6);

    /// <summary>Number of TAB lines a preset uses.</summary>
    public static int LineCount(string preset) => preset == DrumTab ? 7 : 6;

    /// <summary>Line names shown at the left of the TAB for line-based presets (null = none).</summary>
    public static string[]? LineNames(string preset) => preset == DrumTab
        ? new[] { "CC", "HH", "SD", "T1", "T2", "FT", "BD" }
        : null;

    public static DrumMapEntry Default(string preset, int midi)
    {
        var (step, head, abbrev, group) = Info(midi);
        return preset switch
        {
            GuitarPro6 => new DrumMapEntry { Midi = midi, TabLine = Math.Min(5, group), Label = abbrev, StaffStep = step, Head = head },
            DrumTab => new DrumMapEntry
            {
                Midi = midi, StaffStep = step, Head = head,
                TabLine = abbrev switch { "CC" or "CH" or "SP" or "RC" or "RB" => 0, "HH" or "OH" or "PH" => 1, "SD" or "SS" or "CL" => 2,
                    "T1" => 3, "T2" or "T3" => 4, "FT" => 5, "BD" => 6, _ => 2 },
                Label = head == "normal" ? "o" : abbrev == "OH" ? "O" : "x",
            },
            _ => new DrumMapEntry { Midi = midi, TabLine = GuitarProImporter.DrumLine(midi), Label = midi.ToString(), StaffStep = step, Head = head },
        };
    }

    /// <summary>The effective entry for a note on a drum track (custom overrides fall back to the chosen base preset).</summary>
    public static DrumMapEntry For(TrackModel track, int midi)
    {
        var preset = string.IsNullOrEmpty(track.DrumMapPreset) ? GuitarPro5 : track.DrumMapPreset;
        if (preset == Custom)
        {
            var custom = track.CustomDrumMap?.FirstOrDefault(e => e.Midi == midi);
            if (custom is not null) return custom;
            preset = GuitarPro5;
        }
        return Default(preset, midi);
    }

    /// <summary>Applies a preset to a drum track: line count and every note's TAB line follow the map.</summary>
    public static void Apply(TrackModel track, string preset)
    {
        track.DrumMapPreset = preset;
        var lines = preset == Custom ? Math.Max(6, (track.CustomDrumMap?.Select(e => e.TabLine).DefaultIfEmpty(5).Max() ?? 5) + 1) : LineCount(preset);
        track.StringTunings = Enumerable.Range(0, lines).Select(i => 60 - i).ToList(); // placeholders: drum pitch comes from the note itself
        foreach (var measure in track.Measures)
            foreach (var cell in measure.Cells.Concat(measure.Voice2Cells))
                foreach (var note in cell.Notes)
                {
                    var midi = note.MidiValue > 0 ? note.MidiValue : note.Fret;
                    note.StringIndex = Math.Clamp(For(track, midi).TabLine, 0, lines - 1);
                    note.Fret = midi;
                }
    }
}
