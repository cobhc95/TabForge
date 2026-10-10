using System.Text.Json.Serialization;

namespace TabForge.KeyboardMode;

// Owns: the saved Keyboard mode values (timing tolerance, look-ahead, size, MIDI input, wait, hands filter, note names, finger numbers) and their bounds.
// Does not own: the Preferences rows (KeyboardModeSettingsRows), the control bar (KeyboardModeControlBar), the pane or the module that merges them.
// Tests: TestKeyboardModeNoteStream, TestPreferencesCatalog.
/// <summary>What the Keyboard mode pane remembers.</summary>
public sealed class KeyboardModeSettings
{
    public const int MinLookAheadSeconds = 2;
    public const int MaxLookAheadSeconds = 12;
    public const int DefaultLookAheadSeconds = 4;
    /// <summary>The MIDI input choices besides a device name: listen to nothing, or to every connected device.</summary>
    public const string MidiOff = "Off", MidiAny = "Any";

    /// <summary>How many seconds of music show above the keys (fewer: longer, slower notes).</summary>
    public int LookAheadSeconds { get; set; } = DefaultLookAheadSeconds;
    /// <summary>Which MIDI input Keyboard mode listens to: <see cref="MidiOff"/>, <see cref="MidiAny"/> (every device, the default) or one device's name.</summary>
    public string MidiInput { get; set; } = MidiAny;
    /// <summary>Listen to a MIDI keyboard and judge it against the selected track (the MIDI input is not Off). Not saved: <see cref="MidiInput"/> holds it.</summary>
    [JsonIgnore]
    public bool PlayAlongKeyboard
    {
        get => !string.Equals(MidiInput, MidiOff, StringComparison.Ordinal);
        set { if (value != PlayAlongKeyboard) MidiInput = value ? MidiAny : MidiOff; }
    }
    /// <summary>Keyboard play-along: the song pauses at each chord until its notes are pressed.</summary>
    public bool WaitForNotes { get; set; }
    /// <summary>How exactly a press must hit its note's time: Strict, Normal or Relaxed (the default; wait mode has no timing limit).</summary>
    public string TimingTolerance { get; set; } = TimingRelaxed;
    public const string TimingStrict = "Strict", TimingNormal = "Normal", TimingRelaxed = "Relaxed";
    public static readonly string[] Timings = { TimingStrict, TimingNormal, TimingRelaxed };
    public static string NormalizeTiming(string? timing) => Timings.FirstOrDefault(t => t == timing) ?? TimingRelaxed;
    /// <summary>Which hand is practised (<see cref="KeyboardHands.FilterNames"/>).</summary>
    public string Hands { get; set; } = KeyboardHands.FilterNames[0];
    /// <summary>Note names (F#3) on the falling bars.</summary>
    public bool ShowNoteNames { get; set; } = true;
    /// <summary>Finger numbers on the falling bars when the score has them.</summary>
    public bool ShowFingers { get; set; } = true;

    /// <summary>How much of the window Keyboard mode takes: Normal, Large or Full (the falling notes take the score's place).</summary>
    public string KeyboardSize { get; set; } = SizeFull;

    public const string SizeNormal = "Normal", SizeLarge = "Large", SizeFull = "Full";
    public static readonly string[] Sizes = { SizeNormal, SizeLarge, SizeFull };
    public static string NormalizeSize(string? size, string fallback) => Sizes.FirstOrDefault(s => s == size) ?? fallback;
    public static KeyboardModeSize ParseSize(string? size) => size switch { SizeLarge => KeyboardModeSize.Large, SizeFull => KeyboardModeSize.Full, _ => KeyboardModeSize.Normal };

    public static int NormalizeLookAhead(int seconds) => Math.Clamp(seconds, MinLookAheadSeconds, MaxLookAheadSeconds);
    public static string NormalizeMidiInput(string? input) => string.IsNullOrWhiteSpace(input) ? MidiAny : input.Trim();
    public static string NormalizeHands(string? hands) => KeyboardHands.NameOf(KeyboardHands.Parse(hands));
}
