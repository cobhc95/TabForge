using TabForge.Models;

namespace TabForge.Features.Recording.Rules;

/// <summary>How MIDI takes land: <see cref="Merge"/> adds the notes into the clip underneath (else each take gets its own lane), and
/// <see cref="QuantizeGrid"/> (a SnapSettings grid value, null: off) snaps the recorded note starts at commit time (the caller runs
/// MidiTakeQuantize before <see cref="MidiTakeLanding.Land"/>: the grid needs the song's bar timing, outside these rules).</summary>
public sealed record MidiRecordOptions(bool Merge = false, string? QuantizeGrid = null);

// Owns: placing finished MIDI takes on a track by the recording options: merging
//   a take into the MIDI clip underneath, as plain model edits (the caller owns the one undo step).
// Does not own: the input quantize (Presenters.MidiTakeQuantize, run first by the caller), the capture (MidiTakeCapture), lane placement of a new-lane take (RecordedTakes.Place), the settings rows, the undo step.
// Tests: TabForge.Avalonia.Tests.MidiRecordLandingTests.
public static class MidiTakeLanding
{
    public const string MergeChoice = "Merge into the clip underneath", NewLaneChoice = "New lane";

    /// <summary>Lands the takes on <paramref name="track"/> (already quantized by the caller when asked): each into the MIDI clip that holds its start
    /// (merge) or on its own free lane. A take with no clip under its start gets a lane as usual.</summary>
    public static void Land(SongProject project, TrackModel track, List<AudioClip> clips, MidiRecordOptions options)
    {
        _ = project;   // kept for the call shape: the quantize that needed it runs before (MidiTakeQuantize)
        if (!options.Merge) { RecordedTakes.Place(track, clips, midi: true); return; }
        var lanes = new List<AudioClip>();
        foreach (var take in clips)
        {
            var host = track.AudioClips.FirstOrDefault(c => c.IsMidi && c.TakeGroupId is null && c.StartSec <= take.StartSec + 1e-6 && c.EndSec > take.StartSec);
            if (host is null) { lanes.Add(take); continue; }
            var shift = take.StartSec - host.StartSec;
            host.Notes!.AddRange((take.Notes ?? new()).Select(n => n with { StartSec = n.StartSec + shift }));
            host.Notes = host.Notes.OrderBy(n => n.StartSec).ThenBy(n => n.Pitch).ToList();
            var end = host.Notes.Max(n => n.StartSec + n.LengthSec);
            if (end > host.SourceLengthSec) host.SourceLengthSec = end;
        }
        RecordedTakes.Place(track, lanes, midi: true);
    }
}
