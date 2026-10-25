using System.Collections.Generic;
using System.Linq;
using TabForge.Models;

namespace TabForge.Features.Recording.Model;

// Owns: the writes on one repeat take: removing the whole take and muting it as one.
// Does not own: the read rules (TakeGroups), the undo step (DocumentEdits), the command keys or the status text.
// Tests: TabForge.Avalonia.Tests.RepeatTakeLandingTests.
public static class TakeGroupEdits
{
    /// <summary>
    /// Removes only this clip (a clip over a repeat is one piece, so there is no group to take with it). False when the clip was not on the track.
    /// </summary>
    public static bool Remove(TrackModel track, AudioClip clip)
    {
        if (!track.AudioClips.Remove(clip)) return false;
        return true;
    }

    /// <summary>
    /// Mutes every member when the clip plays; unmuting restores every member.
    /// A repeat take has no active pass: unmuting restores every member. An ordinary clip flips alone.
    /// </summary>
    public static bool ToggleMute(TrackModel track, AudioClip clip)
    {
        if (!TakeGroups.IsGrouped(clip)) return AudioClipEdits.ToggleMute(clip);
        var members = TakeGroups.Members(track, clip);
        if (!members.All(member => member.Muted))
        {
            var changed = false;
            foreach (var member in members) { changed |= !member.Muted; member.Muted = true; }
            return changed;
        }
        var unmuted = false;
        foreach (var member in members) { unmuted |= member.Muted; member.Muted = false; }
        return unmuted;
    }
}
