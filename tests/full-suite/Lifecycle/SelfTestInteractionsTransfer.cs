using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using TabForge.Audio;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Plugins;
using TabForge.Services;
using TabForge.Shell;
using TabForge.Views;

namespace TabForge;

/// <summary>Interaction scenarios about tabs: I-4 (tab transfer during playback), I-7 (a tab switch keeps per-tab view state).</summary>
public static partial class SelfTest
{
    /// <summary>The document that owns the engine slot of <paramref name="track"/> (the engine client's owner table), or null when the track has no slot.</summary>
    private static object? IxSlotOwner(TrackModel track)
    {
        var client = AudioEngineClient.Instance;
        var slot = client.SlotOf(track);
        if (slot < 0) return null;
        var owners = LtField<System.Collections.IDictionary>(client, "_slotOwners")!;
        return owners.Contains(slot) ? owners[slot] : null;
    }

    private static SongProject IxDemoWithPlugin(string name)
    {
        var song = IxDemoSong();
        song.Tracks[0].SoundSource = SoundSources.Plugins;
        song.Tracks[0].Rig.Plugins.Add(new PluginSlot { Name = name, Path = $@"C:\NoSuch\{name}.vst3", Format = "VST3", Type = PluginSlotType.Instrument });
        song.IsDirty = false;
        return song;
    }

    // ---------- I-4: tab transfer during playback ----------

    private static void IxTabTransferCase(LifetimeContext context)
    {
        var baseline = LifetimeSnapshot(context.Store);
        var (w1Ref, sessionRef, projectRef, bRef) = IxTransferStage(context);
        CollectUntilStable(() => (w1Ref.IsAlive ? 1 : 0) + (sessionRef.IsAlive ? 1 : 0) + (projectRef.IsAlive ? 1 : 0) + (bRef.IsAlive ? 1 : 0));
        Check("interactions: I-4: after both windows are closed the first window, the moved song and the other tab are all collected", !w1Ref.IsAlive && !sessionRef.IsAlive && !projectRef.IsAlive && !bRef.IsAlive,
            $"first window {w1Ref.IsAlive}, moved document {sessionRef.IsAlive}, project {projectRef.IsAlive}, other tab {bRef.IsAlive}");
        var diff = LifetimeDiff(baseline, LifetimeSnapshot(context.Store));
        Check("interactions: I-4: every static, engine-client and registry attachment is back to what it was before the windows existed", diff.Length == 0, diff);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference W1, WeakReference Session, WeakReference Project, WeakReference B) IxTransferStage(LifetimeContext context)
    {
        AudioEngineClient.Instance.ResetSongIdsForTest();   // earlier tests may still hold documents that keep every transport id taken
        var w1 = NewLifetimeWindow();
        var songA = IxDemoWithPlugin("Mover");
        var a = IxOpen(w1, songA, out var output);
        var b = IxOpen(w1, DoSong(2, 8), out _);
        var results = (new WeakReference(w1), new WeakReference(a), new WeakReference(songA), new WeakReference(b));
        MainWindow? w2 = null;
        try
        {
            IxActivate(w1, a);
            LtCall(w1, "ApplySpeed", 2.0);
            var engine = a.Playback.Engine;
            engine.StartDiagnostics();
            IxCursor(w1, 0, 0, 0, 1);
            IxCommand(w1, "Transport.PlayFromStart");
            var trace = new IxPlayheadTrace(a);
            var playbackReady = IxPumpUntil(() => IxPosition(a) >= 2.0, 15000, trace.Sample);
            var ownerBefore = IxSlotOwner(songA.Tracks[0]);
            var transportId = AudioEngineClient.Instance.ExistingOwnerId(a);
            var timeline = engine.Timeline;
            // The resets the start of playback queues run on a pool thread: wait until none is left to come.
            var lastReset = output.Resets;
            var quietSince = System.Diagnostics.Stopwatch.GetTimestamp();
            var resetsSettled = IxPumpUntil(() =>
            {
                if (output.Resets != lastReset) { lastReset = output.Resets; quietSince = System.Diagnostics.Stopwatch.GetTimestamp(); }
                return System.Diagnostics.Stopwatch.GetElapsedTime(quietSince).TotalMilliseconds >= 100;
            }, 5000, trace.Sample);
            Check("interactions: I-4: playback reaches the transfer point and its engine resets settle", playbackReady && resetsSettled && engine.IsPlaying, $"ready {playbackReady}, resets settled {resetsSettled}, playing {engine.IsPlaying}");
            var resets = output.Resets;
            var glitches = IxGlitches.Take(context, a);
            Check("interactions: I-4: the song plays in the first window and owns its engine slot", engine.IsPlaying && ReferenceEquals(ownerBefore, a), $"playing {engine.IsPlaying}, owner {(ownerBefore is null ? "none" : ownerBefore.GetType().Name)}");

            // Tear it off through the tab strip's entry point (the held drag needs the mouse button; its transfer is this one).
            var windowsBefore = Application.Current.Windows.OfType<MainWindow>().ToHashSet();
            LtCall(w1, "DetachDocumentToNewWindow", IxIndexOf(w1, a));
            var targetReady = IxPumpUntil(() =>
            {
                w2 = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault(w => !windowsBefore.Contains(w));
                return w2 is { IsLoaded: true } && PresentationSource.FromVisual(w2) is not null;
            }, 5000, trace.Sample);
            Check("interactions: I-4: the torn-off window becomes ready", targetReady && w2 is not null, w2 is null ? "no target window appeared" : $"loaded {w2.IsLoaded}");
            if (!targetReady || w2 is null) throw new InvalidOperationException("torn-off window did not become ready within 5 s");
            var positionBeforeTransferCheck = IxPosition(a);
            var progressedAfterTransfer = IxWaitForPlaybackProgress(a, positionBeforeTransferCheck, 0.25, 10000, trace.Sample);
            var documents1 = LtField<DocumentManager>(w1, "_documents")!;
            Check("interactions: I-4: the song is in the new window and no longer in the first, and it kept playing without a restart (same timeline, no reset)",
                w2.OpenDocuments.Contains(a) && !w1.OpenDocuments.Contains(a) && progressedAfterTransfer && engine.IsPlaying && ReferenceEquals(engine.Timeline, timeline) && output.Resets == resets,
                $"in new {w2.OpenDocuments.Contains(a)}, in old {w1.OpenDocuments.Contains(a)}, playing {engine.IsPlaying}, same timeline {ReferenceEquals(engine.Timeline, timeline)}, resets {output.Resets - resets}");
            Check("interactions: I-4: the playhead only moved forward across the transfer, with no jump or stall", trace.Backward == 0 && trace.Jumps == 0 && trace.Stalls == 0, trace.ToString());
            var owners = IxSlotOwner(songA.Tracks[0]);
            Check("interactions: I-4: the engine holds the song's chain once, under the same owner as before", ReferenceEquals(owners, ownerBefore) && AudioEngineClient.Instance.SlotOf(songA.Tracks[0]) >= 0, $"owner {(owners is null ? "none" : owners.GetType().Name)}");
            Check("interactions: I-4: the song keeps its own engine transport id across the tear-off (its clips stay on its own position)",
                transportId >= 0 && AudioEngineClient.Instance.ExistingOwnerId(a) == transportId && AudioEngineClient.Instance.PositionSentForTest(transportId) is { Playing: true }, $"id {transportId} -> {AudioEngineClient.Instance.ExistingOwnerId(a)}");
            Check("interactions: I-4: the first window now shows the other tab, which is not playing (the background-playback setting leaves it silent)",
                !ReferenceEquals(documents1.Active, a) && w1.OpenDocuments.All(d => !ReferenceEquals(d, a) && !d.Playback.Engine.IsPlaying), $"active '{documents1.Active.DisplayName}'");
            var documents2 = LtField<DocumentManager>(w2, "_documents")!;
            var tick = LtField<Controllers.PlaybackViewController>(w2, "_playbackView")?.IsTicking == true;
            Check("interactions: I-4: the new window follows the playing song (its playback display runs, the editor shows the playhead)", ReferenceEquals(documents2.Active, a) && tick && IxEditor(w2).PlaybackActive, $"active {ReferenceEquals(documents2.Active, a)}, tick {tick}, editor active {IxEditor(w2).PlaybackActive}");

            // The first window closes.
            foreach (var document in w1.OpenDocuments) document.MarkClean();
            w1.Close();
            SettleLifetimeDispatcher();
            var reachedAfterClose = IxPumpUntil(() => IxPosition(a) >= 5.0 || !engine.IsPlaying, 15000, trace.Sample);
            Check("interactions: I-4: closing the first window leaves the moved song playing on, in order, with one run of the scheduler (no note sent twice, nothing rewound)",
                reachedAfterClose && engine.IsPlaying && IxPosition(a) >= 5.0 && IxReplays(engine) is (0, 0) && trace.Backward == 0 && trace.Jumps == 0, $"{trace}; position {IxPosition(a):0.00}; replays {IxReplays(engine)}");
            Check("interactions: I-4: the engine slot is still the moved song's, and the output was neither disposed nor reset", ReferenceEquals(IxSlotOwner(songA.Tracks[0]), a) && !output.Disposed && output.Resets == resets, $"disposed {output.Disposed}, resets {output.Resets - resets}");
            Check("interactions: I-4: the glitch counters are unchanged across the transfer and the close", IxGlitches.Take(context, a) == glitches, $"{IxGlitches.Take(context, a)} vs {glitches}");
            Check("interactions: I-4: the new window is still playing the moved song and shows it", w2.IsVisible && ReferenceEquals(LtField<DocumentManager>(w2, "_documents")!.Active, a));
        }
        finally
        {
            if (w2 is not null) { foreach (var document in w2.OpenDocuments) document.MarkClean(); IxCommand(w2, "Transport.Stop"); }
            IxRelease(a);
            IxRelease(b);
            SettleLifetimeDispatcher();
            Check("interactions: I-4: releasing the document gives its transport id back", AudioEngineClient.Instance.ExistingOwnerId(a) < 0, $"id {AudioEngineClient.Instance.ExistingOwnerId(a)}");
            try { w2?.Close(); } catch (InvalidOperationException) { }
            try { w1.Close(); } catch (InvalidOperationException) { }
            SettleLifetimeDispatcher();
        }
        return results;
    }

    // ---------- I-7: a tab switch keeps per-tab view state ----------

    private static (int Grid, int Editor, int Bar) IxShown(MainWindow w) => (IxTrackGrid(w).SelectedIndex, IxEditor(w).SelectedTrackIndex, IxEditor(w).SelectedMeasure);

    private static void IxViewStateCase(LifetimeContext context)
    {
        var w = NewLifetimeWindow();
        var songA = DoSong(4, 8); songA.Lyrics = "lyrics of A";   // the lyrics are in the song before it is shown, so the box shows them
        var songB = DoSong(4, 8); songB.Lyrics = "lyrics of B";
        var a = IxOpen(w, songA, out _);
        var b = IxOpen(w, songB, out _);
        var tempoBox = LtField<TextBox>(w, "TempoBox")!;
        var lyricsBox = LtField<TextBox>(w, "LyricsBox")!;
        try
        {
            // Each tab gets its own track and cursor: A track 3 at bar 4, B track 1 at bar 2.
            IxActivate(w, a); IxCursor(w, 2, 3, 0, 1);
            IxActivate(w, b); IxCursor(w, 0, 1, 0, 1);
            IxActivate(w, a);
            var shownA = IxShown(w);
            Check("interactions: I-7: switching back to A restores its cursor bar", shownA.Bar == 3, $"bar {shownA.Bar + 1}");
            Check("interactions: I-7: " + "switching back to A shows A's own track (track 3), not the track B had selected", shownA.Grid == 2 && shownA.Editor == 2, $"grid {shownA.Grid}, editor {shownA.Editor}");
            IxCursor(w, 2, 3, 0, 1);   // track 3 selected in A again (whatever the switch showed), then B: which track does it show?
            IxActivate(w, b);
            var shownB = IxShown(w);
            Check("interactions: I-7: switching to B restores its cursor bar", shownB.Bar == 1, $"bar {shownB.Bar + 1}");
            Check("interactions: I-7: " + "switching to B shows B's own track (track 1)", shownB.Grid == 0 && shownB.Editor == 0, $"grid {shownB.Grid}, editor {shownB.Editor}");
            Check("interactions: I-7: the track list and the editor show the same track after every switch", shownA.Grid == shownA.Editor && shownB.Grid == shownB.Editor);

            // A tempo and lyrics typed in A and not committed (no Enter, no focus change), then the tab changes.
            IxActivate(w, a);
            var (tempoA, tempoB) = (a.Project.Tempo, b.Project.Tempo);
            var (undoA, undoB) = (a.Undo.UndoCount, b.Undo.UndoCount);
            tempoBox.Text = "150"; lyricsBox.Text = "typed in A";
            IxActivate(w, b);
            // The box losing focus a moment later (the editor takes it at the end of a switch) applies what the box holds, to the tab then shown.
            LtCall(w, "ApplyTempo");
            LtCall(w, "LyricsBox_LostFocus", w, new RoutedEventArgs());
            SettleLifetimeDispatcher();
            Check("interactions: I-7: what was typed in A is never applied to B (tempo, lyrics, undo history and unsaved state of B are as they were)",
                b.Project.Tempo == tempoB && b.Project.Lyrics == "lyrics of B" && b.Undo.UndoCount == undoB && !b.IsDirty && tempoBox.Text == tempoB.ToString() && lyricsBox.Text == "lyrics of B",
                $"B tempo {b.Project.Tempo}, lyrics '{b.Project.Lyrics}', undo +{b.Undo.UndoCount - undoB}, dirty {b.IsDirty}, boxes '{tempoBox.Text}' / '{lyricsBox.Text}'");
            var applied = a.Project.Tempo == 150 && a.Project.Lyrics == "typed in A" && a.Undo.UndoCount == undoA + 2;
            var discarded = a.Project.Tempo == tempoA && a.Project.Lyrics == "lyrics of A" && a.Undo.UndoCount == undoA;
            Check("interactions: I-7: what was typed in A is either applied to A (one undo step each) or discarded, never half done", applied || discarded,
                $"A tempo {a.Project.Tempo}, lyrics '{a.Project.Lyrics}', undo +{a.Undo.UndoCount - undoA}");
            Check("interactions: I-7: " + "a tempo and lyrics typed in A and not yet committed are applied to A when the tab changes (not lost)", applied, $"A tempo {a.Project.Tempo} (was {tempoA}), lyrics '{a.Project.Lyrics}', undo +{a.Undo.UndoCount - undoA}");
            IxActivate(w, a);
            Check("interactions: I-7: back in A the boxes show A's own tempo and lyrics", tempoBox.Text == a.Project.Tempo.ToString() && lyricsBox.Text == (a.Project.Lyrics ?? ""), $"boxes '{tempoBox.Text}' / '{lyricsBox.Text}', A {a.Project.Tempo} / '{a.Project.Lyrics}'");
        }
        finally
        {
            foreach (var document in w.OpenDocuments.ToList()) document.MarkClean();
            IxRelease(a); IxRelease(b);
            w.Close();
            SettleLifetimeDispatcher();
        }
    }

    // ---------- I-8: a tab switch while recording ----------

    /// <summary>The engine's "take finished" event for <paramref name="track"/>, delivered to every handler (the engine here is a fake and sends nothing).</summary>
    private static void IxRaiseRecorded(TrackModel track, string file, double startSec, double lengthSec)
    {
        var handlers = LtField<Delegate>(AudioEngineClient.Instance, "Recorded");
        if (handlers is null) throw new InvalidOperationException("nobody listens for recorded takes: the recording was not set up");
        foreach (var handler in handlers.GetInvocationList()) handler.DynamicInvoke(track, file, startSec, lengthSec);
    }

    private static void IxRecordingTabSwitchCase(LifetimeContext context)
    {
        var folder = DoScratchFolder();
        var w = NewLifetimeWindow();
        DocumentSession? a = null, b = null;
        try
        {
            a = IxOpen(w, DoSong(2, 8), out _);
            b = IxOpen(w, DoSong(2, 8), out _);
            var track = a.Project.Tracks[0];
            IxActivate(w, a);
            w.ToggleArm(track);
            SettleLifetimeDispatcher();
            Check("interactions: I-8: the track is armed for recording (audio input)", track.RecordArm && !AudioInputs.IsMidi(track.AudioInput));
            LtCall(w, "ApplySpeed", 1.0);
            IxCommand(w, "Transport.Record");
            Check("interactions: I-8: Record starts the recording and plays the song", w.IsRecording && a.Playback.Engine.IsPlaying, $"recording {w.IsRecording}, playing {a.Playback.Engine.IsPlaying}");
            var recordingProgressed = IxWaitForPlaybackProgress(a, IxPosition(a), 0.25, 5000);
            Check("interactions: I-8: recording playback advances before switching tabs", recordingProgressed && w.IsRecording && a.Playback.Engine.IsPlaying, $"recording {w.IsRecording}, position {IxPosition(a):0.00}");

            // Another tab is shown while the recording runs; then Record is pressed again.
            IxActivate(w, b);
            var (undoA, undoB) = (a.Undo.UndoCount, b.Undo.UndoCount);
            IxCommand(w, "Transport.Record");
            SettleLifetimeDispatcher();
            Check("interactions: I-8: Record again ends the recording", !w.IsRecording);
            Check("interactions: I-8: ending the recording from another tab stops the playback it started (the recorded song's)", !a.Playback.Engine.IsPlaying, $"recorded song playing {a.Playback.Engine.IsPlaying}");

            // The engine reports the finished take of A's track.
            var take = Path.Combine(folder, "take 1.wav");
            File.WriteAllBytes(take, new byte[64]);
            IxRaiseRecorded(track, take, 0.5, 1.5);
            SettleLifetimeDispatcher();
            Check("interactions: I-8: the take lands in the song that was armed: a clip on its track, one undo step in it", track.AudioClips.Count == 1 && track.AudioClips[0].File == take && a.Undo.UndoCount == undoA + 1,
                $"A clips {track.AudioClips.Count}, A undo +{a.Undo.UndoCount - undoA}");
            Check("interactions: I-8: the displayed tab B gets no clip, no undo step and no unsaved mark from A's take", b.Project.Tracks.All(t => t.AudioClips.Count == 0) && b.Undo.UndoCount == undoB && !b.IsDirty, $"B undo +{b.Undo.UndoCount - undoB}, dirty {b.IsDirty}");
            var referenced = a.Project.Tracks.Concat(b.Project.Tracks).SelectMany(t => t.AudioClips).Any(c => c.File == take);
            Check("interactions: I-8: no take file is left without a clip that uses it (no orphan file)", !File.Exists(take) || referenced, $"file exists {File.Exists(take)}, used by a clip {referenced}");
        }
        finally
        {
            foreach (var document in w.OpenDocuments.ToList()) document.MarkClean();
            if (a is not null) { a.Playback.Engine.Stop(); IxRelease(a); }
            if (b is not null) IxRelease(b);
            w.Close();
            SettleLifetimeDispatcher();
            DoCleanFolder(folder);
        }
    }
}
