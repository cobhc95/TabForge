using System.Windows;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Views;

/// <summary>Owns: the OK/Cancel question before a drum/pitched instrument change re-maps a track's notes. Does not own: the change.</summary>
internal static class FamilyChangePrompt
{
    /// <summary>True when the change may go ahead (no notes to re-map, or the user confirmed).</summary>
    public static bool Confirm(Window? owner, TrackModel track, string newInstrument, bool toDrums)
    {
        if (!TrackSetup.ConversionRemapsNotes(track, toDrums)) return true;
        var prompt = new ThemedConfirmDialog("Change instrument",
            $"Change {track.InstrumentName} to {newInstrument}? Its notes are re-mapped to the new instrument.",
            yesToolTip: "Change the instrument and re-map the notes", noToolTip: "Keep the current instrument",
            showCancel: false, yesText: "OK", noText: "Cancel", defaultIsNo: false) { Owner = owner };
        return DialogHost.ShowModal(prompt) == true && prompt.Result == MessageBoxResult.Yes;
    }
}
