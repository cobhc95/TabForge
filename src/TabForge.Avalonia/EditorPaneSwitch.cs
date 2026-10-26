using System;
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
//       the choice is saved with it; and the piano roll's window wiring (playback, the transport's snap, audition, the loop, the prompts, settings and
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
    private SongDocument? _watched;
    private (SongDocument? Song, int Track, bool Roll) _shown;

    public EditorPaneSwitch(ScrollViewer score, ScorePreviewView scoreView, MainWindowTabs songs, MainWindowDocumentPanes panes,
        AvaloniaPlaybackSessions? playback, MainWindowSettings? settings, Func<Window?> owner, Action<string> report)
    {
        _score = score;
        _scoreView = scoreView;
        _songs = songs;
        _panes = panes;
        PianoRoll = new PianoRollView(new PianoRollHost
        {
            Document = () => songs.Songs.Count == 0 ? null : songs.Active,
            Session = () => songs.Songs.Count == 0 ? null : playback?.For(songs.Active),
            Snap = () => panes.Arrangement?.Transport.Snap,
            ToggleSnap = () => panes.Arrangement?.Transport.ToggleSnap(),
            OpenSnapSettings = () => panes.Arrangement?.Transport.OpenSnapSettings(),
            Audition = (track, pitch) =>
            {
                if (playback is null || track.IsAudio) return;
                UiTasks.Forget(playback.PlayTestNoteAsync(track.MidiOutputDeviceId, track.MidiChannel, track.MidiProgram, pitch,
                    settings?.Settings.Audio.PreviewLengthMs ?? new AudioSettings().PreviewLengthMs), "Note preview", report);
            },
            Editing = () => settings?.Settings.Editing ?? _fallbackEditing,
            SaveSettings = () => settings?.SaveSettingsQuietly(),
            MakeLoop = (start, startCell, end, endCell, fromSelection) => panes.Selection.LoopPianoRollSpan(start, end, fromSelection, startCell, endCell),
            AfterEdit = AfterEdit,
            Commands = _commands,
            ConfirmTab = name => ConfirmTab(owner(), name),
            AskText = (title, label, initial) => AskText(owner(), title, label, initial),
            Report = report,
            MidiInput = () => playback?.Engine?.MidiInput,
            ScrollTimeline = seconds => panes.Arrangement?.ScrollToSeconds(seconds),
            ClearLoop = () => { if (songs.Songs.Count > 0) DocumentEdits.EditLoop(songs.Active, model => model.ClearLoop(SelectionOrigin.Timeline), "Clear loop"); },
        }) { IsVisible = false };
        Host.Children.Add(score);
        Host.Children.Add(PianoRoll);
        // Retired 26 Oct (tombstone): the pills floated over both views' top right; MainWindowMenu docks them in the menu row now.
        Pills.Picked += piano => SetView(piano ? TrackEditorView.PianoRoll : TrackEditorView.Score);
        songs.ActiveChanged += () => Watch(songs.Active);
        if (panes.Arrangement is { } arrangement) Follow(arrangement);
        panes.ArrangementCreated += Follow;
        Watch(songs.Active);
    }

    /// <summary>The Piano / Tab pills the menu row shows (disabled for no track or a track the piano roll cannot show).</summary>
    public EditorModePills Pills { get; } = new();

    /// <summary>Raised when the pane switches between score (false) and piano roll (true); the window hides or shows the instrument panel.</summary>
    public Action<bool>? ModeChanged { get; set; }

    // The piano roll follows an arrangement's sideways scroll and its transport's snap magnet (each wired once per arrangement).
    private void Follow(ArrangementView arrangement)
    {
        arrangement.Transport.SnapChanged -= PianoRoll.RefreshSnap;
        arrangement.Transport.SnapChanged += PianoRoll.RefreshSnap;
        arrangement.SidewaysScrolled -= OnTimelineScrolled;
        arrangement.SidewaysScrolled += OnTimelineScrolled;
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
        commands.Add(ScoreMenus.ShowInPianoRollId, () => SetView(TrackEditorView.PianoRoll));
        commands.Add(PianoRollMenus.ShowInScoreId, () => SetView(TrackEditorView.Score));
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

    /// <summary>Shows the score or the piano roll for the shown song's selected track; the piano roll is told only when its song, track or visit changed.</summary>
    public void Update()
    {
        if (_songs.Songs.Count == 0) return;
        var document = _songs.Active;
        var index = document.TrackIndex;
        var track = index >= 0 && index < document.Project.Tracks.Count ? document.Project.Tracks[index] : null;
        var roll = track is not null && PianoRollViewRules.ShowsPianoRoll(track);
        Pills.IsEnabled = track is not null && PianoRollViewRules.CanShow(track);
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
        if (modeChanged) ModeChanged?.Invoke(roll);
    }

    /// <summary>Saves the view choice on the selected track as one undo step (the song is marked changed, so the choice is saved), then switches.</summary>
    internal void SetView(TrackEditorView view)
    {
        if (_songs.Songs.Count == 0) return;
        var document = _songs.Active;
        var index = document.TrackIndex;
        if (index < 0 || index >= document.Project.Tracks.Count) return;
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
