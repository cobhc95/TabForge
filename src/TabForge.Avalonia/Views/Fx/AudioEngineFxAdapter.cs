using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TabForge.Audio;
using TabForge.Audio.Contracts;
using TabForge.Models;
using TabForge.Plugins;

namespace TabForge.Avalonia.Views.Fx;

// Owns: the FX chain window's view of the engine client: each member forwards to the same-named AudioEngineClient member.
// Does not own: any engine behaviour (AudioEngineClient).
// Tests: TabForge.Avalonia.Tests.FxEngineCallTests (through the window, with a recording engine instead).
public sealed class AudioEngineFxAdapter : IFxChainEngine
{
    private readonly AudioEngineClient _client;

    /// <summary>Wraps the engine client the chain window drives through this adapter.</summary>
    public AudioEngineFxAdapter(AudioEngineClient client) => _client = client;

    /// <summary>The wrapped engine client.</summary>
    public AudioEngineClient Client => _client;
    /// <summary>Whether the engine is running.</summary>
    public bool IsRunning => _client.IsRunning;
    /// <summary>The engine's CPU load.</summary>
    public double CpuLoad => _client.CpuLoad;
    /// <summary>The engine's auto-pitch matcher.</summary>
    public AutoPitchMatcher? AutoPitch => _client.AutoPitch;
    /// <summary>The engine's mixer options.</summary>
    public MixerOptions Mixer => _client.Mixer;
    /// <summary>Gets the loaded plug-in info of the track's slots.</summary>
    public Task<LoadedPluginInfo?[]> GetPluginInfoAsync(TrackModel track, IReadOnlyList<PluginSlot> plugins) => _client.GetPluginInfoAsync(track, plugins);
    /// <summary>The engine slot of the track.</summary>
    public int SlotOf(TrackModel track) => _client.SlotOf(track);
    /// <summary>The track's input peak.</summary>
    public float InputPeakOf(TrackModel track) => _client.InputPeakOf(track);
    /// <summary>The track's output peak.</summary>
    public float PeakOf(TrackModel track) => _client.PeakOf(track);
    /// <summary>Says whether the consumer wants the tuner.</summary>
    public void SetTunerWanted(object consumer, bool on) => _client.SetTunerWanted(consumer, on);
    /// <summary>The engine's tuner reading.</summary>
    public (float Hz, float Clarity, int Seq) TunerReading => _client.TunerReading;
    /// <summary>Applies the slot's enabled state to the engine.</summary>
    public void SetPluginEnabled(TrackModel track, PluginSlot slot) => _client.SetPluginEnabled(track, slot);
    /// <summary>Applies the slot's gain to the engine.</summary>
    public void SetPluginGain(TrackModel track, PluginSlot slot) => _client.SetPluginGain(track, slot);
    /// <summary>Opens the plug-in editor window.</summary>
    public bool OpenEditor(TrackModel track, PluginSlot slot, IntPtr parent, bool dark, bool docked, bool onTop) => _client.OpenEditor(track, slot, parent, dark, docked, onTop);
    /// <summary>Closes the plug-in editor window.</summary>
    public void CloseEditor(TrackModel track, PluginSlot slot) => _client.CloseEditor(track, slot);
    /// <summary>Asks the plug-in for its programs.</summary>
    public void RequestPrograms(TrackModel track, PluginSlot slot) => _client.RequestPrograms(track, slot);
    /// <summary>Sets the plug-in's program.</summary>
    public void SetProgram(TrackModel track, PluginSlot slot, int program) => _client.SetProgram(track, slot, program);
    /// <summary>Sets the plug-in's state.</summary>
    public void SetState(TrackModel track, PluginSlot slot, string state) => _client.SetState(track, slot, state);
    /// <summary>Collects the plug-in states of the tracks.</summary>
    public Task CollectStatesAsync(IEnumerable<TrackModel> tracks, int timeoutMs) => _client.CollectStatesAsync(tracks, timeoutMs);
    /// <summary>Sets the track's MIDI processors.</summary>
    public void SetMidiProcessors(TrackModel track, PluginSlot plugin) => _client.SetMidiProcessors(track, plugin);
    /// <summary>Starts or stops the track's MIDI log.</summary>
    public void WatchMidiLog(TrackModel track, bool watch) => _client.WatchMidiLog(track, watch);
    /// <summary>A MIDI log line arrived.</summary>
    public event Action<int, IReadOnlyList<MidiLogLine>>? MidiLogReceived { add => _client.MidiLogReceived += value; remove => _client.MidiLogReceived -= value; }
    /// <summary>The chain finished loading.</summary>
    public event Action? ChainLoaded { add => _client.ChainLoaded += value; remove => _client.ChainLoaded -= value; }
    /// <summary>The plug-in editor closed.</summary>
    public event Action? EditorClosed { add => _client.EditorClosed += value; remove => _client.EditorClosed -= value; }
    /// <summary>The plug-in editor resized.</summary>
    public event Action<int, int, int, int>? EditorSized { add => _client.EditorSized += value; remove => _client.EditorSized -= value; }
    /// <summary>The plug-in's programs arrived.</summary>
    public event Action<int, int, int, IReadOnlyList<string>>? ProgramsReceived { add => _client.ProgramsReceived += value; remove => _client.ProgramsReceived -= value; }
}
