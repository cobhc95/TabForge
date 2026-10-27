using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the playback read-model gate: every engine file (PlaybackEngine*.cs, the schedule reuse, the seek rebuild, the audio-growth refresh) reads the song
//   only through IScoreQuery on the scheduler thread and pool threads. A live-song or snapshot-copy read (tracks, measures, mixer, revisions, LiveProject,
//   the copy) is allowed only in the reviewed owner-thread entries listed below, and SongProject is mentioned only in the reviewed caller-thread entry
//   points. A count may only shrink.
// Does not own: the compiler (it compiles the immutable copy it is handed) or the playback behaviour itself (PlaybackSnapshotIsolationTests).
// Tests: PlaybackScoreQueryGateTests (this file).
public sealed class PlaybackScoreQueryGateTests
{
    private static readonly Regex LiveMember = new(@"\.Song\b|\.Tracks\b|\.Measures\b|\.Mixer\b|\bTrackModel\b|\bMixerGroups\b|\bLiveProject\b|\bPublishedSongCopy\b|\bContentRevision\b|\bTimelineRevision\b", RegexOptions.Compiled);

    private static readonly Regex SongProjectMention = new(@"\bSongProject\b", RegexOptions.Compiled);

    private static string PlaybackDir() => Path.Combine(ArchitectureScan.RepoRoot(), "Source", "src", "TabForge.Core", "Playback");

    private static IEnumerable<string> PlaybackFiles() => Directory.EnumerateFiles(PlaybackDir(), "PlaybackEngine*.cs")
        .Concat(new[] { "PlaybackScheduleReuse.cs", "PlaybackSeekRebuild.cs", "AudioGrowthRefreshFlow.cs" }.Select(name => Path.Combine(PlaybackDir(), name)))
        .OrderBy(path => path, StringComparer.Ordinal);

    // Live-song and snapshot-copy reads per file, each with the reason it runs on the owner (UI) thread, on the immutable copy, or on a copy handed in. A file not listed has none. Counts only shrink.
    private static readonly Dictionary<string, (int Count, string Reason)> AllowedLiveReads = new()
    {
        ["PlaybackEngine.cs"] = (7, "LiveProject accessor and its owner-thread users (SetPracticeSilence, loop restart); AudibleMask(project) is the static the snapshot calls with its copy; PublishedSongCopy is the self-test view"),
        ["PlaybackEngine.MidiOut.cs"] = (1, "RearmChannelSetup while stopped: owner thread, no scheduler runs"),
        ["PlaybackEngine.Loop.cs"] = (1, "loop restart in UpdateOptions: owner thread, live song read only for the restart"),
        ["PlaybackEngine.ChannelState.cs"] = (6, "PushLiveChannelState: the owner-thread live send of the song's program, volume, pan and sends (the setup events are written under the output lock the scheduler reads them under); LiveProject only for the paused resume refresh"),
        ["PlaybackEngine.Transport.cs"] = (1, "Stop keeps the live song as an identity reference only; it is never dereferenced"),
        ["PlaybackScheduleReuse.cs"] = (11, "Capture reads the snapshot's immutable copy; TryResolve and the route stamps run on the owner thread (seek) against the live song"),
        ["PlaybackSeekRebuild.cs"] = (1, "TakeSnapshot cache key: the live ContentRevision, read on the owner thread"),
    };

    // SongProject mentions per file, each with the reason it is a caller-thread entry point or an identity key. Counts only shrink.
    private static readonly Dictionary<string, (int Count, string Reason)> AllowedSongProject = new()
    {
        ["PlaybackEngine.cs"] = (9, "owner-thread API (Start, Stop, Seek and friends) taking the live song as a parameter or identity key"),
        ["PlaybackEngine.MidiOut.cs"] = (1, "SendSetupFromProject: the stopped setup send, owner thread"),
        ["PlaybackEngine.Loop.cs"] = (2, "UpdateOptions/Rebuild: owner-thread API taking the live song as a parameter"),
        ["PlaybackEngine.ChannelState.cs"] = (3, "PushLiveChannelState and its worker: owner-thread API taking the live song as a parameter"),
        ["PlaybackEngine.ProgramPreview.cs"] = (3, "Begin/EndProgramPreview and their channel push: owner-thread API taking the live song as a parameter"),
        ["PlaybackEngine.Resume.cs"] = (1, "TryRestartInPlace: owner-thread restart entry point"),
        ["PlaybackEngine.Transport.cs"] = (5, "Seek/SetSpeed/restart entry points: owner thread, the live song is an identity key"),
        ["PlaybackScheduleReuse.cs"] = (8, "the live song as identity key, the route stamps and the snapshot copy at Capture: owner thread"),
        ["PlaybackEngine.Scheduler.cs"] = (1, "Publish: the live song is the identity key of the seek-reuse check, never read there"),
        ["PlaybackEngine.StartWorker.cs"] = (6, "BeginStart/Reseek/Change/StartCompiled: owner-thread entry points that hand the live song through as an identity key; the compile reads the snapshot"),
        ["PlaybackSeekRebuild.cs"] = (9, "TakeSnapshot and the cache key (owner thread); CompilerOptionsFor/LoopEntryMs/KeepStartInsideLoop statics that SongSnapshot calls with its copy"),
        ["PlaybackEngine.Arrangement.cs"] = (7, "RefreshArrangement entry points (owner thread) and the pending-refresh records that carry the live song as an identity key"),
        ["AudioGrowthRefreshFlow.cs"] = (1, "the reservation carries the live song as an identity key; the compile reads the reserved snapshot"),
    };

    [Fact]
    public void PlaybackFilesReadTheSongOnlyInReviewedEntries()
    {
        var files = PlaybackFiles().ToList();
        Assert.True(files.Count >= 6, "playback gate found no engine files");
        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            var actual = LiveMember.Matches(ArchitectureScan.Strip(ArchitectureScan.ReadText(file), true)).Count;
            var allowed = AllowedLiveReads.TryGetValue(name, out var entry) ? entry.Count : 0;
            Assert.True(actual == allowed, $"{name} reads the song {actual} times, reviewed allow-list says {allowed} ({entry.Reason}): " +
                (actual > allowed ? "scheduler/pool-thread playback code must read the song through IScoreQuery" : "lower the allow-list count (counts only shrink)"));
        }
    }

    [Fact]
    public void SongProjectMentionsStayInReviewedEntryPoints()
    {
        foreach (var file in PlaybackFiles())
        {
            var name = Path.GetFileName(file);
            var actual = SongProjectMention.Matches(ArchitectureScan.Strip(ArchitectureScan.ReadText(file), true)).Count;
            var allowed = AllowedSongProject.TryGetValue(name, out var entry) ? entry.Count : 0;
            Assert.True(actual == allowed, $"{name} mentions SongProject {actual} times, reviewed allow-list says {allowed} ({entry.Reason}): " +
                (actual > allowed ? "read the song through IScoreQuery instead" : "lower the allow-list count (counts only shrink)"));
        }
    }
}
