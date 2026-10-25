// Owns: the wording, buttons, Enter default, radio choices, remember box and answer mapping of the small track and confirm prompts (Add track,
//       Delete track, Convert to audio track, Convert to instrument track, Change instrument and the generic Confirm), and the Undo sentence they share.
// Does not own: showing a prompt (WPF Views/ConfirmPrompt, Avalonia Views/PromptWindow), the Undo key text (the caller passes it in), or whether
//       a prompt is asked at all (the flows and TrackSetup).
// Tests: none dedicated(WPF characterization), PromptWindowTests (Avalonia headless).
namespace TabForge.Presenters;

/// <summary>The answer a prompt gets. Esc, the close box and the title-bar cross all give <see cref="Cancel"/>.</summary>
public enum PromptAnswer { Yes, No, Cancel }

/// <summary>What the Add track prompt chose.</summary>
public enum AddTrackChoice { None, Audio, Instrument }

/// <summary>What the Convert to instrument track clip prompt chose. Cancel abandons the conversion.</summary>
public enum ClipAnswer { Cancel, WriteNotation, KeepClips }

/// <summary>One prompt as plain data. Each field is one setting of the themed dialog built from it.</summary>
/// <param name="Choices">Radio choices listed above the buttons, or null for none.</param>
/// <param name="RememberText">Label of the "remember" check box, or null for none.</param>
public sealed record PromptSpec(
    string Title,
    string Message,
    string YesText,
    string NoText,
    string YesToolTip,
    string NoToolTip,
    bool ShowCancel,
    bool DefaultIsNo,
    IReadOnlyList<string>? Choices = null,
    string? RememberText = null);

public static class PromptTextsPresenter
{
    /// <summary>The sentence that tells the user how to undo: it names the Undo key when there is one.</summary>
    public static string UndoHint(string undoKey) =>
        undoKey.Length == 0 ? "You can undo this." : $"You can undo this with {undoKey}.";

    /// <summary>True for the Yes answer of a two-button question.</summary>
    public static bool IsConfirmed(PromptAnswer answer) => answer == PromptAnswer.Yes;

    public static PromptSpec AddTrack() => new(
        "Add track", "What kind of track do you want to add?",
        YesText: "Audio track", NoText: "Instrument…",
        YesToolTip: "An audio track: it holds audio and MIDI clips and has no notation",
        NoToolTip: "An instrument track: you pick the instrument next",
        ShowCancel: true, DefaultIsNo: false);

    public static AddTrackChoice AddTrackAnswer(PromptAnswer answer) => answer switch
    {
        PromptAnswer.Yes => AddTrackChoice.Audio,
        PromptAnswer.No => AddTrackChoice.Instrument,
        _ => AddTrackChoice.None,
    };

    public static PromptSpec DeleteTrack(string trackName, string undoKey) => new(
        "Delete track", $"Delete the track '{trackName}'? {UndoHint(undoKey)}",
        YesText: "Delete", NoText: "Cancel",
        YesToolTip: "Delete the track", NoToolTip: "Keep the track",
        ShowCancel: false, DefaultIsNo: true);

    /// <summary>Delete or Cut on several selected track rows: one question listing them ("Delete 2 tracks: A, B?"); one track keeps <see cref="DeleteTrack"/>.</summary>
    public static PromptSpec DeleteTracks(IReadOnlyList<string> trackNames, string undoKey) => trackNames.Count == 1 ? DeleteTrack(trackNames[0], undoKey) : new(
        "Delete tracks", $"Delete {trackNames.Count} tracks: {NamesShown(trackNames)}? {UndoHint(undoKey)}",
        YesText: "Delete", NoText: "Cancel",
        YesToolTip: "Delete the tracks", NoToolTip: "Keep the tracks",
        ShowCancel: false, DefaultIsNo: true);

    private const int MaxNamesShown = 5;

    /// <summary>The first few names, then "and N more" (a long selection must not make the question taller than the screen).</summary>
    private static string NamesShown(IReadOnlyList<string> names) =>
        names.Count <= MaxNamesShown ? string.Join(", ", names) : $"{string.Join(", ", names.Take(MaxNamesShown))} and {names.Count - MaxNamesShown} more";

    /// <summary>Delete or Cut on the grey timeline highlight: the cells' notes go, the bars stay. "Don't ask again" turns the question off (Preferences > Editing turns it back on).</summary>
    public static PromptSpec ClearHighlightedBars(int cells) => new(
        "Clear highlighted bars", $"Clear the notes in {cells} highlighted {(cells == 1 ? "bar" : "bars")}? The bars stay.",
        YesText: "Clear", NoText: "Cancel",
        YesToolTip: "Clear the notes in the highlighted bars", NoToolTip: "Keep the notes",
        ShowCancel: false, DefaultIsNo: false, RememberText: "Don't ask again");

    public static PromptSpec ConvertTrackToAudio(string trackName, string undoKey) => new(
        "Convert to audio track",
        $"Convert '{trackName}' to an audio track? Its notation becomes a MIDI clip on the track, so nothing is lost, and the bars are emptied. " +
        $"The track is silent unless an instrument plug-in is on it. Clips already on the track stay on their lanes. {UndoHint(undoKey)}",
        YesText: "Convert", NoText: "Cancel",
        YesToolTip: "Convert the track", NoToolTip: "Keep the instrument track",
        ShowCancel: false, DefaultIsNo: true);

    /// <summary>The question after the instrument pick of Convert to instrument track: what happens to the clips.</summary>
    public static PromptSpec ConvertTrackToInstrumentClips(string trackName, bool hasMidi) => hasMidi
        ? new PromptSpec("Convert to instrument track", $"'{trackName}' has MIDI clips. What should happen to them?",
            YesText: "Convert", NoText: "Cancel",
            YesToolTip: "Convert the track", NoToolTip: "Keep the audio track",
            ShowCancel: false, DefaultIsNo: false,
            Choices: new[] { "Write them as notation", "Keep them as MIDI on a second lane" },
            RememberText: "Remember my choice")
        : new PromptSpec("Convert to instrument track",
            $"'{trackName}' has audio clips. They stay on the track on a second lane under the new notation lane.",
            YesText: "OK", NoText: "Cancel",
            YesToolTip: "Convert the track", NoToolTip: "Keep the audio track",
            ShowCancel: false, DefaultIsNo: false);

    /// <summary>
    /// The clip question's answer. Anything but Yes cancels; without MIDI clips Yes keeps the clips as audio; with MIDI clips the
    /// first option writes them as notation, the second keeps them as MIDI, and the remember box is only kept on Yes.
    /// </summary>
    public static (ClipAnswer Answer, bool Remember) ConvertClipsAnswer(PromptAnswer answer, bool hasMidi, int selectedChoice, bool rememberTicked)
    {
        if (answer != PromptAnswer.Yes) return (ClipAnswer.Cancel, false);
        if (!hasMidi) return (ClipAnswer.KeepClips, false);
        return (selectedChoice == 0 ? ClipAnswer.WriteNotation : ClipAnswer.KeepClips, rememberTicked);
    }

    public static PromptSpec ChangeInstrument(string currentInstrument, string newInstrument) => new(
        "Change instrument",
        $"Change {currentInstrument} to {newInstrument}? Its notes are re-mapped to the new instrument.",
        YesText: "OK", NoText: "Cancel",
        YesToolTip: "Change the instrument and re-map the notes", NoToolTip: "Keep the current instrument",
        ShowCancel: false, DefaultIsNo: false);

    /// <summary>The generic themed question; with <paramref name="withUndoHint"/> the Undo sentence follows the text.</summary>
    public static PromptSpec Confirm(string title, string text, string yesText, bool withUndoHint, string undoKey) => new(
        title, withUndoHint ? $"{text} {UndoHint(undoKey)}" : text,
        YesText: yesText, NoText: "Cancel",
        YesToolTip: yesText, NoToolTip: "Leave things as they are",
        ShowCancel: false, DefaultIsNo: false);
}
