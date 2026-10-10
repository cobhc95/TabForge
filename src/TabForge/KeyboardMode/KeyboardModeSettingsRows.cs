using TabForge.KeyboardMode;

namespace TabForge.Services;

// Owns: the Keyboard mode rows of the settings catalogue (Fretboard page); KeyboardModeFeatureModule hands them to the catalogue.
// Does not own: page layout (SettingsCatalog.Layout) or the stored values (AppSettings.Learn).
// Tests: TestPreferencesCatalog, TestSettingsAudit.
public static partial class SettingsCatalog
{
    internal static List<SettingDescriptor> KeyboardModeRows(AppSettings s)
    {
        var learn = s.Learn ??= new KeyboardModeSettings();
        return new List<SettingDescriptor>
        {
            Int(Fretboard, "Keyboard mode (experimental)", "learn.lookahead", "Look-ahead", v => learn.LookAheadSeconds = KeyboardModeSettings.NormalizeLookAhead(v), () => KeyboardModeSettings.NormalizeLookAhead(learn.LookAheadSeconds),
                KeyboardModeSettings.MinLookAheadSeconds, KeyboardModeSettings.MaxLookAheadSeconds,
                "How many seconds of the song Keyboard mode shows above the keys. Fewer seconds make the notes longer and easier to read; more show further ahead. Also - and + in its control bar, or Ctrl + mouse wheel over the notes.", "keyboard mode learn look ahead seconds notes fall", "s"),
            Bool(Fretboard, "Keyboard mode (experimental)", "learn.playalong", "Play along with a MIDI keyboard", v => learn.PlayAlongKeyboard = v, () => learn.PlayAlongKeyboard,
                "Listen to a MIDI keyboard in Keyboard mode (every connected device unless one is picked in its control bar), play it through the selected track's sound and score it; the track itself is silent meanwhile. Off: Keyboard mode does not open the MIDI input. Nothing is recorded.", "keyboard mode learn midi play along score input"),
            Bool(Fretboard, "Keyboard mode (experimental)", "learn.wait", "Wait for the right notes", v => learn.WaitForNotes = v, () => learn.WaitForNotes,
                "With a MIDI keyboard play-along: pause the song at each chord until you press its notes, then carry on. Press the skip key to give up a chord. Off by default.", "keyboard mode learn wait pause chord practice"),
            Choice(Fretboard, "Keyboard mode (experimental)", "learn.sizekeys", "Size", v => learn.KeyboardSize = KeyboardModeSettings.NormalizeSize(v, KeyboardModeSettings.SizeFull), () => KeyboardModeSettings.NormalizeSize(learn.KeyboardSize, KeyboardModeSettings.SizeFull),
                KeyboardModeSettings.Sizes, "How much room Keyboard mode takes. Full shows the falling notes instead of the score, above the keyboard.", "keyboard mode learn size layout piano normal large full"),
            Choice(Fretboard, "Keyboard mode (experimental)", "learn.hands", "Hands", v => learn.Hands = KeyboardModeSettings.NormalizeHands(v), () => KeyboardModeSettings.NormalizeHands(learn.Hands),
                KeyboardHands.FilterNames, "Which hand you practise in Keyboard mode: its notes are judged and awaited; the other hand's notes are faded, or hidden with \"only\".", "keyboard mode learn hands left right practise one hand fade"),
            Choice(Fretboard, "Keyboard mode (experimental)", "learn.timing", "Timing tolerance", v => learn.TimingTolerance = KeyboardModeSettings.NormalizeTiming(v), () => KeyboardModeSettings.NormalizeTiming(learn.TimingTolerance),
                KeyboardModeSettings.Timings, "How exactly you must hit the notes' time. Relaxed (default) counts a press up to 150 ms early or late as the best grade and up to 300 ms as a hit; Normal 100 / 220 ms; Strict 60 / 150 ms. \"Wait for the right notes\" has no timing limit at all.", "keyboard mode learn timing tolerance strict normal relaxed judge perfect good"),
            Bool(Fretboard, "Keyboard mode (experimental)", "learn.notenames", "Note names on the notes", v => learn.ShowNoteNames = v, () => learn.ShowNoteNames,
                "Write each note's name (F#3) on its falling bar when the bar has room.", "keyboard mode learn note names labels"),
            Bool(Fretboard, "Keyboard mode (experimental)", "learn.fingers", "Finger numbers on the notes", v => learn.ShowFingers = v, () => learn.ShowFingers,
                "Show the finger written in the score (1 thumb to 5 little) on each falling bar. Songs without fingering show none.", "keyboard mode learn fingering finger numbers"),
        };
    }
}
