using System.Windows;

namespace TabForge.Views;

// Owns: the themed questions of the track conversions (Esc and Cancel change nothing).
// Tests: TestInstrumentToAudioConversion, TestConvertToInstrumentFlow.
internal static class ConvertTrackPrompts
{
    /// <summary>True when the user chose to turn the instrument track <paramref name="trackName"/> into an audio track.</summary>
    public static bool AskToAudio(Window? owner, string trackName)
    {
        var undoKey = Services.HotkeyCatalog.DisplayAll(TooltipShortcuts.Hotkeys, "Edit.Undo");
        var undoHint = undoKey.Length == 0 ? "You can undo this." : $"You can undo this with {undoKey}.";
        var dialog = new ThemedConfirmDialog("Convert to audio track",
            $"Convert '{trackName}' to an audio track? Its notation becomes a MIDI clip on the track, so nothing is lost, and the bars are emptied. " +
            $"The track is silent unless an instrument plug-in is on it. Clips already on the track stay on their lanes. {undoHint}",
            yesToolTip: "Convert the track", noToolTip: "Keep the instrument track", showCancel: false, yesText: "Convert", noText: "Cancel", defaultIsNo: true);
        if (owner is { IsLoaded: true }) dialog.Owner = owner;
        return DialogHost.ShowModal(dialog) == true && dialog.Result == MessageBoxResult.Yes;
    }

    public enum ClipAnswer { Cancel, WriteNotation, KeepClips }

    /// <summary>The prompt after the instrument pick of Convert to instrument track: what happens to the clips. Cancel (Esc, close, No) abandons the conversion.</summary>
    public static ClipAnswer AskClips(Window? owner, string trackName, bool hasMidi, out bool remember)
    {
        remember = false;
        var dialog = hasMidi
            ? new ThemedConfirmDialog("Convert to instrument track",
                $"'{trackName}' has MIDI clips. What should happen to them?", yesToolTip: "Convert the track", noToolTip: "Keep the audio track",
                showCancel: false, yesText: "Convert", noText: "Cancel", rememberText: "Remember my choice",
                choices: new[] { "Write them as notation", "Keep them as MIDI on a second lane" })
            : new ThemedConfirmDialog("Convert to instrument track",
                $"'{trackName}' has audio clips. They stay on the track on a second lane under the new notation lane.",
                yesToolTip: "Convert the track", noToolTip: "Keep the audio track", showCancel: false, yesText: "OK", noText: "Cancel");
        if (owner is { IsLoaded: true }) dialog.Owner = owner;
        if (DialogHost.ShowModal(dialog) != true || dialog.Result != MessageBoxResult.Yes) return ClipAnswer.Cancel;
        if (!hasMidi) return ClipAnswer.KeepClips;
        remember = dialog.RememberChoice;
        return dialog.SelectedChoice == 0 ? ClipAnswer.WriteNotation : ClipAnswer.KeepClips;
    }
}
