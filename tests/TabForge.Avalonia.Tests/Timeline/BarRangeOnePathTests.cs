using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the one path of every bar-range edit (the score hotkeys go through TimelineRangeEdit like the timeline, and EditRouting lists them) and the written-time rule:
//   with a repeat before the range, Span, capture, paste, clip impact and remove all work on the written bars, never on performed repeat time.
// Does not own: the delete prompt (TimelineRangeDeleteTests), the gap rules themselves (TestBarRangeGaps).
// Tests: this class.
public sealed class BarRangeOnePathTests
{
    private const int Bars = 8;

    // 8 bars of 4/4; bars 2-3 (index 1-2) are a repeat played twice, so the performed song is two bars longer than the written one.
    private static SongProject RepeatSong()
    {
        var project = new SongProject();
        project.Tracks.Add(new TrackModel
        {
            Name = "Guitar",
            Measures = Enumerable.Range(0, Bars).Select(bar =>
            {
                var measure = new MeasureModel { Number = bar + 1 };
                measure.Cells[0].Notes.Add(new TabNote { StringIndex = 0, Fret = bar });
                return measure;
            }).ToList(),
        });
        project.Tracks[0].Measures[1].RepeatStart = true;
        project.Tracks[0].Measures[2].RepeatEnd = true;
        project.Tracks.Add(NewTrackFactory.CreateTrack(project, TrackKind.Audio));
        return project;
    }

    private static double BarSec(SongProject project) => MusicTime.BarMs(project, 0) / 1000.0;

    private static AudioClip Clip(double start, double length) => new() { File = "take.wav", Name = "take", StartSec = start, SourceLengthSec = length, FileLengthSec = length };

    [Fact]
    public void SpanAfterARepeatIsWrittenTime()
    {
        var project = RepeatSong();
        var bar = BarSec(project);
        var span = BarRangeGaps.Span(project, 4, 5);
        Assert.NotNull(span);
        Assert.Equal(4 * bar, span!.Value.From, 3);
        Assert.Equal(6 * bar, span.Value.To, 3);
    }

    [Fact]
    public void CaptureImpactAndRemoveTouchExactlyTheWrittenBars()
    {
        var project = RepeatSong();
        var bar = BarSec(project);
        var audio = project.Tracks[1];
        audio.AudioClips.Add(Clip(2.5 * bar, bar));    // before the range
        audio.AudioClips.Add(Clip(4.25 * bar, bar));   // inside written bars 5-6
        audio.AudioClips.Add(Clip(7.25 * bar, bar));   // after
        var impact = ClipDeleteImpact.Find(project, new[] { (4, 5) });
        Assert.NotNull(impact);
        Assert.Equal(1, impact!.Sum(i => i.Clips));
        var snapshot = RangeCapture.Capture(project, 4, 5);
        Assert.Equal(2 * bar, snapshot.LengthSec, 3);
        Assert.Single(snapshot.Tracks[1].Clips);
        var result = TimelineRangeEdit.Apply(project, 4, 5, BarRangeAction.Remove, -1, fillRests: false);
        Assert.True(result.Changed);
        Assert.Equal(new[] { 2.5 * bar, 5.25 * bar }, audio.AudioClips.Select(c => c.StartSec).OrderBy(s => s).ToArray());
    }

    [Fact]
    public void PasteLandsOnTheWrittenBar()
    {
        var project = RepeatSong();
        var bar = BarSec(project);
        project.Tracks[1].AudioClips.Add(Clip(4.25 * bar, bar));
        var snapshot = RangeCapture.Capture(project, 4, 5);
        RangePaste.Apply(project, snapshot, 6, 0, new RangePasteOptions());
        Assert.Contains(project.Tracks[1].AudioClips, c => Math.Abs(c.StartSec - 6.25 * bar) < 0.001);
    }

    [Fact]
    public void EveryBarRangeCommandIsInTheRoutingTable()
    {
        var routing = new EditRouting();
        foreach (var id in new[] { "Range.Clear", "Range.Remove", "Range.InsertBefore", "Range.InsertAfter" })
        {
            Assert.NotNull(routing.Classify(id));
            Assert.Equal(id, routing.Pick(FocusTarget.Loop, new[] { id }));
            Assert.Equal(id, routing.Pick(FocusTarget.ScoreSelection, new[] { id }));
        }
        Assert.Equal("Range.Remove", routing.Resolve(FocusTarget.ScoreSelection, new EditSelectionState(HasScoreSelection: true), EditVerb.RemoveBars).CommandId);
    }

    [AvaloniaFact]
    public void ScoreRangeRemoveOverARepeatEdgeIsRefusedWithTheNotice()
    {
        var project = RepeatSong();
        var document = new SongDocument(() => null) { Project = project };
        document.MarkClean();
        var score = new ScorePreviewView();
        score.Show(document);
        var selection = new SelectionModel();
        selection.SetRange(0, 2, 3, SelectionOrigin.Timeline);
        var notices = new List<string>();
        MainWindowRangeCommands.RunOnSelection(score, selection, notices.Add, BarRangeAction.Remove);
        Assert.Single(notices);
        Assert.Contains("repeat", notices[0]);
        Assert.Equal(Bars, project.Tracks[0].Measures.Count);
        Assert.False(document.Undo.CanUndo);
    }

    [AvaloniaFact]
    public void ScoreRangeRemoveOfBarOneKeepsTheLaterMeter()
    {
        var project = RepeatSong();
        project.Tracks[0].Measures[1].RepeatStart = false;
        project.Tracks[0].Measures[2].RepeatEnd = false;
        foreach (var track in project.Tracks)
            if (track.Measures.Count > 1) { track.Measures[1].TimeSigNum = 3; track.Measures[1].TimeSigDenom = 4; }
        var document = new SongDocument(() => null) { Project = project };
        document.MarkClean();
        var score = new ScorePreviewView();
        score.Show(document);
        var selection = new SelectionModel();
        selection.SetRange(0, 0, 0, SelectionOrigin.Timeline);
        MainWindowRangeCommands.RunOnSelection(score, selection, _ => { }, BarRangeAction.Remove);
        Assert.Equal(3, project.TimeSignatureNumerator);
        Assert.Equal(Bars - 1, project.Tracks[0].Measures.Count);
    }
}
