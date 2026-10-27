using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using TabForge.Audio.Contracts;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Services;

namespace TabForge.Audio;

// Owns: decoding engine events into client events.
// Does not own: the rest of this type (see the main file).
// Tests: no dedicated test class.
public sealed partial class AudioEngineClient
{
    /// <summary>One engine event, on the reader thread.</summary>
    internal void HandleEvent(byte type, BinaryReader r)
    {
        switch ((EngineEvent)type)
        {
            case EngineEvent.CommandRejected: { var command = r.ReadByte(); Services.Trace.Error(Services.Trace.Engine, $"engine: command {command} rejected: {r.ReadBoundedString(256)}"); break; }
            case EngineEvent.Pong: Volatile.Write(ref _lastAlive, Stopwatch.GetTimestamp()); break;
            case EngineEvent.PluginSlow:
            {
                r.ReadInt32(); r.ReadInt32(); var path = r.ReadBoundedString(1024); var kind = (PluginCallKind)r.ReadInt32(); var seconds = r.ReadInt32();
                RaiseOnUi(() => PluginSlow?.Invoke(path, kind, seconds));
                break;
            }
            case EngineEvent.Ready: OnReady(r); break;
            case EngineEvent.ChainLoaded: OnChainLoaded(r); break;
            case EngineEvent.DeviceError: { _awaitingReady = false; var message = r.ReadBoundedString(); RaiseOnUi(() => DeviceError?.Invoke(message)); break; }
            case EngineEvent.PluginFailed:
            {
                r.ReadInt32(); r.ReadInt32(); var path = r.ReadBoundedString(1024); var why = r.ReadBoundedString();
                RaiseOnUi(() => PluginFailed?.Invoke(path, why));
                break;
            }
            case EngineEvent.PluginMisbehaved:
            {
                r.ReadInt32(); var index = r.ReadInt32(); var path = r.ReadBoundedString(1024);
                RaiseOnUi(() => PluginMisbehaved?.Invoke(path, index));
                break;
            }
            case EngineEvent.PluginState: OnPluginState(r); break;
            case EngineEvent.AsioProbed: OnAsioProbed(r); break;
            case EngineEvent.AsioPanelShown: OnAsioPanelShown(r); break;
            case EngineEvent.PluginInfo: OnPluginInfo(r); break;
            case EngineEvent.PitchMeasured: OnPitchMeasured(r); break;
            case EngineEvent.StateChanged: OnPluginEdited(r.ReadInt32()); break;
            case EngineEvent.Recorded:
            {
                var slot = r.ReadInt32(); var path = r.ReadBoundedString(1024); var start = r.ReadDouble(); var length = r.ReadDouble();
                RaiseOnUi(() => { TrackModel? track; lock (_recordingGate) _recordingTracks.TryGetValue(slot, out track); if (track is not null) Recorded?.Invoke(track, path, start, length); });
                break;
            }
            case EngineEvent.InputError: { var message = r.ReadBoundedString(); RaiseOnUi(() => InputError?.Invoke(message)); break; }
            case EngineEvent.MasterAudio: ReadMasterAudio(r); break;
            case EngineEvent.RecordingLoss: { var message = r.ReadBoundedString(); RaiseOnUi(() => RecordingLoss?.Invoke(message)); break; }
            case EngineEvent.EditorClosed: RaiseOnUi(() => EditorClosed?.Invoke()); break;
            case EngineEvent.EditorSize:
            {
                var slot = r.ReadInt32(); var index = r.ReadInt32(); var width = Math.Clamp(r.ReadInt32(), 0, 8192); var height = Math.Clamp(r.ReadInt32(), 0, 8192);
                RaiseOnUi(() => EditorSized?.Invoke(slot, index, width, height));
                break;
            }
            case EngineEvent.Programs: OnPrograms(r); break;
            case EngineEvent.Params: OnParams(r); break;
            case EngineEvent.MidiLog: OnMidiLog(r); break;
            case EngineEvent.RenderProgress:
            {
                var info = new RenderProgressInfo(r.ReadDouble(), r.ReadDouble(), r.ReadDouble());
                var sink = _renderProgress;
                if (sink is not null) RaiseOnUi(() => sink.Report(info));
                break;
            }
            case EngineEvent.RenderDone:
            {
                var result = RenderResult.Read(r);
                var source = _renderTask; _renderTask = null; _renderProgress = null;
                source?.TrySetResult(result);
                break;
            }
            case EngineEvent.RenderFailed:
            {
                var message = r.ReadBoundedString(); var cancelled = r.ReadBoolean(); var plugin = r.ReadBoundedString(1024);
                FailRender(new RenderException(message, cancelled, plugin));
                break;
            }
            case EngineEvent.PluginCrashed: OnPluginCrashed(r); break;
            default:
                Services.Trace.Error(Services.Trace.Engine, $"engine: event {type} is not handled by this build; ignored");
                break;
        }
    }

    /// <summary>The device is open: records the format, the channel names and the recordable inputs, then tells the UI.</summary>
    private void OnReady(BinaryReader r)
    {
        _awaitingReady = false;
        ForgetSingleRateAsio();
        NoteReady();   // a Ready after the first one: the device was reopened, so the audio state is sent again
        Volatile.Write(ref _lastAlive, Stopwatch.GetTimestamp());
        var rate = r.ReadInt32(); var latency = r.ReadInt32(); var description = r.ReadBoundedString(512);
        var buffer = r.ReadInt32();
        var inNames = AsioChannelNames.Read(r);
        var outNames = AsioChannelNames.Read(r);
        AsioChannels = inNames.Length + outNames.Length > 0 && _config is { } named ? (named.Device, inNames, outNames) : null;
        var recordable = new string[r.BaseStream.Position < r.BaseStream.Length ? Math.Clamp(r.ReadInt32(), 0, AudioInputAddress.MaxChannels) : 0];   // the inputs tracks can record
        for (var i = 0; i < recordable.Length; i++) recordable[i] = r.ReadBoundedString(128);
        _engineProtocol = r.BaseStream.Position < r.BaseStream.Length ? r.ReadInt32() : 1;
        var inputDevice = _config is { } cfg ? (cfg.Driver == AudioDrivers.Asio ? cfg.Device : cfg.InputDevice) : "";
        RaiseOnUi(() => AudioInputs.SetDevice(inputDevice, recordable));   // before StatusChanged below: the pickers rebuilt for it see the device's inputs
        DeviceDescription = $"{description} · {rate} Hz · {latency} ms";
        if (_config is { } running) Output = (running.Driver, description, rate, buffer, latency);
        RaiseOnUi(() => { StatusChanged?.Invoke(); DeviceReady?.Invoke(); });
    }

    /// <summary>A chain finished loading: its per-plug-in results, and the plug-ins whose file changed since approval are marked untrusted.</summary>
    private void OnChainLoaded(BinaryReader r)
    {
        var slot = r.ReadInt32(); r.ReadInt32();
        var generation = r.ReadInt32();
        var results = new List<PluginLoadResult>();
        for (int i = 0, n = Math.Clamp(r.ReadInt32(), 0, 512); i < n; i++)
            results.Add(new PluginLoadResult(r.ReadInt32(), (PluginLoadStatus)r.ReadByte(), r.ReadBoundedString(1024)));
        var ack = new ChainAck(slot, generation, results);
        foreach (var changed in results.Where(x => x.Status == PluginLoadStatus.BlockedChanged)) Trust.MarkChanged(changed.Path);   // the file no longer matches its approval: untrusted until approved again
        RaiseOnUi(() => { lock (_chainLock) _lastAcks[ack.Slot] = ack; ChainAcknowledged?.Invoke(ack); ChainLoaded?.Invoke(); });
    }

    /// <summary>The measured pitch of a plug-in sound.</summary>
    private void OnPitchMeasured(BinaryReader r)
    {
        var slot = r.ReadInt32(); var index = r.ReadInt32(); var id = r.ReadInt32(); var status = (PitchMatch.Status)r.ReadByte();
        var transpose = Math.Clamp(r.ReadInt32(), -48, 48); var confidence = r.ReadDouble();
        var n = Math.Clamp(r.ReadInt32(), 0, 8);
        var notes = new int[n]; var offsets = new double[n];
        for (var i = 0; i < n; i++) { notes[i] = r.ReadInt32(); offsets[i] = r.ReadDouble(); }
        var result = new PitchResult(slot, index, id, status, transpose, confidence, notes, offsets);
        RaiseOnUi(() => PitchMeasured?.Invoke(result));
    }

    /// <summary>The program names of a plug-in.</summary>
    private void OnPrograms(BinaryReader r)
    {
        var slot = r.ReadInt32(); var index = r.ReadInt32(); var current = r.ReadInt32(); var count = Math.Clamp(r.ReadInt32(), 0, 512);
        var names = new List<string>(count);
        for (var i = 0; i < count; i++) names.Add(r.ReadBoundedString(256));
        RaiseOnUi(() => ProgramsReceived?.Invoke(slot, index, current, names));
    }

    /// <summary>The parameter list of a plug-in.</summary>
    private void OnParams(BinaryReader r)
    {
        var slot = r.ReadInt32(); var index = r.ReadInt32();
        var list = PluginParameterWire.Read(r);
        RaiseOnUi(() => ParametersReceived?.Invoke(slot, index, list));
    }

    /// <summary>A batch of logged MIDI lines.</summary>
    private void OnMidiLog(BinaryReader r)
    {
        var slot = r.ReadInt32(); var count = Math.Clamp(r.ReadInt32(), 0, 4096);
        var lines = new MidiLogLine[count];
        for (var i = 0; i < count; i++) lines[i] = new MidiLogLine(r.ReadSingle(), r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte());
        RaiseOnUi(() => MidiLogReceived?.Invoke(slot, lines));
    }

    /// <summary>A plug-in crashed the engine: it goes to the quarantine and its track chain is reloaded without it.</summary>
    private void OnPluginCrashed(BinaryReader r)
    {
        var slot = r.ReadInt32(); r.ReadInt32(); var path = r.ReadBoundedString(1024);
        RaiseOnUi(() =>
        {
            var quarantine = Quarantine?.Invoke();
            if (quarantine is not null && !quarantine.Contains(path, StringComparer.OrdinalIgnoreCase)) quarantine.Add(path);
            _sentChains.Remove(slot); // the next Sync reloads this track's chain without it
            CrashLeftEngineRunning = true;
            PluginCrashed?.Invoke(path);
            CrashLeftEngineRunning = false;
        });
    }

}
