using System;
using System.Collections.Generic;
using Avalonia.Controls;
using TabForge.Avalonia.Platform;
using TabForge.Platform;
using TabForge.Avalonia.Transport;
using TabForge.Avalonia.Views.Fx;
using TabForge.Documents;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: the window's hooks that keep the audio engine in line with the song: attaching the shared audio settings to the playback sessions,
//     the master knob (its value is the saved master percent), each arrangement's track mute, solo and level changes (a live mix push, and MainWindowLiveMix for the other edits), and the window's
//     answers to the engine's events and audio route settings (AvaloniaEngineEvents, detached when the window closes).
// Does not own: the engine sync and levels (AvaloniaPlaybackSessions), the mixer's edits (Views/MixerWindow), the knob (Platform/TransportBarView),
//     the events' rules (AvaloniaEngineEvents).
// Tests: TabForge.Avalonia.Tests.AvaloniaEngineRoutingTests, TabForge.Avalonia.Tests.NotificationBarTests.
public static class MainWindowEngineSync
{
    /// <param name="window">The main window, whose refresh drives the playhead frames and which owns the engine event handlers.</param>
    /// <param name="panes">The document panes; each new arrangement's knob and mute and solo toggles are hooked.</param>
    /// <param name="songs">The tabs; a mute or solo toggle pushes the shown song's mix.</param>
    /// <param name="playback">The playback sessions; null (no engine host) makes this do nothing.</param>
    /// <param name="settings">The shared settings the master percent and plug-ins are read from and saved to; null does nothing.</param>
    /// <param name="showStatus">Shows an engine notice (a crash, a failed or slow plug-in) on the top bar (sticky Error, same source updates in place); null shows nothing.</param>
    public static void Register(Window window, MainWindowDocumentPanes panes, MainWindowTabs songs, AvaloniaPlaybackSessions? playback, MainWindowSettings? settings,
        Action<string>? showStatus = null)
    {
        if (playback is null || settings is null) return;
        playback.AttachSettings(settings.Settings.Audio, settings.Settings.Plugins);
        if (playback.Engine is { } engine)
        {
            FxEngineSetup.Attach(engine, settings.Settings.Plugins, TabForge.Avalonia.Platform.AvaloniaUiThread.ForApplication());
            var events = new AvaloniaEngineEvents(engine, new EngineEventHost(window, songs, playback, settings, showStatus ?? (_ => { })),
                TabForge.Avalonia.Platform.AvaloniaUiThread.ForApplication());
            Action<AppSettings> onSettings = _ => events.ApplyRouteSettings();
            settings.SettingsChanged += onSettings;
            window.Closed += (_, _) => { settings.SettingsChanged -= onSettings; events.Dispose(); };
        }
        var liveMix = new MainWindowLiveMix(playback);
        liveMix.Bind(songs.Active);
        songs.ActiveChanged += () => liveMix.Bind(songs.Active);
        window.Closed += (_, _) => liveMix.Dispose();
        if (playback.Engine is { } monitorEngine)
        {
            var monitor = new MainWindowMidiMonitor(songs, playback, settings, monitorEngine);   // the MIDI keyboard heard through the armed and selected tracks
            window.Activated += (_, _) => monitor.Activate();   // only the active window's monitor sounds
            window.Closed += (_, _) => monitor.Dispose();
        }
        playback.FrameRateCap = () => settings.Settings.Follow.MaxFps;
        playback.FrameWindow = () => window;
        panes.ArrangementCreated += arrangement =>
        {
            var knob = arrangement.Transport.MasterVolumeKnob;
            knob.Value = playback.MasterPercent;
            // A turn pushes the master at most every settings-preview interval (with a trailing run), and once more when the turn ends.
            var coalesced = new TabForge.Avalonia.Platform.SliderChangeCoalescer(() =>
            {
                playback.SetMasterPercent((int)Math.Round(knob.Value));
                settings.SaveSettings();
            });
            knob.ValueChanged += (_, _) => coalesced.SliderChanged();
            knob.EditEnded += (_, _) => coalesced.Flush();
            // The knob follows the master volume set anywhere else (the mixer's Master slider) while it is on screen.
            void FollowMaster(int percent) { if (knob.Value != percent) knob.Value = percent; }
            knob.AttachedToVisualTree += (_, _) => { knob.Value = playback.MasterPercent; playback.MasterPercentChanged += FollowMaster; };
            knob.DetachedFromVisualTree += (_, _) => playback.MasterPercentChanged -= FollowMaster;
            // One engine push per frame, after the click that changed the mix has painted.
            var mixPush = new TabForge.Platform.NextFrameCoalescer(AvaloniaUiThread.ForApplication(), () => PushShownMix(songs, playback));
            arrangement.Input.TrackMuteSoloToggled += mixPush.Request;
            arrangement.Input.TrackLevelChanged += mixPush.Request;
            window.Closed += (_, _) => mixPush.Cancel();
        };
    }

    private static void PushShownMix(MainWindowTabs songs, AvaloniaPlaybackSessions playback)
    {
        SongDocument document = songs.Active;
        playback.PushLiveMix(document);
    }

    /// <summary>The window's side of the engine events: its songs, its settings, its notice row and its engine sync.</summary>
    private sealed class EngineEventHost : IAvaloniaEngineEventHost
    {
        private readonly Window _window;
        private readonly MainWindowTabs _songs;
        private readonly AvaloniaPlaybackSessions _playback;
        private readonly MainWindowSettings _settings;
        private readonly Action<string> _showStatus;

        public EngineEventHost(Window window, MainWindowTabs songs, AvaloniaPlaybackSessions playback, MainWindowSettings settings, Action<string> showStatus)
        {
            (_window, _songs, _playback, _settings, _showStatus) = (window, songs, playback, settings, showStatus);
        }

        public IReadOnlyList<SongDocument> Songs => _songs.Songs;
        public SongDocument Active => _songs.Active;
        public TabForge.Services.PluginSettings Plugins => _settings.Settings.Plugins;
        public Window? Owner => _window;
        public void SaveSettings() => _settings.SaveSettings();
        public void ShowStatus(string text) => _showStatus(text);
        public void SyncActive() => _playback.SyncEngine(_songs.Active, _settings.Settings.Plugins);
        public AvaloniaPlaybackSession? SessionOf(SongDocument song) => _playback.Existing(song);
    }
}
