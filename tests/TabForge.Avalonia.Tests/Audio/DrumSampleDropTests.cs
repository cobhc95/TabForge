using System.Collections.Generic;
using System.Linq;
using Avalonia.Headless.XUnit;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the Composer drum samples and their landing rules: the catalogue, the tab-lane write (drum map on a drum track, rhythm on a pitched one),
//   the MIDI-lane clip (a new lane when the track is occupied, the bar's first slot, GM drum channel), one undo step each, and the lane choice's stored values.
// Does not own: the drag and its ghost, auditioning, or the lane prompt window (MainWindowComposerDrums, DrumSampleLanePromptWindow).
// Tests: this class.
public sealed class DrumSampleDropTests
{
    private static SongDocument SongWithDrumTrack(out TrackModel drums)
    {
        var song = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        drums = NewTrackFactory.CreateTrack(song.Project, TrackKind.Drums);
        song.Project.Tracks.Add(drums);
        return song;
    }

    [Fact]
    public void Catalogue_HasTheSixStarterSamples()
    {
        Assert.Equal(6, DrumSamples.All.Count);
        Assert.Equal(DrumSamples.All.Count, DrumSamples.All.Select(s => s.Id).Distinct().Count());
        foreach (var sample in DrumSamples.All)
        {
            Assert.True(sample.Bars > 0);
            Assert.True(sample.SuggestedBpm > 0);
            Assert.All(sample.KickSteps.Concat(sample.SnareSteps).Concat(sample.HatSteps), step => Assert.InRange(step, 0, DrumSample.StepsPerBar - 1));
            Assert.Same(sample, DrumSamples.ById(sample.Id));
        }
        Assert.Null(DrumSamples.ById("no-such-sample"));
    }

    [Fact]
    public void SampleCells_MatchThePatternSteps()
    {
        var gallop = DrumSamples.ById("gallop-fill")!;
        var cells = gallop.Cells();

        Assert.Equal(16, cells.Count);
        Assert.Equal(new[] { DrumSample.KickMidi, DrumSample.HatMidi }, cells[0].Notes.Select(n => n.MidiValue).OrderBy(m => m));
        Assert.Equal(new[] { DrumSample.SnareMidi, DrumSample.HatMidi }, cells[3].Notes.Select(n => n.MidiValue).OrderBy(m => m));
        Assert.Empty(cells[4].Notes);
        // A hit lasts until the next hit: step 0 rings to step 3 (a dotted 8th), step 14 to the bar line (an 8th).
        Assert.Equal((8, 1), (cells[0].DurationDenominator, cells[0].Dots));
        Assert.Equal((8, 0), (cells[14].DurationDenominator, cells[14].Dots));
    }

    [Fact]
    public void SampleCells_HitDurationsFillEveryBar()
    {
        foreach (var sample in DrumSamples.All)
        {
            var cells = sample.Cells();
            for (var bar = 0; bar < sample.Bars; bar++)
            {
                var filled = cells.Skip(bar * DrumSample.StepsPerBar).Take(DrumSample.StepsPerBar)
                    .Where(c => c.Notes.Count > 0).Sum(c => MusicTime.CellSlots(c));
                Assert.Equal(DrumSample.StepsPerBar, filled);
            }
        }
    }

    [Fact]
    public void Velocities_FollowTheAccentPatternNotChance()
    {
        var straight = DrumSamples.ById("straight-8ths")!;
        var hits = straight.Hits().ToList();

        // The same sample always produces the same hits, on the same rigid steps.
        Assert.Equal(hits, straight.Hits().ToList());
        Assert.All(hits, h => Assert.Contains(h.Velocity, Dynamics.Velocities));
        Assert.True(hits.Select(h => h.Velocity).Distinct().Count() >= 3);

        int Vel(int bar, int step, int midi) => hits.Single(h => h.Bar == bar && h.Step == step && h.Midi == midi).Velocity;
        Assert.Equal(112, Vel(0, 0, DrumSample.KickMidi));    // the kick opening a bar: ff
        Assert.Equal(112, Vel(0, 4, DrumSample.SnareMidi));   // the backbeats: ff
        Assert.Equal(112, Vel(0, 12, DrumSample.SnareMidi));
        Assert.Equal(95, Vel(0, 0, DrumSample.HatMidi));      // a hat on a quarter: f
        Assert.Equal(80, Vel(0, 2, DrumSample.HatMidi));      // a hat on an 8th off-beat: mf
        Assert.Equal(112, Vel(3, 0, DrumSample.HatMidi));      // the last bar lifts its quarter-note hits: f -> ff
    }

    [Fact]
    public void Velocities_SixteenthOffBeatsSitBelowEighthPositions()
    {
        var gallop = DrumSamples.ById("gallop-fill")!;
        var hits = gallop.Hits().ToList();
        int Vel(int step, int midi) => hits.Single(h => h.Step == step && h.Midi == midi).Velocity;
        Assert.Equal(64, Vel(3, DrumSample.SnareMidi));   // a snare on a 16th off-beat: mp, a ghost weight
        Assert.Equal(80, Vel(6, DrumSample.KickMidi));    // a kick on an 8th position: mf
        Assert.All(DrumSamples.All.SelectMany(s => s.Hits()), h => Assert.Contains(h.Velocity, Dynamics.Velocities));
    }
    [AvaloniaFact]
    public void TabDrop_EverySampleLeavesItsBarsComplete()
    {
        foreach (var sample in DrumSamples.All)
        {
            var song = SongWithDrumTrack(out var drums);
            var result = DrumSampleDrop.Apply(song, sample, drums, newTrack: false, bar: 2, DrumSampleLane.Tab);
            Assert.NotNull(result);
            for (var bar = 2; bar < 2 + sample.Bars; bar++)
            {
                var state = MusicTime.AnalyzeBar(song.Project, bar, drums);
                Assert.False(state.Marked, $"{sample.Id} bar {bar + 1} is marked: {state}");
                Assert.True(state.Complete, $"{sample.Id} bar {bar + 1} is incomplete: {state}");
            }
        }
    }
    [AvaloniaFact]
    public void TabDropOnADrumTrack_WritesNotationAtTheBarInOneUndoStep()
    {
        var song = SongWithDrumTrack(out var drums);
        var gallop = DrumSamples.ById("gallop-fill")!;

        var result = DrumSampleDrop.Apply(song, gallop, drums, newTrack: false, bar: 2, DrumSampleLane.Tab);

        Assert.NotNull(result);
        Assert.Same(drums, result!.Track);
        Assert.Equal(DrumSampleLane.Tab, result.Lane);
        Assert.Contains("bar 3", result.Status);
        var notes = drums.Measures[2].Cells.SelectMany(c => c.Notes).ToList();
        Assert.Equal(12, notes.Count);   // 4 kicks + 2 snares + 6 hats
        Assert.All(notes, n => Assert.Contains(n.MidiValue, new[] { DrumSample.KickMidi, DrumSample.SnareMidi, DrumSample.HatMidi }));
        Assert.All(notes, n => Assert.InRange(n.StringIndex, 0, drums.StringTunings.Count - 1));
        Assert.Equal(new[] { 64, 80, 95, 112 }, notes.Select(n => n.Velocity).Distinct().OrderBy(v => v));   // the accent pattern survives into the tab

        Assert.NotNull(DocumentEdits.Undo(song));
        var restored = song.Project.Tracks.Single(t => t.Id == drums.Id);
        Assert.Empty(restored.Measures[2].Cells.SelectMany(c => c.Notes));
    }

    [AvaloniaFact]
    public void TabDropOnAPitchedTrack_LandsTheRhythmOnTheLowestString()
    {
        var song = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        var guitar = NewTrackFactory.CreateTrack(song.Project, TrackKind.Guitar);
        song.Project.Tracks.Add(guitar);
        var gallop = DrumSamples.ById("gallop-fill")!;

        // Bar 2 of the demo song is 4/4 (bar 1 is 6/8).
        var result = DrumSampleDrop.Apply(song, gallop, guitar, newTrack: false, bar: 1, DrumSampleLane.Tab);

        Assert.NotNull(result);
        var notes = guitar.Measures[1].Cells.SelectMany(c => c.Notes).ToList();
        Assert.Equal(6, notes.Count);   // one merged rhythm note per hit cell
        Assert.All(notes, n => Assert.Equal(5, n.StringIndex));   // guitar tunings: string 5 is the lowest open (40)
        Assert.All(notes, n => Assert.Equal(40, n.MidiValue));
    }

    [AvaloniaFact]
    public void TabDropOverExistingNotes_ReplacesTheSpanAtTheBeatPositions()
    {
        var song = SongWithDrumTrack(out var drums);
        // A bar of four quarter-note crashes: four cells, not sixteen, so the hits must land by time, not by cell index.
        drums.Measures[1].Cells = Enumerable.Range(0, 4).Select(_ => new TabCell { DurationDenominator = 4, Notes = { new TabNote { StringIndex = 0, Fret = 49, MidiValue = 49 } } }).ToList();
        var straight = DrumSamples.ById("straight-8ths")!;

        var result = DrumSampleDrop.Apply(song, straight, drums, newTrack: false, bar: 1, DrumSampleLane.Tab);

        Assert.NotNull(result);
        var measure = drums.Measures[1];
        Assert.DoesNotContain(measure.Cells.SelectMany(c => c.Notes), n => n.MidiValue == 49);
        var onsets = BarGrid.Onsets(measure.Cells);
        var snareOnsets = measure.Cells.Select((c, i) => (c, i)).Where(x => x.c.Notes.Any(n => n.MidiValue == DrumSample.SnareMidi)).Select(x => onsets[x.i]).ToList();
        Assert.Equal(new double[] { 4, 12 }, snareOnsets);
    }

    [AvaloniaFact]
    public void TabDropInAThreeFourBar_RunsAcrossTheBarLineWithoutFailing()
    {
        var song = SongWithDrumTrack(out var drums);
        BarSignatures.SetTime(song.Project, 0, 3, 4);
        var gallop = DrumSamples.ById("gallop-fill")!;

        var result = DrumSampleDrop.Apply(song, gallop, drums, newTrack: false, bar: 0, DrumSampleLane.Tab);

        Assert.NotNull(result);
        var hits = drums.Measures[0].Cells.Concat(drums.Measures[1].Cells).SelectMany(c => c.Notes).Count(n => !n.Tied);
        Assert.Equal(12, hits);   // the 16 steps fill bar 1 (12 slots) and the first beat of bar 2
    }

    [AvaloniaFact]
    public void TabDropOnAnAudioTrack_ChangesNothing()
    {
        var song = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        var audio = NewTrackFactory.CreateTrack(song.Project, TrackKind.Audio);
        song.Project.Tracks.Add(audio);

        var result = DrumSampleDrop.Apply(song, DrumSamples.All[0], audio, newTrack: false, bar: 0, DrumSampleLane.Tab);

        Assert.Null(result);
        Assert.False(song.Undo.CanUndo);
    }

    [AvaloniaFact]
    public void MidiDropOnAnEmptyTrack_LandsAClipOnLaneZeroAtTheBarStartInOneUndoStep()
    {
        var song = SongWithDrumTrack(out var drums);
        var gallop = DrumSamples.ById("gallop-fill")!;
        var startSec = AudioClipTimelineRules.SecondsAtBar(song.Project, 3, 0);

        var result = DrumSampleDrop.Apply(song, gallop, drums, newTrack: false, bar: 3, DrumSampleLane.Midi);

        Assert.NotNull(result);
        var clip = Assert.Single(drums.AudioClips);
        Assert.True(clip.IsMidi);
        Assert.Equal(0, clip.Lane);
        Assert.Equal(startSec, clip.StartSec, 6);
        Assert.NotNull(clip.Notes);
        Assert.Equal(12, clip.Notes!.Count);
        Assert.All(clip.Notes, n => Assert.Equal(9, n.Channel));
        Assert.All(clip.Notes, n => Assert.Contains(n.Pitch, new[] { DrumSample.KickMidi, DrumSample.SnareMidi, DrumSample.HatMidi }));
        Assert.Equal(clip.Notes.Select(n => n.StartSec).OrderBy(t => t), clip.Notes.Select(n => n.StartSec));
        Assert.All(clip.Notes, n => Assert.True(n.StartSec + n.LengthSec <= clip.SourceLengthSec + 0.001));

        Assert.NotNull(DocumentEdits.Undo(song));
        Assert.Empty(song.Project.Tracks.Single(t => t.Id == drums.Id).AudioClips);
    }

    [AvaloniaFact]
    public void MidiDropOnAnOccupiedTrack_CreatesANewLane()
    {
        var song = SongWithDrumTrack(out var drums);
        drums.AudioClips.Add(new AudioClip
        {
            Name = "old take", StartSec = 0, SourceLengthSec = 2, FileLengthSec = 2,
            Notes = new List<ClipNote> { new(0, 0.5, 38, 100) },
        });

        var result = DrumSampleDrop.Apply(song, DrumSamples.All[0], drums, newTrack: false, bar: 0, DrumSampleLane.Midi);

        Assert.NotNull(result);
        Assert.Equal(2, drums.AudioClips.Count);
        var added = drums.AudioClips[^1];
        Assert.True(added.Lane > drums.AudioClips[0].Lane);
        Assert.Equal(ClipLanes.Count(drums) - 1, added.Lane);
    }

    [AvaloniaFact]
    public void MidiDropOnTheStrip_CreatesADrumTrack()
    {
        var song = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        var before = song.Project.Tracks.Count;

        var result = DrumSampleDrop.Apply(song, DrumSamples.All[0], null, newTrack: true, bar: 0, DrumSampleLane.Midi);

        Assert.NotNull(result);
        Assert.Equal(before + 1, song.Project.Tracks.Count);
        Assert.Equal(TrackKind.Drums, result!.Track.Kind);
        Assert.Single(result.Track.AudioClips);
        Assert.NotNull(DocumentEdits.Undo(song));
        Assert.Equal(before, song.Project.Tracks.Count);
    }

    [Fact]
    public void Choice_Normalize_AsksForAnythingUnknown()
    {
        Assert.Equal(DrumSampleDropChoice.Ask, DrumSampleDropChoice.Normalize(null));
        Assert.Equal(DrumSampleDropChoice.Ask, DrumSampleDropChoice.Normalize("nonsense"));
        Assert.Equal(DrumSampleDropChoice.Tab, DrumSampleDropChoice.Normalize("Tab"));
        Assert.Equal(DrumSampleDropChoice.Midi, DrumSampleDropChoice.Normalize("Midi"));
        Assert.Equal(DrumSampleDropChoice.Tab, DrumSampleDropChoice.Store(new DrumSampleLaneAnswer(DrumSampleLane.Tab, Remember: true)));
        Assert.Equal(DrumSampleDropChoice.Midi, DrumSampleDropChoice.Store(new DrumSampleLaneAnswer(DrumSampleLane.Midi, Remember: true)));
    }

    [Fact]
    public void PromptBody_NamesTheSampleTrackAndBar()
    {
        var body = DrumSampleLanePromptPresenter.Body(new DrumSampleDropQuestion("Gallop fill", "Drums", 13, 1, TabAvailable: true));
        Assert.Contains("Gallop fill", body);
        Assert.Contains("Drums", body);
        Assert.Contains("bar 13", body);
    }
}
