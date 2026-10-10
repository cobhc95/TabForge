using System.IO;
using System.Linq;
using System.Windows;
using TabForge.Audio;
using TabForge.Audio.Contracts;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Services;
using TabForge.Views;
using EH = TabForge.AudioEngine.EngineHost.Headless;
using EP = TabForge.AudioEngine.Plugins;

namespace TabForge;

/// <summary>Interaction scenario I-5: a plug-in changes on a track while the song plays. The window half (model, undo, dirty, save and reopen) runs against the real window; the engine half (chain, metrics, retirement) against the real engine code in process.</summary>
public static partial class SelfTest
{
    private static void IxPluginChangeCase(LifetimeContext context)
    {
        IxPluginChangeInWindow(context);
        IxPluginChangeInEngine();
    }

    /// <summary>One chain edit as the FX chain window makes it: the host captures the undo step, the change is made to the model, the host is told.</summary>
    private static void IxChainEdit(MainWindow window, TrackModel track, Action change, bool switched = false)
    {
        var host = window.MixerHost;
        host.BeginChainEdit();
        change();
        if (switched) host.PluginSwitched(track); else host.ChainChanged(track);
        SettleLifetimeDispatcher();
    }

    private static void IxPluginChangeInWindow(LifetimeContext context)
    {
        var folder = DoScratchFolder();
        var w = NewLifetimeWindow();
        DocumentSession? session = null, reopened = null;
        try
        {
            var song = IxDemoSong();
            session = IxOpen(w, song, out _);
            session.Path = Path.Combine(folder, "I5.tforge"); session.IsNew = false; session.MarkClean();
            var track = song.Tracks[0];
            var client = AudioEngineClient.Instance;
            var engine = session.Playback.Engine;
            engine.StartDiagnostics();
            LtCall(w, "ApplySpeed", 2.0);
            IxActivate(w, session);
            IxCursor(w, 0, 0, 0, 1);
            IxCommand(w, "Transport.PlayFromStart");
            var trace = new IxPlayheadTrace(session) { BackwardTolerance = 0.15 };
            var playbackReady = IxPumpUntil(() => IxPosition(session) >= 1.0, 10000, trace.Sample);
            var glitches = IxGlitches.Take(context, session);
            var slot = new PluginSlot { Name = "Test FX", Path = EP.Vst2Plugin.TestEffect.PathName, Format = "VST2", Type = PluginSlotType.Effect, Enabled = true };
            Check("interactions: I-5: the song reaches the edit point and the track has no plug-in yet", playbackReady && engine.IsPlaying && track.Rig.Plugins.Count == 0, $"ready {playbackReady}, position {IxPosition(session):0.00}, playing {engine.IsPlaying}");

            void Step(string name, Action change, Func<bool> modelIs, bool toggle = false)
            {
                var (undo, revision, timeline, positionBefore) = (session.Undo.UndoCount, session.Project.TimelineRevision, engine.Timeline, IxPosition(session));
                IxChainEdit(w, track, change, toggle);
                var progressed = IxWaitForPlaybackProgress(session, positionBefore, 0.25, 10000, trace.Sample);
                // A bypass switch is live on the engine: the song is not recompiled for it, so the notes play on.
                if (toggle) Check($"interactions: I-5: {name} does not restart playback (the compiled timeline stays, nothing is cut or sent again)", ReferenceEquals(engine.Timeline, timeline), $"playback restarted: {!ReferenceEquals(engine.Timeline, timeline)}");
                else Log.Add($"  info  I-5: {name}: playback restarted {!ReferenceEquals(engine.Timeline, timeline)}");
                // The rule today: a plug-in change is one undo step (taken once per idle period by the host) and dirties the song; it does not touch the score timeline.
                Check($"interactions: I-5: {name}: the model has the change, and it is one undo step and dirty", modelIs() && session.Undo.UndoCount == undo + 1 && session.IsDirty, $"undo +{session.Undo.UndoCount - undo}, dirty {session.IsDirty}");
                Check($"interactions: I-5: {name}: the score timeline is not invalidated and playback goes on", session.Project.TimelineRevision == revision && progressed && engine.IsPlaying, $"revision +{session.Project.TimelineRevision - revision}, progressed {progressed}, position {IxPosition(session):0.00}, playing {engine.IsPlaying}");
            }

            Step("inserting the effect", () => { track.Rig.Plugins.Add(slot); track.SoundSource = SoundSources.Plugins; }, () => track.Rig.Plugins.Count == 1 && track.SoundSource == SoundSources.Plugins);
            Check("interactions: I-5: inserting loads the track's chain on the engine (it has a slot, owned by this song)", client.SlotOf(track) >= 0 && ReferenceEquals(IxSlotOwner(track), session), $"slot {client.SlotOf(track)}");
            Step("bypassing the effect", () => { slot.Enabled = false; client.SetPluginEnabled(track, slot); }, () => !track.Rig.Plugins[0].Enabled, toggle: true);
            Step("switching the effect on again", () => { slot.Enabled = true; client.SetPluginEnabled(track, slot); }, () => track.Rig.Plugins[0].Enabled, toggle: true);

            // Save with the plug-in's state read from the engine, then reopen: the chain and the state come back exactly.
            const string blob = "AAECAwQFBgcICQoLDA0ODw==";
            IxCommand(w, "File.Save");
            var stateRequests = LtField<System.Collections.IDictionary>(client, "_stateRequests")!;
            var held = IxPumpUntil(() => IxController(w).IsSaving && stateRequests.Count > 0, 10000);
            var answered = false;
            if (held)
            {
                foreach (var request in stateRequests.Values.Cast<AudioEngineClient.StateRequest>().ToArray())
                {
                    var stream = new MemoryStream();
                    Frames.Write(stream, (byte)EngineEvent.PluginState, wr => { wr.Write(request.Slot); wr.Write(request.Id); wr.Write(0); wr.Write(1); wr.Write((byte)PluginStateStatus.Captured); wr.WriteString(blob); });
                    stream.Position = 0;
                    client.OnPluginState(Frames.Read(stream)!.Value.Reader);
                    answered = true;
                }
            }
            Check("interactions: I-5: the save waits for the plug-in's state from the engine, and the state lands in the song", held && answered && IxPumpUntil(() => !IxController(w).IsSaving, 10000) && track.Rig.Plugins[0].State == blob && !session.HasUnsavedChanges, $"held {held}, answered {answered}, state '{track.Rig.Plugins[0].State}'");
            var copy = Path.Combine(folder, "I5-reopen.tforge");
            File.Copy(session.Path!, copy);
            var tabs = w.OpenDocuments.Count;
            LtCall(w, "OpenScore", copy, false, false, null, false);
            SettleLifetimeDispatcher();
            reopened = w.OpenDocuments.Count == tabs + 1 ? w.OpenDocuments[^1] : null;
            var back = reopened?.Project.Tracks[0].Rig.Plugins;
            Check("interactions: I-5: the reopened song has the chain back: the same plug-in, switched on, with the state byte for byte",
                back is { Count: 1 } && back[0].Path == slot.Path && back[0].Enabled && Convert.FromBase64String(back[0].State ?? "").SequenceEqual(Convert.FromBase64String(blob)) && reopened!.Project.Tracks[0].SoundSource == SoundSources.Plugins,
                back is null ? "no tab" : $"{back.Count} plug-ins, state '{back.FirstOrDefault()?.State}'");

            // Undo of the last plug-in step: only that step changes; the other chains of the song stay loaded where they are.
            var loadsBefore = client.ChainLoadsSentForTest;
            IxActivate(w, session);
            IxCommand(w, "Edit.Undo");
            var afterUndoPosition = IxPosition(session);
            var progressedAfterUndo = IxWaitForPlaybackProgress(session, afterUndoPosition, 0.25, 10000, trace.Sample);
            Check("interactions: I-5: playback continues after the plug-in undo", progressedAfterUndo && engine.IsPlaying, $"position {afterUndoPosition:0.00} -> {IxPosition(session):0.00}, playing {engine.IsPlaying}");
            Check("interactions: I-5: undoing a plug-in step does not reload the song's engine chains (the engine matches tracks by identity, not by object)", client.ChainLoadsSentForTest == loadsBefore, $"chain loads sent by the undo: {client.ChainLoadsSentForTest - loadsBefore}");
            Check("interactions: I-5: the undo goes back one plug-in step in the model (the effect is switched on or off as before it)", session.Project.Tracks[0].Rig.Plugins.Count == 1, $"{session.Project.Tracks[0].Rig.Plugins.Count} plug-ins");

            Step2Remove();
            void Step2Remove()
            {
                var current = session.Project.Tracks[0];
                var (undo, revision, positionBefore) = (session.Undo.UndoCount, session.Project.TimelineRevision, IxPosition(session));
                IxChainEdit(w, current, () => current.Rig.Plugins.Clear());
                var progressed = IxWaitForPlaybackProgress(session, positionBefore, 0.25, 10000, trace.Sample);
                Check("interactions: I-5: removing the effect is one undo step, dirty, and playback goes on", current.Rig.Plugins.Count == 0 && session.Undo.UndoCount == undo + 1 && session.IsDirty && session.Project.TimelineRevision == revision && progressed && engine.IsPlaying, $"progressed {progressed}, position {IxPosition(session):0.00}, playing {engine.IsPlaying}");
            }

            Check("interactions: I-5: the playhead only moved forward through all plug-in steps (no jump, stall or step back; a restart from the playhead is not one)", trace.Backward == 0 && trace.Jumps == 0 && trace.Stalls == 0, trace.ToString());
            Check("interactions: I-5: no note is sent twice across the plug-in steps (a restart from the playhead resumes after what was already sent)", IxReplays(engine) is (0, 0), $"duplicates and rewinds {IxReplays(engine)}");
            Check("interactions: I-5: the glitch counters are unchanged across the plug-in steps", IxGlitches.Take(context, session) == glitches, $"{IxGlitches.Take(context, session)} vs {glitches}");
        }
        finally
        {
            foreach (var document in w.OpenDocuments.ToList()) document.MarkClean();
            if (session is not null) { session.Playback.Engine.Stop(); IxRelease(session); }
            if (reopened is not null) IxRelease(reopened);
            w.Close();
            SettleLifetimeDispatcher();
            DoCleanFolder(folder);
        }
    }

    /// <summary>The engine half: insert, bypass, switch on and remove the hosted test effect on a playing chain, with the audio callback pumped between the steps.</summary>
    private static void IxPluginChangeInEngine()
    {
        var shared = SharedBlock.Create($"tf-selftest-{Guid.NewGuid():N}");
        EH.Attach(shared, (spec, rate, block) => spec.Path == EP.Vst2Plugin.TestEffect.PathName
            ? EP.Vst2Plugin.TestEffect.Create(rate, block) : throw new InvalidOperationException($"selftest: unexpected plug-in {spec.Path}"));
        using var callback = new CallbackThread();
        try
        {
            EH.Configure(NullConfig(48000, 256, manual: true));
            var droppedBefore = AudioEngine.Mixing.TrackChain.MidiDropped;
            var fx = new PluginSpec(EP.Vst2Plugin.TestEffect.PathName, "VST2", false, true, 100, null, Id: "fx");
            EH.LoadChain(0, "interaction", false, new List<PluginSpec>());
            EH.SetPlaying(true, 0);
            callback.Pump(16);
            EH.Mix?.Metrics.TakeAndReset();
            var total = (Misses: 0L, Late: 0L, Allocated: 0L, Blocks: 0L);

            void Step(string name, Action change, string[] chain)
            {
                change();
                var retired = 0;
                for (var i = 0; i < 50 && (EH.Collect() > 0 || EH.Retiring > 0); i++) { retired++; Thread.Sleep(2); }
                var ids = EH.ChainAt(0)?.Plugins.Select(p => (p as EP.Vst2Plugin)?.Path ?? "").ToArray() ?? Array.Empty<string>();
                callback.Pump(8);   // the first blocks after a swap
                EH.Mix?.Metrics.TakeAndReset();
                var ran = callback.Pump(40);
                var metrics = EH.Mix!.Metrics.TakeAndReset();
                total = (total.Misses + metrics.DeadlineMisses, total.Late + metrics.LateCalls, total.Allocated + metrics.AllocatedBytes, total.Blocks + metrics.Calls);
                Check($"interactions: I-5 (engine): {name}: the engine's chain is the song's chain, the retire queue is drained and the next 40 callbacks run",
                    ids.SequenceEqual(chain) && EH.Retiring == 0 && ran == 40 && metrics.Calls == 40, $"chain [{string.Join(",", ids)}] expected [{string.Join(",", chain)}], retiring {EH.Retiring}, ran {ran}, calls {metrics.Calls}");
                Check($"interactions: I-5 (engine): {name}: the audio thread allocated nothing and missed no deadline", metrics.AllocatedBytes == 0 && metrics.DeadlineMisses == 0, $"allocated {metrics.AllocatedBytes} B, misses {metrics.DeadlineMisses}");
            }

            string[] none = Array.Empty<string>(), one = { EP.Vst2Plugin.TestEffect.PathName };
            Step("inserting the effect", () => EH.LoadChain(0, "interaction", false, new List<PluginSpec> { fx }), one);
            Step("bypassing it", () => { EH.Command(EngineCommand.SetPluginBypass, w => { w.Write(0); w.Write(0); w.Write(false); }); EH.Collect(); }, one);
            Step("switching it on again", () => { EH.Command(EngineCommand.SetPluginBypass, w => { w.Write(0); w.Write(0); w.Write(true); }); EH.Collect(); }, one);
            Step("removing it", () => EH.LoadChain(0, "interaction", false, new List<PluginSpec>()), none);
            Check("interactions: I-5 (engine): across all four steps no MIDI was dropped and no callback ran late", AudioEngine.Mixing.TrackChain.MidiDropped == droppedBefore && total.Late == 0, $"dropped +{AudioEngine.Mixing.TrackChain.MidiDropped - droppedBefore}, late {total.Late}, blocks {total.Blocks}");
        }
        finally
        {
            EH.Detach();
            SettleDisk();
            shared.Dispose();
        }
    }
}
