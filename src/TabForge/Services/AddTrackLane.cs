using TabForge.Models;

namespace TabForge.Services;

// Owns: the Add-track lane's numbers and rules (height, label, what a drop on it creates).
// Does not own: drawing it (ArrangementPanel / TrackTimeline) or the add-track prompt (Views/AddTrackPrompt).
// Tests: TestAddTrackLane.
/// <summary>The "Add track" strip under the last track, across the track list and the timeline.</summary>
public static class AddTrackLane
{
    /// <summary>The words on the strip.</summary>
    public const string Label = "Add track";

    /// <summary>The words while a file is dragged over the zone.</summary>
    public const string DropLabel = "Drop to add an audio track";

    /// <summary>About one row tall.</summary>
    public const double Height = 30;

    public static double HeightOf(bool shown) => shown ? Height : 0;

    /// <summary>A file dropped on the lane always makes an AUDIO track, whatever the file is (audio or MIDI).</summary>
    public const TrackKind DropKind = TrackKind.Audio;
}
