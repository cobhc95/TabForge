using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TabForge.Audio;
using TabForge.Documents;
using TabForge.Models;

namespace TabForge.Avalonia;

// Owns: the plug-in states a save or a .gp export reads before it writes: every rig of the song (tracks, group buses, master and the monitor chain)
// asked of the audio engine, with the wait the window uses. Without an engine nothing is asked and the song keeps the states it has.
// Does not own: the engine (AudioEngineClient, the playback host's engine), the save sequence (DocumentSaveFlow), what a state is (StateCollection).
// Tests: TabForge.Avalonia.Tests.MainWindowSaveTests.
/// <summary>Reads the plug-in states of a song's rigs for a save. UI thread only (the engine answers on the caller's context).</summary>
public sealed class SongPluginStates
{
    /// <summary>How long a save waits for the engine's state replies; the window waits the same.</summary>
    public const int CollectTimeoutMs = 5000;

    private readonly Func<IEnumerable<TrackModel>, int, Task<StateCollection>>? _readStates;
    private readonly Func<BusChain?> _monitorFx;

    private SongPluginStates(Func<IEnumerable<TrackModel>, int, Task<StateCollection>>? readStates, Func<BusChain?> monitorFx)
    {
        _readStates = readStates;
        _monitorFx = monitorFx;
    }

    /// <summary>States read from the playback host's engine; <paramref name="engine"/> is null when the window runs without one.</summary>
    /// <param name="monitorFx">The app-wide monitor chain (the song's own monitor chain is always included).</param>
    public static SongPluginStates ForEngine(AudioEngineClient? engine, Func<BusChain?> monitorFx) =>
        new(engine is null ? null : (tracks, timeoutMs) => engine.CollectStatesAsync(tracks, timeoutMs), monitorFx);

    /// <summary>States read through <paramref name="readStates"/> (the engine's call), so a test can answer for the engine.</summary>
    internal static SongPluginStates ForReader(Func<IEnumerable<TrackModel>, int, Task<StateCollection>>? readStates, Func<BusChain?> monitorFx) =>
        new(readStates, monitorFx);

    /// <summary>The song's plug-in states as the engine reads them now; an empty collection when there is no engine.</summary>
    public Task<StateCollection> Collect(SongDocument document)
    {
        if (_readStates is null) return Task.FromResult(new StateCollection());
        var tracks = SongRigs.Running(document.Project, _monitorFx()).Select(rig => rig.AsTrack()).ToList();
        return _readStates(tracks, CollectTimeoutMs);
    }
}
