using System.Windows;

namespace TabForge.Views;

/// <summary>What the Add-track prompt chose.</summary>
internal enum AddTrackChoice { None, Audio, Instrument }

// Owns: the first question of the Add-track lane: Audio or Instrument (the instrument itself is then picked in the Add track window).
// Tests: TestAddTrackLane.
internal static class AddTrackPrompt
{
    /// <summary>Asks "Audio track or instrument track?" (Yes = audio, No = instrument, Cancel/Esc = nothing).</summary>
    public static AddTrackChoice Ask(Window? owner)
    {
        var dialog = new ThemedConfirmDialog("Add track", "What kind of track do you want to add?",
            yesToolTip: "An audio track: it holds audio and MIDI clips and has no notation",
            noToolTip: "An instrument track: you pick the instrument next",
            showCancel: true, yesText: "Audio track", noText: "Instrument…");
        if (owner is { IsLoaded: true }) dialog.Owner = owner;
        if (DialogHost.ShowModal(dialog) != true) return AddTrackChoice.None;
        return dialog.Result switch
        {
            MessageBoxResult.Yes => AddTrackChoice.Audio,
            MessageBoxResult.No => AddTrackChoice.Instrument,
            _ => AddTrackChoice.None
        };
    }
}
