using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Transport;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Documents;
using TabForge.Menus;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: the score pane's one slot holding either the score or the piano roll for the selected track (TrackModel.EditorView through
//       PianoRollViewRules: MIDI tracks and takes default to the piano roll, tab tracks to the score), following the shown song, the selected track,
//       undo and edits; the two switch commands (Score.ShowInPianoRoll, PianoRoll.ShowInScore), each one undo step that marks the song changed so
//       the choice is saved with it; the automatic switch a MIDI take click makes (the roll on that take's lane until the track's header or tab lane is
//       clicked, another track is picked or the lane's last take is deleted; never saved, no undo step), and the same automatic roll for a click on empty take-lane
//       space (an audio/MIDI track always; a tab track when Preferences' "Clicking an empty take lane shows" says Piano roll) on a lane that may hold no take yet
//       (its first note creates one); and the piano roll's window wiring (playback, the transport's snap, audition, the loop, the prompts, settings and
//       the refresh after its edits).
// Does not own: the score view or the piano roll themselves (ScorePreviewView, PianoRollView), the dock (DockPaneSetup), the loop rules (SelectionLoopLink).
// Tests: TabForge.Avalonia.Tests.PianoRollViewTests.
public sealed class EditorPaneSwitch
{
    private readonly ScrollViewer _score;
    private readonly ScorePreviewView _scoreView;
    private readonly MainWindowTabs _songs;
    private readonly MainWindowDocumentPanes _panes;
    private readonly LateCommands _commands = new();
    private readonly EditingSettings _fallbackEditing = new();
    private readonly FollowSettings _fallbackFollow = new();
    private SongDocument? _watched;
    private (SongDocument? Song, int Track, bool Roll) _shown;
    private (SongDocument Song, int Track)? _auto;   // the track a MIDI take click opened in the piano roll without saving a view choice
    private int? _autoLane;   // an empty take lane the click opened (no take on it yet); its first take, once written, becomes _autoClips
    private readonly MainWindowSettings? _settings;
    private HashSet<string> _autoClips = new();   // the ids of the takes on the lane that click opened (the lane by identity: lanes renumber when one is deleted); when all are deleted the roll is over

    public EditorPaneSwitch(ScrollViewer score, ScorePreviewView scoreView, MainWindowTabs songs, MainWindowDocumentPanes panes,
        AvaloniaPlaybackSessions? playback, MainWindowSettings? settings, Func<Window?> owner, Action<string> report)
    {
        _score = score;
        _settings = settings;
        _scoreView = scoreView;
        _songs = songs;
        _panes = panes;
        PianoRoll = new PianoRollView(new PianoRollHost
        {
            Document = () => songs.Songs.Count == 0 ? null : songs.Active,
            Session = () => songs.Songs.Count == 0 ? null : playback?.For(songs.Active),
            Snap = () => panes.Arrangement?.Transport.Snap,
            ToggleSnap = () => panes.Arrangement?.Transport.ToggleSnap(),
            Hotkeys = () => settings?.Settings.Hotkeys ?? new HotkeySettings(),
            OpenSnapSettings = () => panes.Arrangement?.Transport.OpenSnapSettings(),
            Audition = (track, pitch) =>
            {
                if (playback is null || track.IsAudio) return;
                UiTasks.Forget(playback.PlayTestNoteAsync(track.MidiOutputDeviceId, track.MidiChannel, track.MidiProgram, pitch,
                    settings?.Settings.Audio.PreviewLengthMs ?? new AudioSettings().PreviewLengthMs), "Note preview", report);
            },
            Editing = () => settings?.Settings.Editing ?? _fallbackEditing,
            Follow = () => settings?.Settings.Follow ?? _fallbackFollow,
            Appearance = () => settings?.Settings.Appearance ?? new AppearanceSettings(),
            SaveSettings = () => settings?.SaveSettingsQuietly(),
            MakeLoop = (start, startCell, end, endCell, fromSelection) => panes.Selection.LoopPianoRollSpan(start, end, fromSelection, startCell, endCell),
            AfterEdit = AfterEdit,
            Commands = _commands,
            ConfirmTab = name => ConfirmTab(owner(), name),
            AskText = (title, label, initial) => AskText(owner(), title, label, initial),
            Report = report,
            MidiInput = () => playback?.Engine?.MidiInput,
            MonitorSounds = track => playback?.Engine?.MidiMonitor?.Sounds(track) == true,
            ScrollTimeline = seconds => panes.Arrangement?.ScrollToSeconds(seconds),
            ClipOverlap = () => panes.Arrangement?.Timeline.ClipOverlap,
            ClearLoop = () => { if (songs.Songs.Count > 0) DocumentEdits.EditLoop(songs.Active, model => model.ClearLoop(SelectionOrigin.Timeline), "Clear loop"); },
        }) { IsVisible = false };
        Host.Children.Add(score);
        Host.Children.Add(PianoRoll);
        if (settings is not null) settings.SettingsChanged += _ => PianoRoll.RefreshNeedle();   // Preferences' "Playback needle" row
        // Retired 26 Oct (tombstone): the pills floated over both views' top right; MainWindowMenu docks them in the menu row now.
        Pills.Picked += piano => SetView(piano ? TrackEditorView.PianoRoll : TrackEditorView.Score);
        songs.ActiveChanged += () => Watch(songs.Active);
        if (panes.Arrangement is { } arrangement) Follow(arrangement);
        panes.ArrangementCreated += Follow;
        Watch(songs.Active);
    }

    /// <summary>The Piano / Tab pills the menu row shows (disabled for no track or a track the piano roll cannot show).</summary>
    public EditorModePills Pills { get; } = new();

    /// <summary>Raised when the pane switches between score (false) and piano roll (true); the window's InstrumentPanelGate hides or restores the instrument panel (raised from Update, so from every switch path).</summary>
    public Action<bool>? ModeChanged { get; set; }

    // The piano roll follows an arrangement's sideways scroll and its transport's snap magnet (each wired once per arrangement).
    private void Follow(ArrangementView arrangement)
    {
        arrangement.Transport.SnapChanged -= PianoRoll.RefreshSnap;
        arrangement.Transport.SnapChanged += PianoRoll.RefreshSnap;
        arrangement.SidewaysScrolled -= OnTimelineScrolled;
        arrangement.SidewaysScrolled += OnTimelineScrolled;
        arrangement.Input.MidiClipClicked -= ShowTakeInPianoRoll;
        arrangement.Input.MidiClipClicked += ShowTakeInPianoRoll;
        arrangement.Input.EmptyLaneClicked -= ShowEmptyLane;
        arrangement.Input.EmptyLaneClicked += ShowEmptyLane;
        arrangement.Input.ScoreRequested -= ShowScoreAgain;
        arrangement.Input.ScoreRequested += ShowScoreAgain;
    }

    // A click on a MIDI take opens its track in the piano roll AUTOMATICALLY, editing that take's lane (every take on it), never the track's tab; a click
    // while the roll already shows the track switches its source to the lane. Nothing is saved: no undo step, the song is not marked changed, and the
    // track's remembered view (TrackModel.EditorView, the user's own choice) stays as it was.
    internal void ShowTakeInPianoRoll(int row, AudioClip clip)
    {
        if (_songs.Songs.Count == 0 || row < 0 || row >= _songs.Active.Project.Tracks.Count) return;
        _songs.Active.TrackIndex = row;
        var lane = _songs.Active.Project.Tracks[row].AudioClips.Where(c => c.IsMidi && c.Lane == clip.Lane).Select(c => c.Id).ToHashSet();
        lane.Add(clip.Id);
        _autoClips = lane;
        _autoLane = null;
        SetAutomatic(true);
        if (_shown.Roll && _shown.Track == row) PianoRoll.ShowTakeLane(clip);
    }

    // A click on empty space in take lane `lane` of track `row`: an audio/MIDI track (no tab) always opens the piano roll there; a tab track does when the setting says
    // Piano roll, else its score comes back as a tab click does. A lane with no MIDI take yet opens as an empty lane: the first note written creates the take.
    // Automatic like a take click: nothing is saved, no undo step.
    internal void ShowEmptyLane(int row, int lane)
    {
        if (_songs.Songs.Count == 0 || row < 0 || row >= _songs.Active.Project.Tracks.Count) return;
        var track = _songs.Active.Project.Tracks[row];
        if (!track.IsAudio && EmptyLaneChoice.Normalize(_settings?.Settings.Editing.EmptyLaneClick ?? _fallbackEditing.EmptyLaneClick) != EmptyLaneChoice.PianoRoll) { ShowScoreAgain(row); return; }
        _songs.Active.TrackIndex = row;
        _autoClips = track.AudioClips.Where(c => c.IsMidi && c.Lane == lane).Select(c => c.Id).ToHashSet();
        _autoLane = _autoClips.Count == 0 ? lane : null;
        SetAutomatic(true);
        if (_shown.Roll && _shown.Track == row) PianoRoll.UseSource(_autoLane is null ? PianoRollSourceChoice.OnLane(lane) : PianoRollSourceChoice.OnFreshLane(lane));
    }

    // The right-click "Show in piano roll" row on a MIDI lane: the user's own choice (remembered on the track, one undo step), on that take's lane.
    internal void ShowTakeInPianoRollByChoice(int row, AudioClip clip)
    {
        if (_songs.Songs.Count == 0 || row < 0 || row >= _songs.Active.Project.Tracks.Count) return;
        _songs.Active.TrackIndex = row;
        SetView(TrackEditorView.PianoRoll);
        if (_shown.Roll && _shown.Track == row) PianoRoll.ShowTakeLane(clip);
    }

    // A click on the track's header or on its tab lane: the score comes back when only a take click had opened the piano roll (a roll the user chose stays).
    private void ShowScoreAgain(int row)
    {
        if (_songs.Songs.Count == 0 || _auto is not { } auto || auto.Song != _songs.Active || auto.Track != row) return;
        SetAutomatic(false);
    }

    // The automatic switch: the take-opened piano roll of the selected track is on or off; the pane follows without touching the song or its undo.
    private void SetAutomatic(bool roll)
    {
        var document = _songs.Active;
        _auto = roll ? (document, document.TrackIndex) : null;
        if (!roll) _autoLane = null;
        Update();
        if (roll && _shown.Roll) PianoRoll.Surface.Focus();
    }

    private void OnTimelineScrolled()
    {
        if (_shown.Roll && _panes.Arrangement is { } arrangement) PianoRoll.FollowTimeline(arrangement.LeftSeconds);
    }

    /// <summary>The control the dock places in the score pane: the score and the piano roll, one of them visible.</summary>
    public Grid Host { get; } = new();

    public PianoRollView PianoRoll { get; }

    /// <summary>True while the pane shows the piano roll.</summary>
    public bool ShowsPianoRoll => _shown.Roll;

    /// <summary>Adds the two switch commands (and gives the piano roll the window's commands for Clear loop).</summary>
    public void Register(MainWindowCommands commands)
    {
        _commands.Inner = commands;
        _panes.Selection.SelectAllFirst = () => _shown.Roll && PianoRoll.IsKeyboardFocusWithin && PianoRoll.RunEdit(null);
        _panes.Selection.InvertFirst = () => _shown.Roll && PianoRoll.IsKeyboardFocusWithin && PianoRoll.RunInvert();
        commands.Add(ScoreMenus.ShowInPianoRollId, () => SetView(TrackEditorView.PianoRoll));
        commands.Add(PianoRollMenus.ShowInScoreId, () => SetView(TrackEditorView.Score));
        commands.AddWithArgument(TimelineMenus.EmptyLaneClickId, value =>   // the take-lane menu's "Clicking an empty lane shows" rows: the Preferences value, read live by the next click
        {
            if (_settings is null) return;
            _settings.Settings.Editing.EmptyLaneClick = EmptyLaneChoice.Normalize(value);
            _settings.SaveSettingsQuietly();
        });
    }

    /// <summary>Edit.Copy, Cut, Paste and Paste special while the piano roll has focus act on its notes; true when it took the verb.</summary>
    public bool RunEdit(EditVerb verb) => _shown.Roll && PianoRoll.IsKeyboardFocusWithin && PianoRoll.RunEdit(verb);

    private void Watch(SongDocument document)
    {
        if (_watched is not null)
        {
            _watched.TrackIndexChanged -= Update;
            _watched.Restored -= Update;
            _watched.TimelineMarked -= OnTimelineMarked;
        }
        _watched = document;
        document.TrackIndexChanged += Update;
        document.Restored += Update;
        document.TimelineMarked += OnTimelineMarked;
        Update();
    }

    // An edit may turn a track into a MIDI track (a take landed) or back (notes written): Auto follows it.
    private void OnTimelineMarked(SongProject _)
    {
        if (!PianoRoll.Presenter.IsCommitting) Update();
    }

    // The lane the take click opened is followed by its takes, not its number: while any of them is left the roll stays on the lane they now sit on
    // (takes added to it join), and when the last one is deleted, even if lanes below shifted up into its number, the roll is over.
    private void FollowAutoLane(TrackModel track)
    {
        var left = track.AudioClips.FirstOrDefault(clip => clip.IsMidi && _autoClips.Contains(clip.Id));
        if (left is null && _autoLane is { } empty)
        {
            left = track.AudioClips.FirstOrDefault(clip => clip.IsMidi && clip.Lane == empty);   // the first note made the lane's take
            if (left is null) return;   // still an empty lane: the roll stays
            _autoLane = null;
        }
        if (left is null) { _auto = null; return; }
        _autoClips = track.AudioClips.Where(clip => clip.IsMidi && clip.Lane == left.Lane).Select(clip => clip.Id).ToHashSet();
    }

    /// <summary>Shows the score or the piano roll for the shown song's selected track; the piano roll is told only when its song, track or visit changed.</summary>
    public void Update()
    {
        if (_songs.Songs.Count == 0) return;
        var document = _songs.Active;
        var index = document.TrackIndex;
        var track = index >= 0 && index < document.Project.Tracks.Count ? document.Project.Tracks[index] : null;
        if (_auto is { } auto && (auto.Song != document || auto.Track != index)) { _auto = null; _autoLane = null; }   // another track or song: the take click's roll is over
        if (_auto is not null && track is not null) FollowAutoLane(track);
        var roll = track is not null && (_auto is not null || PianoRollViewRules.ShowsPianoRoll(track));   // an automatic roll may sit on an empty lane (an audio track with no MIDI take yet)
        Pills.IsEnabled = track is not null && (_auto is not null || PianoRollViewRules.CanShow(track));
        Pills.PianoShown = roll;
        var next = (document, index, roll);
        if (next == _shown) { if (roll) PianoRoll.RefreshIfStale(); return; }
        var modeChanged = roll != _shown.Roll;
        _shown = next;
        _score.IsVisible = !roll;
        PianoRoll.IsVisible = roll;
        if (roll)
        {
            PianoRoll.Show(document, index, modeChanged);
            if (_panes.Arrangement is { } arrangement) PianoRoll.FollowTimeline(arrangement.LeftSeconds);
        }
        else PianoRoll.Hidden();
        if (modeChanged) ModeChanged?.Invoke(roll);   // every path that changes the pane (pill, menu, take click, tab click, track or song change, undo) tells the window here
    }

    /// <summary>Saves the view choice on the selected track as one undo step (the song is marked changed, so the choice is saved), then switches.</summary>
    internal void SetView(TrackEditorView view)
    {
        if (_songs.Songs.Count == 0) return;
        var document = _songs.Active;
        var index = document.TrackIndex;
        if (index < 0 || index >= document.Project.Tracks.Count) return;
        _auto = null;   // the user's own choice replaces any take-click switch
        _autoLane = null;
        DocumentEdits.Run(document, project =>
        {
            var track = project.Tracks[index];
            if (track.EditorView == view) return false;
            track.EditorView = view;
            return true;
        }, invalidatesTimeline: false, label: view == TrackEditorView.PianoRoll ? "Show in piano roll" : "Show in score");
        Update();
        if (_shown.Roll) PianoRoll.Surface.Focus();
    }

    // A piano-roll edit landed: the score repaints its notes, and the timeline does its post-edit work (song growth, lane compaction, engine sync, rows).
    private void AfterEdit(bool tab)
    {
        _scoreView.InvalidateVisual();
        if (_panes.Arrangement is { } arrangement && _songs.Songs.Count > 0)
        {
            arrangement.Timeline.ClipEdited?.Invoke(_songs.Active);
            if (arrangement.Timeline.ClipEdited is null) arrangement.Timeline.InvalidateVisual();
        }
    }

    private static async Task<bool> ConfirmTab(Window? owner, string trackName)
    {
        if (owner is null) return true;
        var outcome = await PromptWindow.AskAsync(owner, PianoRollPresenter.TabPrompt(trackName));
        return PromptTextsPresenter.IsConfirmed(outcome.Answer);
    }

    private static async Task<string?> AskText(Window? owner, string title, string label, string initial) =>
        owner is null ? null : await new TextPromptWindow(title, label, initial).ShowDialog<string?>(owner);

    // The window's command table, given after the switch is built (commands are registered after the dock).
    private sealed class LateCommands : IMenuCommandRunner
    {
        public MainWindowCommands? Inner { get; set; }

        public bool Handles(string commandId) => Inner?.Handles(commandId) == true;

        public void Run(string commandId, string? argument = null) => Inner?.Run(commandId, argument);
    }
}
