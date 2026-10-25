using System;
using System.Collections.Generic;
using Avalonia.Controls;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Services;

namespace TabForge.Avalonia.Views.Fx;

// Owns: the FX chain window's host for a chain that belongs to no song: the live input chain and the preview's sample chain. An edit changes the
//   chain at once (no undo step) and reports to the owner's callback (the live chain saves itself and the engine syncs).
// Does not own: the window (FxChainWindow), the song-bound host (SongFxChainHost).
// Tests: no dedicated test class.
public sealed class StandaloneFxChainHost : IFxChainHost
{
    private readonly Action<TrackModel>? _changed;
    private readonly Action<Window>? _openSettings;
    private readonly TabForge.Plugins.PluginTrust _trust;
    private readonly IPluginFileTrust _files = new TabForge.Platform.Windows.WindowsPluginFileTrust();

    /// <summary>Opens the host on a chain that belongs to no song.</summary>
    /// <param name="plugins">The plug-in settings the window reads and changes.</param>
    /// <param name="hotkeys">The key bindings.</param>
    /// <param name="engine">The engine client; null: no engine.</param>
    /// <param name="tracks">The tracks the wiring window offers as MIDI sources.</param>
    /// <param name="changed">Called after each edit with the edited track.</param>
    /// <param name="saveSettings">Called when the window changes the settings.</param>
    /// <param name="openSettings">Opens the settings page; null does nothing.</param>
    /// <param name="trust">The app's one plug-in trust state (required; a test passes its own).</param>
    public StandaloneFxChainHost(PluginSettings plugins, HotkeySettings hotkeys, TabForge.Plugins.PluginTrust trust, IFxChainEngine? engine, IReadOnlyList<TrackModel> tracks,
        Action<TrackModel>? changed = null, Action? saveSettings = null, Action<Window>? openSettings = null, Action? saveQuietly = null)
    {
        _trust = trust ?? throw new ArgumentNullException(nameof(trust));
        PluginSettings = plugins;
        Hotkeys = hotkeys;
        Engine = engine;
        Tracks = tracks;
        _changed = changed;
        SettingsSaved = saveSettings;
        _saveQuietly = saveQuietly ?? saveSettings;
        _openSettings = openSettings;
    }

    /// <summary>The tracks the wiring window offers as MIDI sources.</summary>
    public IReadOnlyList<TrackModel> Tracks { get; }
    /// <summary>The plug-in settings the window reads and changes.</summary>
    public PluginSettings PluginSettings { get; }
    /// <summary>The key bindings.</summary>
    public HotkeySettings Hotkeys { get; }
    /// <summary>The engine client; null: no engine.</summary>
    public IFxChainEngine? Engine { get; }
    /// <summary>The plug-in file trust.</summary>
    public IPluginFileTrust PluginFiles => _files;
    /// <summary>The app's plug-in trust state.</summary>
    public TabForge.Plugins.PluginTrust Trust => _trust;
    /// <summary>The app-wide chain title.</summary>
    public string? AppWideChainTitle { get; init; }
    /// <summary>The app-wide chain note.</summary>
    public Control? AppWideChainNote { get; init; }
    /// <summary>The live input strip.</summary>
    public Control? LiveStrip { get; init; }
    /// <summary>Always true: standalone chains use the global monitor.</summary>
    public bool MonitorUseGlobal => true;
    /// <summary>Called by <see cref="SaveSettings"/>.</summary>
    public Action? SettingsSaved { get; }
    private readonly Action? _saveQuietly;

    /// <summary>No undo: standalone edits apply at once.</summary>
    public UndoSnapshot? Checkpoint() => null;

    /// <summary>Applies the change at once and reports it to the owner.</summary>
    public void Edit(TrackModel track, Action change, UndoSnapshot? since = null)
    {
        change();
        _changed?.Invoke(track);
    }

    /// <summary>Reports the track to the owner's callback.</summary>
    public void SyncEngine(TrackModel track) => _changed?.Invoke(track);

    /// <summary>Saves the settings through the owner's callback.</summary>
    public void SaveSettings() => SettingsSaved?.Invoke();

    /// <summary>Saves through the owner's quiet callback when it gave one.</summary>
    public void SaveSettingsQuietly() => _saveQuietly?.Invoke();
    /// <summary>Opens the settings page through the owner's callback.</summary>
    public void OpenAudioSettings(Window owner) => _openSettings?.Invoke(owner);
    /// <summary>No-op: standalone chains always use the global monitor.</summary>
    public void SetMonitorUseGlobal(bool useGlobal) { }
}
