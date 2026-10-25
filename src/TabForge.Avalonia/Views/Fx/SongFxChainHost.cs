using System;
using System.Collections.Generic;
using Avalonia.Controls;
using TabForge.Avalonia.Transport;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Services;

namespace TabForge.Avalonia.Views.Fx;

// Owns: the FX chain window's link to one open song: each chain edit is one DocumentEdits step on the song (undo, dirty mark), followed by the
//   engine sync and the track list repaint; an edit of the app-wide monitor chain goes to the settings instead of the song.
// Does not own: the window (FxChainWindow), the window registry (FxChainWindows), the engine sync itself (AvaloniaPlaybackSessions).
// Tests: no dedicated test class.
public sealed class SongFxChainHost : IFxChainHost
{
    private readonly SongDocument _document;
    private readonly AvaloniaPlaybackSessions? _playback;
    private readonly MainWindowSettings? _settings;
    private readonly PluginSettings _fallbackPlugins = new();
    private readonly HotkeySettings _fallbackHotkeys = new();
    private readonly Func<Window?> _owner;
    private readonly Action? _changed;
    private AudioEngineFxAdapter? _engine;
    private readonly TabForge.Plugins.PluginTrust _trust;
    private readonly IPluginFileTrust _files = new TabForge.Platform.Windows.WindowsPluginFileTrust();

    /// <summary>Opens the host on the song the chain windows edit.</summary>
    /// <param name="document">The song whose tracks the windows edit.</param>
    /// <param name="playback">The playback sessions the engine sync goes through; null: no engine.</param>
    /// <param name="settings">The shared settings (plug-in options, key bindings); null uses defaults that are not saved.</param>
    /// <param name="owner">The main window, for the settings page.</param>
    /// <param name="changed">Called after each edit (the track list repaints its FX cells).</param>
    /// <param name="trust">The app's one plug-in trust state (required; a test passes its own).</param>
    public SongFxChainHost(SongDocument document, AvaloniaPlaybackSessions? playback, MainWindowSettings? settings, Func<Window?> owner, Action? changed, TabForge.Plugins.PluginTrust trust)
    {
        _trust = trust ?? throw new ArgumentNullException(nameof(trust));
        _document = document;
        _playback = playback;
        _settings = settings;
        _owner = owner;
        _changed = changed;
    }

    /// <summary>The song whose tracks the windows edit.</summary>
    public SongDocument Document => _document;
    /// <summary>The song's tracks.</summary>
    public IReadOnlyList<TrackModel> Tracks => _document.Project.Tracks;
    /// <summary>The plug-in settings (or unsaved defaults).</summary>
    public PluginSettings PluginSettings => _settings?.Settings.Plugins ?? _fallbackPlugins;
    /// <summary>The key bindings (or defaults).</summary>
    public HotkeySettings Hotkeys => _settings?.Settings.Hotkeys ?? _fallbackHotkeys;
    /// <summary>The engine adapter, or null without playback.</summary>
    public IFxChainEngine? Engine => _playback?.Engine is { } client ? (_engine is not null && ReferenceEquals(_engine.Client, client) ? _engine : _engine = new AudioEngineFxAdapter(client)) : null;
    /// <summary>The plug-in file trust.</summary>
    public IPluginFileTrust PluginFiles => _files;
    /// <summary>The app's plug-in trust state.</summary>
    public TabForge.Plugins.PluginTrust Trust => _trust;
    /// <summary>No app-wide chain title: song chains are per-track.</summary>
    public string? AppWideChainTitle => null;
    /// <summary>No app-wide chain note: song chains are per-track.</summary>
    public Control? AppWideChainNote => null;
    /// <summary>Whether the monitor chain follows the global setting.</summary>
    public bool MonitorUseGlobal => _document.Project.Mixer.MonitorUseGlobal;

    /// <summary>Snapshots the song for undo.</summary>
    public UndoSnapshot? Checkpoint() => _document.Undo.Snapshot(_document.Project);

    /// <summary>Runs one chain edit as an undo step, then syncs the engine and repaints.</summary>
    public void Edit(TrackModel track, Action change, UndoSnapshot? since = null)
    {
        if (MixerBuses.IsMonitor(track) && MonitorUseGlobal)
        {
            // The app-wide monitor chain lives in the settings, not the song: no undo step, no dirty mark.
            change();
            MixerBuses.SyncBack(track);
            SaveSettings();
        }
        else
        {
            DocumentEdits.Run(_document, _ => { change(); if (track.IsBus) MixerBuses.SyncBack(track); return true; }, since, invalidatesTimeline: false);
        }
        _playback?.SyncEngine(_document, PluginSettings);
        _changed?.Invoke();
    }

    /// <summary>Raised when the shared settings changed.</summary>
    public event Action<AppSettings>? SettingsChanged
    {
        add { if (_settings is not null) _settings.SettingsChanged += value; }
        remove { if (_settings is not null) _settings.SettingsChanged -= value; }
    }

    /// <summary>Saves the settings.</summary>
    public void SaveSettings() => _settings?.SaveSettings();

    /// <summary>Saves the settings without the observers' broadcast.</summary>
    public void SaveSettingsQuietly() => _settings?.SaveSettingsQuietly();

    /// <summary>Keeps the settings store open until the handle is disposed.</summary>
    public IDisposable? HoldSettingsOpen() => _settings?.HoldOpen();

    /// <summary>Opens the settings on the Audio/VST page.</summary>
    public void OpenAudioSettings(Window owner) => MainWindowSettings.ShowPreferencesPage(_settings, _owner() ?? owner, SettingsCatalog.AudioVst);

    /// <summary>Syncs the track to the engine and repaints.</summary>
    public void SyncEngine(TrackModel track)
    {
        _playback?.SyncEngine(_document, PluginSettings);
        _changed?.Invoke();
    }

    /// <summary>Switches the monitor chain between global and per-song.</summary>
    public void SetMonitorUseGlobal(bool useGlobal)
    {
        if (MonitorUseGlobal == useGlobal) return;
        DocumentEdits.Run(_document, project => { project.Mixer.MonitorUseGlobal = useGlobal; return true; }, invalidatesTimeline: false);
        _playback?.SyncEngine(_document, PluginSettings);
        _changed?.Invoke();
        FxChainWindows.For(_owner())?.ReopenMonitor();   // the monitor window reopens on the chain now in use
    }
}
