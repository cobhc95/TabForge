using System.Collections.Generic;
using TabForge.Documents;
using System;
using System.Linq;
using Avalonia.Controls;
using TabForge.Audio;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Transport;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: the preview status bar's values: the transport state word (Ready, Playing, Paused, Stopped, Recording, Count-in, Rendering, Exporting video),
//   the shown tab's position, bar and master readouts (the playhead while the song plays, else the first track and the score editor's cursor), the
//   audio device state (the playback engine's running output, else the configured settings), the MIDI output name (shown only when the song has
// a MIDI track), the import idle state, and the transient status lines (metronome, file, edit and mixer messages).
//   The engine is the one the playback sessions use; null (no playback host) shows the idle state.
//   Refreshed when the tab changes, the score or the cursor changes, the shown song's timeline changes, the settings change, the engine's status changes,
//   and (the state word and the readouts) when the transport state, an activity or the throttled playhead tick changes.
// Does not own: the texts (the presenters), the row (StatusBarView), the score cursor (ScorePreviewView), the tabs (MainWindowTabs), the
//   settings store (MainWindowSettings), the engine (AudioEngineClient) or the audio settings window (opened here with the same settings window the View menu uses).
// Tests: TabForge.Avalonia.Tests.StatusBarViewTests, StatusBarTransportTests.
/// <summary>UI thread only. Disposed by the window's lifetime, which ends its subscriptions.</summary>
public sealed class MainWindowStatusBar : IDisposable, INoticeReceiver
{
    /// <summary>The outputs the preview lists: the Microsoft MIDI Mapper only, since the preview has no WinMM device adapter.</summary>
    private static readonly IReadOnlyList<MidiOutputDeviceInfo> PreviewOutputDevices = new MidiOutputDeviceInfo[] { new() { DeviceId = -1, Name = WinMmMidiOutput.MapperName } };

    private readonly MainWindowTabs _tabs;
    private readonly ScorePreviewView _score;
    private readonly MainWindowSettings? _settings;
    private readonly AudioEngineClient? _engine;
    private readonly Window _owner;
    private readonly StatusAudioInterface _audioInterface = new();
    private StatusNoticeBarView? _boundNotice;
    private NotificationBar? _topBar;
    private readonly DocumentWatch _watch = new();
    private BarHighlight _highlight;
    private TabForge.Avalonia.Transport.LivePlayhead? _live;
    private readonly StatusScaleReadout _scale;
    private string? _shownState;
    private bool _shownDanger;

    /// <summary>The scale readout and its popup (tests read it).</summary>
    internal StatusScaleReadout Scale => _scale;

    /// <summary>The long-running activities the state word names; the render, export and recording code set them.</summary>
    public StatusActivity Activity { get; } = new();

    public MainWindowStatusBar(MainWindowTabs tabs, ScorePreviewView score, MainWindowSettings? settings, AudioEngineClient? engine, Window owner)
    {
        _tabs = tabs;
        _watch.Marked = _ => Refresh();
        _watch.ProjectChanged = (_, _) => Refresh();
        _score = score;
        _settings = settings;
        _engine = engine;
        _owner = owner;
        _scale = new StatusScaleReadout(View, () => settings?.Settings.Editing, () => tabs.Active, () => tabs.Active.Project, () => settings?.SaveSettingsDeferred());
        View.ShowImports(ImportStatusPresenter.ForPending(Array.Empty<string>()));
        View.AudioDeviceRequested += OpenAudioSettings;
        Activity.Changed += RefreshReadout;
        _highlight = tabs.Active.Highlight;
        _highlight.Changed += Refresh;
        tabs.ActiveChanged += FollowHighlight;
        tabs.ActiveChanged += Refresh;
        score.CursorChanged += Refresh;
        if (settings is not null)
        {
            settings.SettingsChanged += OnSettingsChanged;
            View.IsVisible = settings.Settings.General.ShowStatusBar;
        }
        if (engine is not null) engine.StatusChanged += RefreshAudioDevice;
        Refresh();
    }

    /// <summary>Follows the playhead: the state word, the position, the bar state and the master readout show it while a song plays, and refresh on its throttled tick.</summary>
    public void FollowPlayhead(TabForge.Avalonia.Transport.LivePlayhead live)
    {
        if (_live is not null) _live.Changed -= RefreshReadout;
        _live = live;
        live.Changed += RefreshReadout;
        Refresh();
    }

    /// <summary>The status bar row this controller fills.</summary>
    public StatusBarView View { get; } = new();

    /// <summary>Shows a transient message on the top bar when its category (a plain call is Info) is Error/Warning/Recording/Render; Info is dropped.</summary>
    public void ShowTransient(string text) => Receive(text, NoticeCategory.Info);

    /// <inheritdoc />
    public void Receive(string text, NoticeCategory category)
    {
        if (_topBar is not null) _topBar.Show(text, NoticeChannel.KindOf(category), category);
        else View.ShowStatus(text, text);
    }

    /// <summary>Routes transients to the unified top bar; null detaches (tests without a bar keep the footer text).</summary>
    public void AttachNotificationBar(NotificationBar? bar) => _topBar = bar;

    /// <summary>Ties a notice row's visibility to the Show status bar setting, so the row hides with the bar.</summary>
    public void BindNotice(StatusNoticeBarView notice)
    {
        _boundNotice = notice;
        notice.StatusBarVisible = View.IsVisible;
    }

    /// <summary>Stops following the tabs, the score, the song's timeline and the settings.</summary>
    public void Dispose()
    {
        if (_live is not null) _live.Changed -= RefreshReadout;
        Activity.Changed -= RefreshReadout;
        _highlight.Changed -= Refresh;
        _tabs.ActiveChanged -= FollowHighlight;
        _tabs.ActiveChanged -= Refresh;
        _score.CursorChanged -= Refresh;
        if (_settings is not null) _settings.SettingsChanged -= OnSettingsChanged;
        if (_engine is not null) _engine.StatusChanged -= RefreshAudioDevice;
        _watch.Dispose();
    }

    // The shown tab changed: the position line follows that song's highlight.
    private void FollowHighlight()
    {
        _highlight.Changed -= Refresh;
        _highlight = _tabs.Active.Highlight;
        _highlight.Changed += Refresh;
    }

    /// <summary>Shows the readouts of the shown song and the editor's cursor (the highlighted cell when the timeline has a highlight), and the audio state.</summary>
    public void Refresh()
    {
        RefreshReadout();
        RefreshAudioDevice();
    }

    /// <summary>Shows the state word and the readouts: the playhead's while the song plays, else the cursor's (with the time at it). The playhead tick calls only this.</summary>
    private void RefreshReadout()
    {
        _watch.Bind(_tabs.Active);
        var song = _tabs.Active.Project;
        var track = song.Tracks.Count > 0 ? song.Tracks[0] : null;
        var editor = _score.Editor;
        var (bar, cell) = (editor.SelectedMeasure, editor.SelectedCell);
        if (_highlight.Has && _highlight.ActiveTrack < song.Tracks.Count)
        {
            track = song.Tracks[_highlight.ActiveTrack];
            (bar, cell) = (_highlight.ActiveBar, 0);   // Measure, track and cell of the highlighted cell
        }
        StatusTextReadout readout;
        if (_live is { Following: true } live)
        {
            readout = StatusTextPresenter.Compose(new StatusTextInput(song, track, TransportRunning: true, PlayheadBar: live.Bar, PlayheadCell: live.Cell,
                SelectedBar: bar, SelectedCell: cell, TimeSec: live.Sec));
            readout = readout with { Tempo = live.Tempo };   // the tempo sounding at the playhead, repeats and ramps included
        }
        else
        {
            var slots = Math.Max(1, MusicTime.BarSlots(song, bar));
            var sec = AudioClipTimelineRules.SecondsAtBar(song, bar, cell / (double)slots);
            readout = StatusTextPresenter.Compose(new StatusTextInput(song, track, TransportRunning: false, PlayheadBar: -1, PlayheadCell: 0,
                SelectedBar: bar, SelectedCell: cell, TimeSec: sec));
            readout = readout with { Tempo = PlaybackTempo.AtCell(song, bar, cell) };   // the tempo at the cursor (a mid-bar change included)
        }
        // The scale of the section at the playhead while the song plays, else at the cursor; the track is the selected one.
        var selected = !_highlight.Has && editor.Track is { } shown && song.Tracks.Contains(shown) ? shown : track;
        _scale.Show(song, _live is { Following: true } playing ? playing.Bar : bar, selected);
        var midiTrack = song.Tracks.FirstOrDefault(t => !t.IsAudio);
        View.ShowReadout(readout, MidiOutputStatusPresenter.OutputText(PreviewOutputDevices, midiTrack?.MidiOutputDeviceId ?? -1), midiTrack is not null);
        ShowState();
    }

    /// <summary>The state word for a transport phase and the running activities, strongest first; Recording is the one shown in the danger colour.</summary>
    public static string StateText(TransportPhase phase, bool recording, bool rendering, bool exportingVideo) =>
        exportingVideo ? "Exporting video…"
        : rendering ? "Rendering…"
        : phase == TransportPhase.CountIn ? "Count-in…"
        : recording ? "Recording"
        : phase switch { TransportPhase.Playing => "Playing", TransportPhase.Paused => "Paused", TransportPhase.Stopped => "Stopped", _ => StatusBarView.ReadyText };

    // The status text is touched only when the word changes, so a transient message is not overwritten by every tick.
    private void ShowState()
    {
        var phase = _live?.Phase ?? TransportPhase.Ready;
        var text = StateText(phase, Activity.Recording, Activity.Rendering, Activity.ExportingVideo);
        var danger = text == "Recording";
        if (text == _shownState && danger == _shownDanger) return;
        _shownState = text;
        _shownDanger = danger;
        View.ShowState(text, danger);
    }

    /// <summary>Shows the running output of the engine, or the configured interface while the engine is idle; the button is always shown.</summary>
    private void RefreshAudioDevice()
    {
        var plugins = _settings?.Settings.Plugins ?? new PluginSettings();
        var running = _engine is { IsRunning: true } engine ? engine.Output : null;
        View.ShowAudioDevice(_audioInterface.Status(plugins, running));
        View.ShowAudioTip(_audioInterface.Tip(plugins, running, AsioChannelNames(plugins)));
    }

    /// <summary>The names of the ASIO driver's channels as the engine reported them (when it ran the driver, else when the settings asked it); null for other drivers or when not known.</summary>
    private (IReadOnlyList<string> Inputs, IReadOnlyList<string> Outputs)? AsioChannelNames(PluginSettings plugins)
    {
        if (plugins.Driver != AudioDrivers.Asio || _engine is not { } engine) return null;
        return engine.AsioChannelNamesOf(plugins.Device);
    }

    private void OnSettingsChanged(AppSettings settings)
    {
        View.IsVisible = settings.General.ShowStatusBar;
        RefreshReadout();   // the scale settings may have changed
        if (_boundNotice is not null) _boundNotice.StatusBarVisible = settings.General.ShowStatusBar;
        RefreshAudioDevice();
    }

    private void OpenAudioSettings() =>
        MainWindowSettings.ShowPreferencesPage(_settings, _owner, SettingsCatalog.AudioVst);
}
