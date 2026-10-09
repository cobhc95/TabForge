using TabForge.Models;
using TabForge.Playback;
using TabForge.Services;

namespace TabForge.Controllers;

// Owns: switching a track's sound in the running playback while an instrument picker is open, and putting it back.
// Does not own: the document (the track is never edited here) or the commit (the caller edits through its normal undo path).
// Tests: TestLiveInstrumentPreview.
public static class InstrumentLivePreview
{
    /// <summary>Begin (name given) or end (null) the preview. Ending sends the track's current program, so call it after a commit or cancel.</summary>
    public static void Apply(PlaybackEngine engine, TrackModel track, string? name)
    {
        if (track.IsAudio) return;
        if (name is null) { engine.EndProgramPreview(track.MidiOutputDeviceId, track.MidiChannel, track.MidiProgram); return; }
        if (!engine.IsPlaying) return;
        // A sound of the other family (kit on a melodic channel or the reverse) would need a channel move: not previewed.
        if (InstrumentCatalog.Find(name) is not { } entry || entry.IsDrumKit != (track.MidiChannel == 9)) return;
        engine.BeginProgramPreview(track.MidiOutputDeviceId, track.MidiChannel, entry.Program);
    }
}
