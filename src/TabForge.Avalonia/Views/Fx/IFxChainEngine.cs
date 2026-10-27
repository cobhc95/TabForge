using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TabForge.Audio;
using TabForge.Audio.Contracts;
using TabForge.Models;
using TabForge.Plugins;

namespace TabForge.Avalonia.Views.Fx;

// Owns: the calls and events the FX chain window uses of the audio engine: the live bypass and gain of a plug-in, the plug-in editors (docked in
//   the window or floating), the programs, the plug-in states, the load reading and the automatic pitch matcher.
// Does not own: the engine itself (AudioEngineClient); a test supplies its own implementation to watch the calls.
// Tests: TabForge.Avalonia.Tests.FxEngineCallTests.
public interface IFxChainEngine
{
    bool IsRunning { get; }
    /// <summary>The engine's audio load as a fraction of one core (1.0: a full core).</summary>
    double CpuLoad { get; }
    /// <summary>The engine slot a track plays through; negative when the engine has no chain for it.</summary>
    int SlotOf(TrackModel track);
    /// <summary>The latest linear peak of the track's raw input (before its chain) and of its output (after the chain, volume and pan); 0 when the engine has none.</summary>
    float InputPeakOf(TrackModel track) => 0;
    float PeakOf(TrackModel track) => 0;
    /// <summary>Says whether <paramref name="consumer"/> wants the engine's tuner (it detects the pitch of the armed input; one tuner serves every consumer, each window counts for itself).</summary>
    void SetTunerWanted(object consumer, bool on) { }
    /// <summary>The engine's latest tuner reading (Hz 0: no pitch) and a counter that changes with each one.</summary>
    (float Hz, float Clarity, int Seq) TunerReading => default;
    void SetPluginEnabled(TrackModel track, PluginSlot slot);
    void SetPluginGain(TrackModel track, PluginSlot slot);
    /// <summary>Opens the plug-in's own window: docked inside the window handle <paramref name="parent"/>, or floating over it. False when the engine has no such plug-in.</summary>
    bool OpenEditor(TrackModel track, PluginSlot slot, IntPtr parent, bool dark, bool docked, bool onTop);
    void CloseEditor(TrackModel track, PluginSlot slot);
    void RequestPrograms(TrackModel track, PluginSlot slot);
    void SetProgram(TrackModel track, PluginSlot slot, int program);
    void SetState(TrackModel track, PluginSlot slot, string state);
    /// <summary>Asks for the plug-in's parameter list (the generic parameter panel); the answer arrives as <see cref="ParametersReceived"/>.</summary>
    void RequestParameters(TrackModel track, PluginSlot slot) { }
    /// <summary>Sets one plug-in parameter to a plain value in the running plug-in.</summary>
    void SetParameter(TrackModel track, PluginSlot slot, uint id, double value) { }
    /// <summary>The plug-in's parameter list arrived: engine slot, plug-in index, parameters.</summary>
    event Action<int, int, IReadOnlyList<PluginParameterInfo>>? ParametersReceived { add { } remove { } }
    /// <summary>Copies the plug-ins' current states into the tracks' slots; waits at most <paramref name="timeoutMs"/>.</summary>
    Task CollectStatesAsync(IEnumerable<TrackModel> tracks, int timeoutMs);
    AutoPitchMatcher? AutoPitch { get; }
    /// <summary>The sound options of the engine mixer (the GM sound rules read them).</summary>
    MixerOptions Mixer { get; }
    /// <summary>What the engine knows of each of the given plug-ins (ids, state, parameters for export); an entry is null when it gave no answer and <see cref="LoadedPluginInfo.Unavailable"/> when the engine has not loaded it.</summary>
    Task<LoadedPluginInfo?[]> GetPluginInfoAsync(TrackModel track, IReadOnlyList<PluginSlot> plugins);
    /// <summary>Sends a plug-in's MIDI processors to the engine (a light message while a knob turns).</summary>
    void SetMidiProcessors(TrackModel track, PluginSlot plugin);
    /// <summary>Starts or stops the engine's MIDI log for a track (the MIDI processing window watches while it is open).</summary>
    void WatchMidiLog(TrackModel track, bool watch);
    /// <summary>The engine's MIDI log lines arrived: engine slot and lines.</summary>
    event Action<int, IReadOnlyList<MidiLogLine>>? MidiLogReceived;
    /// <summary>The engine finished loading a chain.</summary>
    event Action? ChainLoaded;
    /// <summary>The user closed a floating plug-in window.</summary>
    event Action? EditorClosed;
    /// <summary>A docked editor reported its size: engine slot, plug-in index, width and height in pixels.</summary>
    event Action<int, int, int, int>? EditorSized;
    /// <summary>The plug-in's program names arrived: engine slot, plug-in index, current program, names.</summary>
    event Action<int, int, int, IReadOnlyList<string>>? ProgramsReceived;
}
