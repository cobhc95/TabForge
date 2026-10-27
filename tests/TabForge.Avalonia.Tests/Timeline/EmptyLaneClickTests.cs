using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Menus;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: what a click on EMPTY take-lane space shows: a tab track's lane follows the setting "Clicking an empty take lane shows" (Tab: the score, Piano roll: the roll on that
//   lane even with no take, the first note written creating a one-bar MIDI take as one undo step); an audio/MIDI track's main row is a take lane (no per-bar hover cells, taller than a
//   tab row) and a click on it always opens the roll; the take-lane menu holds the same choice; the setting is stored, searchable and mirrored by the menu.
// Does not own: the take click itself (TakeClickAutomaticViewTests), the roll drawing or the lane source maths (PianoRollLaneSourceTests).
// Tests: EditorPaneSwitch.ShowEmptyLane, TimelineInputController.EmptyLaneClicked, PianoRollClipSource (strict lane), TimelineView.LaneBandsOf, TimelineMenus.TakeLane.
public sealed class EmptyLaneClickTests
{
    private sealed record Rig(MainWindow Window, ArrangementView Arrangement, PianoRollView Roll, ScorePreviewView Score, SongDocument Document, AudioClip Take);

    private static MainWindowSettings Settings(string choice)
    {
        var settings = MainWindowSettings.Open(Path.Combine(Path.GetTempPath(), "tf-emptylane-" + Guid.NewGuid().ToString("N"), "settings.json"));
        settings.Settings.Editing.EmptyLaneClick = choice;
        return settings;
    }

    // Row 0: the demo first track (a tab track) with a take on lane 0 over bars 2-3 and an empty lane 1; row 1: an audio/MIDI track (with no clip unless asked).
    private static Rig Open(MainWindowSettings settings, bool audioClip = false)
    {
        var document = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        var project = document.Project;
        var span = BarRangeGaps.Span(project, 2, 3).GetValueOrDefault();
        var take = new AudioClip { File = "", Name = "take", StartSec = span.From, SourceLengthSec = span.To - span.From, FileLengthSec = span.To - span.From, Notes = new List<ClipNote> { new(0, 0.2, 60, 100) } };
        project.Tracks[0].AudioClips.Add(take);
        ClipLanes.Ensure(project.Tracks[0], 2);
        var audio = NewTrackFactory.CreateTrack(project, TrackKind.Audio);
        project.Tracks.Remove(audio);
        project.Tracks.Insert(1, audio);
        if (audioClip) audio.AudioClips.Add(new AudioClip { Name = "notes", Notes = new List<ClipNote> { new(0, 0.2, 62, 100) }, StartSec = span.From, SourceLengthSec = span.To - span.From, FileLengthSec = span.To - span.From });
        var window = new MainWindow(settings, first: document) { Width = 1500, Height = 1000 };
        window.Show();
        var arrangement = window.GetLogicalDescendants().OfType<ArrangementView>().Single();
        arrangement.Timeline.Reload(project);
        Settle(window);
        document.MarkClean();
        return new Rig(window, arrangement, window.GetLogicalDescendants().OfType<PianoRollView>().Single(), window.GetLogicalDescendants().OfType<ScorePreviewView>().Single(), document, take);
    }

    private static void Settle(MainWindow window)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
    }

    private static Point InLane(Rig rig, int row, int lane, int bar) => new(rig.Arrangement.Timeline.BarX(bar) + 3, rig.Arrangement.Timeline.LaneBandTopOf(row, lane) + ClipLanes.AudioLaneHeight / 2);

    private static void Click(Rig rig, Point onTimeline)
    {
        rig.Window.UpdateLayout();
        var at = rig.Arrangement.Timeline.TranslatePoint(onTimeline, rig.Window) ?? default;
        rig.Window.MouseDown(at, MouseButton.Left);
        rig.Window.MouseUp(at, MouseButton.Left);
        Settle(rig.Window);
    }

    // The view switch adds no undo step (a click on a tab cell may add its own Highlight step: that is the highlight, not the switch).
    private static void Quiet(Rig rig, int steps, bool highlight = false)
    {
        Assert.False(rig.Document.Project.IsDirty);
        if (highlight) Assert.DoesNotContain("Show in", rig.Document.Undo.Latest?.Label ?? "");
        else Assert.Equal(steps, rig.Document.Undo.UndoCount);
        Assert.All(rig.Document.Project.Tracks, track => Assert.Equal(TrackEditorView.Auto, track.EditorView));
    }

    private static Task<bool> AddNote(Rig rig, double startBeat, int pitch) =>
        rig.Roll.Presenter.CommitAsync(PianoRollEdits.Add(rig.Roll.Presenter.Notes, new PianoRollNote(startBeat, 1, pitch, 100)), PianoRollEditKind.Notes, "Add note");

    // ---- A: a tab track empty take lane ----

    [AvaloniaFact]
    public void WithTheDefaultTabChoiceAClickOnAnEmptyLaneShowsTheScoreAndNeverTheRoll()
    {
        using var settings = Settings(EmptyLaneChoice.Tab);
        var rig = Open(settings);
        var steps = rig.Document.Undo.UndoCount;
        Click(rig, new Point(rig.Arrangement.Timeline.BarX(2) + 5, rig.Arrangement.Timeline.LaneBandTopOf(0, 0) + 10));   // the take: the roll opens (automatic)
        Assert.True(rig.Roll.IsVisible);

        Click(rig, InLane(rig, 0, 1, 6));
        Assert.False(rig.Roll.IsVisible);
        Assert.True(rig.Score.IsVisible);
        Click(rig, InLane(rig, 0, 0, 8));   // empty space of a lane that has a take, away from it
        Assert.False(rig.Roll.IsVisible);
        Quiet(rig, steps);
        rig.Window.Close();
    }

    [AvaloniaFact]
    public void WithTheRollChoiceAClickOnAnEmptyLaneShowsTheRollOnThatLaneEvenWithNoTakeAndSavesNothing()
    {
        using var settings = Settings(EmptyLaneChoice.PianoRoll);
        var rig = Open(settings);
        var steps = rig.Document.Undo.UndoCount;

        Click(rig, InLane(rig, 0, 1, 6));
        Assert.True(rig.Roll.IsVisible);
        Assert.False(rig.Roll.Presenter.IsTab);
        Assert.Equal(PianoRollSourceChoice.OnFreshLane(1), rig.Roll.Presenter.Choice);
        Assert.Equal("Editing: MIDI lane 2 (no takes)", rig.Roll.Presenter.SourceLabel);
        Assert.Empty(rig.Roll.Presenter.Notes);   // only that lane: not the other lane take
        Quiet(rig, steps);
        Assert.DoesNotContain(rig.Document.Project.Tracks[0].AudioClips, clip => clip.Lane == 1);

        Click(rig, InLane(rig, 0, 0, 8));   // a lane that has a take: an ordinary lane choice
        Assert.True(rig.Roll.IsVisible);
        Assert.Equal(PianoRollSourceChoice.OnLane(0), rig.Roll.Presenter.Choice);
        Click(rig, new Point(rig.Arrangement.Timeline.BarX(6) + 5, rig.Arrangement.Timeline.RowTop(0) + 10));   // the tab lane: the score again
        Assert.False(rig.Roll.IsVisible);
        Quiet(rig, steps, highlight: true);
        rig.Window.Close();
    }

    [AvaloniaFact]
    public async Task TheFirstNoteWrittenOnAnEmptyLaneCreatesAOneBarTakeThereAsOneUndoStep()
    {
        using var settings = Settings(EmptyLaneChoice.PianoRoll);
        var rig = Open(settings);
        Click(rig, InLane(rig, 0, 1, 6));
        var presenter = rig.Roll.Presenter;
        var bar = presenter.Map.Bars[5];
        var steps = rig.Document.Undo.UndoCount;

        Assert.True(await AddNote(rig, bar.StartBeat + 1, 64));
        Settle(rig.Window);

        var one = Assert.Single(rig.Document.Project.Tracks[0].AudioClips, clip => clip.IsMidi && clip.Lane == 1);
        Assert.Equal(steps + 1, rig.Document.Undo.UndoCount);
        Assert.Equal(bar.StartSec, one.StartSec, 4);
        Assert.Equal(bar.EndSec - bar.StartSec, one.SourceLengthSec, 4);   // one bar, at the note bar
        var note = Assert.Single(one.Notes!);
        Assert.Equal(64, note.Pitch);
        Assert.Equal(PianoRollSourceChoice.OnLane(1), presenter.Choice);   // from now on an ordinary lane
        Assert.True(rig.Roll.IsVisible);   // the roll stays on the lane the take now sits on

        DocumentEdits.Undo(rig.Document);   // one step undoes the take
        Settle(rig.Window);
        Assert.DoesNotContain(rig.Document.Project.Tracks[0].AudioClips, clip => clip.Lane == 1);
        rig.Window.Close();
    }

    [AvaloniaFact]
    public async Task ANoteWrittenPastTheBarGrowsTheNewTakeByTheExistingRule()
    {
        using var settings = Settings(EmptyLaneChoice.PianoRoll);
        var rig = Open(settings);
        Click(rig, InLane(rig, 0, 1, 6));
        var bar = rig.Roll.Presenter.Map.Bars[5];
        var beyond = rig.Roll.Presenter.Map.Bars[6];

        Assert.True(await AddNote(rig, bar.StartBeat, 60));
        Assert.True(await AddNote(rig, beyond.StartBeat + 1, 62));   // outside the take: it grows (the nearest take of the lane takes it)

        var one = Assert.Single(rig.Document.Project.Tracks[0].AudioClips, clip => clip.IsMidi && clip.Lane == 1);
        Assert.True(one.EndSec > beyond.StartSec);
        Assert.Equal(2, one.Notes!.Count);
        rig.Window.Close();
    }

    [AvaloniaFact]
    public void AnAudioTracksMainRowIsALaneTallerThanATabRowWithNoHoverCellsAndNoTabCells()
    {
        using var settings = Settings(EmptyLaneChoice.Tab);
        var rig = Open(settings);
        var view = rig.Arrangement.Timeline;
        var tests = new TimelineHitTester(view);
        var inAudio = new Point(view.BarX(4) + 5, view.RowTop(1) + 10);
        Assert.True(view.RowHeightOf(1) > view.RowHeight);
        Assert.Equal(ClipLanes.AudioLaneHeight, view.RowHeightOf(1));
        var hover = tests.HoverAt(inAudio);
        Assert.Equal(1, hover.Row);
        Assert.Equal(-1, hover.Bar);   // a tab track shows its per-bar box here; an audio row shows none
        Assert.True(tests.HoverAt(new Point(view.BarX(4) + 5, view.RowTop(0) + 10)).Bar >= 0);
        Assert.Null(view.TabCellAt(inAudio.X, inAudio.Y));
        Assert.Equal((1, 0), TimelineAudioClips.LaneAt(view, inAudio));
        Assert.True(tests.Hit(inAudio, view).OnLanes);
        rig.Window.Close();
    }

    [AvaloniaFact]
    public async Task AClickOnAnAudioTracksRowAlwaysOpensTheRollOnItsLaneWhateverTheSettingAndWritingThereCreatesATake()
    {
        using var settings = Settings(EmptyLaneChoice.Tab);   // an audio track has no tab: the setting does not apply
        var rig = Open(settings);
        var steps = rig.Document.Undo.UndoCount;

        Click(rig, new Point(rig.Arrangement.Timeline.BarX(5) + 4, rig.Arrangement.Timeline.RowTop(1) + 12));
        Assert.True(rig.Roll.IsVisible);
        Assert.Equal(1, rig.Document.TrackIndex);
        Assert.Equal(PianoRollSourceChoice.OnFreshLane(0), rig.Roll.Presenter.Choice);
        Quiet(rig, steps);
        var bar = rig.Roll.Presenter.Map.Bars[5];
        Assert.True(await AddNote(rig, bar.StartBeat, 67));
        var made = Assert.Single(rig.Document.Project.Tracks[1].AudioClips);
        Assert.True(made.IsMidi);
        Assert.Equal(0, made.Lane);
        Assert.Equal(steps + 1, rig.Document.Undo.UndoCount);
        Assert.Equal(bar.StartSec, made.StartSec, 4);
        Settle(rig.Window);
        Click(rig, new Point(rig.Arrangement.Timeline.BarX(6) + 5, rig.Arrangement.Timeline.RowTop(0) + 10));   // the tab track own tab lane: the score
        Assert.Equal(0, rig.Document.TrackIndex);
        Assert.False(rig.Roll.IsVisible);
        rig.Window.Close();
    }

    [AvaloniaFact]
    public void ClipsOnAnAudioRowKeepTheirBehaviourAndARightClickOnEmptySpaceOpensTheLaneMenuNotTheBarMenu()
    {
        using var settings = Settings(EmptyLaneChoice.Tab);
        var rig = Open(settings, audioClip: true);
        var view = rig.Arrangement.Timeline;
        var clip = rig.Document.Project.Tracks[1].AudioClips[0];
        var box = TimelineAudioClips.ClipBounds(view, rig.Document.Project.Tracks[1], clip);
        var onClipAt = new Point((box.Left + box.Right) / 2, (box.Top + box.Bottom) / 2);
        Click(rig, onClipAt);
        Assert.True(rig.Roll.IsVisible);
        Assert.Equal(PianoRollSourceChoice.OnLane(0), rig.Roll.Presenter.Choice);
        Assert.Same(clip, view.SelectedClip?.Clip);

        var empty = ContextMenuRows.TimelineAt(view, new Point(view.BarX(9) + 4, view.RowTop(1) + 12)).ToList();
        var commands = TimelineMenus.Leaves(empty).Select(row => row.Command).ToList();
        Assert.Contains(TimelineCommand.LanePasteClip, commands);
        Assert.Contains(TimelineCommand.LaneRecordHere, commands);
        Assert.DoesNotContain(TimelineCommand.CopyBar, commands);
        Assert.DoesNotContain(TimelineCommand.PasteBar, commands);
        Assert.DoesNotContain(TimelineMenus.Leaves(empty), row => row.Id == TimelineMenus.EmptyLaneClickId);   // no tab: no choice to make
        var onClip = TimelineMenus.Leaves(ContextMenuRows.TimelineAt(view, onClipAt)).Select(row => row.Command).ToList();
        Assert.Contains(TimelineCommand.ClipCopy, onClip);
        rig.Window.Close();
    }

    [AvaloniaFact]
    public void TheTakeLaneMenuOfATabTrackHoldsTheChoiceAndPickingARowWritesTheStoredSetting()
    {
        using var settings = Settings(EmptyLaneChoice.Tab);
        var rig = Open(settings);
        var view = rig.Arrangement.Timeline;
        var at = InLane(rig, 0, 1, 6);

        var rows = TimelineMenus.Leaves(ContextMenuRows.TimelineAt(view, at)).Where(row => row.Id == TimelineMenus.EmptyLaneClickId).ToList();
        Assert.Equal(new[] { "Tab", "Piano roll" }, rows.Select(r => r.Header).ToArray());
        Assert.Equal(new[] { true, false }, rows.Select(r => r.Checked).ToArray());
        Assert.All(rows, row =>
        {
            Assert.True(row.Radio);
            Assert.Equal("editing.emptylane", row.SettingKey);
            Assert.True(view.ContextMenus.Commands.Handles(ContextMenuRows.CommandIdOf(row)!));
        });

        view.ContextMenus.Commands.Run(TimelineMenus.EmptyLaneClickId, EmptyLaneChoice.PianoRoll);
        Assert.Equal(EmptyLaneChoice.PianoRoll, settings.Settings.Editing.EmptyLaneClick);
        var again = TimelineMenus.Leaves(ContextMenuRows.TimelineAt(view, at)).Where(row => row.Id == TimelineMenus.EmptyLaneClickId).ToList();
        Assert.Equal(new[] { false, true }, again.Select(r => r.Checked).ToArray());
        Click(rig, at);   // and the next click uses it
        Assert.True(rig.Roll.IsVisible);
        rig.Window.Close();
    }

    [Fact]
    public void TheSettingDefaultsToTabRoundTripsInTheSettingsFileAndIsOnePreferencesRowThatSearchFinds()
    {
        Assert.Equal("Tab", new EditingSettings().EmptyLaneClick);
        var settings = new AppSettings();
        settings.Editing.EmptyLaneClick = EmptyLaneChoice.PianoRoll;
        var back = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Assert.Equal("Piano roll", back.Editing.EmptyLaneClick);
        Assert.Equal("Tab", JsonSerializer.Deserialize<EditingSettings>("{}")!.EmptyLaneClick);   // an older file keeps the default
        Assert.Equal(EmptyLaneChoice.Tab, EmptyLaneChoice.Normalize("nonsense"));

        var row = SettingsCatalog.Build(settings).Single(r => r.Key == "editing.emptylane");
        Assert.Equal(SettingsCatalog.Editing, row.Category);
        Assert.Equal("Clicking an empty take lane shows", row.Title);
        Assert.Equal(new[] { "Tab", "Piano roll" }, row.Choices.ToArray());
        Assert.Equal("Piano roll", row.Get());
        row.Set("Tab");
        Assert.Equal(EmptyLaneChoice.Tab, settings.Editing.EmptyLaneClick);   // the menu rows and this row are the one stored value

        var entries = SettingsCatalog.Registry(SettingsCatalog.Build(new AppSettings()), new HotkeySettings());
        foreach (var query in new[] { "lane", "piano roll", "empty" })
            Assert.Contains(entries, entry => entry.Id == "editing.emptylane" && SettingsCatalog.Matches(entry, query));
    }
}
