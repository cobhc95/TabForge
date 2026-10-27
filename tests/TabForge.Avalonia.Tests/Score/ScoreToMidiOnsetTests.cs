using System.Collections.Generic;
using System.Linq;
using TabForge.Models;
using TabForge.Playback;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the compiler places every beat at its written onset (RhythmicPosition, else index/end of the beats before)
/// in each voice lane, and a mid-bar start compares onsets, not cell indices.
/// Tests: this class. 120 bpm 4/4: one slot is 125 ms.
/// </summary>
public class ScoreToMidiOnsetTests
{
    private const double SlotMs = 125;

    private static TabCell Quarter(double? position = null, int stringIndex = 0, int denominator = 4)
    {
        var cell = new TabCell { DurationDenominator = denominator, RhythmicPosition = position };
        cell.Notes.Add(new TabNote { StringIndex = stringIndex, Fret = 3 });
        return cell;
    }

    private static ScoreTimeline Compile(MeasureModel bar, int startCell = 0)
    {
        var song = new SongProject { Tempo = 120 };
        song.Tracks.Add(new TrackModel { Name = "Gtr", Measures = new List<MeasureModel> { bar } });
        return MidiTimelineBuilder.Build(song, new PlaybackOptions { LoopEndBar = 0, StartCell = startCell });
    }

    private static List<double> Onsets(MeasureModel bar, int startCell = 0, int voice = -1)
    {
        var timeline = Compile(bar, startCell);
        return timeline.Notes.Where(n => voice < 0 || n.VoiceIndex == voice).Select(n => n.OnsetMs).OrderBy(x => x).ToList();
    }

    private static List<TabCell> Sparse(params (int Index, TabCell Cell)[] placed)
    {
        var cells = Enumerable.Range(0, placed.Max(p => p.Index) + 1).Select(_ => new TabCell { DurationDenominator = 16 }).ToList();
        foreach (var (index, cell) in placed) cells[index] = cell;
        return cells;
    }

    [Fact]
    public void ASecondVoiceLandsAtItsWrittenPosition()
    {
        var bar = new MeasureModel { Cells = Sparse((0, Quarter()), (4, Quarter()), (8, Quarter()), (12, Quarter())) };
        bar.Voice2Cells = new List<TabCell> { Quarter(8, 1, 2) };
        Assert.Equal(new[] { 1000.0 }, Onsets(bar, voice: 1));
    }

    [Fact]
    public void OverlappingBeatsKeepTheirWrittenOnsets()
    {
        var bar = new MeasureModel { Cells = new List<TabCell> { Quarter(0), Quarter(4, 1), Quarter(4, 2), Quarter(8, 3) } };
        Assert.Equal(new[] { 0.0, 500, 500, 1000 }, Onsets(bar));
    }

    [Fact]
    public void APlainBarKeepsItsSequentialOnsets()
    {
        var bar = new MeasureModel { Cells = Sparse((0, Quarter()), (4, Quarter()), (8, Quarter()), (12, Quarter())) };
        Assert.Equal(new[] { 0.0, 500, 1000, 1500 }, Onsets(bar));
    }

    [Fact]
    public void AnOffGridTupletOnsetIsKept()
    {
        var bar = new MeasureModel { Cells = new List<TabCell> { Quarter(0), Quarter(4.8) } };
        Assert.Equal(4.8 * SlotMs, Onsets(bar)[1], 3);
    }

    [Fact]
    public void AStartCellSkipsByOnsetNotByIndex()
    {
        var bar = new MeasureModel { Cells = new List<TabCell> { Quarter(0), Quarter(4), Quarter(8), Quarter(12) } };
        Assert.Equal(1000.0, Onsets(bar, startCell: 6)[0]);
    }

    private static TabCell Tied(double position, int denominator = 4)
    {
        var cell = Quarter(position, 0, denominator);
        cell.Notes[0].Tied = true;
        return cell;
    }

    [Fact]
    public void ATieAcrossAWrittenPositionContinuesTheOrigin()
    {
        var timeline = Compile(new MeasureModel { Cells = new List<TabCell> { Quarter(0), Tied(4) } });
        Assert.Equal(1, timeline.TieMerges);
        Assert.Equal(0, timeline.TieOrphans);
    }

    [Fact]
    public void ATieFromASwungEighthInAMappedBarContinues()
    {
        var bar = new MeasureModel
        {
            TripletFeel = true,
            MidBarTempos = new List<TempoPoint> { new(12, 120) },
            Cells = new List<TabCell> { Quarter(0, 1, 8), Quarter(2, 0, 8), Tied(4) }
        };
        bar.Cells[2].Notes[0].StringIndex = 0;
        var timeline = Compile(bar);
        Assert.Equal(1, timeline.TieMerges);
        Assert.Equal(0, timeline.TieOrphans);
    }

    [Fact]
    public void AMixOnAnEmptyCellInsideAHeldBeatSitsAtTheEndOfTheBeat()
    {
        var held = Quarter(0, 0, 2);
        var cells = Sparse((0, held), (2, new TabCell { DurationDenominator = 16, Mix = new MixChange { Volume = 5 } }));
        var timeline = Compile(new MeasureModel { Cells = cells });
        Assert.Contains(timeline.Events, e => e.Status == 0xB0 && e.Data1 == 7 && System.Math.Abs(e.TimeMs - 8 * SlotMs) < 0.5);
    }
}
