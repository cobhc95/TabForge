using TabForge.Controllers;
using TabForge.Documents;
using System.Windows;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

// MainWindow: the window's side of the mixer host. The host logic (mixer, FX chain and mixer-window hosts) lives in MixerHost; this
// partial holds the few forwards the commands and settings code use, the narrow IMixerSurface it hands to the host, and the + Track menu.
// Owns: the MixerHost instance of the window and its IMixerSurface view of the window.
// Does not own: the mixer / FX host logic (MixerHost), the windows (MixerWindowsController), plug-in hosting (the audio engine client).
// Tests: listed in docs/feature-map/mixer-and-audio-engine.md.
public partial class MainWindow : IMixerSurface
{
    private MixerHost? _mixerHost;
    internal MixerHost MixerHost => _mixerHost ??= new MixerHost(this);

    private void OpenMixer() => MixerHost.OpenMixer();
    internal void OpenFxChain(TrackModel? track) => MixerHost.OpenFxChain(track);
    internal void OpenBusFx(string? group) => MixerHost.OpenBusFx(group);
    internal void OpenMonitorFx() => MixerHost.OpenMonitorFx();
    internal void OpenWiring(TrackModel? track) => MixerHost.OpenWiring(track);
    internal void OpenMidiProcessing(TrackModel? track) => MixerHost.OpenMidiProcessing(track);
    internal void SetAllGroupsCollapsed(bool collapsed) => MixerHost.SetAllGroupsCollapsed(collapsed);
    internal void OpenGroupRules() => MixerHost.OpenGroupRules();
    private void AttachAppRules(SongProject project) => MixerHost.AttachAppRules(project);

    DocumentSession IMixerSurface.Doc => Doc;
    IReadOnlyList<DocumentSession> IMixerSurface.Documents => _documents.Documents;
    PlaybackEngine IMixerSurface.Midi => _midi;
    EngineSyncController IMixerSurface.EngineSync => _engineSync;
    ArrangementPanel IMixerSurface.Arrangement => Arrangement;
    HotkeyMaps IMixerSurface.Hotkeys => _hotkeys;
    Audio.AudioEngineClient IMixerSurface.Engine => _engine;
    void IMixerSurface.SyncAudioEngine() => SyncAudioEngine();
    void IMixerSurface.RefreshTracks() => RefreshTracks();
    void IMixerSurface.RefreshArrangement() => RefreshArrangement();
    void IMixerSurface.RefreshTabs() => RefreshTabs();
    void IMixerSurface.UpdateTitle() => UpdateTitle();
    void IMixerSurface.ScheduleFitTimelineToTracks() => ScheduleFitTimelineToTracks();
    void IMixerSurface.CheckpointUndo() => CheckpointUndo();
    void IMixerSurface.OpenAudioSettings() => OpenSettingsCategory(SettingsCatalog.AudioVst);

    /// <summary>
    /// Right-click on + Track: "Add track…" and "Audio track", nothing else (the Mixer has its own button). The colour items sit in the
    /// track list's empty-area menu (Colours), and the colour of each group to Settings > Appearance &amp; colours.
    /// </summary>
    private void ShowAddTrackMenu(FrameworkElement target) => BuildAddTrackMenu(target).IsOpen = true;

    /// <summary>The + Track button's right-click menu: "Add track…" and "Audio track" (a test reads and clicks its items).</summary>
    internal System.Windows.Controls.ContextMenu BuildAddTrackMenu(FrameworkElement target)
    {
        var menu = new System.Windows.Controls.ContextMenu
        {
            PlacementTarget = target,
            Style = (Style)FindResource(typeof(System.Windows.Controls.ContextMenu))
        };
        System.Windows.Controls.MenuItem Item(string header, Action action)
        {
            var item = new System.Windows.Controls.MenuItem { Header = header, Style = (Style)FindResource(typeof(System.Windows.Controls.MenuItem)) };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
            return item;
        }
        Item("Add track…", AddTrackWithWindow);
        Item("Audio track", AddAudioTrack);
        return menu;
    }

    /// <summary>Song time for audio clips: the clock of the active song (it belongs to the document, so it stays with the song when its tab moves to another window).</summary>
    internal Audio.SongClock SongClock => Doc.Playback.Clock;

    private void SyncAudioEngine() => _engineSync.Sync();

    /// <summary>Refreshes mixer windows and closes stale FX windows, optionally syncing the restored document once after the frame.</summary>
    private void SyncMixerWindows(bool deferEngineSync = false) => MixerHost.SyncWindows(deferEngineSync);

    /// <summary>The window as the host of its <see cref="EngineSyncController"/>.</summary>
    private sealed class EngineSyncHost : IEngineSyncHost
    {
        private readonly MainWindow _window;
        public EngineSyncHost(MainWindow window) => _window = window;

        public DocumentSession ActiveDocument => _window.Doc;
        public IReadOnlyList<DocumentSession> Documents => _window._documents.Documents;
        public AppSettings Settings => _window._settings;
        public bool IsInitialized => _window._mainWindowInitialized;
        /// <summary>The one "plug-in loading slowly" question the application shows: the engine is shared, so every window hears the same slow load, but only one prompt (in the window whose song uses the plug-in) may be open at a time.</summary>
        private static Views.ThemedConfirmDialog? _slowLoadPrompt;

        public bool ShowSlowLoadPrompt(string pluginName, Action<bool> answered)
        {
            if (_slowLoadPrompt is not null) return false;
            var prompt = _slowLoadPrompt = new Views.ThemedConfirmDialog(
                "Plug-in loading slowly",
                $"{pluginName} is taking a long time to load.\n\nDisable it: the song plays without it while this song is open. " +
                "This is not a quarantine: it is not saved and the plug-in loads normally the next time the song is opened.",
                yesToolTip: "Keep waiting for the plug-in to load",
                noToolTip: "Skip this plug-in for this song and reload its chain without it",
                showCancel: false, yesText: "Keep waiting", noText: "Disable it") { Owner = _window };
            prompt.ShowModeless(result =>
            {
                if (ReferenceEquals(_slowLoadPrompt, prompt)) _slowLoadPrompt = null;
                answered(result == MessageBoxResult.No);
            });
            return true;
        }

        public void CloseSlowLoadPrompt() => _slowLoadPrompt?.Close();
        public void Post(Action work, System.Windows.Threading.DispatcherPriority priority) => _window.PostIfOpen(work, priority);
        public void RebuildMidi() => _window._midi.Rebuild(_window._project);
        public void RearmChannelSetup() => _window._midi.RearmChannelSetup();
        public void SetStatus(string text) => _window.StatusText.Text = text;
        public void CheckpointUndo() => _window.CheckpointUndo();
        public void SaveSettings() => _window.SaveSettings();
        public void UpdateTitle() => _window.UpdateTitle();
        public void RefreshTracks() => _window.RefreshTracks();
        public void RefreshArrangement() => _window.RefreshArrangement();
        public void RefreshArrangementAll() => _window.Arrangement.RefreshAll();
        public void RefreshMixer() => _window.MixerHost.RefreshWindow();
        public void UpdateAudioDeviceStatus() => _window.UpdateAudioDeviceStatus();
        public void UpdatePluginTrustBar() => _window.UpdatePluginTrustBar();
        public void UpdateMediaApprovalBar() => _window.UpdateMediaApprovalBar();
    }
}
