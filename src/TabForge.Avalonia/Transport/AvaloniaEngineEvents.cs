using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using TabForge.Audio;
using TabForge.Audio.Contracts;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Platform;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Transport;

/// <summary>What <see cref="AvaloniaEngineEvents"/> needs from its window.</summary>
public interface IAvaloniaEngineEventHost
{
    /// <summary>The songs open in this window.</summary>
    IReadOnlyList<SongDocument> Songs { get; }
    /// <summary>The song shown.</summary>
    SongDocument Active { get; }
    /// <summary>The plug-in settings the engine sync reads (live).</summary>
    PluginSettings Plugins { get; }
    /// <summary>The window the slow-load question opens over; null: no window.</summary>
    Window? Owner { get; }
    /// <summary>Writes the settings file.</summary>
    void SaveSettings();
    /// <summary>Shows a line in the window's status notice row.</summary>
    void ShowStatus(string text);
    /// <summary>Brings the engine in line with the song shown.</summary>
    void SyncActive();
    /// <summary>The playback session of a song, or null when it has none yet.</summary>
    AvaloniaPlaybackSession? SessionOf(SongDocument song);
}

// Owns: one window's answers to the shared audio engine's events and settings: a plug-in that crashed (the quarantine save, the notice, the resync of
//     the songs that own the engine), a failed or slow plug-in (its notice and the one "loading slowly" question), the device reopen (the songs playing
//     here pause and resume around it; a failed open is said), and the audio route settings (a change reaches the engine at once).
// Does not own: the engine and its events (AudioEngineClient), the engine sync (AvaloniaPlaybackSessions through the host), the settings file, the notice row.
// Tests: TabForge.Avalonia.Tests.PluginEngineEventsTests.
// Threads: UI thread only.
public sealed class AvaloniaEngineEvents : IDisposable
{
    public const string EngineStoppedNotice = "The audio engine stopped responding and was restarted; playback continues";
    public const string TooManyCrashesNotice = "The audio engine stopped several times; plug-in tracks now play on Windows MIDI. Check Settings > Audio & Plug-ins.";
    public const string SlowLoadTitle = "Plug-in loading slowly";
    public const string KeepWaitingText = "Keep waiting", DisableText = "Disable it";

    /// <summary>The one "loading slowly" question of the application: the engine is shared, so only one prompt may be open at a time.</summary>
    private static PromptWindow? _slowLoadPrompt;

    private readonly AudioEngineClient _engine;
    private readonly IAvaloniaEngineEventHost _host;
    private readonly IUiThread _ui;
    private readonly DeviceChangePause _devicePause = new();
    private string _routeKey;
    private bool _disposed;
    private const long DeviceReadyCooldownMs = 1000;   // a flapping device raises DeviceReady in bursts: one resend per burst
    private long _lastDeviceReadyMs = long.MinValue / 2;
    private bool _setupPending;   // the engine has fresh synths that still need the song's programs; sent once every requested chain is loaded

    /// <param name="engine">The shared engine client (its events are attached until <see cref="Dispose"/>).</param>
    /// <param name="host">The window the events answer for.</param>
    /// <param name="ui">The window's UI thread, for the resync queued after a restart.</param>
    public AvaloniaEngineEvents(AudioEngineClient engine, IAvaloniaEngineEventHost host, IUiThread ui)
    {
        (_engine, _host, _ui) = (engine, host, ui);
        _routeKey = RouteKey(host.Plugins);
        _engine.PluginCrashed += OnPluginCrashed;
        _engine.PluginFailed += OnPluginFailed;
        _engine.PluginSlow += OnPluginSlow;
        _engine.ChainLoaded += OnChainLoaded;
        _engine.DeviceChanging += OnDeviceChanging;
        _engine.DeviceReady += OnDeviceReady;
        _engine.DeviceError += OnDeviceError;
        _engine.TrackLimitReached += OnTrackLimitReached;
    }

    /// <summary>The notice for a crash: the plug-in's name when the engine said which one, the restart notice when it hung, the give-up notice after several crashes.</summary>
    public static string CrashNotice(string path, bool tooManyCrashes) =>
        tooManyCrashes ? TooManyCrashesNotice
        : string.IsNullOrEmpty(path) ? EngineStoppedNotice
        : $"{FileName(path)} stopped working and was switched off; playback continues";

    /// <summary>The audio route settings (driver, device, channel pair, rate, buffer, latency, play-all) as one key: a change of any of them reaches the engine.</summary>
    public static string RouteKey(PluginSettings plugins) => string.Join('|', plugins.Driver, plugins.InputDevice, plugins.Device, plugins.SampleRate,
        plugins.BufferSize, plugins.AsioInputChannel, plugins.AsioInputLastChannel, plugins.AsioOutputChannel, plugins.AsioOutputLastChannel,
        plugins.PlayAllThroughEngine, plugins.WindowsMidiLatencyMs);

    /// <summary>The audio route settings changed: when one of them differs, the song shown is synced to the engine at once.</summary>
    public void ApplyRouteSettings()
    {
        if (_disposed) return;
        var key = RouteKey(_host.Plugins);
        if (key == _routeKey) return;
        _routeKey = key;
        // A play-all choice that differs from the engine's own was made by hand: it is no longer set automatically.
        if (_engine.Mixer.PlayAllThroughEngine != _host.Plugins.PlayAllThroughEngine) _host.Plugins.PlayAllSetAutomatically = false;
        _host.SyncActive();
    }

    private void OnPluginCrashed(string path)
    {
        if (_disposed) return;
        _host.SaveSettings();   // the quarantine list the engine wrote is kept
        _host.ShowStatus(CrashNotice(path, _engine.TooManyCrashes));
        if (_engine.TooManyCrashes) return;
        // The restarted engine's synths are new (piano): the owner's song sends its programs again once their chains are loaded.
        _setupPending = true;
        // One plug-in crashed in its slot (the engine and every other chain are intact): only a song that uses that plug-in syncs.
        if (_engine.CrashLeftEngineRunning && !UsesPlugin(path) && !(OwnsEngine() && _engine.LiveInput.Uses(path))) return;
        // The owner of the engine syncs last (queued at a lower priority) so that it is still the owner afterwards.
        _ui.Post(() => { if (!_disposed) { _host.SyncActive(); _engine.ResendPositions(); } }, OwnsEngine() ? UiPriority.Loaded : UiPriority.Normal);
    }

    private void OnPluginFailed(string path, string why)
    {
        if (!_disposed) _host.ShowStatus($"{FileName(path)} could not be loaded: {why}");
    }

    // Slow is not a crash: a big instrument may take a while; it is switched off only if the user says so at the question.
    private void OnPluginSlow(string path, PluginCallKind kind, int seconds)
    {
        if (_disposed) return;
        _host.ShowStatus($"{FileName(path)} {SlowAction(kind)} ({seconds} s)…");
        if (kind == PluginCallKind.Load && seconds >= EngineWatchdog.LongNoticeSec) AskSlowLoad(path);
    }

    private void OnChainLoaded()
    {
        if (_disposed) return;
        _slowLoadPrompt?.Close();   // the load finished: nothing left to decide
        ResendSetupWhenLoaded();
    }

    /// <summary>The owner's shown song sends every channel's program, volume and pan to the engine once, when the chains it asked for are all loaded (fresh synths start as piano).</summary>
    private void ResendSetupWhenLoaded()
    {
        if (!_setupPending || !OwnsEngine()) return;
        var acks = _engine.LastAcknowledgements;
        if (_engine.RequestedChains.Any(r => !acks.Any(a => a.Slot == r.Slot && a.Generation >= r.Generation))) return;
        _setupPending = false;
        _host.SessionOf(_host.Active)?.Engine.RearmChannelSetup();
    }

    private void OnDeviceChanging()
    {
        if (_disposed) return;
        var transports = new List<(bool Playing, Action Pause, Action Resume)>();
        foreach (var song in _host.Songs)
        {
            if (_host.SessionOf(song) is not { } session) continue;
            transports.Add((session.IsPlaying && !session.IsPaused, () => session.PlayOrPause(), () => ResumeIfPaused(song, session)));
        }
        _devicePause.Changing(transports);
    }

    private void OnDeviceReady()
    {
        if (_disposed) return;
        _devicePause.Ready();
        var now = Environment.TickCount64;
        var flapping = now - _lastDeviceReadyMs < DeviceReadyCooldownMs;
        _lastDeviceReadyMs = now;
        if (!flapping) _setupPending = true;   // a new mix: its synths hold nothing the song sent before (the engine replays its own copy of the state too)
        ResendSetupWhenLoaded();
    }

    private void OnTrackLimitReached(int limit)
    {
        if (!_disposed) _host.ShowStatus($"The audio engine plays at most {limit} tracks; the extra tracks stay silent.");
    }

    private void OnDeviceError(string message)
    {
        if (_disposed) return;
        _devicePause.Failed();
        _host.ShowStatus($"Audio output: {message}");
    }

    /// <summary>Resumes a song paused for the device change only while it is still paused (stopped or resumed by the user meanwhile: left alone).</summary>
    private void ResumeIfPaused(SongDocument song, AvaloniaPlaybackSession session)
    {
        if (_disposed || _host.SessionOf(song) is not { } current || !ReferenceEquals(current, session)) return;
        if (current.IsPlaying && current.IsPaused) current.PlayOrPause();
    }

    /// <summary>A plug-in load past the long-notice limit asks (non-modal) whether to keep waiting or disable it for the song that uses it.</summary>
    private void AskSlowLoad(string path)
    {
        var song = OwningSong(path);
        if (song is null || song.SkippedPlugins.Contains(path) || _slowLoadPrompt is not null) return;
        var name = FileName(path);
        var prompt = new PromptWindow(SlowLoadSpec(name));
        _slowLoadPrompt = prompt;
        prompt.Closed += (_, _) =>
        {
            if (ReferenceEquals(_slowLoadPrompt, prompt)) _slowLoadPrompt = null;
            if (prompt.Outcome?.Answer == PromptAnswer.No) Disable(song, path, name);
        };
        if (_host.Owner is { } owner) prompt.Show(owner);
        else prompt.Show();
    }

    private void Disable(SongDocument song, string path, string name)
    {
        if (_disposed) return;
        song.SkippedPlugins.Add(path);
        _host.ShowStatus($"{name} is disabled for this song (not permanently); its chain reloads without it once the engine is free");
        if (ReferenceEquals(song, _host.Active)) _host.SyncActive();
    }

    private static PromptSpec SlowLoadSpec(string name) => new(
        SlowLoadTitle,
        $"{name} is taking a long time to load.\n\nDisable it: the song plays without it while this song is open. " +
        "This is not a quarantine: it is not saved and the plug-in loads normally the next time the song is opened.",
        YesText: KeepWaitingText, NoText: DisableText,
        YesToolTip: "Keep waiting for the plug-in to load",
        NoToolTip: "Skip this plug-in for this song and reload its chain without it",
        ShowCancel: false, DefaultIsNo: false);

    private static string SlowAction(PluginCallKind kind) => kind switch
    {
        PluginCallKind.Load => "is still loading",
        PluginCallKind.SetState => "is still applying its settings",
        PluginCallKind.GetState => "is still saving its settings",
        PluginCallKind.Editor => "is still opening its window",
        _ => "is still busy",
    };

    /// <summary>The song that uses the plug-in: the shown one first, else another song of this window; null when none does.</summary>
    private SongDocument? OwningSong(string path) => path.Length == 0 ? null
        : _host.Songs.Where(song => UsesIn(song, path)).OrderByDescending(song => ReferenceEquals(song, _host.Active)).FirstOrDefault();

    private bool UsesPlugin(string path) => path.Length > 0 && _host.Songs.Any(song => UsesIn(song, path));

    private static bool UsesIn(SongDocument song, string path) =>
        song.Project.Tracks.Any(t => t.Rig.Plugins.Any(p => string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase)));

    /// <summary>True when this window's shown song made the engine's latest sync (or nobody has yet): only that window syncs after a restart.</summary>
    private bool OwnsEngine() => _engine.CurrentOwner is null || ReferenceEquals(_engine.CurrentOwner, _host.Active);

    private static string FileName(string path) => System.IO.Path.GetFileNameWithoutExtension(path);

    /// <summary>Detaches exactly the engine handlers this controller attached. Idempotent.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _engine.PluginCrashed -= OnPluginCrashed;
        _engine.PluginFailed -= OnPluginFailed;
        _engine.PluginSlow -= OnPluginSlow;
        _engine.ChainLoaded -= OnChainLoaded;
        _engine.DeviceChanging -= OnDeviceChanging;
        _engine.DeviceReady -= OnDeviceReady;
        _engine.DeviceError -= OnDeviceError;
        _engine.TrackLimitReached -= OnTrackLimitReached;
    }
}
