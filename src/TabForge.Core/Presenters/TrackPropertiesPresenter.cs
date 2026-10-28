namespace TabForge.Presenters;

/// <summary>
/// The track properties rules both track editors share.
/// Owns: the window title, the typed-field checks and their texts, the name rule, the channel an instrument choice leaves,
///     the string tools (typed note names, semitone steps, adding and removing the low string, thickness).
/// Does not own: the windows (Views/Timeline/TrackPropertiesDialog.cs), tuning presets and the keep-frets retune
///     (TrackPropertiesWindow), family conversion (TrackSetup) or the undo checkpoint (MainWindow.Tracks).
/// Tests: no dedicated test class.
/// </summary>
public static class TrackPropertiesPresenter
{
    public const int LowestNote = 12, HighestNote = 100, FewestStrings = 3, MostStrings = 9;

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
        if (!InRange(capo, 0, 12)) return "Capo must be 0–12.";
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

    /// <summary>Adds a string a fourth below the lowest, up to nine strings.</summary>
    public static List<int> WithLowString(List<int> highToLow)
    {
        if (highToLow.Count < MostStrings) highToLow.Add(Math.Max(LowestNote, highToLow[^1] - 5));
        return highToLow;
    }

    /// <summary>Removes the lowest string, down to three strings.</summary>
    public static List<int> WithoutLowString(List<int> highToLow)
    {
        if (highToLow.Count > FewestStrings) highToLow.RemoveAt(highToLow.Count - 1);
        return highToLow;
    }

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
