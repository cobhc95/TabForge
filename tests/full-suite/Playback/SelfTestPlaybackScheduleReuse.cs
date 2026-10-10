using System.Diagnostics;
using TabForge.Models;
using TabForge.Playback;

namespace TabForge;

/// <summary>Checks that compatible seeks reuse the active schedule and stale schedules fall back to compilation.</summary>
public static partial class SelfTest
{
    private sealed class ScheduleReuseOutput : IMidiOutput
    {
        private readonly object _gate = new();
        private readonly List<(int Status, int Note, int Velocity)> _messages = new();
        public IReadOnlyList<MidiOutputDeviceInfo> Devices { get; } = Array.Empty<MidiOutputDeviceInfo>();
        public (int Status, int Note, int Velocity)[] Messages { get { lock (_gate) return _messages.ToArray(); } }
        public void Send(int deviceId, int status, int data1, int data2) { lock (_gate) _messages.Add((status, data1, data2)); }
        public void ResetAll() { }
        public void Close() { }
        public void Dispose() { }
    }

    private sealed class BlockingScheduleReuseOutput(int blockedReset, int blockedSendNote = -1, bool blockNoteOff = false) : IMidiOutput
    {
        private readonly object _gate = new();
        private readonly List<(int Status, int Note, int Velocity)> _messages = new();
        private int _resetCount;
        private int _completedResets;
        private int _setupBlockArmed;
        private int _setupSendBlocked;
        public ManualResetEventSlim ResetEntered { get; } = new();
        public ManualResetEventSlim ReleaseReset { get; } = new();
        public ManualResetEventSlim SendEntered { get; } = new();
        public ManualResetEventSlim ReleaseSend { get; } = new();
        public int ResetCount => Volatile.Read(ref _resetCount);
        public int CompletedResets => Volatile.Read(ref _completedResets);
        public bool Disposed { get; private set; }
        public IReadOnlyList<MidiOutputDeviceInfo> Devices { get; } = Array.Empty<MidiOutputDeviceInfo>();
        public IReadOnlyList<(int Status, int Note, int Velocity)> Messages { get { lock (_gate) return _messages.ToArray(); } }
        public void ArmSetupSendBlock() => Volatile.Write(ref _setupBlockArmed, 1);
        public void Send(int deviceId, int status, int data1, int data2)
        {
            var noteKind = status & 0xF0;
            var isNote = blockNoteOff ? noteKind == 0x80 || noteKind == 0x90 && data2 == 0 : noteKind == 0x90 && data2 > 0;
            var blockNote = data1 == blockedSendNote && isNote &&
                Interlocked.CompareExchange(ref _sendBlocked, 1, 0) == 0;
            var blockSetup = (status & 0xF0) == 0xB0 && data1 == 7 && Volatile.Read(ref _setupBlockArmed) != 0 &&
                Interlocked.CompareExchange(ref _setupSendBlocked, 1, 0) == 0;
            if (blockNote || blockSetup)
            {
                SendEntered.Set();
                ReleaseSend.Wait(5000);
            }
            lock (_gate) _messages.Add((status, data1, data2));
        }
        private int _sendBlocked;
        public void ResetAll()
        {
            var current = Interlocked.Increment(ref _resetCount);
            try
            {
                if (current == blockedReset)
                {
                    ResetEntered.Set();
                    ReleaseReset.Wait(5000);
                }
            }
            finally { Interlocked.Increment(ref _completedResets); }
        }
        public void Close() { }
        public void Dispose() { Disposed = true; }
    }

    private static void TestPlaybackScheduleReuse()
    {
        var project = SingleTrack(4, 400);
        Beat(project, 0, 1, 0, 4, 67);
        Beat(project, 0, 2, 0, 4, 72);
        var output = new ScheduleReuseOutput();
        using var engine = new PlaybackEngine(output);
        var timelineChanges = 0;
        engine.TimelineChanged += _ => Interlocked.Increment(ref timelineChanges);
        engine.Start(project, new PlaybackOptions(), _ => { }, () => { }, startPaused: true);
        var timeline = engine.Timeline;
        var thread = engine.SchedulerThreadId;
        engine.Seek(project, 1, 0);
        WaitForScheduleSeek(engine, 1);
        var compatibleReused = ReferenceEquals(timeline, engine.Timeline) && engine.SchedulerThreadId == thread && timelineChanges == 1;
        var pausedSilent = !output.Messages.Any(message => (message.Status & 0xF0) == 0x90 && message.Velocity > 0);
        engine.Seek(project, 2, 0);
        WaitForScheduleSeek(engine, 2);
        var latestWins = engine.IsPaused && engine.Playhead().Bar == 2 && ReferenceEquals(timeline, engine.Timeline);
        engine.Resume();
        var targetHeard = WaitForScheduleNote(output, 72, 3000);
        engine.Pause();
        var targetCount = output.Messages.Count(message => (message.Status & 0xF0) == 0x90 && message.Note == 72 && message.Velocity > 0);
        Check("playback schedule: compatible queued seeks reuse one scheduler and the latest paused target is silent until resumed",
            compatibleReused && latestWins && pausedSilent, $"reuse {compatibleReused}, latest {latestWins}, paused silent {pausedSilent}");
        Check("playback schedule: a compatible seek sends the target note once",
            targetHeard && targetCount == 1, $"heard {targetHeard}, target note-on count {targetCount}");

        CheckLatestSeekDuringBlockedReset();
        CheckStopDuringBlockedSeekReset();
        CheckDisposeDuringBlockedSeekReset();
        CheckSeekReturnsWhileOutputSendBlocks();
        CheckSeekClockWaitsForSetupSend();
        CheckBackwardSeekBeforeFinalReleaseFinishes();
        CheckLiveEditReservedAfterPendingSeek();

        var loopProject = SingleTrack(3, 400);
        Beat(loopProject, 0, 1, 0, 4, 69);
        var loopOutput = new ScheduleReuseOutput();
        using var loopEngine = new PlaybackEngine(loopOutput);
        loopEngine.Start(loopProject, new PlaybackOptions { Loop = true, LoopStartBar = 0, LoopEndBar = 1 }, _ => { }, () => { }, startPaused: true);
        var loopTimeline = loopEngine.Timeline;
        var loopThread = loopEngine.SchedulerThreadId;
        loopEngine.Seek(loopProject, 1, 0);
        WaitForScheduleSeek(loopEngine, 1);
        var loopReused = ReferenceEquals(loopTimeline, loopEngine.Timeline) && loopEngine.SchedulerThreadId == loopThread;
        loopEngine.Resume();
        var loopTargetHeard = WaitForScheduleNote(loopOutput, 69, 2200);
        Thread.Sleep(40);
        loopEngine.Pause();
        var loopTargetCount = loopOutput.Messages.Count(message => (message.Status & 0xF0) == 0x90 && message.Note == 69 && message.Velocity > 0);
        Check("playback schedule: loop seek reuses the compiled traversal and dispatches its target once", loopReused && loopTargetHeard && loopTargetCount == 1,
            $"reuse {loopReused}, heard {loopTargetHeard}, target note-on count {loopTargetCount}");

        var staleProject = SingleTrack(4, 400);
        Beat(staleProject, 0, 2, 0, 4, 76);
        using var staleEngine = new PlaybackEngine(new ScheduleReuseOutput());
        var staleChanges = 0;
        staleEngine.TimelineChanged += _ => Interlocked.Increment(ref staleChanges);
        staleEngine.Start(staleProject, new PlaybackOptions(), _ => { }, () => { }, startPaused: true);
        var oldTimeline = staleEngine.Timeline;
        var oldThread = staleEngine.SchedulerThreadId;
        staleProject.MarkTimelineChanged();
        staleEngine.Seek(staleProject, 2, 0);
        WaitForScheduleSeek(staleEngine, 2);
        var staleFallback = staleChanges == 2 && !ReferenceEquals(oldTimeline, staleEngine.Timeline) && staleEngine.SchedulerThreadId != oldThread;
        Check("playback schedule: a changed project revision recompiles instead of reusing the stale schedule", staleFallback,
            $"timeline changes {staleChanges}, new timeline {!ReferenceEquals(oldTimeline, staleEngine.Timeline)}");

        var routeProject = SingleTrack(3, 400);
        Beat(routeProject, 0, 1, 0, 4, 77);
        using var routeEngine = new PlaybackEngine(new ScheduleReuseOutput());
        var routeChanges = 0;
        routeEngine.TimelineChanged += _ => Interlocked.Increment(ref routeChanges);
        routeEngine.Start(routeProject, new PlaybackOptions(), _ => { }, () => { }, startPaused: true);
        routeProject.Tracks[0].MidiChannel = 2; // direct routing changes do not depend on a revision bump
        routeEngine.Seek(routeProject, 1, 0);
        WaitForScheduleSeek(routeEngine, 1);
        Check("playback schedule: a changed MIDI route invalidates schedule reuse without a revision bump", routeChanges == 2,
            $"timeline changes {routeChanges}");

        var optionProject = SingleTrack(3, 400);
        using var optionEngine = new PlaybackEngine(new ScheduleReuseOutput());
        var optionChanges = 0;
        optionEngine.TimelineChanged += _ => Interlocked.Increment(ref optionChanges);
        optionEngine.Start(optionProject, new PlaybackOptions(), _ => { }, () => { }, startPaused: true);
        optionEngine.SetMetronomeSettings(true, 70, 100, 76, 34, 33, 1);
        optionEngine.Seek(optionProject, 1, 0);
        WaitForScheduleSeek(optionEngine, 1);
        Check("playback schedule: changed compile options invalidate reuse independently of project revision", optionChanges == 2,
            $"timeline changes {optionChanges}");

        var repeatProject = SingleTrack(3, 400);
        repeatProject.Tracks[0].Measures[0].RepeatStart = true;
        repeatProject.Tracks[0].Measures[1].RepeatEnd = true;
        repeatProject.Tracks[0].Measures[1].RepeatCount = 2;
        using var repeatEngine = new PlaybackEngine(new ScheduleReuseOutput());
        var repeatChanges = 0;
        repeatEngine.TimelineChanged += _ => Interlocked.Increment(ref repeatChanges);
        repeatEngine.Start(repeatProject, new PlaybackOptions(), _ => { }, () => { }, startPaused: true);
        var repeatedBars = repeatEngine.Timeline?.Bars.Select(bar => bar.Bar).ToArray() ?? Array.Empty<int>();
        repeatEngine.Seek(repeatProject, 1, 0);
        WaitForScheduleSeek(repeatEngine, 1);
        Check("playback schedule: a repeated source bar uses the correct filtered traversal fallback", repeatChanges == 2 &&
            repeatedBars.SequenceEqual(new[] { 0, 1, 0, 1, 2 }),
            $"timeline changes {repeatChanges}, traversal {string.Join(",", repeatedBars)}");

        var outsideProject = SingleTrack(4, 400);
        using var outsideEngine = new PlaybackEngine(new ScheduleReuseOutput());
        var outsideChanges = 0;
        outsideEngine.TimelineChanged += _ => Interlocked.Increment(ref outsideChanges);
        outsideEngine.Start(outsideProject, new PlaybackOptions { StartBar = 2 }, _ => { }, () => { }, startPaused: true);
        var partialTimeline = outsideEngine.Timeline;
        var partialThread = outsideEngine.SchedulerThreadId;
        outsideEngine.Seek(outsideProject, 0, 0);
        WaitForScheduleSeek(outsideEngine, 0);
        Check("playback schedule: a seek before the available compiled prefix recompiles", outsideChanges == 2 &&
            !ReferenceEquals(partialTimeline, outsideEngine.Timeline) && outsideEngine.SchedulerThreadId != partialThread,
            $"timeline changes {outsideChanges}");
    }

    private static void CheckLatestSeekDuringBlockedReset()
    {
        var project = SingleTrack(4, 400);
        Beat(project, 0, 1, 0, 4, 67);
        Beat(project, 0, 2, 0, 4, 72);
        var output = new BlockingScheduleReuseOutput(2);
        var engine = new PlaybackEngine(output);
        try
        {
            engine.Start(project, new PlaybackOptions(), _ => { }, () => { }, startPaused: true);
            var initialReady = WaitForScheduleCondition(() => output.CompletedResets >= 1, 1200);
            engine.Resume();
            engine.Seek(project, 1, 0);
            var firstResetBlocked = output.ResetEntered.Wait(1200);
            engine.Seek(project, 2, 0);
            output.ReleaseReset.Set();
            var newestResetCompleted = WaitForScheduleCondition(() => output.CompletedResets >= 3, 1200);
            var targetMs = engine.Timeline?.Bars.FirstOrDefault(bar => bar.Bar == 2).StartMs ?? 0;
            var targetReanchored = Math.Abs(engine.Playhead().ElapsedMs - targetMs) < 120;
            var latestHeard = WaitForScheduleNote(() => output.Messages, 72, 1800);
            engine.Pause();
            var messages = output.Messages;
            var oldTargetCount = messages.Count(message => (message.Status & 0xF0) == 0x90 && message.Note == 67 && message.Velocity > 0);
            var latestTargetCount = messages.Count(message => (message.Status & 0xF0) == 0x90 && message.Note == 72 && message.Velocity > 0);
            Check("playback schedule: a newer running seek supersedes a target while output reset is blocked", initialReady && firstResetBlocked &&
                newestResetCompleted && targetReanchored && latestHeard && oldTargetCount == 0 && latestTargetCount == 1,
                $"ready {initialReady}, blocked {firstResetBlocked}, latest reset {newestResetCompleted}, anchored {targetReanchored}, old {oldTargetCount}, latest {latestTargetCount}");
        }
        finally
        {
            output.ReleaseReset.Set();
            engine.Dispose();
        }
    }

    private static void CheckStopDuringBlockedSeekReset()
    {
        var project = SingleTrack(3, 400);
        Beat(project, 0, 1, 0, 4, 68);
        var output = new BlockingScheduleReuseOutput(2);
        var engine = new PlaybackEngine(output);
        try
        {
            engine.Start(project, new PlaybackOptions(), _ => { }, () => { }, startPaused: true);
            WaitForScheduleCondition(() => output.CompletedResets >= 1, 1200);
            engine.Seek(project, 1, 0);
            var resetBlocked = output.ResetEntered.Wait(1200);
            engine.Stop();
            var stoppedBeforeRelease = !engine.IsPlaying;
            output.ReleaseReset.Set();
            var resetFinished = WaitForScheduleCondition(() => output.CompletedResets >= 2, 1200);
            Thread.Sleep(30);
            var staleTargetCount = output.Messages.Count(message => (message.Status & 0xF0) == 0x90 && message.Note == 68 && message.Velocity > 0);
            Check("playback schedule: Stop cancels a pending seek generation while its output reset is blocked", resetBlocked && stoppedBeforeRelease &&
                resetFinished && staleTargetCount == 0, $"blocked {resetBlocked}, stopped before release {stoppedBeforeRelease}, reset finished {resetFinished}, stale notes {staleTargetCount}");
        }
        finally
        {
            output.ReleaseReset.Set();
            engine.Dispose();
        }
    }

    private static void CheckDisposeDuringBlockedSeekReset()
    {
        var project = SingleTrack(3, 400);
        Beat(project, 0, 1, 0, 4, 68);
        var output = new BlockingScheduleReuseOutput(2);
        var engine = new PlaybackEngine(output);
        Task? disposing = null;
        try
        {
            engine.Start(project, new PlaybackOptions(), _ => { }, () => { }, startPaused: true);
            var ready = WaitForScheduleCondition(() => output.CompletedResets >= 1, 1200);
            engine.Seek(project, 1, 0);
            var resetBlocked = output.ResetEntered.Wait(1200);
            disposing = Task.Run(engine.Dispose);
            var stoppedBeforeRelease = WaitForScheduleCondition(() => !engine.IsPlaying, 1200);
            var noStaleNoteBeforeRelease = output.Messages.All(message => (message.Status & 0xF0) != 0x90 || message.Note != 68 || message.Velocity == 0);
            output.ReleaseReset.Set();
            var disposeFinished = disposing.Wait(1800);
            var disposed = output.Disposed;
            var resetFinished = WaitForScheduleCondition(() => output.CompletedResets >= 2, 1200);
            var staleTargetCount = output.Messages.Count(message => (message.Status & 0xF0) == 0x90 && message.Note == 68 && message.Velocity > 0);
            Check("playback schedule: disposal cancels a pending seek while output reset is blocked", ready && resetBlocked &&
                stoppedBeforeRelease && noStaleNoteBeforeRelease && disposeFinished && disposed && resetFinished && staleTargetCount == 0,
                $"ready {ready}, blocked {resetBlocked}, stopped before release {stoppedBeforeRelease}, silent {noStaleNoteBeforeRelease}, dispose finished {disposeFinished}, disposed {disposed}, reset finished {resetFinished}, stale notes {staleTargetCount}");
        }
        finally
        {
            output.ReleaseReset.Set();
            if (disposing is not null) disposing.Wait(1800);
            else engine.Dispose();
        }
    }

    private static void CheckSeekReturnsWhileOutputSendBlocks()
    {
        var project = SingleTrack(4, 400);
        Beat(project, 0, 0, 0, 4, 68);
        Beat(project, 0, 2, 0, 4, 72);
        var output = new BlockingScheduleReuseOutput(-1, 68);
        var engine = new PlaybackEngine(output);
        Task? seek = null;
        try
        {
            engine.Start(project, new PlaybackOptions(), _ => { }, () => { });
            var sendBlocked = output.SendEntered.Wait(1500);
            seek = Task.Run(() => engine.Seek(project, 2, 0));
            var seekReturnedPromptly = seek.Wait(700);
            output.ReleaseSend.Set();
            var seekFinished = seekReturnedPromptly || seek.Wait(1800);
            var targetHeard = WaitForScheduleNote(() => output.Messages, 72, 1800);
            engine.Pause();
            var targetCount = output.Messages.Count(message => (message.Status & 0xF0) == 0x90 && message.Note == 72 && message.Velocity > 0);
            Check("playback schedule: a seek returns promptly while MIDI send is blocked", sendBlocked && seekReturnedPromptly &&
                seekFinished && targetHeard && targetCount == 1,
                $"send blocked {sendBlocked}, seek prompt {seekReturnedPromptly}, finished {seekFinished}, target heard {targetHeard}, target note-ons {targetCount}");
        }
        finally
        {
            output.ReleaseSend.Set();
            output.ReleaseReset.Set();
            if (seek is not null) seek.Wait(1800);
            engine.Dispose();
        }
    }

    private static void CheckSeekClockWaitsForSetupSend()
    {
        var project = SingleTrack(6, 400);
        Beat(project, 0, 2, 0, 4, 72);
        var output = new BlockingScheduleReuseOutput(-1);
        var engine = new PlaybackEngine(output);
        try
        {
            engine.Start(project, new PlaybackOptions(), _ => { }, () => { });
            var ready = WaitForScheduleCondition(() => output.CompletedResets >= 1, 1500);
            output.ArmSetupSendBlock();
            engine.Seek(project, 2, 0);
            var setupBlocked = output.SendEntered.Wait(1500);
            Thread.Sleep(180);
            var targetMs = engine.Timeline?.Bars.FirstOrDefault(bar => bar.Bar == 2).StartMs ?? 0;
            var targetStayedFrozen = Math.Abs(engine.SchedulerClockMs - targetMs) < 35;
            output.ReleaseSend.Set();
            var targetHeard = WaitForScheduleNote(() => output.Messages, 72, 1800);
            var targetClockStartedNearTarget = engine.SchedulerClockMs >= targetMs && engine.SchedulerClockMs - targetMs < 120;
            engine.Pause();
            var targetCount = output.Messages.Count(message => (message.Status & 0xF0) == 0x90 && message.Note == 72 && message.Velocity > 0);
            Check("playback schedule: a seek clock stays at its target until setup sends finish", ready && setupBlocked &&
                targetStayedFrozen && targetHeard && targetClockStartedNearTarget && targetCount == 1,
                $"ready {ready}, setup blocked {setupBlocked}, target frozen {targetStayedFrozen}, heard {targetHeard}, clock after release {targetClockStartedNearTarget}, target note-ons {targetCount}");
        }
        finally
        {
            output.ReleaseSend.Set();
            output.ReleaseReset.Set();
            engine.Dispose();
        }
    }

    private static void CheckBackwardSeekBeforeFinalReleaseFinishes()
    {
        var project = SingleTrack(4, 400);
        Beat(project, 0, 0, 0, 1, 72);
        Beat(project, 0, 3, 0, 1, 64);
        var output = new BlockingScheduleReuseOutput(-1, 64, blockNoteOff: true);
        var engine = new PlaybackEngine(output);
        var finished = 0;
        Task? seek = null;
        try
        {
            engine.Start(project, new PlaybackOptions(), _ => { }, () => Interlocked.Increment(ref finished));
            var finalReleaseBlocked = output.SendEntered.Wait(3500);
            var targetBefore = output.Messages.Count(message => (message.Status & 0xF0) == 0x90 && message.Note == 72 && message.Velocity > 0);
            seek = Task.Run(() => engine.Seek(project, 0, 0));
            var seekReturnedPromptly = seek.Wait(700);
            output.ReleaseSend.Set();
            var seekFinished = seekReturnedPromptly || seek.Wait(1800);
            var targetHeardAgain = WaitForScheduleCondition(() => output.Messages.Count(message =>
                (message.Status & 0xF0) == 0x90 && message.Note == 72 && message.Velocity > 0) > targetBefore, 1800);
            engine.Pause();
            var targetAfter = output.Messages.Count(message => (message.Status & 0xF0) == 0x90 && message.Note == 72 && message.Velocity > 0);
            var finalEvents = string.Join(",", (engine.Timeline?.Events ?? new List<ScoreEvent>()).Where(item => item.Data1 == 64)
                .Select(item => $"{item.TimeMs:0}:{item.Status:X2}:{item.Data2}"));
            Check("playback schedule: a backward seek admitted at the final release prevents the old finish", finalReleaseBlocked &&
                seekReturnedPromptly && seekFinished && targetBefore == 1 && targetHeardAgain && targetAfter - targetBefore == 1 && Volatile.Read(ref finished) == 0,
                $"release blocked {finalReleaseBlocked}, seek prompt {seekReturnedPromptly}, finished {seekFinished}, target before {targetBefore}, replayed {targetHeardAgain}, target after {targetAfter}, finish callbacks {finished}, final events {finalEvents}");
        }
        finally
        {
            output.ReleaseSend.Set();
            output.ReleaseReset.Set();
            if (seek is not null) seek.Wait(1800);
            engine.Dispose();
        }
    }

    private static void CheckLiveEditReservedAfterPendingSeek()
    {
        var project = SingleTrack(4, 400);
        Beat(project, 0, 1, 0, 4, 66);
        var output = new BlockingScheduleReuseOutput(2);
        var engine = new PlaybackEngine(output);
        var revised = new ManualResetEventSlim();
        engine.TimelineRevised += _ => revised.Set();
        try
        {
            engine.Start(project, new PlaybackOptions(), _ => { }, () => { }, startPaused: true);
            var ready = WaitForScheduleCondition(() => output.CompletedResets >= 1, 1200);
            engine.Seek(project, 2, 0);
            var resetBlocked = output.ResetEntered.Wait(1200);
            var editedPitch = project.Tracks[0].PitchOf(0, 22);
            LiveAddNote(project, 0, 3, 22);
            _ = engine.RefreshArrangementLive(project, Enumerable.Range(0, 4).ToArray());
            var refreshReserved = WaitForScheduleCondition(() => engine.HasReservedArrangementRefresh, 1800);
            engine.Resume();
            output.ReleaseReset.Set();
            var editedNoteHeard = WaitForScheduleNote(() => output.Messages, editedPitch, 2200);
            var refreshApplied = revised.Wait(1200);
            var eventCount = output.Messages.Count(message => (message.Status & 0xF0) == 0x90 && message.Note == editedPitch && message.Velocity > 0);
            Check("playback schedule: a live edit reserved after a pending seek uses the target bar boundary and is heard once", ready && resetBlocked &&
                refreshReserved && refreshApplied && editedNoteHeard && eventCount == 1,
                $"ready {ready}, blocked {resetBlocked}, reserved {refreshReserved}, revised {refreshApplied}, heard {editedNoteHeard}, note-ons {eventCount}");
        }
        finally
        {
            output.ReleaseReset.Set();
            engine.Dispose();
            revised.Dispose();
        }
    }

    private static void WaitForScheduleSeek(PlaybackEngine engine, int bar)
    {
        var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < 1200 && engine.Playhead().Bar != bar) Thread.Sleep(2);
    }

    private static bool WaitForScheduleNote(ScheduleReuseOutput output, int note, int timeoutMs) =>
        WaitForScheduleNote(() => output.Messages, note, timeoutMs);

    private static bool WaitForScheduleNote(Func<IReadOnlyList<(int Status, int Note, int Velocity)>> messages, int note, int timeoutMs)
    {
        var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < timeoutMs)
        {
            if (messages().Any(message => (message.Status & 0xF0) == 0x90 && message.Note == note && message.Velocity > 0)) return true;
            Thread.Sleep(2);
        }
        return false;
    }

    private static bool WaitForScheduleCondition(Func<bool> condition, int timeoutMs)
    {
        var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < timeoutMs)
        {
            if (condition()) return true;
            Thread.Sleep(2);
        }
        return condition();
    }
}
