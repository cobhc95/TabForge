using System.Windows.Threading;
using TabForge.Audio;
using TabForge.Audio.Contracts;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;
using TabForge.Shell;

namespace TabForge.Controllers;

/// <summary>What the engine sync needs from its window.</summary>
internal interface IEngineSyncHost
{
    DocumentSession ActiveDocument { get; }
    /// <summary>The songs open in this window.</summary>
    IReadOnlyList<DocumentSession> Documents { get; }
    AppSettings Settings { get; }
    /// <summary>False while the window is still being built (the first syncs run before the MIDI side exists).</summary>
    bool IsInitialized { get; }
    /// <summary>Shows the one "plug-in loading slowly" question the application shows (false when it is already open); <paramref name="answered"/> gets true when the plug-in is to be disabled for the song.</summary>
    bool ShowSlowLoadPrompt(string pluginName, Action<bool> answered);
    void CloseSlowLoadPrompt();
    /// <summary>Queues work for the window's thread; dropped when the window has closed by then.</summary>
    void Post(Action work, DispatcherPriority priority);
    void RebuildMidi();
    void RearmChannelSetup();
    void SetStatus(string text);
    void CheckpointUndo();
    void SaveSettings();
    void UpdateTitle();
    void RefreshTracks();
    void RefreshArrangement();
    void RefreshArrangementAll();
    void RefreshMixer();
    void UpdateAudioDeviceStatus();
    void UpdatePluginTrustBar();
    void UpdateMediaApprovalBar();
}

// Owns: one window's side of the shared audio engine client: syncing the displayed song, answering engine events, coalescing
//     sync requests.
// Does not own: the engine process, the engine client implementation and the song model.
// Tests: TestDocumentOperations, TestWindowLifetime.
/// <summary>
/// One window's side of the shared audio engine client: keeps the engine in step with the displayed song (<see cref="Sync"/>), answers the
/// engine's events (crash and restart, failed or slow plug-ins, edits made in a plug-in's own window) and coalesces the sync and the follow-up
/// refresh that mixer slider drags ask for. The client outlives every window: the handlers attached to it are recorded with their detach and
/// <see cref="Dispose"/> (once, when the window has really closed) removes exactly those; work queued before the close does not run after it.
/// </summary>
internal sealed class EngineSyncController : IDisposable
{
    private readonly IEngineSyncHost _host;
    private readonly AudioEngineClient _engine;
    private readonly OwnedSubscriptions _attached = new();
    private bool _hooked, _disposed;
    private bool _syncPending, _followUpPending, _dragRefreshHeld, _undoCaptured;

    public EngineSyncController(IEngineSyncHost host, AudioEngineClient engine)
    {
        _host = host;
        _engine = engine;
    }

    /// <summary>Engine handlers still attached (self-test: zero once the window has closed).</summary>
    public int AttachmentCount => _attached.Count;

    /// <summary>
    /// Starts / updates / stops the audio engine for the active song's plug-in tracks and routes their MIDI to it.
    /// Songs without plug-in tracks never start the engine.
    /// </summary>
    public void Sync()
    {
        using var slowTrace = TabForge.Views.SlowTrace.Measure("engine sync", 0);
        if (_disposed) return;
        Hook();
        var settings = _host.Settings;
        var doc = _host.ActiveDocument;
        _engine.Mixer.AutoGmSound = settings.Plugins.AutoGmSound;
        var playAll = settings.Plugins.PlayAllThroughEngine;
        if (_engine.Mixer.PlayAllThroughEngine != playAll)
        {
            _engine.Mixer.PlayAllThroughEngine = playAll;
            if (_host.IsInitialized) _host.RebuildMidi();
        }
        AudioRouting.Apply(doc.Project, doc.Playback.Routing, _engine, settings.Plugins, settings.Audio.MasterVolume, owner: doc, media: doc.Media, skippedPlugins: doc.SkippedPlugins);   // the active document owns the engine; its media context and skipped plug-ins go with it
        _host.UpdateAudioDeviceStatus();
        _host.UpdatePluginTrustBar();
        _host.UpdateMediaApprovalBar();
    }

    /// <summary>Mute/solo: the engine's track levels change at once, ahead of the full <see cref="Sync"/>.</summary>
    public void SyncLevelsNow()
    {
        if (_disposed || !OwnsEngine()) return;
        AudioRouting.ApplyLevelsNow(_host.ActiveDocument.Project, _engine, _host.Settings.Audio.MasterVolume);
    }

    /// <summary>At most one engine sync per frame while a mixer slider is dragged (only changed values are sent).</summary>
    public void ScheduleSync()
    {
        if (_syncPending) return;
        _syncPending = true;
        _host.Post(() => { _syncPending = false; Sync(); }, DispatcherPriority.Render);
    }

    /// <summary>
    /// Rebuilding the track list and timeline on every slider step would let the UI thread fall behind the pointer and make the value jump.
    /// The sound is updated at once; the list refresh runs once, when input is idle.
    /// </summary>
    public void ScheduleMixerFollowUp()
    {
        if (_followUpPending) return;
        _followUpPending = true;
        _host.Post(() =>
        {
            _followUpPending = false;
            _host.RefreshTracks();
            _host.RefreshArrangement();
        }, DispatcherPriority.ContextIdle);
    }

    /// <summary>A mixer slider moved mid-drag: the engine sync is coalesced and the full refresh waits for the drag's end.</summary>
    public void MixerSliderDragged()
    {
        ScheduleSync();
        _dragRefreshHeld = true;
    }

    /// <summary>The mixer slider drag ended: the held refresh runs now.</summary>
    public void MixerSliderDragEnded()
    {
        if (!_dragRefreshHeld) return;
        _dragRefreshHeld = false;
        ScheduleMixerFollowUp();
    }

    /// <summary>One undo step per mixer gesture: the first change after a pause captures, later ones in the same drag do not.</summary>
    public void BeginMixerEdit()
    {
        if (_undoCaptured) return;
        _host.CheckpointUndo();
        _undoCaptured = true;
        _host.Post(() => _undoCaptured = false, DispatcherPriority.ApplicationIdle);
    }

    /// <summary>True when this window's displayed song made the engine's latest sync (or nobody has yet): only that window answers engine events with a sync.</summary>
    private bool OwnsEngine() => _engine.CurrentOwner is null || ReferenceEquals(_engine.CurrentOwner, _host.ActiveDocument);

    private void On<THandler>(Action<THandler> attach, Action<THandler> detach, THandler handler) => _attached.Attach(() => attach(handler), () => detach(handler));

    private void Hook()
    {
        if (_hooked) return;
        _hooked = true;
        var engine = _engine;
        // The shared engine client outlives every window. The delegates it keeps read the app-wide shared settings store or the open-window
        // registry, never this window; the event handlers below are detached when this window has really closed.
        engine.Quarantine = static () => AppSettingsStore.Shared.Settings.Plugins.Quarantined;
        // Multi-tab playback: a song that still plays in another tab keeps its engine tracks live (not parked) when this tab takes over.
        engine.IsOwnerPlaying = static owner => owner is DocumentSession { Playback.Engine.IsPlaying: true };
        // A synth created while the song plays starts on the default piano: give it its programs again.
        On(h => engine.ChainLoaded += h, h => engine.ChainLoaded -= h, (Action)(() => { if (!_disposed) _host.RearmChannelSetup(); }));
        On(h => engine.ChainAcknowledged += h, h => engine.ChainAcknowledged -= h, (Action<ChainAck>)(ack =>
        {
            if (_disposed || !OwnsEngine()) return;   // the engine's owner re-syncs; another window syncing here would take the engine over
            // A plug-in whose file changed since its approval was refused at load: show it in the trust bar and re-send it as Skip.
            if (ack.Plugins.Any(x => x.Status == PluginLoadStatus.BlockedChanged)) { _host.UpdatePluginTrustBar(); Sync(); }
            // An instrument that failed to load (or loads fine again) changes whether GM takes over: re-apply the automatic GM sound.
            else if (engine.RefreshAvailability(_host.ActiveDocument.Project.Tracks, _host.Settings.Plugins, _host.ActiveDocument.SkippedPlugins)) { Sync(); _host.RebuildMidi(); _host.RefreshTracks(); _host.RefreshMixer(); }
        }));
        engine.AutoPitch ??= new AutoPitchMatcher(engine, static () => AppSettingsStore.Shared.Settings.Plugins);   // automatic pitch matching of VST instruments
        On(h => engine.PluginCrashed += h, h => engine.PluginCrashed -= h, (Action<string>)OnEngineRestarted);
        On(h => engine.PluginFailed += h, h => engine.PluginFailed -= h, (Action<string, string>)((path, why) =>
        {
            if (!_disposed) _host.SetStatus($"{System.IO.Path.GetFileNameWithoutExtension(path)} could not be loaded: {why}");
        }));
        // Invalid audio (NaN / infinity) never reaches the mix; the plug-in is skipped until it is switched off and on again.
        On(h => engine.PluginMisbehaved += h, h => engine.PluginMisbehaved -= h, (Action<string, int>)((path, index) =>
        {
            if (!_disposed) _host.SetStatus(index >= 0
                ? $"{System.IO.Path.GetFileNameWithoutExtension(path)} produced invalid audio and was bypassed; switch it off and on in its FX chain to try again"
                : $"A track's sound ({path}) produced invalid audio; it was muted for that moment");
        }));
        On(h => engine.DeviceError += h, h => engine.DeviceError -= h, (Action<string>)(message => { if (!_disposed) _host.SetStatus($"Audio output: {message}"); }));
        // Slow is not a crash: a big instrument may take a while; it is switched off only if it passes its limit (load 90 s).
        On(h => engine.PluginSlow += h, h => engine.PluginSlow -= h, (Action<string, PluginCallKind, int>)((path, kind, seconds) =>
        {
            if (_disposed) return;
            _host.SetStatus($"{System.IO.Path.GetFileNameWithoutExtension(path)} " + kind switch
            {
                PluginCallKind.Load => "is still loading",
                PluginCallKind.SetState => "is still applying its settings",
                PluginCallKind.GetState => "is still saving its settings",
                PluginCallKind.Editor => "is still opening its window",
                _ => "is still busy",
            } + $" ({seconds} s)…");
            if (kind == PluginCallKind.Load && seconds >= EngineWatchdog.LongNoticeSec) PromptSlowLoad(path);
        }));
        On(h => engine.ChainLoaded += h, h => engine.ChainLoaded -= h, (Action)(() => { if (!_disposed) _host.CloseSlowLoadPrompt(); }));   // the load finished: nothing left to decide
        // A plug-in edit marks the song whose chain it is, wherever that song is shown; a window that does not hold that song ignores it.
        On(h => engine.PluginEdited += h, h => engine.PluginEdited -= h, (Action<object?>)(owner =>
        {
            if (_disposed) return;
            var doc = _host.Documents.FirstOrDefault(d => ReferenceEquals(d, owner) || ReferenceEquals(d.Project, owner));
            if (doc is null || doc.Project.IsDirty) return;
            doc.Project.IsDirty = true;
            if (ReferenceEquals(doc, _host.ActiveDocument)) _host.UpdateTitle();
        }));
    }

    /// <summary>
    /// The engine process ended (a crash or a restart): every chain it held is gone, so every open window syncs its displayed song again
    /// (a single plug-in's crash, with the engine still running, syncs only the windows whose songs use that plug-in).
    /// The window whose song owned the engine syncs last (queued at a lower priority) so that it is still the owner afterwards.
    /// </summary>
    private void OnEngineRestarted(string path)
    {
        if (_disposed) return;
        _host.SaveSettings();
        // No path: the engine hung outside any plug-in call (or stopped answering) and was restarted; nothing was switched off.
        _host.SetStatus(string.IsNullOrEmpty(path)
            ? "The audio engine stopped responding and was restarted; playback continues"
            : $"{System.IO.Path.GetFileNameWithoutExtension(path)} stopped working and was switched off; playback continues");
        if (!_engine.TooManyCrashes)
        {
            // One plug-in crashed in its slot (the engine and every other chain are intact): only a window whose song uses that plug-in syncs.
            if (_engine.CrashLeftEngineRunning && !UsesPlugin(path)) { _host.RefreshMixer(); _host.RefreshArrangementAll(); return; }
            // Who owns the engine is read now, before any window syncs (a sync takes the ownership): the owner's sync is queued behind the others'.
            _host.Post(() => { Sync(); _engine.ResendPositions(); }, OwnsEngine() ? DispatcherPriority.Loaded : DispatcherPriority.Normal);
        }
        else _host.SetStatus("The audio engine stopped several times; plug-in tracks now play on Windows MIDI. Check Settings > Audio & Plug-ins.");
        _host.RefreshMixer();
        _host.RefreshArrangementAll();
    }

    private bool UsesPlugin(string path) =>
        path.Length > 0 && _host.Documents.Any(d => d.Project.Tracks.Any(t => t.Rig.Plugins.Any(p => string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase))));

    /// <summary>A plug-in load past 30 s asks (non-modal) whether to keep waiting or disable it for this song.</summary>
    private void PromptSlowLoad(string path)
    {
        // The song that owns this plug-in answers (not whichever tab is shown): the displayed one if it uses the plug-in, else another tab of this window; a plug-in used only by another window's song is that window's question.
        var active = _host.ActiveDocument;
        var doc = _host.Documents.Where(d => d.Project.Tracks.Any(t => t.Rig.Plugins.Any(p => string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase))))
            .OrderByDescending(d => ReferenceEquals(d, active)).FirstOrDefault();
        if (doc is null || string.IsNullOrEmpty(path) || doc.SkippedPlugins.Contains(path)) return;
        var name = System.IO.Path.GetFileNameWithoutExtension(path);
        _host.ShowSlowLoadPrompt(name, disable =>
        {
            if (!disable) return;
            doc.SkippedPlugins.Add(path);
            _host.SetStatus($"{name} is disabled for this song (not permanently); its chain reloads without it once the engine is free");
            if (ReferenceEquals(doc, _host.ActiveDocument)) Sync();
            _host.RefreshMixer();
        });
    }

    /// <summary>Detaches exactly the engine handlers this controller attached. Idempotent.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _attached.Dispose();
    }
}
