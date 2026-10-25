using TabForge.Documents;
using System;
using Avalonia.Controls;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Transport;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Docking;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: keeping the document-bound panes on the shown tab's song: the one score view, and the latest arrangement the dock made (the dock
//   rebuilds its panes, an older one is dropped); a tab change shows the new song in both. Owns the window's one Keyboard Learn engine, shared by the
//   docked pane and the pop-out so their bars stay in sync (wait, hands, speed, loop, track, MIDI, look-ahead; judge, score, wait-gating, loop feed).
// Does not own: the tabs (MainWindowTabs), the panes' drawing and edits (the Views), the dock, the settings file (wired through AttachLearnSettings).
// Tests: TabForge.Avalonia.Tests.MainWindowDocumentsTests, TabForge.Avalonia.Tests.KeyboardLearnEngineTests.
public sealed class MainWindowDocumentPanes
{
    private readonly MainWindowTabs _tabs;
    private readonly AvaloniaPlaybackSessions? _playback;
    private ArrangementView? _arrangement;
    private KeyboardModePane? _keyboard;
    private BandView? _band;
    private readonly SharedMeterTick? _meterTick;
    private readonly ToolWindows _tools = new();

    public MainWindowDocumentPanes(MainWindowTabs tabs, ScorePreviewView score, AvaloniaPlaybackSessions? playback = null, SharedMeterTick? meterTick = null)
    {
        _meterTick = meterTick;
        _tabs = tabs;
        _playback = playback;
        Score = score;
        Selection = new SelectionLoopLink(tabs, score, playback);
        LearnEngine.LoopSecondsProvider = () => Selection.LoopSeconds;
        LearnEngine.AttachMidi(() => _playback?.Engine?.MidiInput,
            () => _playback?.Engine is { } engine && engine.IsRunning ? engine.LatencyTicks / (double)System.Diagnostics.Stopwatch.Frequency : 0);
        // A click on the score moves playback to the cell pressed (the session keeps playing if it was playing); never while recording.
        score.CursorPlaced += (bar, cell) => { if (!IsRecording) _playback?.For(_tabs.Active)?.Seek(bar, cell); };
        Selection.TabChanged(() => score.Show(tabs.Active));   // the document's own selection (a torn-off tab carries it) is shown, not wiped by the score's first show
        score.LightPaper = !tabs.Active.DarkPaper;
        score.BindPlayback(playback?.For(tabs.Active));
        LearnEngine.BindSong(tabs.Active, playback?.For(tabs.Active));
        WatchTrack(tabs.Active);
        tabs.ActiveChanged += () => Selection.TabChanged(ShowActive);
        PlayheadTrace.AttachIfOn(tabs, playback, score, () => _arrangement);
    }

    // The shown tab's song in every document-bound pane.
    private void ShowActive()
    {
        WatchTrack(_tabs.Active);
        Score.Show(_tabs.Active);
        Score.LightPaper = !_tabs.Active.DarkPaper;   // every tab keeps its own score paper
        Score.BindPlayback(_playback?.For(_tabs.Active));
        // Perf E2b (H4/H5): the one reused arrangement follows tab switches (was a fresh pane per Show).
        // Its Timeline/Header are rebuilt only when the song itself changed; Show alone keeps the old song's rows.
        if (_arrangement is not null && !ReferenceEquals(_arrangement.Timeline.Song, _tabs.Active.Project))
        {
            _arrangement.Timeline.Reload(_tabs.Active.Project);
            _arrangement.Header.Reload(_tabs.Active.Project);
        }
        _arrangement?.Show(_tabs.Active);
        _arrangement?.BindPlayback(_playback?.For(_tabs.Active));
        LearnEngine.BindSong(_tabs.Active, _playback?.For(_tabs.Active));
        _keyboard?.BindPlayback(_playback?.For(_tabs.Active), _tabs.Active, LearnEngine);
        _band?.ShowSong(_tabs.Active);
        _band?.BindPlayback(_playback?.For(_tabs.Active));
    }

    /// <summary>The window's one Keyboard Learn engine (the docked pane and the pop-out share it).</summary>
    public KeyboardLearnEngine LearnEngine { get; } = new();

    /// <summary>Wires the saved Learn settings the engine persists bar choices to (MainWindowLearnMode.Register calls it; null leaves choices unpersisted).</summary>
    public void AttachLearnSettings(AppSettings? settings, Action? saveSettings) => LearnEngine.AttachSettings(settings, saveSettings);

    private SongDocument? _watched;

    // The score shows the selected track of the shown song and follows it when another track is selected.
    private void WatchTrack(SongDocument document)
    {
        if (_watched is not null) { _watched.TrackIndexChanged -= ShowSelectedTrack; _watched.ScorePaperChanged -= ShowPaper; }
        _watched = document;
        document.TrackIndexChanged += ShowSelectedTrack;
        document.ScorePaperChanged += ShowPaper;
        ShowSelectedTrack();
    }

    // A theme change turns a default-paper song's page: the shown score follows the song's paper live.
    private void ShowPaper()
    {
        if (_watched is { } document) Score.LightPaper = !document.DarkPaper;
    }

    private void ShowSelectedTrack()
    {
        if (_watched is { } document && document.TrackIndex >= 0 && document.TrackIndex < document.Project.Tracks.Count)
        {
            var changed = Score.Editor.SelectedTrackIndex != document.TrackIndex;
            Score.ShowTrack(document.TrackIndex);
            if (changed) Selection?.RefreshMirror();
        }
    }

    public ScorePreviewView Score { get; }

    /// <summary>The window's shared selection and loop area.</summary>
    public SelectionLoopLink Selection { get; }

    /// <summary>The latest arrangement pane the dock made, or null before the dock makes one.</summary>
    public ArrangementView? Arrangement => _arrangement;

    /// <summary>True while this window records: the arrangement's Record button state, the same check its bar/lane seek gate uses.</summary>
    public bool IsRecording => _arrangement?.Transport.Recording == true;

    /// <summary>A new arrangement pane for the dock, showing the shown song and following later tab changes.</summary>
    /// <summary>Raised with each new arrangement pane, so its owners can apply saved view modes to it.</summary>
    public event Action<ArrangementView>? ArrangementCreated;

    public ArrangementView NewArrangement()
    {
        // Perf E2b (H4/H5): reuse the one arrangement pane across Shows (Detached/Shared). A fresh Timeline/Header
        // per Show rebuilt every row; the reused pane only reloads when the song itself changed (tab switch/Undo).
        if (_arrangement is not null)
        {
            var active = _tabs.Active;
            if (!ReferenceEquals(_arrangement.Document, active) || !ReferenceEquals(_arrangement.Timeline.Song, active.Project))
            {
                _arrangement.Timeline.Reload(active.Project);
                _arrangement.Header.Reload(active.Project);
                _arrangement.Show(active);
            }
            return (ArrangementView)DockPaneSetup.Detached(_arrangement);
        }
        _arrangement = new ArrangementView(_tabs.Active.Project, _tabs.Active, Score.ContextMenus, meterTick: _meterTick);
        _arrangement.BindPlayback(_playback?.For(_tabs.Active));
        Selection.AttachArrangement(_arrangement);
        ArrangementCreated?.Invoke(_arrangement);
        return _arrangement;
    }

    /// <summary>The latest Band view pane the dock made, or null before the dock makes one.</summary>
    public BandView? Band => _band;

    /// <summary>Raised with each new Band view pane, so its owners can give it the window's settings before it shows.</summary>
    public event Action<BandView>? BandCreated;

    /// <summary>A new Band view pane for the dock: the shown song's track panels, following the shown tab's playback.</summary>
    public Control NewBand()
    {
        // Perf E2b (H1/H2/H4/H5): reuse the one Band view across Shows. BandView already keeps per-song cards
        // (ShowSong) and follows tab switches via ActiveChanged; a rebuild only re-parents the same control.
        // BandCreated fires only on the first creation, so ScoreMenu/Fretboard settings are built once, not per Show.
        if (_band is not null)
        {
            if (!ReferenceEquals(_band.Song, _tabs.Active)) _band.ShowSong(_tabs.Active);
            return DockPaneSetup.Detached(_band);
        }
        _band = new BandView(_tabs.Active, Selection.AttachStaff, Selection.DetachStaff);
        _band.IsRecording = () => IsRecording;
        BandCreated?.Invoke(_band);
        _band.BindPlayback(_playback?.For(_tabs.Active));
        return _band;
    }

    /// <summary>The latest Keyboard mode pane the dock made, or null before the dock makes one.</summary>
    public KeyboardModePane? KeyboardMode => _keyboard;

    /// <summary>A new docked Keyboard mode pane for the dock, following the shown tab's playback; its Pop out opens the Keyboard mode window on the same session.</summary>
    public KeyboardModePane NewKeyboardMode(Action close)
    {
        _keyboard = new KeyboardModePane(OpenKeyboardWindow, close);
        _keyboard.BindPlayback(_playback?.For(_tabs.Active), _tabs.Active, LearnEngine);
        return _keyboard;
    }

    private void OpenKeyboardWindow()
    {
        var window = _tools.OpenOrActivate("Keyboard pop-out", null, () => new KeyboardModePopoutWindow(), null);
        window.BindPlayback(_playback?.For(_tabs.Active), _tabs.Active, LearnEngine);
    }
}
