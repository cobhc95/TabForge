namespace TabForge.Presenters;

/// <summary>
/// The track properties rules both track editors share.
/// Owns: the window title, the typed-field checks and their texts, the name rule, the channel an instrument choice leaves,
///     the string tools (typed note names, semitone steps, thickness, the capo stepper text); the string edit list is TrackTuningDraft.
/// Does not own: the windows (Views/Timeline/TrackPropertiesDialog.cs), tuning presets and the keep-frets retune
///     (TrackPropertiesWindow), family conversion (TrackSetup) or the undo checkpoint (MainWindow.Tracks).
/// Tests: TabForge.Avalonia.Tests.TrackTuningDraftTests, TrackPropertiesWindowTests.
/// </summary>
public static class TrackPropertiesPresenter
{
    public const int LowestNote = 12, HighestNote = 100, FewestStrings = 3, MostStrings = 10, MaxCapo = 12;

    public static string Title(string trackName, bool adding) => adding ? "Add track" : $"Track properties — {trackName}";

    /// <summary>The first failing field message, or null when program, bank, channel, frets and capo are all in range (channel 9, shown as 10, is for drum tracks only, and a drum track stays on it). A bank of -1 means off. An audio track has no MIDI channel, program, bank, frets or capo, so none of them can refuse the save.</summary>
    public static string? FieldError(string program, string bankMsb, string bankLsb, string channel, string frets, string capo, bool isDrumTrack, bool isAudioTrack = false)
    {
        if (isAudioTrack) return null;
        if (!InRange(program, 0, MidiLimits.Max)) return "MIDI program must be 0–127.";
        if (!InRange(bankMsb, -1, MidiLimits.Max)) return "Bank MSB must be –1 (off) to 127.";
        if (!InRange(bankLsb, -1, MidiLimits.Max)) return "Bank LSB must be –1 (off) to 127.";
        if (!InRange(channel, 0, 15)) return "MIDI channel must be 0–15.";
        var onDrumChannel = channel.Trim() == "9";
        if (!isDrumTrack && onDrumChannel) return "Channel 10 is for drums only.";
        if (isDrumTrack && !onDrumChannel) return "A drum track must stay on channel 10.";
        if (!InRange(frets, 12, 36)) return "Frets must be 12–36.";
        if (!InRange(capo, 0, MaxCapo)) return "Capo must be 0–12.";
        return null;
    }

    public static string PositionError(int trackCount) => $"Track number must be 1–{trackCount + 1}.";

    private static bool InRange(string text, int min, int max) => int.TryParse(text, out var v) && v >= min && v <= max;

    /// <summary>A blank name keeps the track's name; a typed one is trimmed.</summary>
    public static string ResolvedName(string typed, string current) => string.IsNullOrWhiteSpace(typed) ? current : typed.Trim();

    /// <summary>Drum kits live on channel 9; leaving a kit while 9 is typed returns to the track's own melodic channel, or 0.</summary>
    public static string ChannelAfterInstrument(bool isDrumKit, string typedChannel, int trackChannel)
    {
        if (isDrumKit) return "9";
        if (typedChannel.Trim() == "9") return trackChannel != 9 ? trackChannel.ToString() : "0";
        return typedChannel;
    }

    public static int Step(int midi, int delta) => Math.Clamp(midi + delta, LowestNote, HighestNote);

    /// <summary>The capo stepper's text: "Off" at fret 0, else "Fret N".</summary>
    public static string CapoText(int capo) => capo <= 0 ? "Off" : $"Fret {capo}";

    /// <summary>One capo step, held to 0 (off) .. 12 like the field check.</summary>
    public static int StepCapo(int capo, int delta) => Math.Clamp(capo + delta, 0, MaxCapo);

    /// <summary>The string-removal question: the affected notes, and how many must change pitch because no remaining string can play them.</summary>
    public static string RemovalWarningText(int total, int unplayable)
    {
        var affected = $"{NoteCount(total)} {IsAre(total)} affected by removing the string.";
        if (unplayable <= 0)
            return $"{affected} Move {(total == 1 ? "it" : "them")} to {(total == 1 ? "another string" : "other strings")} that can play the same pitch?";
        if (unplayable >= total)
            return total == 1
                ? $"{affected} No remaining string can play its pitch, so it will move to the nearest playable pitch and its pitch will change. Continue?"
                : $"{affected} No remaining string can play their pitch, so they will all move to the nearest playable pitch and their pitch will change. Continue?";
        var keep = total - unplayable;
        return $"{affected} {NoteCount(unplayable)} cannot keep {(unplayable == 1 ? "its" : "their")} pitch on any remaining string and will move to the nearest playable pitch, so "
            + $"{(unplayable == 1 ? "its" : "their")} pitch will change; the other {keep} keep{(keep == 1 ? "s" : "")} {(keep == 1 ? "its" : "their")} pitch. Continue?";
    }

    /// <summary>The question before OK when a later retune makes notes on removed strings change pitch that the removal question did not mention.</summary>
    public static string UndisclosedPitchChangeWarningText(int newlyUnplayable) =>
        newlyUnplayable == 1
            ? "After your tuning change, 1 note on a removed string can no longer keep its pitch, which the removal warning did not mention. It will move to the nearest playable pitch and its pitch will change. Apply this tuning?"
            : $"After your tuning change, {newlyUnplayable} notes on removed strings can no longer keep their pitch, which the removal warning did not mention. They will move to the nearest playable pitch and their pitch will change. Apply this tuning?";

    private static string NoteCount(int count) => $"{count} note{(count == 1 ? "" : "s")}";

    private static string IsAre(int count) => count == 1 ? "is" : "are";

    /// <summary>The drawn string's thickness: thicker for lower pitches, like wound strings.</summary>
    public static double StringThickness(int midi) => Math.Clamp(1 + (70 - midi) / 9.0, 1, 5);

    /// <summary>Parses "C4", "D#2", "Eb3", "f#" (octave optional: octave 3, the middle of the guitar range) to MIDI 12-108.</summary>
    public static bool TryParseNote(string text, out int midi)
    {
        midi = 0;
        var m = System.Text.RegularExpressions.Regex.Match(text.Trim(), @"^([A-Ga-g])([#b♯♭]?)(-?\d)?$");
        if (!m.Success) return false;
        var pc = char.ToUpperInvariant(m.Groups[1].Value[0]) switch { 'C' => 0, 'D' => 2, 'E' => 4, 'F' => 5, 'G' => 7, 'A' => 9, _ => 11 };
        var acc = m.Groups[2].Value;
        if (acc is "#" or "♯") pc++;
        else if (acc is "b" or "♭") pc--;
        var octave = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 3;
        midi = (octave + 1) * 12 + pc;
        return midi is >= 12 and <= 108;
    }
}
