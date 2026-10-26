using TabForge.Models;
using TabForge.Services;

namespace TabForge.Presenters;

// Owns: the MIDI recording's input quantize: snapping recorded note starts to the shared SnapCore grid (triplets included) in song bars.
// Does not own: where the takes land (Features.Recording.Rules.MidiTakeLanding, called after this), the capture, the undo step.
// Tests: TabForge.Avalonia.Tests.MidiRecordLandingTests.
public static class MidiTakeQuantize
{
    /// <summary>Snaps each note start of <paramref name="clips"/> to the grid (song bars; the note keeps its length, never starts before 0).</summary>
    public static void Quantize(SongProject project, IEnumerable<AudioClip> clips, string grid)
    {
        foreach (var clip in clips)
        {
            if (clip.Notes is not { } notes) continue;
            for (var i = 0; i < notes.Count; i++)
            {
                var absolute = clip.StartSec + notes[i].StartSec;
                var (bar, fraction) = AudioClipTimelineRules.BarAtSeconds(project, absolute);
                var slots = Math.Max(1, MusicTime.BarSlots(project, Math.Max(0, bar)));
                var snapped = AudioClipTimelineRules.SecondsAtBar(project, Math.Max(0, bar), SnapCore.NearestInBar(fraction, 0, 1, slots, grid));
                notes[i] = notes[i] with { StartSec = Math.Max(0, snapped - clip.StartSec) };
            }
            clip.Notes = notes.OrderBy(n => n.StartSec).ThenBy(n => n.Pitch).ToList();
        }
    }
}
