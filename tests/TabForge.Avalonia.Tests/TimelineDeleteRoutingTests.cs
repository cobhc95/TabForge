using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Menus;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the Delete routing of the arrangement by the focus target (clip, selected bars, track row; the score keeps its own keys), the Range.Delete decision
/// (fake prompt) applying Leave empty / Remove+close gap as one undo step, the timeline focus that selects the context, and the enabled
/// selection-menu rows.
/// Does not own: the Delete-bars window (PasteOptionsSpecialAndDeleteBarsTests), the gap rules (BarRangeGaps), the clip deletion itself
/// (RecordingLaneLifecycleTests / ClipEditingBatchFourTests) or the score editor's keys (ScorePreviewEditorTests).
/// Tests: this class.
/// </summary>
public sealed class TimelineDeleteRoutingTests
{
    private static TrackModel AudioTrackWithClip(SongDocument song)
    {
        var track = NewTrackFactory.CreateTrack(song.Project, TrackKind.Audio);
        track.AudioClips.Add(new AudioClip { File = "take.wav", Name = "take", StartSec = 0, SourceLengthSec = 2, FileLengthSec = 2 });
        song.Project.Tracks.Add(track);
        return track;
    }

    // The focus target decides which Delete runs, not a fixed clip -> range -> track row cascade: the old cascade let a leftover clip or bar range
    // shadow a track row the user had just clicked (the bug this stage fixes), so each context is now reached by focusing it.
    [AvaloniaFact]
    public void DeleteRoutesByTheFocusTargetNotByACascade()
    {
        var song = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        var track = AudioTrackWithClip(song);
        var clip = track.AudioClips[0];
        var view = new TimelineView(song.Project);
        var model = new SelectionModel();
        var commands = new MainWindowCommands(new Window(), () => { });
        var ran = new List<string>();
        var clipSelected = true;
        commands.AddWhen("Clip.Delete", () => ran.Add("clip"), () => clipSelected);
        commands.AddWhen("Range.Delete", () => ran.Add("range"), () => model.HasRange);
        commands.AddWhen("TrackRow.Delete", () => ran.Add("row"), () => model.Focus == FocusTarget.TrackRow);
        var keyboard = new MainWindowKeyboard(new HotkeySettings(), commands, focus: () => model.Focus);

        view.SelectedClip = new TimelineClipSelection(track, clip);
        model.SetRange(0, 1, 2, SelectionOrigin.Timeline);
        model.SetFocus(FocusTarget.Clips);
        Assert.True(keyboard.Route(Key.Delete, KeyModifiers.None, view));
        Assert.Equal("clip", Assert.Single(ran));

        // Every context is selected at once; the focus alone picks the command.
        model.SetFocus(FocusTarget.Loop);
        Assert.True(keyboard.Route(Key.Delete, KeyModifiers.None, view));
        Assert.Equal("range", ran[^1]);

        model.SetFocus(FocusTarget.TrackRow);
        Assert.True(keyboard.Route(Key.Delete, KeyModifiers.None, view));
        Assert.Equal("row", ran[^1]);

        // The same keys with no timeline focus do not use the timeline's contexts.
        ran.Clear();
        Assert.False(keyboard.Route(Key.Delete, KeyModifiers.None, new TextBox()));
        Assert.Empty(ran);
    }

    [AvaloniaFact]
    public async Task RangeDeleteAsksAndAppliesLeaveEmptyAndRemoveAsOneUndoStep()
    {
        var actions = new[] { BarRangeAction.Clear, BarRangeAction.Remove };
        foreach (var action in actions)
        {
            var song = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
            var barsBefore = song.Project.Tracks[0].Measures.Count;
            var settings = new EditingSettings();
            var asked = "";
            MainWindowTimelineBars.DeletePrompt ask = (question, preselect, allTracks) =>
            {
                asked = question;
                return Task.FromResult<BarRangeAnswer?>(new BarRangeAnswer(action, allTracks, Remember: false));
            };

            var result = await MainWindowTimelineBars.DeleteRange(song, 1, 2, 0, allTracksDefault: true, _ => { }, ask, () => settings, () => { });

            Assert.Equal(action, result);
            Assert.Contains("Bars 2-3 are selected", asked);
            var expected = action switch
            {
                BarRangeAction.Remove => barsBefore - 2,
                _ => barsBefore,
            };
            Assert.Equal(expected, song.Project.Tracks[0].Measures.Count);
            Assert.NotNull(DocumentEdits.Undo(song));
            Assert.Equal(barsBefore, song.Project.Tracks[0].Measures.Count);
        }
    }

    [AvaloniaFact]
    public void DeleteOnTheTimelineDeletesASelectedClipAndUndoRestoresIt()
    {
        var song = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        var track = AudioTrackWithClip(song);
        var clip = track.AudioClips[0];
        var window = new MainWindow(first: song);
        window.Show();
        window.UpdateLayout();
        var arrangement = window.GetLogicalDescendants().OfType<ArrangementView>().First();
        var timeline = arrangement.Timeline;
        timeline.SelectedClip = new TimelineClipSelection(track, clip);
        timeline.Focus();

        window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Delete, Source = window });

        Assert.Empty(track.AudioClips);
        Assert.NotNull(DocumentEdits.Undo(song));
        Assert.Single(song.Project.Tracks.Single(candidate => candidate.Name == track.Name).AudioClips);
        song.MarkClean();   // closing a dirty window asks to save
        window.Close();
    }

    [AvaloniaFact]
    public void UndoAfterDeleteReloadsTheTimelineSoTheClipIsSeenAgain()
    {
        var song = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        var track = AudioTrackWithClip(song);
        var clip = track.AudioClips[0];
        var window = new MainWindow(first: song);
        window.Show();
        window.UpdateLayout();
        var arrangement = window.GetLogicalDescendants().OfType<ArrangementView>().First();
        var timeline = arrangement.Timeline;
        timeline.SelectedClip = new TimelineClipSelection(track, clip);
        timeline.Focus();

        window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Delete, Source = window });
        Assert.Empty(track.AudioClips);

        // Ctrl+Z undoes through the window: the engine re-syncs and the arrangement reloads the restored song.
        window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Z, KeyModifiers = KeyModifiers.Control, Source = window });

        var restored = timeline.Song.Tracks.Single(candidate => candidate.Name == track.Name);
        var restoredClip = Assert.Single(restored.AudioClips);
        Assert.Equal(0, restoredClip.StartSec, 6);
        song.MarkClean();
        window.Close();
    }

    [AvaloniaFact]
    public void DeleteWithATrackRowSelectedDeletesThatTrackAfterThePrompt()
    {
        var song = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        while (song.Project.Tracks.Count < 2) MainWindowTrackMenu.AddTrack(song, TrackKind.Audio);
        var before = song.Project.Tracks.Count;
        var selected = 1;
        var window = new MainWindow(first: song);
        window.Show();
        window.UpdateLayout();
        var arrangement = window.GetLogicalDescendants().OfType<ArrangementView>().First();
        var timeline = arrangement.Timeline;
        song.TrackIndex = selected;
        timeline.ContextMenus.ClickedTrack = selected;
        timeline.TrackRowFocused = true;
        timeline.Focus();

        window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Delete, Source = window });
        Dispatcher.UIThread.RunJobs();

        var prompt = Assert.Single(window.OwnedWindows.OfType<PromptWindow>());
        var delete = prompt.GetLogicalDescendants().OfType<Button>().Single(button => (button.Content as string) == "Delete");
        delete.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(before - 1, song.Project.Tracks.Count);
        Assert.NotNull(DocumentEdits.Undo(song));
        Assert.Equal(before, song.Project.Tracks.Count);
        song.MarkClean();
        window.Close();
    }

    [AvaloniaFact]
    public void TheScoreKeepsItsOwnDeleteWhileABarRangeIsSelected()
    {
        var song = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        var model = new SelectionModel();
        model.SetRange(0, 1, 2, SelectionOrigin.Timeline);
        var commands = new MainWindowCommands(new Window(), () => { });
        var rangeRan = false;
        commands.AddWhen("Range.Delete", () => rangeRan = true, () => model.HasRange);
        var keyboard = new MainWindowKeyboard(new HotkeySettings(), commands);
        var score = new ScorePreviewView();
        score.Show(song);
        var host = new Window { Content = score };
        host.Show();
        score.Focus();

        var key = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Delete, Source = score };
        score.RaiseEvent(key);

        Assert.True(key.Handled);       // the score's editor handled Delete itself
        Assert.False(rangeRan);         // and the window's Range.Delete never stole it
        host.Close();
    }

    [AvaloniaFact]
    public void TheSelectionMenuEnablesDeleteLoopAndClearAndLeavesTheUnimplementedRowsDisabled()
    {
        var rows = TimelineMenus.Leaves(TimelineMenus.Selection(new SelectionMenuState("Bars 2-3 selected", CanPaste: true, Looping: true, Skipped: false, AnySkipped: false), _ => "")).ToList();
        Assert.Equal("Range.Delete", ContextMenuRows.CommandIdOf(rows.Single(row => row.Command == TimelineCommand.DeleteSelection)));
        Assert.Equal("Transport.Loop", ContextMenuRows.CommandIdOf(rows.Single(row => row.Command == TimelineCommand.LoopSelection)));
        Assert.Equal("Transport.ClearLoopArea", ContextMenuRows.CommandIdOf(rows.Single(row => row.Command == TimelineCommand.ClearSelection)));
        Assert.Null(ContextMenuRows.CommandIdOf(rows.Single(row => row.Command == TimelineCommand.MoveSelection)));
        Assert.Null(ContextMenuRows.CommandIdOf(rows.Single(row => row.Command == TimelineCommand.SkipSelection)));

        var handled = new[]
        {
            "Edit.Copy", "Edit.Cut", "Edit.Paste", "Range.Delete", "Transport.Loop", "Transport.ClearLoopArea", TrackListMenus.SettingsId,
        };
        var runner = new RowsRunner(handled);
        var menu = ContextMenuBuilder.Build(rows, runner);
        var items = Leaves(menu.Items).Where(item => item.Header?.ToString() is { Length: > 0 }).ToList();
        Assert.True(items.Single(item => item.Header?.ToString() == "Delete…").IsEnabled);
        Assert.True(items.Single(item => item.Header?.ToString() == "Loop").IsEnabled);
        Assert.True(items.Single(item => item.Header?.ToString() == "Clear loop").IsEnabled);
        Assert.False(items.Single(item => item.Header?.ToString() == "Move selection…").IsEnabled);   // Arrange rows are not implemented
        Assert.False(items.Single(item => item.Header?.ToString() == "Skip during playback").IsEnabled);
    }

    private sealed class RowsRunner : IMenuCommandRunner
    {
        private readonly HashSet<string> _handled;
        public RowsRunner(IEnumerable<string> handled) => _handled = new HashSet<string>(handled, StringComparer.Ordinal);
        public bool Handles(string commandId) => _handled.Contains(commandId);
        public void Run(string commandId, string? argument) { }
    }

    private static IEnumerable<MenuItem> Leaves(IEnumerable items)
    {
        foreach (var item in items.OfType<MenuItem>())
        {
            if (item.Items.OfType<MenuItem>().Any())
                foreach (var leaf in Leaves(item.Items)) yield return leaf;
            else yield return item;
        }
    }
}
