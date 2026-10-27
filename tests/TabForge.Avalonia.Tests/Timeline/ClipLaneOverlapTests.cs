using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: what happens when an edit makes two clips (audio or MIDI, any pair) overlap on a lane: the existing clip moves to the first free lower lane (a new lane when none
/// is free) while the edited clip stays, in the edit own undo step; the prompt is asked only under the Ask setting (Remember stores Move or Keep); Cancel undoes the edit;
/// a take that grows in the piano roll asks too; and the setting, the words and the window.
/// Tests: ClipLaneOverlap, ClipOverlapPolicy, ClipOverlapPromptPresenter, ClipOverlapPromptWindow, TimelineClipGestureController (drop), PianoRollPresenter, SettingsCatalog (editing.clipoverlap).
/// </summary>
public class ClipLaneOverlapTests
{
    private const string Q = "\u0027";

    private sealed class Rig
    {
        public SongDocument Doc = null!;
        public TimelineView View = null!;
        public TrackModel Track = null!;
        public List<ClipOverlapQuestion> Asked = new();
        public string Setting = ClipOverlapChoice.Ask;
        public ClipOverlapAnswer? Answer;
        public int Refreshes;
        public ClipOverlapPolicy Policy = null!;
        public AudioClip Clip(string name) => Doc.Project.Tracks.Last().AudioClips.Single(c => c.Name == name);
    }

    private static AudioClip Audio(string name, double start, double length, int lane = 0) =>
        new() { Name = name, File = name + ".wav", StartSec = start, SourceLengthSec = length, FileLengthSec = length, Lane = lane };

    private static AudioClip Midi(string name, double start, double length, int lane = 0) =>
        new() { Name = name, StartSec = start, SourceLengthSec = length, FileLengthSec = length, Lane = lane, Notes = new List<ClipNote>() };

    private static Rig Make(ClipOverlapAnswer? answer, params AudioClip[] clips)
    {
        var song = SongDocument.BlankProject();   // no repeats: seconds map straight onto bars
        var track = NewTrackFactory.CreateTrack(song, TrackKind.Audio);
        song.Tracks.Add(track);
        foreach (var clip in clips) { track.AudioClips.Add(clip); ClipLanes.Ensure(track, clip.Lane + 1); }
        var rig = new Rig { Doc = new SongDocument(() => null) { Project = song }, Track = track, Answer = answer };
        rig.Policy = new ClipOverlapPolicy
        {
            Setting = () => rig.Setting,
            Remember = value => rig.Setting = value,
            Prompt = question => { rig.Asked.Add(question); return Task.FromResult(rig.Answer); },
        };
        rig.View = new TimelineView(song) { Snap = new SnapSettings { Enabled = false }, ClipOverlap = rig.Policy, ClipEdited = _ => rig.Refreshes++ };
        return rig;
    }

    /// <summary>Presses the middle of clip A (0.2-1.2 s) and drops it 3 s to the right, then releases (the commit point of a clip drag).</summary>
    private static bool DragAThreeSecondsRight(Rig rig)
    {
        var a = rig.Clip("Take A");
        var view = rig.View;
        var press = new global::Avalonia.Point((view.XOfSeconds(a.StartSec) + view.XOfSeconds(a.EndSec)) / 2, view.RowTop(rig.Doc.Project.Tracks.Count - 1) + 4);
        var gestures = new TimelineClipGestureController(view);
        gestures.Press(true, press);
        Assert.True(gestures.Begin());
        gestures.Update(new global::Avalonia.Point(view.XOfSeconds(view.SecondsOfX(press.X) + 3), press.Y), altHeld: false);
        return gestures.Commit(rig.Doc);
    }

    // ---- through a clip drop ----

    [AvaloniaFact]
    public void DroppingAClipOnAnotherAsksAndMoveSendsTheExistingOneToTheNextFreeLane()
    {
        var rig = Make(new ClipOverlapAnswer(Move: true, Remember: false), Audio("Take A", 0.2, 1), Audio("Take B", 3, 2));
        Assert.True(DragAThreeSecondsRight(rig));
        Assert.Equal((3.2, 0, 3.0), (rig.Clip("Take A").StartSec, rig.Clip("Take A").Lane, rig.Clip("Take B").StartSec));
        var asked = Assert.Single(rig.Asked);
        Assert.Equal(new ClipOverlapQuestion("Take A", "Take B", 1), asked);
        var (a, b) = (rig.Clip("Take A"), rig.Clip("Take B"));
        Assert.Equal(0, a.Lane);
        Assert.Equal(3.2, a.StartSec, 1);   // the edited clip stays where it was dropped
        Assert.Equal(1, b.Lane);
        Assert.Equal(3, b.StartSec, 6);     // the existing clip keeps its timing
        Assert.True(rig.Track.Lanes.Count >= 2);
        Assert.Equal(1, rig.Doc.Undo.UndoCount);
    }

    [AvaloniaFact]
    public void OneUndoStepGivesBackTheDropAndTheLaneMove()
    {
        var rig = Make(new ClipOverlapAnswer(true, false), Audio("Take A", 0.2, 1), Audio("Take B", 3, 2));
        DragAThreeSecondsRight(rig);
        Assert.NotNull(DocumentEdits.Undo(rig.Doc));
        Assert.Equal(0, rig.Doc.Undo.UndoCount);
        var (a, b) = (rig.Clip("Take A"), rig.Clip("Take B"));
        Assert.Equal(0.2, a.StartSec, 6);
        Assert.Equal((0, 0), (a.Lane, b.Lane));
    }

    [AvaloniaFact]
    public void KeepLeavesBothClipsOverlappingOnTheLane()
    {
        var rig = Make(new ClipOverlapAnswer(Move: false, Remember: false), Audio("Take A", 0.2, 1), Audio("Take B", 3, 2));
        DragAThreeSecondsRight(rig);
        Assert.Single(rig.Asked);
        Assert.Equal((0, 0), (rig.Clip("Take A").Lane, rig.Clip("Take B").Lane));
        Assert.Equal(1, rig.Doc.Undo.UndoCount);
        Assert.Equal(ClipOverlapChoice.Ask, rig.Setting);
    }

    [AvaloniaFact]
    public void CancelUndoesTheDropThatCausedTheOverlap()
    {
        var rig = Make(answer: null, Audio("Take A", 0.2, 1), Audio("Take B", 3, 2));
        DragAThreeSecondsRight(rig);
        Assert.Single(rig.Asked);
        Assert.Equal(0.2, rig.Clip("Take A").StartSec, 6);
        Assert.Equal((0, 0), (rig.Clip("Take A").Lane, rig.Clip("Take B").Lane));
        Assert.Equal(0, rig.Doc.Undo.UndoCount);
    }

    [AvaloniaFact]
    public async Task RememberMoveStoresTheChoiceAndTheNextOverlapMovesWithoutAsking()
    {
        var rig = Make(new ClipOverlapAnswer(true, Remember: true), Audio("Take A", 0.2, 1), Audio("Take B", 3, 2), Audio("Take D", 9, 1));
        DragAThreeSecondsRight(rig);
        Assert.Equal(ClipOverlapChoice.Move, rig.Setting);
        Assert.Single(rig.Asked);
        var a = rig.Clip("Take A");
        DocumentEdits.Run(rig.Doc, _ => { a.StartSec = 9.2; return true; });
        Assert.Equal(ClipOverlapOutcome.Moved, await rig.Policy.ResolveAsync(rig.Doc, new[] { a }));
        Assert.Single(rig.Asked);   // not asked again
        Assert.Equal(1, rig.Clip("Take D").Lane);
    }

    [AvaloniaFact]
    public async Task RememberKeepStoresTheChoiceAndLaterOverlapsStay()
    {
        var rig = Make(new ClipOverlapAnswer(false, Remember: true), Audio("Take A", 0.2, 1), Audio("Take B", 3, 2), Audio("Take D", 9, 1));
        DragAThreeSecondsRight(rig);
        Assert.Equal(ClipOverlapChoice.Keep, rig.Setting);
        var a = rig.Clip("Take A");
        DocumentEdits.Run(rig.Doc, _ => { a.StartSec = 9.2; return true; });
        Assert.Equal(ClipOverlapOutcome.Kept, await rig.Policy.ResolveAsync(rig.Doc, new[] { a }));
        Assert.Single(rig.Asked);
        Assert.Equal(0, rig.Clip("Take D").Lane);
    }

    [AvaloniaFact]
    public void WhenEveryLowerLaneIsBusyANewLaneIsCreated()
    {
        var rig = Make(new ClipOverlapAnswer(true, false), Audio("Take A", 0.2, 1), Audio("Take B", 3, 2), Audio("Take C", 3, 2, lane: 1));
        DragAThreeSecondsRight(rig);
        Assert.Equal(2, rig.Clip("Take B").Lane);
        Assert.Equal(1, rig.Clip("Take C").Lane);
        Assert.Equal(3, rig.Track.Lanes.Count);
    }

    [AvaloniaFact]
    public void NoOverlapMeansNoPrompt()
    {
        var rig = Make(new ClipOverlapAnswer(true, false), Audio("Take A", 0.2, 1), Audio("Take B", 4.5, 2));
        DragAThreeSecondsRight(rig);   // A lands at 3.2-4.2 s: B starts after it
        Assert.Empty(rig.Asked);
        Assert.Equal(0, rig.Clip("Take B").Lane);
    }

    [AvaloniaFact]
    public void ABareViewWithoutAPolicyLeavesOverlapsAlone()
    {
        var rig = Make(new ClipOverlapAnswer(true, false), Audio("Take A", 0.2, 1), Audio("Take B", 3, 2));
        rig.View.ClipOverlap = null;
        DragAThreeSecondsRight(rig);
        Assert.Empty(rig.Asked);
        Assert.Equal(0, rig.Clip("Take B").Lane);
        Assert.True(rig.Refreshes > 0);
    }

    // ---- any pair of clips, several at once ----

    [AvaloniaFact]
    public async Task AudioOverAudioMidiOverAudioAndAudioOverMidiAllCount()
    {
        foreach (var make in new Func<(AudioClip Edited, AudioClip Existing)>[]
        {
            () => (Audio("Edited", 1, 2), Audio("Existing", 2, 2)),
            () => (Midi("Edited", 1, 2), Audio("Existing", 2, 2)),
            () => (Audio("Edited", 1, 2), Midi("Existing", 2, 2)),
            () => (Midi("Edited", 1, 2), Midi("Existing", 2, 2)),
        })
        {
            var (edited, existing) = make();
            var rig = Make(null, edited, existing);
            rig.Setting = ClipOverlapChoice.Move;
            Assert.Equal(ClipOverlapOutcome.Moved, await rig.Policy.ResolveAsync(rig.Doc, new[] { edited }));
            Assert.Equal(0, edited.Lane);
            Assert.Equal(1, existing.Lane);
            Assert.Equal(2, existing.StartSec, 6);
            Assert.Empty(rig.Asked);
        }
    }

    [AvaloniaFact]
    public async Task SeveralOverlapsFromOneActionAskOnceAndAllMove()
    {
        var edited = new[] { Audio("Edit 1", 1, 2), Midi("Edit 2", 10, 2) };
        var rig = Make(new ClipOverlapAnswer(true, false), edited[0], edited[1], Audio("Hit 1", 2, 2), Midi("Hit 2", 11, 2));
        Assert.Equal(ClipOverlapOutcome.Moved, await rig.Policy.ResolveAsync(rig.Doc, edited));
        Assert.Single(rig.Asked);
        Assert.Equal(new[] { 1, 1 }, new[] { rig.Clip("Hit 1").Lane, rig.Clip("Hit 2").Lane });
    }

    [Fact]
    public void ClipsEditedTogetherNeverCountAgainstEachOtherAndTouchingIsNoOverlap()
    {
        var one = Audio("One", 1, 2);
        var two = Audio("Two", 2, 2);
        var three = Audio("Three", 3, 1);   // starts where One ends
        var song = new SongProject();
        var track = new TrackModel();
        track.AudioClips.AddRange(new[] { one, two, three });
        song.Tracks.Add(track);
        Assert.DoesNotContain(ClipLaneOverlap.Find(song, new[] { one, two }), o => o.Existing == three && o.Edited == one);
        var found = ClipLaneOverlap.Find(song, new[] { one });
        Assert.Same(two, Assert.Single(found).Existing);
    }

    // ---- the piano roll ----

    [AvaloniaFact]
    public async Task AMidiTakeGrowingOverAnAudioClipAsksAndMoveSendsTheAudioDown()
    {
        var document = PianoRollSongs.Midi();
        var track = document.Project.Tracks[0];
        var take = track.AudioClips[0];
        take.SourceLengthSec = 2;   // the take covers 0-2 s
        track.AudioClips.Add(Audio("Backing", 2.5, 2));
        var asked = new List<ClipOverlapQuestion>();
        var policy = new ClipOverlapPolicy { Prompt = question => { asked.Add(question); return Task.FromResult<ClipOverlapAnswer?>(new ClipOverlapAnswer(true, false)); } };
        var presenter = PianoRollSongs.Presenter(document);
        presenter.Overlaps = () => policy;
        presenter.Select(new[] { 0 });
        var steps = document.Undo.UndoCount;

        Assert.True(await presenter.MoveAsync(6, 0));   // the first note moves from 0.5 s to 3.5 s: the take grows to 4 s

        Assert.Equal("Backing", Assert.Single(asked).Existing);
        Assert.Equal(0, take.Lane);
        Assert.Equal(4, take.EndSec, 3);
        Assert.Equal(1, track.AudioClips.Single(c => c.Name == "Backing").Lane);
        Assert.Equal(steps + 1, document.Undo.UndoCount);
        DocumentEdits.Undo(document);
        Assert.Equal(0, document.Project.Tracks[0].AudioClips.Single(c => c.Name == "Backing").Lane);
        Assert.Equal(2, document.Project.Tracks[0].AudioClips.Single(c => c.IsMidi).EndSec, 3);
    }

    [AvaloniaFact]
    public async Task CancellingTheQuestionAfterAPianoRollGrowthUndoesTheNoteEdit()
    {
        var document = PianoRollSongs.Midi();
        var track = document.Project.Tracks[0];
        track.AudioClips[0].SourceLengthSec = 2;
        track.AudioClips.Add(Audio("Backing", 2.5, 2));
        var policy = new ClipOverlapPolicy { Prompt = _ => Task.FromResult<ClipOverlapAnswer?>(null) };
        var presenter = PianoRollSongs.Presenter(document);
        presenter.Overlaps = () => policy;
        presenter.Select(new[] { 0 });

        Assert.False(await presenter.MoveAsync(6, 0));

        var midi = document.Project.Tracks[0].AudioClips.Single(c => c.IsMidi);
        Assert.Equal(2, midi.EndSec, 3);
        Assert.Equal(0.5, midi.Notes![0].StartSec, 3);
    }

    // ---- the setting and the words ----

    [Fact]
    public void TheSettingDefaultsToAskAndRoundTripsInSettingsJson()
    {
        Assert.Equal("Ask", new EditingSettings().ClipOverlap);
        var settings = new AppSettings();
        settings.Editing.ClipOverlap = ClipOverlapChoice.Move;
        var back = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Assert.Equal(ClipOverlapChoice.Move, back.Editing.ClipOverlap);
        Assert.Equal(ClipOverlapChoice.Ask, ClipOverlapChoice.Normalize("nonsense"));
    }

    [Fact]
    public void PreferencesRowSitsInEditingClipsAndWritesTheStoredValue()
    {
        var settings = new AppSettings();
        var rows = SettingsCatalog.Build(settings);
        var row = rows.Single(r => r.Key == "editing.clipoverlap");
        Assert.Equal(SettingsCatalog.Editing, row.Category);
        Assert.Equal("When clips overlap on a lane", row.Title);
        Assert.Equal(new[] { "Ask each time", "Move the existing clip", "Keep overlapping" }, row.Choices);
        Assert.Equal(row.Choices[0], row.Get());
        row.Set(row.Choices[1]);
        Assert.Equal(ClipOverlapChoice.Move, settings.Editing.ClipOverlap);
        row.Set(row.Choices[2]);
        Assert.Equal(ClipOverlapChoice.Keep, settings.Editing.ClipOverlap);
    }

    [Fact]
    public void ThePromptWordsNameTheClipsAndTheLane()
    {
        var question = new ClipOverlapQuestion("Take A", "Take B", 2);
        Assert.Equal($"{Q}Take A{Q} now overlaps {Q}Take B{Q} on lane 2.", ClipOverlapPromptPresenter.Body(question));
        Assert.Equal($"Move {Q}Take B{Q} to a free lane", ClipOverlapPromptPresenter.MoveTitle(question));
        Assert.Equal("Clips overlap", ClipOverlapPromptPresenter.Title);
        Assert.Equal("It keeps its timing; only its lane changes.", ClipOverlapPromptPresenter.MoveHint);
    }

    private static T Named<T>(Window window, string name) where T : Control => window.GetVisualDescendants().OfType<T>().First(c => c.Name == name);

    [AvaloniaFact]
    public void TheWindowDefaultsToMoveEnterIsOkAndEscCancels()
    {
        var question = new ClipOverlapQuestion("Take A", "Take B", 1);
        var ok = new ClipOverlapPromptWindow(question);
        ok.Show();
        Assert.True(Named<RadioButton>(ok, "MoveRadio").IsChecked);
        Assert.False(Named<RadioButton>(ok, "KeepRadio").IsChecked);
        Assert.Equal(ClipOverlapPromptPresenter.RememberHint, Named<TextBlock>(ok, "RememberHint").Text);
        Named<CheckBox>(ok, "RememberBox").IsChecked = true;
        ok.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Assert.Equal(new ClipOverlapAnswer(true, true), ok.Answer);

        var keep = new ClipOverlapPromptWindow(question);
        keep.Show();
        Named<RadioButton>(keep, "KeepRadio").IsChecked = true;
        keep.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Assert.Equal(new ClipOverlapAnswer(false, false), keep.Answer);

        var cancelled = new ClipOverlapPromptWindow(question);
        cancelled.Show();
        cancelled.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Assert.Null(cancelled.Answer);
    }
}
