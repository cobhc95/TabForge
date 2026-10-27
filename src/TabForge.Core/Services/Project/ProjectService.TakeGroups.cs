using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TabForge.Models;

namespace TabForge.Services;

// Owns: the load-time migrations of old take groups: loop-record pass groups to separate takes (takes v2) and stamped repeat takes (the T2
//   build) to one ordinary clip (repeat view, RV7).
// Does not own: the landing (RecordedTakes), the save/validate path or the drawing.
// Tests: TabForge.Avalonia.Tests.LoopPassGroupTests, RepeatViewTests.
public static partial class ProjectService
{
    /// <summary>
    /// Old loop groups load as separate takes (takes v2): each pass becomes its own ordinary clip on its own lane
    /// (in pass order, so overlapping laps stack upward), the newest audible and older ones muted; the loop-only
    /// fields are dropped. Repeat takes are left alone. Unknown JSON fields from old files are ignored by the
    /// deserializer, so removed optional fields need no read-only shadow.
    /// </summary>
    internal static void MigrateLoopGroupsToSeparateTakes(SongProject project)
    {
        if (project.Tracks is not { } tracks) return;
        foreach (var track in tracks)
        {
            if (track is null || track.AudioClips is not { Count: > 0 }) continue;
            var groups = track.AudioClips
                .Where(clip => clip is not null && !string.IsNullOrEmpty(clip.TakeGroupId) && clip.TakeGroupKind != TakeGroupKind.Repeat)
                .GroupBy(clip => clip.TakeGroupId!, StringComparer.Ordinal).ToList();
            foreach (var group in groups)
            {
                var members = TakeGroups.Ordered(group.ToList());
                if (members.Count == 0) continue;
                var lane = members.Min(member => Math.Max(0, member.Lane));
                for (var index = 0; index < members.Count; index++)
                {
                    var member = members[index];
                    member.TakeGroupId = null;
                    member.TakeGroupKind = TakeGroupKind.Loop;
                    member.TakeGroupPass = 0;
                    member.Lane = lane + index;
                    member.Muted = index < members.Count - 1;
                }
                ClipLanes.Ensure(track, lane + members.Count);
            }
        }
    }

    private const double RepeatMergeToleranceSec = 1e-3;
    private static readonly Regex RepeatPartSuffix = new(@"\s+(pre|post|part \d+|mid \d+)$", RegexOptions.CultureInvariant);

    /// <summary>
    /// Stamped repeat takes (the T2 build: a group of clips marked <see cref="TakeGroupKind.Repeat"/>, one per play) load as ONE ordinary clip
    /// spanning the first part start to the last part end in performed time (the repeat view). Audio parts merge when they read the same file
    /// contiguously; MIDI parts always merge (notes shifted onto the merged clip). A group whose audio is not contiguous keeps its earliest
    /// part as the one clip and drops the rest. Returns how many groups kept only their earliest part.
    /// </summary>
    internal static int MigrateRepeatGroupsToOneClip(SongProject project)
    {
        var keptEarliestOnly = 0;
        if (project.Tracks is not { } tracks) return 0;
        foreach (var track in tracks)
        {
            if (track is null || track.AudioClips is not { Count: > 0 }) continue;
            var groupIds = track.AudioClips
                .Where(clip => clip is not null && clip.TakeGroupKind == TakeGroupKind.Repeat && !string.IsNullOrEmpty(clip.TakeGroupId))
                .Select(clip => clip.TakeGroupId!).Distinct(StringComparer.Ordinal).ToList();
            foreach (var groupId in groupIds)
            {
                var members = track.AudioClips.Where(clip => clip is not null && clip.TakeGroupId == groupId).ToList();
                var at = track.AudioClips.IndexOf(members[0]);
                var merged = MergeRepeatMembers(members, out var contiguous);
                if (!contiguous) keptEarliestOnly++;
                foreach (var member in members) track.AudioClips.Remove(member);
                track.AudioClips.Insert(Math.Min(at, track.AudioClips.Count), merged);
            }
        }
        return keptEarliestOnly;
    }

    private static AudioClip MergeRepeatMembers(List<AudioClip> members, out bool contiguous)
    {
        var inTime = members.OrderBy(member => member.StartSec).ThenBy(member => member.TakeGroupPass).ToList();
        var first = inTime[0];
        var last = inTime.OrderBy(member => member.EndSec).Last();
        var merged = first.Clone();
        merged.TakeGroupId = null;
        merged.TakeGroupKind = TakeGroupKind.Loop;
        merged.TakeGroupPass = 0;
        merged.Lane = members.Min(member => Math.Max(0, member.Lane));
        merged.Name = RepeatPartSuffix.Replace(first.Name ?? "", "");
        merged.Muted = members.All(member => member.Muted);
        merged.FadeOutSec = last.FadeOutSec;
        contiguous = true;
        if (first.IsMidi)
        {
            merged.Notes = inTime.SelectMany(member => (member.Notes ?? new List<ClipNote>())
                .Select(note => note with { StartSec = note.StartSec + member.StartSec - first.StartSec })).OrderBy(note => note.StartSec).ThenBy(note => note.Pitch).ToList();
            merged.OffsetSec = 0;
            merged.SourceLengthSec = last.EndSec - first.StartSec;
            return merged;
        }
        // Audio parts in file order must read the same file one after another; the merged clip then reads it straight through.
        var inFile = inTime.OrderBy(member => member.OffsetSec).ToList();
        for (var i = 1; i < inFile.Count; i++)
            if (inFile[i].File != inFile[0].File || Math.Abs(inFile[i].OffsetSec - (inFile[i - 1].OffsetSec + inFile[i - 1].SourceLengthSec)) > RepeatMergeToleranceSec)
                contiguous = false;
        if (!contiguous) { merged.FadeOutSec = first.FadeOutSec; merged.Muted = first.Muted; return merged; }
        merged.StartSec = inFile[0].StartSec;
        merged.OffsetSec = inFile[0].OffsetSec;
        merged.SourceLengthSec = inFile[^1].OffsetSec + inFile[^1].SourceLengthSec - inFile[0].OffsetSec;
        merged.FadeInSec = inFile[0].FadeInSec;
        merged.FadeOutSec = inFile[^1].FadeOutSec;
        return merged;
    }
}
