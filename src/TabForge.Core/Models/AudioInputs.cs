using TabForge.Audio.Contracts;

namespace TabForge.Models;

// Owns: the ids of the inputs a track (or the live input) records and monitors ("Input 3", "Inputs 3+4 (stereo)",
//       MIDI), their engine address, the list the running audio device offers, and which of them each picker shows.
// Does not own: opening the device (the engine), storing the per-device choices (PluginSettings.HiddenInputs),
//       the pickers themselves (AudioInputPicker, LiveInputMenu), the dialog that edits the choices (InputsShownDialog).
// Tests: no dedicated test class.

/// <summary>Where a list of inputs is shown.</summary>
public enum AudioInputUse { Recording, LiveInput }

/// <summary>The input ids one device's pickers leave out, per use. Kept per device name in the app settings; a device without an entry shows every input.</summary>
public sealed class HiddenAudioInputs
{
    public List<string> Recording { get; set; } = new();
    public List<string> LiveInput { get; set; } = new();
}

/// <summary>One row of an input picker: the stored id and the text shown for it.</summary>
public sealed record AudioInputChoice(string Id, string Label) { public override string ToString() => Label; }   // a picker's closed box shows the item's text: the label, never the record's fields

/// <summary>Which input channels a track records / monitors.</summary>
public static class AudioInputs
{
    public const string Input1 = "Input 1";
    public const string Input2 = "Input 2";
    public const string Stereo = "Inputs 1+2 (stereo)";
    /// <summary>MIDI from every MIDI input device (recorded as a MIDI clip, played live through the track).</summary>
    public const string Midi = "MIDI (all inputs)";
    public const string NotAvailable = " (not available)";

    /// <summary>The device's inputs as the engine reports them, and every device's hidden ids. Replaced as a whole on the UI thread.</summary>
    private sealed record Snapshot(string Device, string[] Names, string[] Audio, string[] All, IReadOnlyDictionary<string, HiddenAudioInputs> Hidden);

    private static readonly IReadOnlyDictionary<string, HiddenAudioInputs> None = new Dictionary<string, HiddenAudioInputs>();
    private static Snapshot _now = Build("", Array.Empty<string>(), None);

    /// <summary>UI thread: the device's inputs or the hidden ones changed (open pickers rebuild their rows; nothing is written to a track).</summary>
    public static event Action? Changed;

    /// <summary>Audio inputs of the running device: every input, then the stereo pairs. Inputs 1, 2 and 1+2 while no device has reported.</summary>
    public static string[] Audio => _now.Audio;
    /// <summary><see cref="Audio"/> and MIDI.</summary>
    public static string[] All => _now.All;
    /// <summary>The device the list belongs to ("" before one reports).</summary>
    public static string Device => _now.Device;
    /// <summary>The device's own name of each input (may be empty), "Input 1" first.</summary>
    public static IReadOnlyList<string> Names => _now.Names;

    public static bool IsMidi(string input) => input == Midi;
    /// <summary>True when the track takes MIDI: the legacy MIDI input, or at least one MIDI device ticked next to its audio input.</summary>
    public static bool TakesMidi(TrackModel track) => IsMidi(track.AudioInput) || track.MidiInputs.Count > 0;
    public static string Mono(int channel) => $"Input {channel}";
    public static string Pair(int first) => $"Inputs {first}+{first + 1} (stereo)";

    /// <summary>UI thread, when the engine reports its device: the inputs it offers (their names, possibly empty strings). None: the three inputs every device has.</summary>
    public static void SetDevice(string device, IReadOnlyList<string> names)
    {
        _now = Build(device, names.Take(AudioInputAddress.MaxChannels).ToArray(), _now.Hidden);
        Changed?.Invoke();
    }

    /// <summary>UI thread: every device's hidden ids, as the app settings hold them (the running device's entry applies; none: all shown).</summary>
    public static void SetHidden(IReadOnlyDictionary<string, HiddenAudioInputs>? hidden)
    {
        if (ReferenceEquals(_now.Hidden, hidden)) return;
        _now = _now with { Hidden = hidden ?? None };
        Changed?.Invoke();
    }

    private static Snapshot Build(string device, string[] names, IReadOnlyDictionary<string, HiddenAudioInputs> hidden)
    {
        var count = Math.Max(2, names.Length);   // a one-input device is doubled by the engine
        var audio = Enumerable.Range(1, count).Select(Mono).Concat(Enumerable.Range(0, count / 2).Select(p => Pair(p * 2 + 1))).ToArray();
        return new Snapshot(device, names, audio, audio.Append(Midi).ToArray(), hidden);
    }

    /// <summary>Reads an audio input id: its first channel (zero-based) and whether it is the pair with the next one.</summary>
    public static bool TryParse(string? id, out int first, out bool stereo)
    {
        first = 0; stereo = false;
        if (id is null) return false;
        if (id.StartsWith("Input ", StringComparison.Ordinal) && int.TryParse(id.AsSpan(6), System.Globalization.NumberStyles.None, null, out var mono) && Mono(mono) == id)
            first = mono - 1;
        else if (id.StartsWith("Inputs ", StringComparison.Ordinal) && id.IndexOf('+') is > 7 and var plus
                 && int.TryParse(id.AsSpan(7, plus - 7), System.Globalization.NumberStyles.None, null, out var left) && Pair(left) == id)
        { first = left - 1; stereo = true; }
        else return false;
        return first >= 0 && first + (stereo ? 1 : 0) < AudioInputAddress.MaxChannels;
    }

    /// <summary>A well-formed id (whether or not the running device has it): what a song or a setting may hold.</summary>
    public static bool IsValid(string? id) => id == Midi || TryParse(id, out _, out _);

    /// <summary>The engine's input mode for an audio input id (<see cref="AudioInputAddress"/>); -1 for MIDI or an unknown id.</summary>
    public static int WireMode(string? id) => TryParse(id, out var first, out var stereo) ? AudioInputAddress.Encode(first, stereo) : -1;

    /// <summary>True when the running device has the input (MIDI always).</summary>
    public static bool IsAvailable(string id) => Array.IndexOf(_now.All, id) >= 0;

    /// <summary>The text shown for an id: the device's channel names when it gives them, and a note when the device lacks the input.</summary>
    public static string Label(string id)
    {
        if (!TryParse(id, out var first, out var stereo)) return id;
        if (!IsAvailable(id)) return id + NotAvailable;
        string Name(int channel) => channel < _now.Names.Length ? _now.Names[channel].Trim() : "";
        if (!stereo) return Name(first).Length == 0 ? id : $"{first + 1}: {Name(first)}";
        return Name(first).Length == 0 || Name(first + 1).Length == 0 ? id : $"{first + 1}+{first + 2}: {Name(first)} + {Name(first + 1)}";
    }

    /// <summary>
    /// The audio input ids a picker shows: the device's, less the ones hidden for that use. The selected id always stays
    /// (also when it is hidden or the device lacks it), so nothing is reassigned behind the user's back.
    /// </summary>
    public static IReadOnlyList<string> For(AudioInputUse use, string? selected = null)
    {
        var hidden = !_now.Hidden.TryGetValue(_now.Device, out var entry) || entry is null ? new List<string>() : use == AudioInputUse.Recording ? entry.Recording : entry.LiveInput;
        var shown = _now.Audio.Where(id => id == selected || !hidden.Contains(id)).ToList();
        if (selected is not null && !shown.Contains(selected) && TryParse(selected, out _, out _)) shown.Add(selected);
        return shown;
    }

    /// <summary>The rows of a track's input picker: <see cref="For"/> the recording use, then MIDI.</summary>
    public static IReadOnlyList<AudioInputChoice> Choices(string? selected) =>
        For(AudioInputUse.Recording, selected).Append(Midi).Select(id => new AudioInputChoice(id, Label(id))).ToList();
}
