using System;
using System.Collections.Generic;
using System.Linq;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the rules of the timeline bar highlight that need no window: the BarHighlight model (cell, block, arrows, clamping, snapshots), the edit target setting and its rule, the routing table
//   rows of the highlight, the cell edits (tab-only copy, clear), the highlight as undo steps, and the two Preferences rows.
// Does not own: the gestures (TimelineBarHighlightInputTests), the commands in a window (TimelineBarHighlightEditTests) or the drawing (TimelineBarHighlightDrawingTests).
// Tests: this class.
public sealed class TimelineBarHighlightModelTests
{
    private const int Bars = 8;

    // Track 0 and 2 are guitars of 8 bars (the fret of the first cell is the bar index), track 1 is an audio track with a clip over bars 2-3.
    private static SongDocument NewSong()
    {
        var project = new SongProject();
        TrackModel Guitar(string name) => new()
        {
            Name = name,
            Measures = Enumerable.Range(0, Bars).Select(bar =>
            {
                var measure = new MeasureModel { Number = bar + 1 };
                measure.Cells[0].Notes.Add(new TabNote { StringIndex = 0, Fret = bar });
                return measure;
            }).ToList(),
        };
        project.Tracks.Add(Guitar("Guitar 1"));
        var audio = NewTrackFactory.CreateTrack(project, TrackKind.Audio);
        var span = BarRangeGaps.Span(project, 2, 3).GetValueOrDefault();
        audio.AudioClips.Add(new AudioClip { File = "take.wav", Name = "take", StartSec = span.From, SourceLengthSec = span.To - span.From, FileLengthSec = span.To - span.From });
        project.Tracks.Add(audio);
        project.Tracks.Add(Guitar("Guitar 2"));
        var document = new SongDocument(() => null) { Project = project };
        document.MarkClean();
        return document;
    }

    private static List<int?> Frets(TrackModel track) => track.Measures.Select(m => m.Cells[0].Notes.Select(n => (int?)n.Fret).FirstOrDefault()).ToList();

    private static bool IsTab(SongProject project, int track) => project.Tracks[track].HasNotation;

    [Fact]
    public void ClickingACellThenShiftExtendsAlongTheTrackOrMakesABlockFromTheAnchor()
    {
        var h = new BarHighlight();

        h.Set(2, 5);
        Assert.Equal((2, 2, 5, 5), (h.FirstTrack, h.LastTrack, h.FirstBar, h.LastBar));
        h.ExtendTo(2, 2);   // same track, an earlier bar: the block is normalised
        Assert.Equal((2, 2, 2, 5), (h.FirstTrack, h.LastTrack, h.FirstBar, h.LastBar));
        h.ExtendTo(0, 6);   // another track: the rectangle between the anchor (track 2, bar 5) and the click
        Assert.Equal((0, 2, 5, 6), (h.FirstTrack, h.LastTrack, h.FirstBar, h.LastBar));
        Assert.True(h.Contains(1, 5));
        Assert.False(h.Contains(1, 4));
        h.Set(1, 1);   // a plain click moves the anchor
        h.ExtendTo(1, 3);
        Assert.Equal((1, 1, 1, 3), (h.FirstTrack, h.LastTrack, h.FirstBar, h.LastBar));
    }

    [Fact]
    public void ShiftWithNoHighlightMakesTheCellAndANoChangeRaisesNothing()
    {
        var h = new BarHighlight();
        var raised = 0;
        h.Changed += () => raised++;

        Assert.True(h.ExtendTo(0, 3));
        Assert.False(h.Set(0, 3));
        Assert.False(h.ExtendTo(0, 3));
        Assert.True(h.Clear());
        Assert.False(h.Clear());

        Assert.Equal(2, raised);
        Assert.False(h.Has);
    }

    [Fact]
    public void ArrowsMoveTheActiveCornerSkipTracksWithoutNotationAndStayInsideTheSong()
    {
        var song = NewSong().Project;
        var h = new BarHighlight();
        bool Tab(int i) => IsTab(song, i);
        h.Set(0, 0);

        h.Move(1, 0, extend: false, 3, Bars, Tab);
        Assert.Equal((2, 2, 0, 0), (h.FirstTrack, h.LastTrack, h.FirstBar, h.LastBar));   // the audio track in between has no cells
        h.Move(1, 0, extend: false, 3, Bars, Tab);
        Assert.Equal(2, h.ActiveTrack);   // no track below: stays
        h.Move(0, -1, extend: false, 3, Bars, Tab);
        Assert.Equal(0, h.ActiveBar);
        h.Move(0, 1, extend: true, 3, Bars, Tab);
        h.Move(0, 1, extend: true, 3, Bars, Tab);
        Assert.Equal((2, 2, 0, 2), (h.FirstTrack, h.LastTrack, h.FirstBar, h.LastBar));   // Shift keeps the anchor and grows the block
        h.Move(-1, 0, extend: true, 3, Bars, Tab);
        Assert.Equal((0, 2, 0, 2), (h.FirstTrack, h.LastTrack, h.FirstBar, h.LastBar));
        h.Move(0, 99, extend: false, 3, Bars, Tab);
        Assert.Equal((Bars - 1, Bars - 1), (h.FirstBar, h.LastBar));
        Assert.False(new BarHighlight().Move(0, 1, false, 3, Bars, Tab));
    }

    [Fact]
    public void ASnapshotRestoresAndIsKeptInsideTheSong()
    {
        var h = new BarHighlight();
        h.SetBlock(0, 1, 2, 6);
        var snapshot = h.Snapshot();
        h.Clear();

        h.Restore(snapshot, trackCount: 2, barCount: 4);
        Assert.Equal((0, 1, 1, 3), (h.FirstTrack, h.LastTrack, h.FirstBar, h.LastBar));
        Assert.False(h.ClampTo(2, 4));
        h.ClampTo(0, 4);
        Assert.False(h.Has);
    }

    [Theory]
    [InlineData(EditTargetMode.LastClicked, FocusTarget.ScoreSelection, true, FocusTarget.ScoreSelection)]
    [InlineData(EditTargetMode.LastClicked, FocusTarget.BarHighlight, true, FocusTarget.BarHighlight)]
    [InlineData(EditTargetMode.HighlightWins, FocusTarget.ScoreSelection, true, FocusTarget.BarHighlight)]
    [InlineData(EditTargetMode.HighlightWins, FocusTarget.Loop, true, FocusTarget.BarHighlight)]
    [InlineData(EditTargetMode.HighlightWins, FocusTarget.Clips, true, FocusTarget.Clips)]
    [InlineData(EditTargetMode.HighlightWins, FocusTarget.ScoreSelection, false, FocusTarget.ScoreSelection)]
    [InlineData(EditTargetMode.SelectionWins, FocusTarget.BarHighlight, true, FocusTarget.ScoreSelection)]
    [InlineData(EditTargetMode.SelectionWins, FocusTarget.ScoreSelection, true, FocusTarget.ScoreSelection)]
    public void TheEditTargetSettingDecidesWhichOfTheHighlightAndTheSelectionTheKeysAct(EditTargetMode mode, FocusTarget focus, bool highlighted, FocusTarget expected)
    {
        var selection = new SelectionModel();
        selection.SetRange(0, 1, 2, SelectionOrigin.Editor);
        var highlight = new BarHighlight();
        if (highlighted) highlight.Set(0, 3);

        Assert.Equal(expected, EditTargetRule.Effective(mode, focus, selection, highlight));
    }

    [Fact]
    public void SelectionWinsLeavesTheHighlightWhenNothingIsSelectedAndTheChoiceWordsRoundTrip()
    {
        var highlight = new BarHighlight();
        highlight.Set(0, 3);

        Assert.Equal(FocusTarget.BarHighlight, EditTargetRule.Effective(EditTargetMode.SelectionWins, FocusTarget.BarHighlight, new SelectionModel(), highlight));
        foreach (var mode in Enum.GetValues<EditTargetMode>()) Assert.Equal(mode, EditTargetRule.FromChoice(EditTargetRule.ToChoice(mode)));
        Assert.Equal(EditTargetMode.LastClicked, EditTargetRule.FromChoice("something else"));
        Assert.Equal(EditTargetMode.LastClicked, EditTargetRule.FromChoice(null));
    }

    [Fact]
    public void TheRoutingTableSendsCopyCutPasteAndDeleteOfTheHighlightToItsOwnCommands()
    {
        var routing = new EditRouting();
        var state = new EditSelectionState(HasHighlight: true, ClipboardHasBars: true, ClipboardHasRange: true);

        Assert.Equal("Highlight.Copy", routing.Resolve(FocusTarget.BarHighlight, state, EditVerb.Copy).CommandId);
        Assert.Equal("Highlight.Cut", routing.Resolve(FocusTarget.BarHighlight, state, EditVerb.Cut).CommandId);
        Assert.Equal("Highlight.Paste", routing.Resolve(FocusTarget.BarHighlight, state, EditVerb.Paste).CommandId);
        Assert.Equal("Highlight.Delete", routing.Resolve(FocusTarget.BarHighlight, state, EditVerb.Delete).CommandId);
        Assert.False(routing.Resolve(FocusTarget.BarHighlight, state, EditVerb.PasteSpecial).IsAvailable);
        Assert.False(routing.Resolve(FocusTarget.BarHighlight, new EditSelectionState(), EditVerb.Copy).IsAvailable);
        Assert.Equal((FocusTarget.BarHighlight, EditVerb.Delete), routing.Classify("Highlight.Delete"));
        Assert.Equal("Highlight.Cut", routing.Pick(FocusTarget.BarHighlight, new[] { "Edit.Cut", "Range.Cut", "Highlight.Cut" }));
    }

    [Fact]
    public void TheHighlightCommandsAreBindableInTheirOwnContextSharingTheEditKeys()
    {
        var map = HotkeyCatalog.BuildMap(new HotkeySettings(), highlightContext: true);

        Assert.Equal(new[] { "Highlight.Copy", "Highlight.Cut", "Highlight.Delete", "Highlight.Paste" }, map.Values.OrderBy(id => id).ToArray());
        Assert.Equal("Highlight.Copy", map["Ctrl+C"]);
        Assert.Equal(5, HotkeyCatalog.ContextOf("Highlight.Delete"));
        Assert.False(HotkeyCatalog.SameContext("Highlight.Copy", "Edit.Copy"));
    }

    [Fact]
    public void CopyTakesExactlyTheHighlightedTabCellsAndNoClips()
    {
        var song = NewSong();
        var h = new BarHighlight();
        h.SetBlock(0, 2, 2, 3);   // tracks 0..2 include the audio track

        var snapshot = HighlightEdits.Capture(song.Project, h);

        Assert.Equal(new[] { song.Project.Tracks[0].Id, song.Project.Tracks[2].Id }, snapshot.Tracks.Select(t => t.SourceTrackId).ToArray());
        Assert.Equal(2, snapshot.BarCount);
        Assert.All(snapshot.Tracks, t => Assert.Empty(t.Clips));
        Assert.Equal(new[] { 2, 3 }, snapshot.Tracks[1].Bars.Select(b => b.Cells[0].Notes[0].Fret).ToArray());
        Assert.Equal(4, HighlightEdits.CellCount(song.Project, h));
    }

    [Fact]
    public void ClearEmptiesOnlyTheHighlightedTabCellsAndKeepsTheBarsAndTheClips()
    {
        var song = NewSong();
        var clip = song.Project.Tracks[1].AudioClips[0];
        var (start, length) = (clip.StartSec, clip.SourceLengthSec);
        var h = new BarHighlight();
        h.SetBlock(0, 2, 2, 3);

        Assert.True(HighlightEdits.Clear(song.Project, h, fillRests: true));

        Assert.Equal(new int?[] { 0, 1, null, null, 4, 5, 6, 7 }, Frets(song.Project.Tracks[0]));
        Assert.Equal(new int?[] { 0, 1, null, null, 4, 5, 6, 7 }, Frets(song.Project.Tracks[2]));
        Assert.Equal(Bars, song.Project.Tracks[0].Measures.Count);
        Assert.Equal((start, length), (clip.StartSec, clip.SourceLengthSec));
    }

    [Fact]
    public void AHighlightMoveIsOneUndoStepThatLeavesTheSongCleanAndRedoShowsItAgain()
    {
        var song = NewSong();
        var before = song.Highlight.Snapshot();
        song.Highlight.Set(0, 3);
        DocumentEdits.RecordHighlight(song, before);
        var second = song.Highlight.Snapshot();
        song.Highlight.ExtendTo(2, 5);
        DocumentEdits.RecordHighlight(song, second);

        Assert.Equal(2, song.Undo.UndoCount);
        Assert.False(song.IsDirty);
        DocumentEdits.Undo(song);
        Assert.Equal((0, 0, 3, 3), (song.Highlight.FirstTrack, song.Highlight.LastTrack, song.Highlight.FirstBar, song.Highlight.LastBar));
        DocumentEdits.Undo(song);
        Assert.False(song.Highlight.Has);
        Assert.False(song.IsDirty);
        DocumentEdits.Redo(song);
        DocumentEdits.Redo(song);
        Assert.Equal((0, 2, 3, 5), (song.Highlight.FirstTrack, song.Highlight.LastTrack, song.Highlight.FirstBar, song.Highlight.LastBar));
        Assert.False(song.Undo.CanRedo);
    }

    [Fact]
    public void RecordingAHighlightThatDidNotChangeStoresNothing()
    {
        var song = NewSong();
        var same = song.Highlight.Snapshot();

        DocumentEdits.RecordHighlight(song, same);

        Assert.False(song.Undo.CanUndo);
    }

    [Fact]
    public void UndoingAnEditShowsTheHighlightThatWentWithTheStateBeforeIt()
    {
        var song = NewSong();
        var none = song.Highlight.Snapshot();
        song.Highlight.Set(0, 1);
        DocumentEdits.RecordHighlight(song, none);
        DocumentEdits.Run(song, project => { project.Tracks[0].Measures[1].Cells[0].Notes.Clear(); return true; });
        var afterEdit = song.Highlight.Snapshot();
        song.Highlight.Set(2, 6);
        DocumentEdits.RecordHighlight(song, afterEdit);

        DocumentEdits.Undo(song);   // the highlight move after the edit
        DocumentEdits.Undo(song);   // the edit: the highlight the edit started with
        Assert.Equal(1, Frets(song.Project.Tracks[0])[1]);
        Assert.Equal((0, 1), (song.Highlight.FirstTrack, song.Highlight.FirstBar));
        DocumentEdits.Redo(song);
        Assert.Null(Frets(song.Project.Tracks[0])[1]);
    }

    [Fact]
    public void AnEditThatRemovesTheHighlightedBarsKeepsTheHighlightInsideTheSong()
    {
        var song = NewSong();
        song.Highlight.SetBlock(0, 6, 2, 7);

        DocumentEdits.Run(song, project => TimelineRangeEdit.Apply(project, 4, 7, TimelineDeleteMode.Remove, -1, fillRests: true).Changed);

        Assert.True(song.Highlight.LastBar < BarRangeEditor.MaxMeasures(song.Project));
    }

    [Fact]
    public void ThePreferencesHaveTheEditTargetAndTheClearQuestionRowsAndTheyAreFoundBySearch()
    {
        var settings = new AppSettings();
        var rows = SettingsCatalog.Build(settings);
        var target = rows.Single(d => d.Key == "editing.edittarget");
        var confirm = rows.Single(d => d.Key == "editing.confirmhighlightclear");

        Assert.Equal("Last clicked", target.Get());
        foreach (var choice in EditTargetRule.Choices)
        {
            target.Set(choice);
            Assert.Equal(EditTargetRule.FromChoice(choice), EditTargetRule.FromChoice(settings.Editing!.EditTarget));
        }
        Assert.Equal(true, confirm.Get());
        confirm.Set(false);
        Assert.False(settings.Editing!.ConfirmHighlightClear);

        var entries = SettingsCatalog.Registry(rows, settings.Hotkeys);
        Assert.Contains(entries, e => e.Id == "editing.edittarget" && SettingsCatalog.Matches(e, "highlight wins"));
        Assert.Contains(entries, e => e.Id == "editing.confirmhighlightclear" && SettingsCatalog.Matches(e, "ask again"));
    }

    [Fact]
    public void ThePromptWordsNameTheCellsAndOfferDontAskAgain()
    {
        var one = TabForge.Presenters.PromptTextsPresenter.ClearHighlightedBars(1);
        var many = TabForge.Presenters.PromptTextsPresenter.ClearHighlightedBars(6);

        Assert.Equal("Clear the notes in 1 highlighted bar? The bars stay.", one.Message);
        Assert.Equal("Clear the notes in 6 highlighted bars? The bars stay.", many.Message);
        Assert.Equal("Don't ask again", many.RememberText);
        Assert.Null(many.Choices);
    }
}
