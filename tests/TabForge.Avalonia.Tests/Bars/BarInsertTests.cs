using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
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
/// Owns: every bar-insert path going through the one shared insert (BarInsert): Bar.Insert (Ctrl+Insert), Insert > Bar > After / Several bars (before, after, at the end) /
/// At the end, the timeline bar menu rows Before / After this bar, Range.InsertBefore / InsertAfter, Bar.Duplicate and Edit.RepeatSelection: the same index on every track (a short
/// track included), empty new bars, sections, tempo, repeats and ties following their bars, clips after the point moving later, one undo step that restores the song exactly.
/// Does not own: the dialog wording (InsertMenuTests), the range prompts (TimelineRangeDeleteTests).
/// Tests: BarInsert, BarStructureEdits, BarRangeEditor.Insert, MainWindowInsertMenu, MainWindowEdit.
/// </summary>
public sealed class BarInsertTests
{
    private const int Bars = 64, Verse2 = 56;

    // The reported song shape: Chorus 41-48, Break 49-56, Verse 2 from 57; a lead track with a note in every bar (a tie into bar 57, a tempo change and a repeat from 57 on),
    // a bass track that stops after bar 48 and is SHORTER than the song (52 bars), a drum track that only plays from bar 57, and a MIDI clip at bar 61 plus one at bar 11.
    private static SongProject Song()
    {
        var song = new SongProject();
        TrackModel Track(string name, TrackKind kind, int bars) { var t = new TrackModel { Name = name, Kind = kind, Measures = TemplateFactory.Measures(bars) }; song.Tracks.Add(t); return t; }
        var lead = Track("Lead", TrackKind.Guitar, Bars);
        var bass = Track("Bass", TrackKind.Bass, 52);
        var drums = Track("Drums", TrackKind.Drums, Bars);
        var audio = Track("Audio", TrackKind.Audio, Bars);
        for (var b = 0; b < Bars; b++) Note(lead, b, b % 12 + 1);
        for (var b = 0; b < 48; b++) Note(bass, b, b % 5 + 1);
        for (var b = Verse2; b < Bars; b++) Note(drums, b, 2);
        Note(lead, Verse2 - 1, 5);
        Note(lead, Verse2, 5, tied: true);
        lead.Measures[Verse2].TempoChange = 140;
        lead.Measures[Verse2].RepeatStart = true;
        lead.Measures[Verse2 + 3].RepeatEnd = true;
        song.Markers.Add(new MarkerModel { MeasureIndex = 40, Title = "Chorus" });
        song.Markers.Add(new MarkerModel { MeasureIndex = 48, Title = "Break" });
        song.Markers.Add(new MarkerModel { MeasureIndex = Verse2, Title = "Verse 2" });
        audio.AudioClips.Add(new AudioClip { Name = "late", StartSec = Start(song, 60), SourceLengthSec = 2, Notes = new List<ClipNote>() });
        audio.AudioClips.Add(new AudioClip { Name = "early", StartSec = Start(song, 10), SourceLengthSec = 2, Notes = new List<ClipNote>() });
        return song;
    }

    private static void Note(TrackModel track, int bar, int fret, bool tied = false)
    {
        var cell = track.Measures[bar].Cells[0];
        cell.DurationDenominator = 1;
        cell.Notes.Clear();
        cell.Notes.Add(new TabNote { StringIndex = 0, Fret = fret, Tied = tied });
        cell.IsTied = tied;
    }

    private static double Start(SongProject song, int bar) => BarRangeGaps.Span(song, bar, bar)!.Value.From;

    private static string Notes(TrackModel track, int bar) => bar >= track.Measures.Count ? ""
        : string.Join(",", track.Measures[bar].Cells.Concat(track.Measures[bar].Voice2Cells).SelectMany(c => c.Notes).Select(n => $"{n.StringIndex}:{n.Fret}"));

    /// <summary>The song after <paramref name="count"/> bars went in before bar <paramref name="at"/>: every track as long as the song, the new bars empty on every track,
    /// every other bar with its notes, tempo and repeats shifted by the count, the sections after the point moved, the late clip still on its bar and the early one in place.</summary>
    private static void AssertInserted(SongProject before, SongProject after, int at, int count)
    {
        var total = BarRangeEditor.MaxMeasures(before) + count;
        Assert.All(after.Tracks, t => Assert.Equal(total, t.Measures.Count));
        for (var t = 0; t < after.Tracks.Count; t++)
            for (var bar = 0; bar < total; bar++)
            {
                var expected = bar < at ? Notes(before.Tracks[t], bar) : bar < at + count ? "" : Notes(before.Tracks[t], bar - count);
                Assert.True(expected == Notes(after.Tracks[t], bar), $"{after.Tracks[t].Name} bar {bar + 1}: expected [{expected}], got [{Notes(after.Tracks[t], bar)}]");
            }
        var lead = after.Tracks[0].Measures;
        int Moved(int bar) => bar < at ? bar : bar + count;
        Assert.Equal(140, lead[Moved(Verse2)].TempoChange);
        Assert.True(lead[Moved(Verse2)].RepeatStart);
        Assert.True(lead[Moved(Verse2 + 3)].RepeatEnd);
        Assert.Equal(new[] { Moved(40), Moved(48), Moved(Verse2) }, after.Markers.OrderBy(m => m.MeasureIndex).Select(m => m.MeasureIndex));
        var clips = after.Tracks[3].AudioClips;
        Assert.Equal(Start(after, Moved(60)), clips.Single(c => c.Name == "late").StartSec, 6);
        Assert.Equal(Start(after, 10), clips.Single(c => c.Name == "early").StartSec, 6);
    }

    private sealed class Rig
    {
        public MainWindowTabs Tabs = new(new SongDocument(() => null) { Project = Song() }, _ => Task.FromResult(PromptButton.Cancel), _ => Task.FromResult(false));
        public ScorePreviewView Score = new();
        public MainWindowCommands Commands;
        public ContextMenuHost Menus = new();
        public InsertBarsAnswer? NextBars;

        public SongDocument Doc => Tabs.Active;

        public Rig()
        {
            var panes = new MainWindowDocumentPanes(Tabs, Score);
            panes.NewArrangement();
            var owner = new Window();
            Commands = new MainWindowCommands(owner, () => { });
            void Landed() => MainWindowInsertRefresh.Landed(panes, Score, Tabs.Active);
            MainWindowEdit.Register(Commands, Score, owner, Menus, () => false, _ => { }, afterInsert: Landed);
            MainWindowInsertMenu.Register(Commands, Tabs, panes, Score, new MainWindowInsertMenu.Prompts(_ => Task.FromResult(NextBars), _ => null),
                _ => { }, () => { }, (_, _, _) => Task.CompletedTask);
        }

        /// <summary>Runs <paramref name="id"/> with the cursor on bar <paramref name="cursorBar"/>, checks the insert and the cursor bar, then that one Undo restores the song exactly.</summary>
        public void InsertAndUndo(string id, int cursorBar, int at, int count, int cursorAfter)
        {
            var before = ProjectService.Snapshot(Doc.Project);
            var undo = Doc.Undo.UndoCount;
            Score.Probe.Cursor = (cursorBar, 0, 0);
            Commands.Run(id);
            Dispatcher.UIThread.RunJobs();
            AssertInserted(Song(), Doc.Project, at, count);
            Assert.Equal(cursorAfter, Score.Editor.SelectedMeasure);
            Assert.Equal(undo + 1, Doc.Undo.UndoCount);
            Commands.Run("Edit.Undo");
            Assert.Equal(before, ProjectService.Snapshot(Doc.Project));
        }
    }

    [AvaloniaFact]
    public void FourBarsBeforeTheFirstBarOfASectionGoInEveryTrackAndTheSectionsFollow()
    {
        var rig = new Rig { NextBars = new InsertBarsAnswer(4, InsertBarsPlace.BeforeSelected) };
        rig.InsertAndUndo("Bar.InsertSeveral", Verse2, Verse2, 4, Verse2);
    }

    [AvaloniaFact]
    public void TheReportedCaseLeavesBreakOwningTheNewBarsAndVerse2StartingAtBar61()
    {
        var rig = new Rig { NextBars = new InsertBarsAnswer(4, InsertBarsPlace.BeforeSelected) };
        rig.Score.Probe.Cursor = (Verse2, 0, 0);
        rig.Commands.Run("Bar.InsertSeveral");
        Dispatcher.UIThread.RunJobs();
        var song = rig.Doc.Project;
        Assert.True(SectionLayout.TryGetBounds(song, song.Markers.Single(m => m.Title == "Break"), out var start, out var end));
        Assert.Equal((48, 60), (start, end));   // bars 49-60
        Assert.Equal(60, song.Markers.Single(m => m.Title == "Verse 2").MeasureIndex);   // bar 61
        Assert.All(song.Tracks, t => Assert.Equal(Bars + 4, t.Measures.Count));
        var tied = song.Tracks[0].Measures[60].Cells[0];
        Assert.False(tied.IsTied || tied.Notes.Any(n => n.Tied));   // the tie from bar 56 cannot hang over four empty bars
    }

    [AvaloniaTheory]
    [InlineData("Bar.Insert", Verse2, Verse2, Verse2)]
    [InlineData("Bar.InsertAfter", Verse2 - 1, Verse2, Verse2)]
    public void TheOneBarCommandsInsertAtTheSameIndexOnEveryTrack(string id, int cursor, int at, int cursorAfter)
    {
        var rig = new Rig();
        rig.InsertAndUndo(id, cursor, at, 1, cursorAfter);
    }

    [AvaloniaTheory]
    [InlineData(InsertBarsPlace.AfterSelected, Verse2 - 1, Verse2)]
    [InlineData(InsertBarsPlace.AtEnd, 3, Bars)]
    public void SeveralBarsAfterOrAtTheEndUseTheSameInsert(InsertBarsPlace place, int cursor, int at)
    {
        var rig = new Rig { NextBars = new InsertBarsAnswer(3, place) };
        rig.InsertAndUndo("Bar.InsertSeveral", cursor, at, 3, at);
    }

    [AvaloniaFact]
    public void BarAppendAddsOneBarOnEveryTrackAndMovesNothing()
    {
        var rig = new Rig();
        rig.InsertAndUndo("Bar.Append", 3, Bars, 1, 3);
    }

    [AvaloniaTheory]
    [InlineData(ContextMenuRows.InsertBarBeforeId, Verse2, Verse2)]
    [InlineData(ContextMenuRows.InsertBarAfterId, Verse2 - 1, Verse2)]
    public void TheTimelineBarMenuRowsUseTheSameInsert(string id, int clicked, int at)
    {
        var rig = new Rig();
        rig.Menus.ClickedBar = clicked;
        rig.InsertAndUndo(id, 0, at, 1, at);
    }

    [Theory]
    [InlineData(BarRangeAction.InsertBefore, Verse2, Verse2 + 3, Verse2)]
    [InlineData(BarRangeAction.InsertAfter, Verse2 - 4, Verse2 - 1, Verse2)]
    public void RangeInsertGapUsesTheSameInsert(BarRangeAction action, int first, int last, int at)
    {
        var doc = new SongDocument(() => null) { Project = Song() };
        var before = ProjectService.Snapshot(doc.Project);
        DocumentEdits.Run(doc, p => TimelineRangeEdit.Apply(p, first, last, action, -1, false).Changed);
        AssertInserted(Song(), doc.Project, at, 4);
        Assert.Equal(1, doc.Undo.UndoCount);
        Assert.Equal(before, ProjectService.Snapshot(doc.Undo.Restore(doc.Undo.Latest!.Value)));
    }

    [Fact]
    public void DuplicateAndRepeatMoveSectionsAndClipsAndKeepATieIntoTheCopy()
    {
        var song = Song();
        Assert.NotNull(BarRangeEditor.Duplicate(song, Verse2 - 1, Verse2 - 1));   // bar 56 copied in front of Verse 2
        Assert.Equal(Verse2 + 1, song.Markers.Single(m => m.Title == "Verse 2").MeasureIndex);
        Assert.Equal(Start(song, 61), song.Tracks[3].AudioClips.Single(c => c.Name == "late").StartSec, 6);
        Assert.All(song.Tracks, t => Assert.Equal(Bars + 1, t.Measures.Count));
        Assert.True(song.Tracks[0].Measures[Verse2 + 1].Cells[0].IsTied);   // the copy of bar 56 still ends on the tied pitch

        var repeated = Song();
        Assert.Equal(2, BarStructureEdits.RepeatRange(repeated, Verse2 - 1, Verse2 - 1, 2));
        Assert.Equal(Verse2 + 2, repeated.Markers.Single(m => m.Title == "Verse 2").MeasureIndex);
        Assert.All(repeated.Tracks, t => Assert.Equal(Bars + 2, t.Measures.Count));
        Assert.Equal(Start(repeated, 62), repeated.Tracks[3].AudioClips.Single(c => c.Name == "late").StartSec, 6);
    }

    [Fact]
    public void AResizedSectionThatEndsWhereTheNextStartsTakesTheBarsInsertedBetweenThem()
    {
        var song = Song();
        song.Markers.Single(m => m.Title == "Break").LengthBars = 8;
        song.Markers.Single(m => m.Title == "Chorus").LengthBars = 4;   // bars 41-44, then a gap up to Break
        Assert.NotNull(BarInsert.Empty(song, Verse2, 4, fillRests: false));
        Assert.Equal(12, song.Markers.Single(m => m.Title == "Break").LengthBars);   // no gap opens before Verse 2
        Assert.NotNull(BarInsert.Empty(song, 44, 2, fillRests: false));
        Assert.Equal(4, song.Markers.Single(m => m.Title == "Chorus").LengthBars);   // a resized section with a gap after it keeps its length: the bars join the gap
        Assert.Equal(50, song.Markers.Single(m => m.Title == "Break").MeasureIndex);
    }

    [Fact]
    public void BarsInsertedBeforeBarOneMoveEverySectionButTheFirst()
    {
        var song = Song();
        song.Markers.Add(new MarkerModel { MeasureIndex = 0, Title = "Intro", LengthBars = 8 });
        Assert.NotNull(BarInsert.Empty(song, 0, 2, fillRests: false));
        Assert.Equal(new[] { 0, 42, 50, 58 }, song.Markers.OrderBy(m => m.MeasureIndex).Select(m => m.MeasureIndex));
        Assert.Equal(10, song.Markers.Single(m => m.Title == "Intro").LengthBars);
    }

    [Fact]
    public void BarDeleteMovesTheSectionsAndClipsBack()
    {
        var song = Song();
        Assert.True(BarStructureEdits.DeleteBar(song, 50, -1, allTracks: true));
        Assert.Equal(new[] { 40, 48, Verse2 - 1 }, song.Markers.OrderBy(m => m.MeasureIndex).Select(m => m.MeasureIndex));
        Assert.Equal(Start(song, 59), song.Tracks[3].AudioClips.Single(c => c.Name == "late").StartSec, 6);
    }
}
