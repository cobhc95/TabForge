using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the timeline range copy and cut: the snapshot of a range over several tracks (tab bars, clip parts cut at the edges, bar settings), mapping its tracks by kind, its private JSON
//   round trip and ASCII text slot, the legacy ScoreClip JSON still parsing, Cut as one undo step that only touches the clipboard after OK, and Copy never asking.
//   The prompt is always the injected fake: no modal window opens.
// Does not own: the delete rules (TimelineRangeDeleteTests), the routing table (EditRoutingTests), the score clipboard (MainWindowClipboardTests) or pasting (a later stage).
// Tests: this class.
public sealed class RangeClipboardTests
{
    private const int Bars = 8;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private sealed class FakeSystemClipboard : IScoreClipboard
    {
        public uint SequenceNumber { get; private set; } = 1;
        public string? Json { get; private set; }
        public string? AsciiTab { get; private set; }
        public int Writes { get; private set; }
        public bool TryWrite(string json) { Json = json; AsciiTab = null; Writes++; SequenceNumber++; return true; }
        public bool TryWriteRange(string json, string asciiTab) { Json = json; AsciiTab = asciiTab; Writes++; SequenceNumber++; return true; }
        public ScoreClipboardRead TryRead(int maxBytes) => new(true, AsciiTab ?? Json);
    }

    // Two guitar tracks of 8 bars (the fret of the first cell is the bar index) and an audio track whose clip lies over bars 2-6.
    private static SongDocument NewSong()
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
        audio.AudioClips.Add(new AudioClip { File = "take.wav", Name = "take", StartSec = span.From, SourceLengthSec = span.To - span.From, FileLengthSec = span.To - span.From, FadeInSec = 0.1, FadeOutSec = 0.1 });
        project.Tracks.Add(audio);
        var document = new SongDocument(() => null) { Project = project };
        document.MarkClean();
        return document;
    }

    private static MainWindowTimelineBars.DeletePrompt Answer(BarRangeAction? action, Action? asked = null) => (_, _, _) =>
    {
        asked?.Invoke();
        return Task.FromResult<BarRangeAnswer?>(action is { } picked ? new BarRangeAnswer(picked, AllTracks: true, Remember: false) : null);
    };

    private static Task<bool> Cut(SongDocument song, SelectionModel model, ClipboardService clipboard, MainWindowTimelineBars.DeletePrompt prompt)
    {
        var settings = new EditingSettings();
        return MainWindowRangeDelete.DeleteAsync(song, model, clipboard, _ => { }, prompt, () => settings, () => { }).WaitAsync(Timeout);
    }

    private static SelectionModel RangeOf(int first, int last)
    {
        var model = new SelectionModel();
        model.SetLoop(first, last);
        return model;
    }

    [Fact]
    public void CaptureOfTwoBarsOverThreeTracksCutsTheSpanningClipAtBothEdges()
    {
        var song = NewSong();
        var project = song.Project;
        var span = BarRangeGaps.Span(project, 2, 3).GetValueOrDefault();
        var clip = project.Tracks[2].AudioClips[0];
        var clipStart = clip.StartSec;

        var snapshot = RangeCapture.Capture(project, 2, 3);

        Assert.Equal(2, snapshot.BarCount);
        Assert.Equal(3, snapshot.Tracks.Count);
        Assert.Equal(span.To - span.From, snapshot.LengthSec, 3);
        Assert.All(snapshot.Bars, bar => Assert.Equal((4, 4), (bar.Numerator, bar.Denominator)));
        foreach (var tab in snapshot.Tracks.Take(2))
            Assert.Equal(new[] { 2, 3 }, tab.Bars.Select(bar => bar.Cells[0].Notes[0].Fret));
        Assert.Empty(snapshot.Tracks[2].Bars);
        var part = Assert.Single(snapshot.Tracks[2].Clips);
        Assert.Equal(0, part.OffsetSec, 3);
        Assert.Equal(span.To - span.From, part.Clip.SourceLengthSec, 3);
        Assert.Equal(span.From - clipStart, part.Clip.OffsetSec, 3);
        Assert.Equal((0.0, 0.0), (part.Clip.FadeInSec, part.Clip.FadeOutSec));
        Assert.Equal(clipStart, clip.StartSec);
    }

    [Fact]
    public void CaptureKeepsTheFadeOnAnUncutEdgeAndPositionsThePartInsideTheRange()
    {
        var song = NewSong();
        var clip = song.Project.Tracks[2].AudioClips[0];
        var snapshot = RangeCapture.Capture(song.Project, 4, 7);
        var part = Assert.Single(snapshot.Tracks[2].Clips);
        var rangeStart = BarRangeGaps.Span(song.Project, 4, 4).GetValueOrDefault().From;
        Assert.Equal(0, part.OffsetSec, 3);
        Assert.Equal(clip.EndSec - rangeStart, part.Clip.SourceLengthSec, 3);
        Assert.Equal(0.1, part.Clip.FadeOutSec, 3);

        var early = RangeCapture.Capture(song.Project, 0, 2);
        var earlyPart = Assert.Single(early.Tracks[2].Clips);
        Assert.Equal(clip.StartSec, earlyPart.OffsetSec, 3);
        Assert.Equal(0.1, earlyPart.Clip.FadeInSec, 3);
        Assert.Equal(0, earlyPart.Clip.FadeOutSec, 3);
        Assert.Empty(RangeCapture.Capture(song.Project, 7, 7).Tracks[2].Clips);
    }

    [Fact]
    public void MappingGoesByKindInOrderAndSkipsWhatDoesNotFit()
    {
        var source = RangeCapture.Capture(NewSong().Project, 2, 3);
        var target = new SongProject();
        target.Tracks.Add(new TrackModel { Name = "Lead", Kind = TrackKind.Guitar });
        target.Tracks.Add(new TrackModel { Name = "Low", Kind = TrackKind.Bass });
        target.Tracks.Add(NewTrackFactory.CreateTrack(target, TrackKind.Audio));

        var all = PasteMapper.Map(source, target.Tracks, 0);
        Assert.Equal(new[] { new TrackPairing(0, 0), new TrackPairing(1, 1), new TrackPairing(2, 2) }, all.Pairs);
        Assert.Equal(0, all.Skipped);
        Assert.Null(all.Notice);

        var shifted = PasteMapper.Map(source, target.Tracks, 1);
        Assert.Equal(new[] { new TrackPairing(0, 1) }, shifted.Pairs);
        Assert.Equal(2, shifted.Skipped);
        Assert.Equal("2 tracks skipped: no matching track", shifted.Notice);

        var audioOnTab = PasteMapper.Map(source, target.Tracks.Take(2).ToList(), 0);
        Assert.Equal(2, audioOnTab.Pairs.Count);
        Assert.Equal("1 track skipped: no matching track", audioOnTab.Notice);
    }

    [Fact]
    public void SnapshotJsonRoundTripsAndOnlyRangeJsonParses()
    {
        var snapshot = RangeCapture.Capture(NewSong().Project, 2, 3);
        var json = snapshot.ToJson();

        Assert.True(RangeSnapshot.TryParse(json, out var read, out var error));
        Assert.Null(error);
        Assert.Equal(json, read!.ToJson());
        Assert.Equal(snapshot.Tracks.Select(t => t.Kind), read.Tracks.Select(t => t.Kind));
        Assert.Equal(snapshot.Tracks[2].Clips[0].Clip.SourceLengthSec, read.Tracks[2].Clips[0].Clip.SourceLengthSec);

        Assert.False(RangeSnapshot.TryParse("not json", out _, out var failure));
        Assert.Equal(ClipboardService.NotTabForgeNotesMessage, failure);
        Assert.False(RangeSnapshot.TryParse(json.Replace(RangeSnapshot.FormatName, "Other"), out _, out _));
        var scoreClip = ClipboardService.CaptureBars(NewSong().Project, new[] { 0 }, 2, 3).ToJson();
        Assert.False(RangeSnapshot.TryParse(scoreClip, out _, out _));
    }

    [Fact]
    public void LegacyScoreClipJsonStillParsesAndRangeCopyDoesNotChangeIt()
    {
        var project = NewSong().Project;
        var system = new FakeSystemClipboard();
        var clipboard = new ClipboardService(system);
        var scoreClip = ClipboardService.CaptureBars(project, new[] { 0, 1 }, 2, 3);
        clipboard.Copy(scoreClip);
        var json = system.Json!;

        Assert.True(ScoreClip.TryParse(json, out var read, out _));
        Assert.Equal(2, read!.BarCount);

        clipboard.CopyRange(RangeCapture.Capture(project, 2, 3));

        Assert.Same(scoreClip, clipboard.Current);
        Assert.NotNull(clipboard.CurrentRange);
        Assert.True(ScoreClip.TryParse(json, out var again, out _));
        Assert.Equal(read.ToJson(), again!.ToJson());
    }

    [Fact]
    public void CopyWritesPrivateJsonAndAsciiTextAndNeverAsks()
    {
        var song = NewSong();
        var system = new FakeSystemClipboard();
        var clipboard = new ClipboardService(system);
        var notices = new List<string>();

        Assert.True(MainWindowRangeDelete.Copy(song.Project, RangeOf(2, 3), clipboard, notices.Add));

        Assert.Equal(1, system.Writes);
        Assert.True(RangeSnapshot.TryParse(system.Json, out var written, out _));
        Assert.Equal(2, written!.BarCount);
        Assert.Contains("[Guitar 1]", system.AsciiTab);
        Assert.Contains("|2--", system.AsciiTab);
        Assert.Equal("Copied bars 3-4 of all tracks", Assert.Single(notices));
        Assert.False(song.Undo.CanUndo);
        Assert.Equal(Bars, song.Project.Tracks[0].Measures.Count);
    }

    [Fact]
    public async Task CutCopiesThenDeletesAsOneUndoStep()
    {
        var song = NewSong();
        var system = new FakeSystemClipboard();
        var clipboard = new ClipboardService(system);
        var model = RangeOf(2, 3);

        Assert.True(await Cut(song, model, clipboard, Answer(BarRangeAction.Remove)));

        Assert.Equal(Bars - 2, song.Project.Tracks[0].Measures.Count);
        var copied = Assert.IsType<RangeSnapshot>(clipboard.CurrentRange);
        Assert.Equal(new[] { 2, 3 }, copied.Tracks[0].Bars.Select(bar => bar.Cells[0].Notes[0].Fret));
        Assert.Single(copied.Tracks[2].Clips);
        Assert.Equal(1, system.Writes);
        Assert.NotNull(DocumentEdits.Undo(song));
        Assert.False(song.Undo.CanUndo);
        Assert.Equal(Bars, song.Project.Tracks[0].Measures.Count);
        Assert.Equal(new[] { 2, 3 }, song.Project.Tracks[0].Measures.Skip(2).Take(2).Select(m => m.Cells[0].Notes[0].Fret));
    }

    [Fact]
    public async Task CancelInTheDialogLeavesTheOldClipboardAndTheSongUntouched()
    {
        var song = NewSong();
        var system = new FakeSystemClipboard();
        var clipboard = new ClipboardService(system);
        var old = RangeCapture.Capture(song.Project, 0, 0);
        clipboard.CopyRange(old);
        var oldJson = system.Json;
        var asked = 0;

        Assert.False(await Cut(song, RangeOf(2, 3), clipboard, Answer(null, () => asked++)));

        Assert.Equal(1, asked);
        Assert.Same(old, clipboard.CurrentRange);
        Assert.Equal(oldJson, system.Json);
        Assert.Equal(1, system.Writes);
        Assert.False(song.Undo.CanUndo);
        Assert.Equal(Bars, song.Project.Tracks[0].Measures.Count);
    }

    [Fact]
    public async Task ARefusedCutLeavesTheClipboardAlone()
    {
        var song = NewSong();
        song.Project.Tracks[0].Measures[1].RepeatStart = true;
        song.Project.Tracks[0].Measures[4].RepeatEnd = true;
        var clipboard = new ClipboardService(new FakeSystemClipboard());

        Assert.False(await Cut(song, RangeOf(3, 5), clipboard, Answer(BarRangeAction.Remove)));

        Assert.Null(clipboard.CurrentRange);
        Assert.False(song.Undo.CanUndo);
    }
}
