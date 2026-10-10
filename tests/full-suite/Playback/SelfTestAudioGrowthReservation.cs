using TabForge.Playback;
using TabForge.Services;

namespace TabForge;

// Owns: cancellation, latest-growth and loop regressions for audio-growth reservation.
// Does not own: MIDI devices or the audio backend. Tests: TestAudioGrowthReservation.
public static partial class SelfTest
{
    private static void TestAudioGrowthReservation()
    {
        CheckAudioGrowthCanceledDuringUpload(dispose: false);
        CheckAudioGrowthCanceledDuringUpload(dispose: true);
        CheckLatestAudioGrowthWins();
        CheckAudioGrowthPreservesLoop();
        CheckAudioGrowthSnapshotFailure();
    }

    private static void CheckAudioGrowthCanceledDuringUpload(bool dispose)
    {
        using var f = LongAudioMakeFixture(withMarker: false);
        f.Engine.Start(f.Project, new PlaybackOptions(), _ => { }, f.Finished.Set, startPaused: true);
        var ready = f.Output.WaitForResetCount(2, TimeSpan.FromSeconds(3));
        var reservedBeforeUpload = false;
        f.Host.DuringSync = () =>
        {
            reservedBeforeUpload = f.Engine.HasReservedArrangementRefresh;
            if (dispose) f.Engine.Dispose(); else f.Engine.Stop();
        };
        var plan = MediaDrop.PlanAddTrackLane(f.Project, new[] { LongAudioItem("canceled-growth") }, SongQuarterMap.For(f.Project));
        f.Clips.ApplyMediaDrop(f.Document, plan);
        Check($"audio growth: {(dispose ? "disposal" : "Stop")} during upload clears the prior reservation and leaves transport stopped",
            ready && reservedBeforeUpload && !f.Engine.IsPlaying && f.Engine.Timeline is null &&
            !f.Engine.HasReservedArrangementRefresh && f.TimelineStarts == 1 && f.TimelineRevisions == 0,
            $"ready {ready}, reserved before upload {reservedBeforeUpload}, playing {f.Engine.IsPlaying}, revisions {f.TimelineRevisions}");
    }

    private static void CheckLatestAudioGrowthWins()
    {
        using var f = LongAudioMakeFixture(withMarker: false);
        f.Engine.Start(f.Project, new PlaybackOptions { Speed = 2 }, _ => { }, f.Finished.Set, startPaused: true);
        var ready = f.Output.WaitForResetCount(2, TimeSpan.FromSeconds(3));
        var originalEnd = f.Engine.TotalMs;
        SongExtent.EnsureCovers(f.Project, 12);
        var first = f.Engine.RefreshArrangementForAudioGrowth(f.Project,
            Enumerable.Range(0, f.Project.Tracks[0].Measures.Count).ToArray());
        SongExtent.EnsureCovers(f.Project, 20);
        var latestEnd = SongExtent.Measure(f.Project).EndSec * 1000 / 2;
        var second = f.Engine.RefreshArrangementForAudioGrowth(f.Project,
            Enumerable.Range(0, f.Project.Tracks[0].Measures.Count).ToArray());
        var reserved = f.Engine.HasReservedArrangementRefresh;
        var resets = f.Output.ResetCount;
        f.Engine.Resume();
        var latestPublished = SpinWait.SpinUntil(() => Volatile.Read(ref f.TimelineRevisions) > 0 &&
            f.Engine.TotalMs > originalEnd && f.Engine.TotalMs >= latestEnd - 1, TimeSpan.FromSeconds(4));
        Check("audio growth: two paused reservations publish only the latest extent without restarting MIDI",
            ready && first && second && reserved && latestPublished && f.Engine.IsPlaying && !f.Finished.IsSet &&
            f.TimelineStarts == 1 && f.TimelineRevisions == 1 && f.Output.ResetCount == resets,
            $"ready {ready}, accepted {first}/{second}, reserved {reserved}, latest {latestPublished}, total {f.Engine.TotalMs}/{latestEnd}, revisions {f.TimelineRevisions}, resets {resets}/{f.Output.ResetCount}");
    }

    private static void CheckAudioGrowthSnapshotFailure()
    {
        using var f = LongAudioMakeFixture(withMarker: false);
        f.Engine.Start(f.Project, new PlaybackOptions { Speed = 2 }, _ => { }, f.Finished.Set, startPaused: true);
        var ready = f.Output.WaitForResetCount(2, TimeSpan.FromSeconds(3));
        var accepted = f.Engine.RefreshArrangementForAudioGrowth(null!, new[] { 0, 1 });
        var released = !f.Engine.HasReservedArrangementRefresh && f.Engine.IsPaused;
        f.Engine.Resume();
        var finished = f.Finished.Wait(TimeSpan.FromSeconds(3));
        Check("audio growth: failed snapshot releases its boundary reservation and the original song finishes",
            ready && !accepted && released && finished && !f.Engine.IsPlaying && f.TimelineStarts == 1 && f.TimelineRevisions == 0,
            $"ready {ready}, accepted {accepted}, released {released}, finished {finished}, revisions {f.TimelineRevisions}");
    }

    private static void CheckAudioGrowthPreservesLoop()
    {
        using var f = LongAudioMakeFixture(withMarker: false);
        var loops = 0;
        Action<PlaybackEngine, int> onLoop = (engine, _) => { if (ReferenceEquals(engine, f.Engine)) Interlocked.Increment(ref loops); };
        PlaybackEngine.LoopCompleted += onLoop;
        try
        {
        f.Engine.Start(f.Project, new PlaybackOptions { Speed = 2, Loop = true, LoopStartBar = 0, LoopEndBar = 1 },
            _ => { }, f.Finished.Set, startPaused: true);
        var ready = f.Output.WaitForResetCount(2, TimeSpan.FromSeconds(3));
        var oldEnd = f.Engine.TotalMs;
        var resets = f.Output.ResetCount;
        var plan = MediaDrop.PlanAddTrackLane(f.Project, new[] { LongAudioItem("loop-growth") }, SongQuarterMap.For(f.Project));
        f.Clips.ApplyMediaDrop(f.Document, plan);
        var stayedPaused = f.Engine.IsPaused;
        f.Engine.Resume();
        var continuedLoop = SpinWait.SpinUntil(() => Volatile.Read(ref loops) >= 2, TimeSpan.FromSeconds(4));
        Check("audio growth: audio-only track growth preserves the selected loop and paused transport without MIDI reset",
            ready && stayedPaused && continuedLoop && f.Engine.IsPlaying && !f.Finished.IsSet &&
            f.Engine.TotalMs > oldEnd && f.TimelineStarts == 1 && f.Output.ResetCount == resets,
            $"ready {ready}, paused {stayedPaused}, loops {loops}, total {oldEnd}/{f.Engine.TotalMs}, starts {f.TimelineStarts}, resets {resets}/{f.Output.ResetCount}");
        }
        finally { PlaybackEngine.LoopCompleted -= onLoop; }
    }
}
