using System.Collections.Generic;
using System.IO;
using System.Linq;
using TabForge.Models;

namespace TabForge.Features.Recording.Rules;

// Owns: turning one finished engine recording into its separate take clips (one per loop lap) and placing takes on a track's free lanes,
//   the newest take playing while older ones are muted, the S5b rule that a take mutes the older clips it covers completely, and closing
//   the lanes the recording opened that no take needs. A recording through score repeats is one ordinary clip (the repeat view only folds
//   its drawing, see RepeatFold).
// Does not own: capture (AudioEngineClient), the undo step around the placement (the callers run it in DocumentEdits), the status text shown.
// Tests: TabForge.Avalonia.Tests.TransportRecordingTests (full suite), RepeatPassesTests, RepeatTakeLandingTests, RepeatViewTests.
public static class RecordedTakes
{
    /// <summary>Recordings shorter than this (seconds) are discarded.</summary>
    public const double MinimumLengthSec = 0.05;

    /// <summary>
    /// The audio takes of a recording of <paramref name="file"/> that began at <paramref name="startSec"/> and lasted <paramref name="lengthSec"/>,
    /// cut at the transport loop when one was used (one ordinary take per lap, the T1 rule: take 1 runs from the record start to the end of lap 1,
    /// every later take covers one whole lap, all but the newest muted), else one ordinary clip. A recording through score repeats is one piece in
    /// performed time (the repeat view folds only its drawing).
    /// </summary>
    public static List<AudioClip> FromRecording(string file, double startSec, double lengthSec, (double Start, double End)? loop)
    {
        var loopRange = loop is { } candidate && candidate.End - candidate.Start >= MinimumLengthSec
            ? candidate
            : ((double Start, double End)?)null;
        var name = Path.GetFileNameWithoutExtension(file);
        var passes = RecordingPasses.Split(startSec, lengthSec, loopRange);
        var takes = new List<AudioClip>();
        var recordingId = passes.Count > 1 ? System.Guid.NewGuid().ToString("N") : null;
        for (var index = 0; index < passes.Count; index++)
        {
            var pass = passes[index];
            takes.Add(new AudioClip
            {
                File = file, StartSec = pass.SongStart, OffsetSec = pass.FileOffset,
                SourceLengthSec = pass.Length, FileLengthSec = lengthSec,
                Muted = index < passes.Count - 1, LoopRecordingId = recordingId,
            });
        }
        for (var index = 0; index < takes.Count; index++)
            takes[index].Name = takes.Count > 1 ? $"{name} take {index + 1}" : name;
        return takes;
    }

    /// <summary>
    /// Replaces one pass of a pass group with <paramref name="take"/>: only the audio source fields change (file, trim,
    /// source and file length), the target keeps its id, part number, group, lane and name, its fades are reset to 0.
    /// <paramref name="midi"/> replaces the part's notes instead. False when the
    /// group or the part no longer exists.
    /// </summary>
    public static bool ReplacePass(TrackModel track, string groupId, int pass, AudioClip take, bool midi)
    {
        var ordered = TakeGroups.Ordered(TakeGroups.GroupOf(track, groupId));
        var target = ordered.FirstOrDefault(member => member.TakeGroupPass == pass);
        if (target is null) return false;
        if (midi)
            target.Notes = take.Notes is null ? new List<ClipNote>() : take.Notes.Select(note => note with { }).ToList();
        else
        {
            target.File = take.File;
            target.OffsetSec = take.OffsetSec;
            target.SourceLengthSec = take.SourceLengthSec;
            target.FileLengthSec = take.FileLengthSec;
        }
        target.FadeInSec = 0;
        target.FadeOutSec = 0;
        return true;
    }

    /// <summary>
    /// Replaces a MIDI pass's notes with every note the punch's finished takes hold (none: the pass becomes empty). The notes keep
    /// their offsets inside the pass; see <see cref="ReplacePass"/>.
    /// </summary>
    public static bool ReplaceMidiPass(TrackModel track, string groupId, int pass, IReadOnlyList<AudioClip> takes)
    {
        var notes = takes.SelectMany(take => take.Notes ?? new List<ClipNote>())
            .OrderBy(note => note.StartSec).ThenBy(note => note.Pitch).ToList();
        return ReplacePass(track, groupId, pass, new AudioClip { Notes = notes }, midi: true);
    }

    /// <summary>The status line after takes landed on <paramref name="track"/>.</summary>
    public static string StatusText(TrackModel track, int takeCount, double lengthSec) =>
        takeCount > 1 ? $"Recorded {takeCount} takes on {track.Name} (the newest plays)" : $"Recorded {lengthSec:0.0} s on {track.Name}";

    /// <summary>
    /// Adds the takes to the track: take 1 takes its own first free lane (the normal rule, searched over its whole span), every later lap
    /// of the same landing stacks onto a new lane above it (the laps overlap on purpose, so the free-lane search always moves up, and the
    /// lane is ensured). The takes arrive with their mute already set (older laps muted, the newest audible) and keep it; the newest take's
    /// lane plays when any take needed a new lane. The lanes the
    /// recording opened while the take was still growing are closed again when no clip needs them: the live box is drawn at the raw
    /// song second, while the landed take is stamped at that second less the engine's alignment (output delay, input latency), so a
    /// growing take can need a lane the finished take does not. A track left armed after recording would keep that lane, because
    /// <see cref="ClipLanes.Compact"/> never touches an armed track and <see cref="ClipLanes.Trim"/> only runs on disarm.
    /// </summary>
    public static void Place(TrackModel track, List<AudioClip> takes, bool midi)
    {
        if (takes.Count == 0) return;
        var newLane = false;

        foreach (var take in takes)
        {
            take.Lane = ClipLanes.FreeLane(track, take.StartSec, take.EndSec, except: null, takeGroupId: null);
            newLane |= take.Lane > 0 || track.AudioClips.Any(c => c.IsMidi == midi && c.Overlaps(take.StartSec, take.EndSec));
            ClipLanes.Ensure(track, take.Lane + 1);
            track.AudioClips.Add(take);
            // S5b: an older clip the landed take covers completely is muted, in this same call (the caller's one undo step).
            TakeOverlap.MuteCovered(track, take);
        }
        if (newLane) ClipLanes.PlayNewTake(track, takes[^1].Lane, midi);
        ClipLanes.Trim(track);   // the lane the growing take opened that no take needed: an armed track is never compacted
    }
}
