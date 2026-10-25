using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: pasting a copied timeline range: Overwrite / Merge / Insert, tabs and/or clips, trim or new take lane, repeat, bars added past the end, ONE undo step, the pasted bars selected,
//   skipped tracks and a differing time signature reported, Paste Special remembering its choices, the window sections, the routing rows and the score paste of a range.
//   Windows are fakes or headless: no real modal opens, every wait has a 10 s limit.
// Does not own: the snapshot (RangeClipboardTests), the delete rules (TimelineRangeDeleteTests) or the clipboard text slot.
// Tests: this class.
public sealed class TimelinePasteTests
{
    private const int Bars = 8;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    // Two guitar tracks of 8 bars (the fret of the first cell is the bar index) and an audio track whose clip lies over bars 1-5.
    private static SongDocument NewSong(bool laterClip = false)
    {
        var project = new SongProject();
        for (var t = 0; t < 2; t++)
            project.Tracks.Add(new TrackModel
            {
                Name = "Guitar " + (t + 1),
                Measures = Enumerable.Range(0, Bars).Select(bar =>
                {
                    var measure = new MeasureModel { Number = bar + 1 };
                    measure.Cells[0].Notes.Add(new TabNote { StringIndex = 0, Fret = bar });
                    return measure;
                }).ToList(),
            });
        var audio = NewTrackFactory.CreateTrack(project, TrackKind.Audio);
        var span = BarRangeGaps.Span(project, 1, 5).GetValueOrDefault();
        audio.AudioClips.Add(new AudioClip { File = "take.wav", Name = "take", StartSec = span.From, SourceLengthSec = span.To - span.From, FileLengthSec = span.To - span.From });
        if (laterClip)
        {
            var late = BarRangeGaps.Span(project, 6, 6).GetValueOrDefault();
            audio.AudioClips.Add(new AudioClip { File = "late.wav", Name = "late", StartSec = late.From, SourceLengthSec = late.To - late.From, FileLengthSec = late.To - late.From });
        }
        project.Tracks.Add(audio);
        var document = new SongDocument(() => null) { Project = project };
        document.MarkClean();
        return document;
    }

    private static RangeSnapshot Copy(int first, int last) => RangeCapture.Capture(NewSong().Project, first, last);

    private static RangePasteResult Paste(SongDocument song, RangeSnapshot snapshot, int at, int track = 0, RangePasteOptions? options = null)
    {
        RangePasteResult? result = null;
        DocumentEdits.Run(song, project => (result = RangePaste.Apply(project, snapshot, at, track, options ?? new RangePasteOptions())).Changed);
        return result!;
    }

    private static int[] Frets(SongDocument song, int track, int from, int to) =>
        Enumerable.Range(from, to - from + 1).Select(bar => song.Project.Tracks[track].Measures[bar].Cells[0].Notes[0].Fret).ToArray();

    private static double BarStart(SongDocument song, int bar) => BarRangeGaps.Span(song.Project, bar, bar).GetValueOrDefault().From;

    [Fact]
    public void OverwritePastesTabsOnEveryTrackAndTrimsTheClipUnderTheAudioPart()
    {
        var song = NewSong();
        var existing = song.Project.Tracks[2].AudioClips[0];
        var originalEnd = existing.EndSec;
        var result = Paste(song, Copy(2, 3), 4);

        Assert.True(result.Changed);
        Assert.Equal((4, 5), (result.FirstBar, result.LastBar));
        Assert.Equal(new[] { 2, 3 }, Frets(song, 0, 4, 5));
        Assert.Equal(new[] { 2, 3 }, Frets(song, 1, 4, 5));
        Assert.Equal(new[] { 6, 7 }, Frets(song, 0, 6, 7));
        var clips = song.Project.Tracks[2].AudioClips;
        Assert.Equal(2, clips.Count);
        Assert.Equal(BarStart(song, 4), existing.EndSec, 3);
        var pasted = clips.Single(c => !ReferenceEquals(c, existing));
        Assert.Equal(BarStart(song, 4), pasted.StartSec, 3);
        Assert.Equal(BarStart(song, 6), pasted.EndSec, 3);
        Assert.True(originalEnd > existing.EndSec);
    }

    [Fact]
    public void OnePasteIsOneUndoStep()
    {
        var song = NewSong();
        var before = DocumentEdits.Fingerprint(song);
        Paste(song, Copy(2, 3), 6, options: new RangePasteOptions(Repeat: 2));   // grows the song AND writes tabs AND places clips
        Assert.NotEqual(before, DocumentEdits.Fingerprint(song));

        Assert.NotNull(DocumentEdits.Undo(song));
        Assert.Equal(before, DocumentEdits.Fingerprint(song));
        Assert.Equal(Bars, BarRangeEditor.MaxMeasures(song.Project));
        Assert.Single(song.Project.Tracks[2].AudioClips);
    }

    [Fact]
    public void MergeFillsOnlyEmptySpotsAndSkipsABusyClipSpot()
    {
        var source = NewSong();
        source.Project.Tracks[0].Measures[2].Cells[1].Notes.Add(new TabNote { StringIndex = 1, Fret = 5 });
        var snapshot = RangeCapture.Capture(source.Project, 2, 2);
        var song = NewSong();

        var result = Paste(song, snapshot, 4, options: new RangePasteOptions(RangePasteMode.Merge));

        var bar = song.Project.Tracks[0].Measures[4];
        Assert.Equal(4, bar.Cells[0].Notes[0].Fret);   // the existing note wins
        Assert.Equal(5, bar.Cells[1].Notes[0].Fret);   // the empty beat is filled
        Assert.Single(song.Project.Tracks[2].AudioClips);   // the lane is busy there: the part is skipped
        Assert.Contains("1 clip skipped", result.Notice);
    }

    [Fact]
    public void OverwriteReplacesTheBeatsThatMergeWouldKeep()
    {
        var source = NewSong();
        source.Project.Tracks[0].Measures[2].Cells[1].Notes.Add(new TabNote { StringIndex = 1, Fret = 5 });
        var song = NewSong();
        Paste(song, RangeCapture.Capture(source.Project, 2, 2), 4);
        var bar = song.Project.Tracks[0].Measures[4];
        Assert.Equal(2, bar.Cells[0].Notes[0].Fret);
        Assert.Equal(5, bar.Cells[1].Notes[0].Fret);
    }

    [Fact]
    public void InsertRipplesLaterBarsAndClipsToTheRight()
    {
        var song = NewSong(laterClip: true);
        var late = song.Project.Tracks[2].AudioClips.Single(c => c.Name == "late");
        var lateStart = late.StartSec;
        var shift = BarRangeGaps.Span(song.Project, 3, 6).GetValueOrDefault();

        var result = Paste(song, Copy(2, 3), 3, options: new RangePasteOptions(RangePasteMode.Insert, Repeat: 2));

        Assert.Equal((3, 6), (result.FirstBar, result.LastBar));
        Assert.Equal(Bars + 4, BarRangeEditor.MaxMeasures(song.Project));
        Assert.Equal(new[] { 2, 3, 2, 3 }, Frets(song, 0, 3, 6));
        Assert.Equal(new[] { 3, 4, 5, 6, 7 }, Frets(song, 0, 7, 11));   // the old bars 3..7 moved right by four
        var moved = song.Project.Tracks[2].AudioClips.Single(c => c.Name == "late");
        Assert.Equal(lateStart + (shift.To - shift.From), moved.StartSec, 3);
    }

    [Fact]
    public void InsertInsideAClipSplitsItAndTheRightPartRipples()
    {
        var song = NewSong();
        var clip = song.Project.Tracks[2].AudioClips[0];
        var (start, end, length) = (clip.StartSec, clip.EndSec, clip.SourceLengthSec);
        var point = BarStart(song, 3);   // inside the clip (bars 1-5)
        var shift = BarRangeGaps.Span(song.Project, 3, 4).GetValueOrDefault();

        Paste(song, Copy(2, 3), 3, options: new RangePasteOptions(RangePasteMode.Insert, RangePasteWhat.Tabs));

        var parts = song.Project.Tracks[2].AudioClips.Where(c => c.Name == "take").OrderBy(c => c.StartSec).ToList();
        Assert.Equal(2, parts.Count);
        Assert.Equal(start, parts[0].StartSec, 3);
        Assert.Equal(point, parts[0].EndSec, 3);
        Assert.Equal(point + (shift.To - shift.From), parts[1].StartSec, 3);
        Assert.Equal(end + (shift.To - shift.From), parts[1].EndSec, 3);
        Assert.Equal(length, parts[0].SourceLengthSec + parts[1].SourceLengthSec, 3);
    }

    [Fact]
    public void PastingPastTheEndAddsBarsThatAreTrackedAsAutoAdded()
    {
        var song = NewSong();
        var result = Paste(song, Copy(0, 3), 6);

        Assert.Equal((6, 9), (result.FirstBar, result.LastBar));
        Assert.Equal(10, BarRangeEditor.MaxMeasures(song.Project));
        Assert.All(song.Project.Tracks, track => Assert.Equal(10, track.Measures.Count));
        Assert.Equal(new[] { 2, 3 }, Frets(song, 0, 8, 9));
        Assert.Equal(2, song.Project.AutoBars.Count);
    }

    [Fact]
    public void NewTakeLaneKeepsBothClipsAndTheNewestPlays()
    {
        var song = NewSong();
        var existing = song.Project.Tracks[2].AudioClips[0];
        var endBefore = existing.EndSec;
        Paste(song, Copy(2, 3), 4, options: new RangePasteOptions(Overlap: RangeClipOverlap.NewTakeLane));

        var clips = song.Project.Tracks[2].AudioClips;
        Assert.Equal(2, clips.Count);
        Assert.Equal(endBefore, existing.EndSec, 3);   // not trimmed
        var pasted = clips.Single(c => !ReferenceEquals(c, existing));
        Assert.Equal(1, pasted.Lane);
        Assert.False(song.Project.Tracks[2].Lanes[0].Plays);
        Assert.True(song.Project.Tracks[2].Lanes[1].Plays);
    }

    [Fact]
    public void RepeatPlacesCopiesBackToBack()
    {
        var song = NewSong();
        var result = Paste(song, Copy(2, 3), 0, options: new RangePasteOptions(Repeat: 3));
        Assert.Equal((0, 5), (result.FirstBar, result.LastBar));
        Assert.Equal(new[] { 2, 3, 2, 3, 2, 3, 6 }, Frets(song, 0, 0, 6));
    }

    [Fact]
    public void WhatTabsOnlyAndClipsOnlyEachLeaveTheOtherPartAlone()
    {
        var tabsOnly = NewSong();
        Paste(tabsOnly, Copy(2, 3), 4, options: new RangePasteOptions(What: RangePasteWhat.Tabs));
        Assert.Equal(new[] { 2, 3 }, Frets(tabsOnly, 0, 4, 5));
        Assert.Single(tabsOnly.Project.Tracks[2].AudioClips);

        var clipsOnly = NewSong();
        Paste(clipsOnly, Copy(2, 3), 4, options: new RangePasteOptions(What: RangePasteWhat.Clips));
        Assert.Equal(new[] { 4, 5 }, Frets(clipsOnly, 0, 4, 5));
        Assert.Equal(2, clipsOnly.Project.Tracks[2].AudioClips.Count);
    }

    [Fact]
    public void TracksMapByKindInOrderAndTheMismatchesAreReportedOnce()
    {
        var song = NewSong();
        var result = Paste(song, Copy(2, 3), 4, track: 1);   // guitar -> track 1; guitar -> the audio row: wrong kind; audio -> no row left

        Assert.True(result.Changed);
        Assert.Equal(new[] { 2, 3 }, Frets(song, 1, 4, 5));
        Assert.Equal(new[] { 4, 5 }, Frets(song, 0, 4, 5));
        Assert.Single(song.Project.Tracks[2].AudioClips);
        Assert.Equal("2 tracks skipped: no matching track", Assert.Single(result.Notices));

        var nothing = NewSong();
        var none = Paste(nothing, Copy(2, 3), 4, track: 2);   // starting on the audio row: nothing maps
        Assert.False(none.Changed);
        Assert.Contains("skipped", none.Notice);
        Assert.Equal(new[] { 4, 5 }, Frets(nothing, 0, 4, 5));
    }

    [Fact]
    public void ADifferentTimeSignatureKeepsTheTargetsAndCutsWhatDoesNotFit()
    {
        var source = NewSong();
        source.Project.Tracks[0].Measures[2].Cells[13].Notes.Add(new TabNote { StringIndex = 1, Fret = 9 });
        var snapshot = RangeCapture.Capture(source.Project, 2, 2);
        var song = NewSong();
        foreach (var track in song.Project.Tracks) { track.Measures[4].TimeSigNum = 3; track.Measures[4].TimeSigDenom = 4; }

        var result = Paste(song, snapshot, 4);

        Assert.True(result.Changed);
        Assert.Equal(1, result.SignatureMismatches);
        Assert.Equal(1, result.CutCells);
        Assert.Contains("another time signature", result.Notice);
        var bar = song.Project.Tracks[0].Measures[4];
        Assert.Equal((3, 4), (bar.TimeSigNum, bar.TimeSigDenom));
        Assert.Equal(2, bar.Cells[0].Notes[0].Fret);
        Assert.DoesNotContain(bar.Cells, cell => cell.Notes.Any(n => n.Fret == 9));
    }

    private static readonly Func<EditingSettings> NoSettings = () => new EditingSettings();

    private static Task<bool> PasteWithModel(SongDocument song, SelectionModel model, RangeSnapshot snapshot, int playheadBar, List<string> notices) =>
        MainWindowRangePaste.PasteAsync(song, model, snapshot, playheadBar, special: false, _ => throw new InvalidOperationException("Ctrl+V never asks"),
            NoSettings, () => { }, notices.Add).WaitAsync(Timeout);

    [Fact]
    public async Task TheRangePastesAtTheLoopStartOrThePlayhead()
    {
        var song = NewSong();
        var notices = new List<string>();
        var snapshot = Copy(2, 3);

        Assert.True(await PasteWithModel(song, song.Selection, snapshot, playheadBar: 5, notices));   // no loop: the playhead bar
        Assert.Contains("Pasted 2 bars at bar 6", notices.Single());
        Assert.False(song.Selection.HasLoop);   // a paste never makes a loop

        song.Selection.SetLoop(1, 1);
        Assert.True(await PasteWithModel(song, song.Selection, snapshot, playheadBar: 5, notices));   // a loop: its start
        Assert.Contains("at bar 2", notices.Last());
        Assert.Equal(new[] { 2, 3 }, Frets(song, 0, 1, 2));
    }

    [Fact]
    public async Task PasteSpecialRemembersItsChoicesOnlyWhenAskedAndCancelChangesNothing()
    {
        var song = NewSong();
        var settings = new EditingSettings();
        var requests = new List<RangePasteRequest>();
        PasteSpecialOptions? answer = null;
        MainWindowRangePaste.PastePrompt ask = request => { requests.Add(request); return Task.FromResult(answer); };
        Task<bool> Special() => MainWindowRangePaste.PasteAsync(song, song.Selection, Copy(2, 3), 0, special: true, ask, () => settings, () => { }, _ => { }).WaitAsync(Timeout);

        Assert.False(await Special());   // Cancel
        Assert.Equal(new[] { 0, 1 }, Frets(song, 0, 0, 1));
        Assert.Equal(new RangePasteOptions(), requests[0].Initial);
        Assert.True(requests[0].HasTabs && requests[0].HasClips);

        var chosen = new RangePasteOptions(RangePasteMode.Merge, RangePasteWhat.Tabs, RangeClipOverlap.NewTakeLane, 2);
        answer = new PasteSpecialOptions(2, Range: chosen, Remember: true);
        Assert.True(await Special());
        Assert.True(settings.RememberPasteSpecial);
        Assert.Equal(chosen, settings.PasteSpecialRange);

        answer = new PasteSpecialOptions(1, Range: new RangePasteOptions(), Remember: false);
        Assert.True(await Special());
        Assert.Equal(chosen, requests[^1].Initial);   // the second window started from the remembered choices
        Assert.True(requests[^1].Remember);
        Assert.False(settings.RememberPasteSpecial);

        Assert.True(await Special());
        Assert.Equal(new RangePasteOptions(), requests[^1].Initial);   // not remembered any more
    }

    private static T Named<T>(Control root, string name) where T : Control => root.GetLogicalDescendants().OfType<T>().Single(c => c.Name == name);

    [AvaloniaFact]
    public void ThePasteSpecialWindowShowsTheRangeSectionsTheClipboardNeeds()
    {
        var request = new RangePasteRequest(new RangePasteOptions(RangePasteMode.Insert, RangePasteWhat.Clips, RangeClipOverlap.NewTakeLane, 3), Remember: true);
        var window = new PasteSpecialWindow(request);
        window.Show();
        Assert.Equal(PasteSpecialPresenter.RangeModeLabels, window.GetLogicalDescendants().OfType<RadioButton>().Where(r => r.Name!.StartsWith("Mode")).Select(r => (string)r.Content!));
        Assert.Equal("3", Named<TextBox>(window, "RepeatBox").Text);
        Assert.True(Named<CheckBox>(window, "RememberBox").IsChecked);
        Assert.DoesNotContain(window.GetLogicalDescendants().OfType<Button>(), b => b.Name is "OctaveUp" or "OctaveDown");   // score-only options are not offered
        Assert.Equal(request.Initial with { Repeat = 3 }, window.Current().Range);
        Assert.True(window.Current().Remember);

        Named<RadioButton>(window, "Mode1").IsChecked = true;   // Merge never overlaps a clip
        Assert.False(Named<RadioButton>(window, "Overlap0").IsEnabled);
        Assert.Equal(RangePasteMode.Merge, window.Current().Range!.Mode);
        window.Close();

        var tabsOnly = new PasteSpecialWindow(new RangePasteRequest(new RangePasteOptions(), HasClips: false));
        tabsOnly.Show();
        Assert.DoesNotContain(tabsOnly.GetLogicalDescendants().OfType<RadioButton>(), r => r.Name!.StartsWith("What") || r.Name.StartsWith("Overlap"));
        tabsOnly.Close();

        var score = new PasteSpecialWindow(bars: true);
        score.Show();
        Assert.Null(score.Current().Range);   // a score clip keeps its own sections and options
        Assert.NotNull(Named<CheckBox>(score, "BarSettingsBox"));
        score.Close();
    }

    [Fact]
    public void TheRoutingTableSendsTimelineRangePasteToTheRangeCommands()
    {
        var routing = new EditRouting();
        var state = new EditSelectionState(HasRange: true, ClipboardHasBars: true);
        Assert.Equal(EditRouting.RangePasteId, routing.Resolve(FocusTarget.Loop, state, EditVerb.Paste).CommandId);
        Assert.Equal(EditRouting.RangePasteSpecialId, routing.Resolve(FocusTarget.Loop, state, EditVerb.PasteSpecial).CommandId);
        Assert.Equal((FocusTarget.Loop, EditVerb.Paste), routing.Classify(EditRouting.RangePasteId));
        Assert.Equal("Range.Paste", routing.Pick(FocusTarget.Loop, new[] { "Edit.Paste", "Range.Paste", "Clip.Paste" }));
        Assert.Equal("Edit.Paste", routing.Pick(FocusTarget.ScoreSelection, new[] { "Edit.Paste", "Range.Paste", "Clip.Paste" }));
        Assert.Contains(HotkeyCatalog.All, a => a.Id == "Range.Paste" && a.DefaultGesture == "Ctrl+V");
    }

    private sealed class FakeSystemClipboard : IScoreClipboard
    {
        public uint SequenceNumber { get; private set; } = 1;
        public bool TryWrite(string json) { SequenceNumber++; return true; }
        public bool TryWriteRange(string json, string asciiTab) { SequenceNumber++; return true; }
        public ScoreClipboardRead TryRead(int maxBytes) => new(true, null);
        public void CopiedElsewhere() => SequenceNumber++;
    }

    [Fact]
    public void TheNewestCopyDecidesWhetherARangeIsPasted()
    {
        var system = new FakeSystemClipboard();
        var clipboard = new ClipboardService(system);
        clipboard.CopyRange(Copy(2, 3));
        Assert.NotNull(clipboard.TryGetRange());

        clipboard.Copy(ClipboardService.CaptureBars(NewSong().Project, new[] { 0 }, 1, 1));
        Assert.Null(clipboard.TryGetRange());   // a score copy came after

        clipboard.CopyRange(Copy(2, 3));
        Assert.NotNull(clipboard.TryGetRange());
        system.CopiedElsewhere();
        Assert.Null(clipboard.TryGetRange());   // another application copied something
    }

    [AvaloniaFact]
    public async Task ScorePasteOfACopiedRangeWritesTheNotesOfTheShownTrackAtTheCursor()
    {
        var view = new ScorePreviewView();
        var flow = new MainWindowClipboard(view, new AvaloniaScoreClipboard(() => null), () => new EditingSettings(), _ => Task.FromResult<PasteSpecialOptions?>(null));
        var source = new SongProject();
        var guitar = new TrackModel { Name = "Guitar", Measures = Enumerable.Range(0, 2).Select(bar => new MeasureModel { Number = bar + 1 }).ToList() };
        guitar.Measures[0].Cells[0].Notes.Add(new TabNote { StringIndex = 0, Fret = 7 });
        guitar.Measures[1].Cells[0].Notes.Add(new TabNote { StringIndex = 0, Fret = 9 });
        source.Tracks.Add(guitar);
        flow.Clipboard.CopyRange(RangeCapture.Capture(source, 0, 1));

        var editor = view.Editor;
        editor.SetPosition(2, 0, 0);
        Assert.True(await flow.PasteAsync().WaitAsync(Timeout));

        var track = editor.Project!.Tracks[0];
        Assert.Equal(7, track.Measures[2].Cells[0].Notes[0].Fret);
        Assert.Equal(9, track.Measures[3].Cells[0].Notes[0].Fret);
        Assert.True(view.Run("Edit.Undo"));   // one undo step
        Assert.DoesNotContain(editor.Project!.Tracks[0].Measures[2].Cells, cell => cell.Notes.Any(n => n.Fret == 7));
    }
}
