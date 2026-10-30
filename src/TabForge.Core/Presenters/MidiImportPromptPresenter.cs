using TabForge.Services;

namespace TabForge.Presenters;

// Owns: the words and the answer of the "Import MIDI" question (title, file and track lines, the two choices, the buttons) and the rule for what is picked at first.
// Does not own: the window (MidiImportPromptWindow), the conversion (MidiToSong) or the stored setting (MidiImportModes).
// Tests: TabForge.Avalonia.Tests.MidiImportChoiceTests.

/// <summary>What the person answered: the mode to import with, and whether to remember it.</summary>
public readonly record struct MidiImportAnswer(MidiImportMode Mode, bool Remember);

/// <summary>The wording of the "Import MIDI" question.</summary>
public static class MidiImportPromptPresenter
{
    public const string Title = "Import MIDI";
    public const string TabTitle = "As tab and notation tracks";
    public const string TabHint = "Guitar, bass and drums become tab tracks you can read and edit as tab; MIDI details such as controllers are kept out of the way.";
    public const string MidiTitle = "As MIDI tracks (piano roll)";
    public const string MidiHint = "Every track keeps its notes, controllers and pitch bends as MIDI and is edited in the piano roll.";
    public const string RememberText = "Remember my choice";
    public const string RememberHint = "You can change this in Settings.";
    public const string ImportText = "Import";
    public const string CancelText = "Cancel";

    public static string FileLine(string path) => System.IO.Path.GetFileName(path);

    public static string TracksLine(int tracks) => $"{tracks} track{(tracks == 1 ? "" : "s")}";

    /// <summary>The mode picked when the question opens: tab when a track is a guitar, bass or drum part, else MIDI.</summary>
    public static MidiImportMode DefaultMode(MidiImportPreview preview) => preview.DefaultMode;
}
