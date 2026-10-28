using System.Globalization;
using TabForge.Models;

namespace TabForge.Playback;

// Owns: one technique-to-MIDI rule (what to send, its two numbers, latched or per note) and its stored text form, the technique rows a rule can attach to,
//   and the note names the editor shows (C0 = MIDI 24).
// Does not own: the map of a whole instrument (ArticulationMap), the presets (ArticulationPresets) or the compile hook (ScoreToMidiCompiler.Articulations).
// Tests: TestArticulationKeyswitches.

/// <summary>What a rule does with a note that has its technique.</summary>
public enum ArticulationAction
{
    /// <summary>No rule: the note plays as written.</summary>
    None,
    /// <summary>A keyswitch note, sent a few milliseconds before the note.</summary>
    Keyswitch,
    /// <summary>The note plays at a fixed velocity.</summary>
    Velocity,
    /// <summary>A controller message (number, value), sent a few milliseconds before the note.</summary>
    Cc,
    /// <summary>The note plays another pitch.</summary>
    Replace,
}

/// <summary>When a keyswitch or controller rule is sent.</summary>
public enum ArticulationMode
{
    /// <summary>Only when the articulation changes; the "Normal" rule is sent when the technique ends.</summary>
    Latched,
    /// <summary>Before every note that has the technique.</summary>
    PerNote,
}

/// <summary>One rule. <see cref="A"/> is the keyswitch note, the velocity, the controller number or the replacing pitch; <see cref="B"/> is the controller value.</summary>
public readonly record struct ArticulationRule(ArticulationAction Action, int A, int B, ArticulationMode Mode)
{
    public const int MaxEncodedChars = 64;

    /// <summary>The stored form, e.g. "Keyswitch;26;0;Latched" (a text, so the song file keeps its string-to-string map).</summary>
    public string Encode() => string.Create(CultureInfo.InvariantCulture, $"{Action};{A};{B};{Mode}");

    /// <summary>Reads the stored form; false for anything malformed, an unknown action or a number outside 0..127.</summary>
    public static bool TryParse(string? text, out ArticulationRule rule)
    {
        rule = default;
        var parts = (text ?? "").Split(';');
        if (parts.Length != 4 || text!.Length > MaxEncodedChars) return false;
        if (!Enum.TryParse(parts[0], true, out ArticulationAction action) || action == ArticulationAction.None || !Enum.IsDefined(action)) return false;
        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var a) || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var b)) return false;
        if (!Enum.TryParse(parts[3], true, out ArticulationMode mode) || !Enum.IsDefined(mode)) return false;
        if (a is < 0 or > MidiLimits.Max || b is < 0 or > MidiLimits.Max) return false;
        rule = new ArticulationRule(action, a, b, mode);
        return true;
    }
}

/// <summary>One technique a rule can attach to: its stored id, the name the editor shows and the test on a note.</summary>
public sealed record ArticulationTechnique(string Id, string Display, Func<TabNote, TabCell, HashSet<string>, bool> Matches);

public static class ArticulationTechniques
{
    /// <summary>The id of the row that holds the rule sent when no other technique applies (and when a latched one ends).</summary>
    public const string Normal = "Normal";

    /// <summary>The rows in priority order: when a note has several techniques, the first one with a keyswitch or controller rule wins.</summary>
    public static IReadOnlyList<ArticulationTechnique> All { get; } = new ArticulationTechnique[]
    {
        new(Normal, "Normal", (_, _, _) => false),
        new("Dead", "Dead note", (n, _, t) => n.Dead || t.Contains("Dead")),
        new(TechniqueNames.PalmMute, "Palm mute", (_, _, t) => TechniqueNames.HasPalmMute(t)),
        new(TechniqueNames.ArtificialHarmonic, "Artificial / pinch harmonic", (_, _, t) => t.Contains(TechniqueNames.ArtificialHarmonic) || t.Contains("PinchHarmonic") || t.Contains("TapHarmonic")),
        new(TechniqueNames.Harmonic, "Natural harmonic", (_, _, t) => t.Contains(TechniqueNames.Harmonic) || t.Contains("SemiHarmonic") || t.Contains("FeedbackHarmonic")),
        new("Slide", "Slide in / out", (_, _, t) => t.Contains("SlideInBelow") || t.Contains("SlideInAbove") || t.Contains("SlideOutUp") || t.Contains("SlideOutDown")),
        new(TechniqueNames.LegatoSlide, "Legato slide", (_, _, t) => t.Contains(TechniqueNames.LegatoSlide)),
        new("HammerPull", "Hammer-on / pull-off", (_, _, t) => t.Contains(TechniqueNames.Hopo) || t.Contains(TechniqueNames.HopoDestination)),
        new(TechniqueNames.Tapping, "Tapping", (_, _, t) => t.Contains(TechniqueNames.Tapping) || t.Contains("LeftTap")),
        new(TechniqueNames.Bend, "Bend", (_, _, t) => t.Contains(TechniqueNames.Bend)),
        new(TechniqueNames.Vibrato, "Vibrato", (_, _, t) => t.Contains(TechniqueNames.Vibrato) || t.Contains(TechniqueNames.WideVibrato)),
        new(TechniqueNames.LetRing, "Let ring", (_, _, t) => t.Contains(TechniqueNames.LetRing)),
        new("Ghost", "Ghost note", (n, _, t) => n.Ghost || t.Contains("Ghost")),
        new("Accent", "Accent", (_, c, t) => c.Accent > 0 || t.Contains("Accent")),
    };

    public static ArticulationTechnique? Find(string id) => All.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase));

    private static string LetterOf(int pitchClass) => pitchClass switch
    {
        0 => "C", 1 => "C#", 2 => "D", 3 => "D#", 4 => "E", 5 => "F", 6 => "F#", 7 => "G", 8 => "G#", 9 => "A", 10 => "A#", _ => "B",
    };

    /// <summary>The note's name with C0 = MIDI 24 (the octave numbering of the Ample Sound manuals, middle C is C3 = 60).</summary>
    public static string NoteName(int midi) => LetterOf(((midi % 12) + 12) % 12) + (midi / 12 - 2).ToString(CultureInfo.InvariantCulture);

    /// <summary>A note typed as a number (0..127) or a name such as "C#0" or "A-1" (C0 = MIDI 24); false when it is neither.</summary>
    public static bool TryParseNote(string? text, out int midi)
    {
        midi = 0;
        text = (text ?? "").Trim();
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out midi)) return midi is >= 0 and <= MidiLimits.Max;
        if (text.Length < 2) return false;
        var length = text.Length > 2 && text[1] == '#' ? 2 : 1;
        var index = Enumerable.Range(0, 12).FirstOrDefault(i => string.Equals(LetterOf(i), text[..length], StringComparison.OrdinalIgnoreCase), -1);
        if (index < 0 || !int.TryParse(text[length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var octave)) return false;
        midi = (octave + 2) * 12 + index;
        return midi is >= 0 and <= MidiLimits.Max;
    }
}
