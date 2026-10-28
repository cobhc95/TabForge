using System;
using System.Collections.Generic;
using Avalonia.Controls;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Services;

namespace TabForge.Avalonia.Views.Fx;

// Owns: what one FX chain window needs from the app: the song's tracks, the plug-in settings, the engine, the one way a chain edit is made
//   (one undo step, the dirty mark and the engine sync together), and the app-wide choices the window's menus change.
// Does not own: the window (FxChainWindow), the song (SongDocument), the engine's work (AudioEngineClient).
// Tests: TabForge.Avalonia.Tests.FxChainWindowTests.
public interface IFxChainHost
{
    /// <summary>All tracks of the open song (the wiring window offers them as MIDI sources).</summary>
    IReadOnlyList<TrackModel> Tracks { get; }
    PluginSettings PluginSettings { get; }
    /// <summary>The live key bindings (the FX chain keys and the menu gesture text read them).</summary>
    HotkeySettings Hotkeys { get; }
    /// <summary>The audio engine; null in a window with no engine (the plug-in editors and the CPU reading are then off).</summary>
    IFxChainEngine? Engine { get; }
    /// <summary>The file and signature checks plug-in trust uses.</summary>
    IPluginFileTrust PluginFiles { get; }
    /// <summary>The plug-in trust state (one per app); the file checks above are what it asks.</summary>
    TabForge.Plugins.PluginTrust Trust { get; }
    /// <summary>The remembered folders of the file dialogs this window opens; null when the host keeps no settings.</summary>
    TabForge.Services.DialogFolderMemory? DialogFolders => null;

    /// <summary>The undo state to hand back to <see cref="Edit"/> when a drag that changes the model live ends; null when this host has no undo.</summary>
    UndoSnapshot? Checkpoint();
    /// <summary>
    /// Runs <paramref name="change"/> on <paramref name="track"/>'s chain as one undo step (the step starts at <paramref name="since"/> when given), marks the
    /// song changed and brings the engine in line. The change is the only code that touches the model.
    /// </summary>
    void Edit(TrackModel track, Action change, UndoSnapshot? since = null);
    /// <summary>Brings the engine in line with <paramref name="track"/> after a change the song's undo does not hold (an Allow, a quarantine lifted): no undo step, no dirty mark.</summary>
    void SyncEngine(TrackModel track);
    void SaveSettings();
    /// <summary>Saves the settings without telling the settings observers (the window closing: nothing on screen depends on it).</summary>
    void SaveSettingsQuietly();
    /// <summary>Keeps the settings store open until the returned handle is disposed (a close that still collects plug-in states must save them after the last main window closed); null when the host has no store.</summary>
    IDisposable? HoldSettingsOpen() => null;
    /// <summary>Sends one live MIDI message to the instrument of <paramref name="track"/> (the Articulations Test buttons); false when it is not routed to the engine. A host with no engine sends nothing.</summary>
    bool SendInstrumentMidi(TrackModel track, int status, int data1, int data2) => false;
    /// <summary>Raised when the settings changed (Preferences Apply or OK); the window reads its options again. A host without settings never raises it.</summary>
    event Action<AppSettings>? SettingsChanged { add { } remove { } }
    /// <summary>Opens the audio and plug-in settings page over <paramref name="owner"/>.</summary>
    void OpenAudioSettings(Window owner);
    /// <summary>Monitor FX window: the open song uses the app-wide monitor chain (true, default) or its own.</summary>
    bool MonitorUseGlobal { get; }
    void SetMonitorUseGlobal(bool useGlobal);
    /// <summary>The window title of an app-wide chain that is no track and no bus (its window shows no track options); null for every other chain.</summary>
    string? AppWideChainTitle { get; }
    /// <summary>What an app-wide chain's window shows in place of the track options; null: nothing.</summary>
    Control? AppWideChainNote { get; }
    /// <summary>The strip a live input chain shows in the empty space right of the menu row (meters, tuner, knobs); null for every other chain.</summary>
    Control? LiveStrip => null;
}
