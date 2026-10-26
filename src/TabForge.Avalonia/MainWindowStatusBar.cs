using System.Collections.Generic;
using TabForge.Documents;
using System;
using System.Linq;
using Avalonia.Controls;
using TabForge.Audio;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: the preview status bar's values: the shown tab's position, bar and master readouts (the first track, the score editor's cursor), the
//   audio device state (the playback engine's running output, else the configured settings), the MIDI output name (shown only when the song has
// a MIDI track), the import idle state, and the transient status lines (metronome, file, edit and mixer messages).
//   The engine is the one the playback sessions use; null (no playback host) shows the idle state.
//   Refreshed when the tab changes, the score or the cursor changes, the shown song's timeline changes, the settings change and the engine's status changes.
// Does not own: the texts (the presenters), the row (StatusBarView), the score cursor (ScorePreviewView), the tabs (MainWindowTabs), the
//   settings store (MainWindowSettings), the engine (AudioEngineClient) or the audio settings window (opened here with the same settings window the View menu uses).
// Tests: TabForge.Avalonia.Tests.StatusBarViewTests.
/// <summary>UI thread only. Disposed by the window's lifetime, which ends its subscriptions.</summary>
public sealed class MainWindowStatusBar : IDisposable
{
    /// <summary>The note value the preview's readouts use (a quarter), since the preview has no note value tool yet.</summary>
    private const int PreviewDurationDenominator = 4;

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
    private TabForge.Avalonia.Transport.LiveTempo? _live;

    public MainWindowStatusBar(MainWindowTabs tabs, ScorePreviewView score, MainWindowSettings? settings, AudioEngineClient? engine, Window owner)
    {
        _tabs = tabs;
        _watch.Marked = _ => Refresh();
        _watch.ProjectChanged = (_, _) => Refresh();
        _score = score;
        _settings = settings;
        _engine = engine;
        _owner = owner;
        View.ShowImports(ImportStatusPresenter.ForPending(Array.Empty<string>()));
        View.AudioDeviceRequested += OpenAudioSettings;
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

    /// <summary>Follows the playing tempo: the master readout shows it while a song plays and refreshes when it changes.</summary>
    public void FollowTempo(TabForge.Avalonia.Transport.LiveTempo live)
    {
        if (_live is not null) _live.Changed -= Refresh;
        _live = live;
        live.Changed += Refresh;
        Refresh();
    }

    /// <summary>The status bar row this controller fills.</summary>
    public StatusBarView View { get; } = new();

    /// <summary>Shows a transient message on the top bar (Info, 5 s); the footer keeps only position/master readouts.</summary>
    public void ShowTransient(string text)
    {
        if (_topBar is not null)
        {
            _topBar.Show(text, NotificationKind.Info);
            return;
        }
        View.ShowStatus(text, text);
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
        if (_live is not null) _live.Changed -= Refresh;
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
        var readout = StatusTextPresenter.Compose(new StatusTextInput(
            song, track, TransportRunning: false, PlayheadBar: -1, PlayheadCell: 0, SelectedBar: bar, SelectedCell: cell,
            DurationDenominator: PreviewDurationDenominator, Dots: 0, Triplet: false));
        // The live tempo of the playing bar and note, else the tempo at the cursor (a mid-bar change included).
        readout = readout with { Tempo = _live?.Playing ?? PlaybackTempo.AtCell(song, bar, cell) };
        var midiTrack = song.Tracks.FirstOrDefault(t => !t.IsAudio);
        View.ShowReadout(readout, MidiOutputStatusPresenter.OutputText(PreviewOutputDevices, midiTrack?.MidiOutputDeviceId ?? -1), midiTrack is not null);
        RefreshAudioDevice();
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
        if (_boundNotice is not null) _boundNotice.StatusBarVisible = settings.General.ShowStatusBar;
        RefreshAudioDevice();
    }

    private void OpenAudioSettings() =>
        MainWindowSettings.ShowPreferencesPage(_settings, _owner, SettingsCatalog.AudioVst);
}
