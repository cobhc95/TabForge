using System.Diagnostics;
using System.IO.Pipes;
using NAudio.Wave;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine.Editors;
using TabForge.Core.Midi;
using TabForge.AudioEngine.Mixing;

namespace TabForge.AudioEngine;

// Owns: the command frames of the engine host: one command is parsed on the reader thread (bounded reads) and its work is posted to the main thread, in groups (lifecycle, chain, plug-in settings, mix, transport).
// Does not own: the run loop, the pipe and the watchdog (EngineHost.cs) or the handlers the commands post (EngineSession).
// Tests: TestEngineMessagesRoundTrip, TestEngineOwnerTransports.
public static partial class EngineHost
{
    /// <summary>
    /// Reader thread: parses one command (bounded reads) and posts its <see cref="EngineSession"/> handler to the main thread. Only the
    /// transport (SetPosition / SetTransport), chain-load assembly, RenderCancel and Panic act here, on state built for this thread.
    /// The posted lambdas read <see cref="_session"/> when they run, as before.
    /// </summary>
    private static void HandleCommand(byte type, BinaryReader r)
    {
        // Parse on this thread (bounded reads), act on the main thread.
        if (HandleLifecycleCommand(type, r) || HandleChainCommand(type, r) || HandlePluginSettingCommand(type, r) || HandleMixCommand(type, r) || HandleTransportCommand(type, r)) return;
        // Version skew or a stray frame: said so (log + NACK), not silently ignored.
        Log.Write($"unknown command {type} ignored");
        Send(EngineEvent.CommandRejected, w => { w.Write(type); w.WriteString("unknown command"); });
    }

    /// <summary>Session, diagnostics and render requests. Returns false for a command this group does not know.</summary>
    private static bool HandleLifecycleCommand(byte type, BinaryReader r)
    {
        switch ((EngineCommand)type)
        {
            case EngineCommand.Ping: { var seq = PingMessage.Read(r).Seq; Threads.Post(() => Send(EngineEvent.Pong, w => w.Write(seq))); break; }   // answered by the main thread: a deaf main thread misses it
#if TABFORGE_TEST_HOOKS
            case EngineCommand.TestHang:
            {
                var seconds = Math.Clamp(TestHangMessage.Read(r).Seconds, 1, 120);
                if (!TestHooks) { Log.Write("test hang ignored (test hooks are off)"); break; }
                Threads.Post(() => { Log.Write($"test hook: main thread sleeps {seconds} s"); Thread.Sleep(seconds * 1000); });
                break;
            }
#endif
            case EngineCommand.Configure: { var c = r.ReadConfig(); Threads.Post(() => _session.Configure(c)); break; }
            case EngineCommand.Shutdown: _exit = true; break;
            case EngineCommand.RenderCancel: _session.RenderCancel = true; break;   // reader thread: works while the engine renders
            case EngineCommand.RenderOffline:
            {
                RenderSpec spec;
                try { spec = RenderSpec.Read(r); }
                catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException)
                {
                    Send(EngineEvent.RenderFailed, w => { w.WriteString("The render request was not valid: " + ex.Message); w.Write(false); w.WriteString(""); });
                    break;
                }
                Threads.Post(() => _session.BeginRender(spec));
                break;
            }
            case EngineCommand.Panic: { var s = _session; if (!s.RenderActive) Volatile.Read(ref s.Mix)?.Panic(); break; }
            case EngineCommand.PanicSlots: ReadPanicSlots(r); break;
            case EngineCommand.SetTunerWindow: { var ms = Math.Clamp(SetTunerWindowMessage.Read(r).Ms, 1, Audio.PitchDetector.MaxWindowMs); Threads.Post(() => _session.TunerWindowMs = ms); break; }
            case EngineCommand.ProbeAsio: { var m = ProbeAsioMessage.Read(r); Threads.Post(() => _session.ProbeAsio(m.RequestId, m.Driver)); break; }
            case EngineCommand.ShowAsioPanel: { var m = ProbeAsioMessage.Read(r); Threads.Post(() => _session.ShowAsioPanel(m.RequestId, m.Driver)); break; }
            case EngineCommand.SetWindowsPathOffset: { var db = Math.Clamp(SetWindowsPathOffsetMessage.Read(r).Db, -60f, 12f); Threads.Post(() => _session.SetWindowsPathOffset(db)); break; }
            case EngineCommand.SetLiveLimiter: { var on = SetLiveLimiterMessage.Read(r).On; Threads.Post(() => _session.SetLiveLimiter(on)); break; }
            case EngineCommand.SetSoundBank: { var path = SetSoundBankMessage.Read(r).Path; Threads.Post(() => _session.SetSoundBank(path)); break; }
            case EngineCommand.SetMasterTap: { var on = SetMasterTapMessage.Read(r).On; Threads.Post(() => { _session.Tap.Sink ??= SendMasterAudio; _session.Tap.Enable(on, _session.SampleRate); }); break; }
            default: return false;
        }
        return true;
    }

    /// <summary>Chain loading, editors and the engine process of a chain. Returns false for a command this group does not know.</summary>
    private static bool HandleChainCommand(byte type, BinaryReader r)
    {
        switch ((EngineCommand)type)
        {
            case EngineCommand.LoadChain:
            {
                _session.ChainLoads.OnLoadChain(r);   // the states follow one frame per plug-in; built on ChainCommit
                break;
            }
            case EngineCommand.ChainState: _session.ChainLoads.OnChainState(r); break;
            case EngineCommand.ChainCommit:
            {
                if (_session.ChainLoads.OnCommit(r) is not { } ready) break;
                Threads.Post(() => _session.CommitChain(ready));
                break;
            }
            case EngineCommand.RemoveTrack: { var slot = RemoveTrackMessage.Read(r).Slot; Threads.Post(() => _session.RemoveChain(slot)); break; }
            case EngineCommand.OpenEditor:
            {
                var m = OpenEditorMessage.Read(r); var window = new IntPtr(m.Window);
                Threads.Post(() => _session.OpenEditor(m.Slot, m.Index, window, m.Dark, m.Docked, m.OnTop));
                break;
            }
            case EngineCommand.CloseEditor:
            {
                var (slot, index) = CloseEditorMessage.Read(r);
                Threads.Post(() => _session.CloseEditor(slot, index));
                break;
            }
            case EngineCommand.SetMidiProcessors: ReadSetMidiProcessors(r); break;
            case EngineCommand.SetAutoPitch: ReadSetAutoPitch(r); break;
            case EngineCommand.MeasurePitch: ReadMeasurePitch(r); break;
            default: return false;
        }
        return true;
    }

    /// <summary>The programs, state, gain, bypass and wiring of the plug-ins of a chain. Returns false for a command this group does not know.</summary>
    private static bool HandlePluginSettingCommand(byte type, BinaryReader r)
    {
        switch ((EngineCommand)type)
        {
            case EngineCommand.GetPrograms:
            {
                var (slot, index) = GetProgramsMessage.Read(r);
                Threads.Post(() => _session.SendPrograms(slot, index));
                break;
            }
            case EngineCommand.GetParams:
            {
                var (slot, index) = GetParamsMessage.Read(r);
                Threads.Post(() => _session.SendParams(slot, index));
                break;
            }
            case EngineCommand.SetParam:
            {
                var (slot, index, id, value) = SetParamMessage.Read(r);
                if (!double.IsFinite(value)) break;
                Threads.Post(() => _session.SetParam(slot, index, id, value));
                break;
            }
            case EngineCommand.GetPluginInfo:
            {
                var (slot, requestId) = GetPluginInfoMessage.Read(r);
                Threads.Post(() => _session.SendPluginInfo(slot, requestId));
                break;
            }
            case EngineCommand.SetProgram:
            {
                var (slot, index, program) = SetProgramMessage.Read(r);
                Threads.Post(() => _session.SetProgram(slot, index, program));
                break;
            }
            case EngineCommand.SetState:
            {
                var (slot, index, state) = SetStateMessage.Read(r);
                Threads.Post(() => _session.SetState(slot, index, state));
                break;
            }
            case EngineCommand.GetStates: { var (slot, requestId) = GetStatesMessage.Read(r); Threads.Post(() => _session.SendStates(slot, requestId)); break; }
            case EngineCommand.SetPluginGain:
            {
                var (slot, index, db) = SetPluginGainMessage.Read(r);
                var gain = db <= -59.9 ? 0f : (float)Math.Pow(10, Math.Clamp(db, -60, 12) / 20);
                Threads.Post(() => _session.SetPluginGain(slot, index, gain));
                break;
            }
            case EngineCommand.SetPluginBypass:
            {
                var (slot, index, enabled) = SetPluginBypassMessage.Read(r);
                Threads.Post(() => _session.SetPluginBypass(slot, index, enabled));
                break;
            }
            case EngineCommand.SetPluginWiring:
            {
                var (slot, index, flags) = SetPluginWiringMessage.Read(r);
                if (slot < 0 || slot >= MixEngine.TotalSlots) break;
                Threads.Post(() => _session.SetPluginWiring(slot, index, flags));
                break;
            }
            default: return false;
        }
        return true;
    }

    /// <summary>Mix levels, routing, the graph, clips and record arming. Returns false for a command this group does not know.</summary>
    private static bool HandleMixCommand(byte type, BinaryReader r)
    {
        switch ((EngineCommand)type)
        {
            case EngineCommand.SetTrackMix:
            {
                var (slot, volume, pan) = SetTrackMixMessage.Read(r);
                if (slot < 0 || slot >= MixEngine.TotalSlots) throw new InvalidDataException($"SetTrackMix: slot {slot} out of range");
                Threads.Post(() => _session.SetTrackMix(slot, volume, pan));
                break;
            }
            case EngineCommand.SetInputGain:
            {
                var (slot, db) = SetInputGainMessage.Read(r);
                if (slot < 0 || slot >= MixEngine.TotalSlots) throw new InvalidDataException($"SetInputGain: slot {slot} out of range");
                var gain = float.IsFinite(db) ? MathF.Pow(10, Math.Clamp(db, -60f, 24f) / 20) : 1f;
                Threads.Post(() => _session.SetInputGain(slot, gain));
                break;
            }
            case EngineCommand.SetSynth:
            {
                var (slot, on) = SetSynthMessage.Read(r);
                if (slot < 0 || slot >= MixEngine.TotalSlots) break;
                Threads.Post(() => _session.SetSynth(slot, on));
                break;
            }
            case EngineCommand.SetMidiRoute:
            {
                var (slot, index, source, rawMask) = SetMidiRouteMessage.Read(r); var mask = rawMask & 0xFFFF;
                if (source < -2 || source >= MixEngine.MaxSlots || slot < 0 || slot >= MixEngine.MaxSlots) break;
                Threads.Post(() => _session.SetMidiRoute(slot, index, source, mask));
                break;
            }
            case EngineCommand.SetGraph: ReadSetGraph(r); break;
            case EngineCommand.SetMidiLogWatch:
            {
                var (slot, watch) = SetMidiLogWatchMessage.Read(r);
                if (slot < 0 || slot >= MixEngine.MaxSlots) break;
                Threads.Post(() => _session.SetMidiLogWatch(slot, watch));
                break;
            }
            case EngineCommand.SetClips: ReadSetClips(r); break;
            case EngineCommand.SetArm:
            {
                var (slot, armed, rawMode, monitor) = SetArmMessage.Read(r); var mode = Math.Clamp(rawMode, 0, AudioInputAddress.MaxMode);
                if (slot < 0 || slot >= MixEngine.MaxSlots) throw new InvalidDataException($"SetArm: track slot {slot} out of range");
                Threads.Post(() => _session.SetArm(slot, armed, mode, monitor));
                break;
            }
            default: return false;
        }
        return true;
    }

    /// <summary>Position, transport and recording. Returns false for a command this group does not know.</summary>
    private static bool HandleTransportCommand(byte type, BinaryReader r)
    {
        switch ((EngineCommand)type)
        {
            case EngineCommand.SetPosition: ReadSetPosition(r); break;
            case EngineCommand.Record: ReadRecord(r); break;
            case EngineCommand.SetTransport: ReadSetTransport(r); break;
            default: return false;
        }
        return true;
    }
}
