using System.Collections.Generic;
using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using TabForge.Avalonia.Platform;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Presenters;
using TabForge.Presets;
using TabForge.Services;
using TabForge.Views.Score;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the score cursor-stop gaps (beats only, chords as one stop, no resting inside a
/// beat, bar-crossing moves, and writing leaving the cursor put).
/// Tests: CursorPositions, ScoreCursorStops, ScoreCursorNavigator, ScorePreviewEditor.
/// </summary>
public class ScoreCursorStopsTests
{
    private static (ScoreGeometryProbe Probe, ScorePreviewEditor Editor, TrackModel Track) MakeDemo()
    {
        var song = DemoSongFactory.Create();
        var probe = new ScoreGeometryProbe(song, new AvaloniaDrawContext());
        return (probe, new ScorePreviewEditor(probe, song, Views.PreferencesCatalog.DefaultHotkeys()), song.Tracks[0]);
    }

    private static bool Press(ScorePreviewEditor editor, Key key, bool ctrl = false, bool shift = false) =>
        editor.Key(AvaloniaEditorKeys.ToEditorKey(key), key.ToString(), ctrl, shift, false);

    private static SongProject TwoBarQuarters(out TrackModel track)
    {
        var song = new SongProject { Tempo = 120 };
        track = new TrackModel
        {
            Name = "G",
            Kind = TrackKind.Guitar,
            StringTunings = new List<int> { 64, 59, 55, 50, 45, 40 },
            Measures = new List<MeasureModel> { new MeasureModel { Number = 1 }, new MeasureModel { Number = 2 } }
        };
        foreach (var bar in new[] { 0, 1 })
            foreach (var i in new[] { 0, 4, 8, 12 })
            {
                var cell = track.Measures[bar].Cells[i];
                cell.DurationDenominator = 4; // MusicTime.cs:36 quarter = 4 slots
                cell.Notes.Add(new TabNote { StringIndex = 0, Fret = 5, MidiValue = track.PitchOf(0, 5) }); // SongProject.cs:151
            }
        song.Tracks.Add(track);
        return song;
    }

    private static ScoreCursorStops StopsFor(SongProject song, TrackModel track) =>
        new(track, m => m.CellsForVoice(0), m => MusicTime.BarSlots(song, m)); // ScoreCursorStops.cs:9

    [Fact]
    public void FourQuartersIn44_HasExactlyFourStops()
    {
        var song = TwoBarQuarters(out var track);
        var cells = track.Measures[0].CellsForVoice(0); // SongProject.cs:254
        Assert.Equal(new[] { 0, 4, 8, 12 }, CursorPositions.Allowed(cells, 16)); // CursorPositions.cs:27
    }

    [Fact]
    public void HalfPlusTwoQuarters_HasThreeStopsAtBeats1_3_4()
    {
        var song = TwoBarQuarters(out var track);
        var cells = track.Measures[0].CellsForVoice(0);
        cells[0].DurationDenominator = 2; // half = 8 slots (MusicTime.cs:36)
        Assert.Equal(new[] { 0, 8, 12 }, CursorPositions.Allowed(cells, 16)); // CursorPositions.cs:27
    }

    [Fact]
    public void Chord_IsOneStop()
    {
        var cells = Enumerable.Range(0, 16).Select(_ => new TabCell()).ToList();
        cells[0].DurationDenominator = 2; // half = 8 slots
        foreach (var s in new[] { 0, 1, 2 })
            cells[0].Notes.Add(new TabNote { StringIndex = s, Fret = 5, MidiValue = 60 + s });
        cells[8].DurationDenominator = 4;
        cells[8].Notes.Add(new TabNote { StringIndex = 0, Fret = 3, MidiValue = 67 });
        cells[12].DurationDenominator = 4;
        cells[12].Notes.Add(new TabNote { StringIndex = 0, Fret = 3, MidiValue = 67 });
        Assert.Equal(new[] { 0, 8, 12 }, CursorPositions.Allowed(cells, 16)); // one cell, several strings = one beat
    }

    [Fact]
    public void CursorNeverRestsInsideBeatLength()
    {
        var song = TwoBarQuarters(out var track);
        var cells = track.Measures[0].CellsForVoice(0);
        foreach (var interior in new[] { 1, 2, 3, 5, 6, 7, 9, 13 })
            Assert.Contains(CursorPositions.Snap(cells, interior, 16), new[] { 0, 4, 8, 12 }); // CursorPositions.cs:135
        Assert.DoesNotContain(CursorPositions.Allowed(cells, 16), c => c is 1 or 2 or 3); // CursorPositions.cs:27
    }

    [Fact]
    public void RightFromLastBeat_GoesToNextBarFirstBeat()
    {
        var song = TwoBarQuarters(out var track);
        var stops = StopsFor(song, track);
        Assert.Equal((1, 0), (ScoreCursorNavigator.ForwardBeat(stops, 0, 12)!.Value.Measure, ScoreCursorNavigator.ForwardBeat(stops, 0, 12)!.Value.Cell)); // ScoreCursorNavigator.cs:40
    }

    [Fact]
    public void LeftFromFirstBeatOfSecondBar_GoesToLastStopOfFirstBar()
    {
        var song = TwoBarQuarters(out var track);
        var stops = StopsFor(song, track);
        var back = ScoreCursorNavigator.BackBeat(stops, 1, 0)!.Value; // ScoreCursorNavigator.cs:48
        Assert.Equal((0, 12), (back.Measure, back.Cell));
    }

    [Fact]
    public void BarEnd_IsLastWrittenBeat()
    {
        var song = TwoBarQuarters(out var track);
        var stops = StopsFor(song, track);
        Assert.Equal(12, ScoreCursorNavigator.BarEnd(stops, 0, 0)); // ScoreCursorNavigator.cs:69
    }

    [AvaloniaFact]
    public void WritingNote_DoesNotMoveCursor()
    {
        var (probe, editor, track) = MakeDemo();
        editor.Tools.AutoAdvanceAfterEntry = false; // ScoreEditToolState.cs:20
        var free = Enumerable.Range(0, track.StringTunings.Count)
            .First(s => track.Measures[5].CellsForVoice(0).All(c => c.Notes.All(n => n.StringIndex != s)));
        probe.Cursor = (5, 0, free);
        Assert.True(Press(editor, Key.D5)); // ScorePreviewEditor.cs:108 -> ScoreEditCommands.cs:371 EnterFret
        Assert.Equal((5, 0), (editor.Cursor!.Value.Measure, editor.Cursor!.Value.Cell));
    }

    [Fact]
    public void ExampleA_DottedQuarterEighthHalf()
    {
        var cells = Enumerable.Range(0, 16).Select(_ => new TabCell()).ToList();
        cells[0].DurationDenominator = 4; cells[0].Dots = 1; // SongProject.cs:340; MusicTime.cs:40-41 dotted quarter = 6 slots
        cells[0].Notes.Add(new TabNote { StringIndex = 0, Fret = 5, MidiValue = 60 });
        cells[6].DurationDenominator = 8; // eighth = 2 slots
        cells[6].Notes.Add(new TabNote { StringIndex = 0, Fret = 5, MidiValue = 60 });
        cells[8].DurationDenominator = 2; // half = 8 slots
        cells[8].Notes.Add(new TabNote { StringIndex = 0, Fret = 5, MidiValue = 60 });
        Assert.Equal(new[] { 0, 6, 8 }, CursorPositions.Allowed(cells, 16)); // CursorPositions.cs:27
    }

    [Fact]
    public void ExampleB_TripletEighthsQuarterHalf()
    {
        var cells = Enumerable.Range(0, 16).Select(_ => new TabCell()).ToList();
        foreach (var i in new[] { 0, 2, 3 })
        {
            cells[i].DurationDenominator = 8; // SongProject.cs:343-345 TupletNumerator/Denominator; MusicTime.cs:42-43 triplet eighth = 4/3 slots
            cells[i].TupletNumerator = 3; cells[i].TupletDenominator = 2;
            cells[i].Notes.Add(new TabNote { StringIndex = 0, Fret = 5, MidiValue = 60 });
        }
        cells[4].DurationDenominator = 4;
        cells[4].Notes.Add(new TabNote { StringIndex = 0, Fret = 5, MidiValue = 60 });
        cells[8].DurationDenominator = 2;
        cells[8].Notes.Add(new TabNote { StringIndex = 0, Fret = 5, MidiValue = 60 });
        var allowed = CursorPositions.Allowed(cells, 16); // CursorPositions.cs:27
        var starts = CursorPositions.Starts(cells); // CursorPositions.cs:68
        var slots = allowed.Select(i => starts[i]).ToArray();
        var expected = new[] { 0.0, 4.0 / 3, 8.0 / 3, 4.0, 8.0 };
        Assert.Equal(expected.Length, slots.Length);
        for (var k = 0; k < expected.Length; k++)
            Assert.True(System.Math.Abs(slots[k] - expected[k]) < 0.001, $"stop {k}: {slots[k]} vs {expected[k]}");
    }

    [Fact]
    public void ExampleC_GraceBeforeQuarter()
    {
        var cells = Enumerable.Range(0, 16).Select(_ => new TabCell()).ToList();
        cells[0].DurationDenominator = 4;
        cells[0].Notes.Add(new TabNote { StringIndex = 1, Fret = 5, MidiValue = 60 });
        cells[0].Notes.Insert(0, new TabNote { StringIndex = 0, Fret = 5, MidiValue = 62, IsGraceNote = true }); // SongProject.cs:432 same cell, no extra time
        foreach (var i in new[] { 4, 8, 12 })
        {
            cells[i].DurationDenominator = 4;
            cells[i].Notes.Add(new TabNote { StringIndex = 0, Fret = 5, MidiValue = 60 });
        }
        Assert.Equal(new[] { 0, 4, 8, 12 }, CursorPositions.Allowed(cells, 16)); // grace attaches, not its own stop
    }

    [Fact]
    public void ExampleD_TiedQuartersPlusHalf()
    {
        var cells = Enumerable.Range(0, 16).Select(_ => new TabCell()).ToList();
        cells[0].DurationDenominator = 4;
        cells[0].Notes.Add(new TabNote { StringIndex = 0, Fret = 5, MidiValue = 60 });
        cells[4].DurationDenominator = 4; cells[4].IsTied = true; // SongProject.cs:356 tie keeps its own stop
        cells[4].Notes.Add(new TabNote { StringIndex = 0, Fret = 5, MidiValue = 60, Tied = true }); // SongProject.cs:440
        cells[8].DurationDenominator = 2;
        cells[8].Notes.Add(new TabNote { StringIndex = 0, Fret = 5, MidiValue = 60 });
        Assert.Equal(new[] { 0, 4, 8 }, CursorPositions.Allowed(cells, 16));
    }

    [Fact]
    public void ExampleE_ChordQuarterPlusQuarters()
    {
        var cells = Enumerable.Range(0, 16).Select(_ => new TabCell()).ToList();
        cells[0].DurationDenominator = 4;
        foreach (var s in new[] { 0, 1, 2 })
            cells[0].Notes.Add(new TabNote { StringIndex = s, Fret = 5, MidiValue = 60 + s }); // one cell, one stop
        foreach (var i in new[] { 4, 8, 12 })
        {
            cells[i].DurationDenominator = 4;
            cells[i].Notes.Add(new TabNote { StringIndex = 0, Fret = 5, MidiValue = 60 });
        }
        Assert.Equal(new[] { 0, 4, 8, 12 }, CursorPositions.Allowed(cells, 16));
    }

    [Fact]
    public void ExampleF_UnderfullVsCompleteHalf()
    {
        var under = Enumerable.Range(0, 16).Select(_ => new TabCell()).ToList();
        under[0].DurationDenominator = 2;
        under[0].Notes.Add(new TabNote { StringIndex = 0, Fret = 5, MidiValue = 60 });
        Assert.Equal(new[] { 0, 8 }, CursorPositions.Allowed(under, 16)); // append where next note would go
        Assert.False(CursorPositions.IsBeatCell(under[8])); // CursorPositions.cs:21 append is not a beat
        var full = Enumerable.Range(0, 16).Select(_ => new TabCell()).ToList();
        full[0].DurationDenominator = 2;
        full[0].Notes.Add(new TabNote { StringIndex = 0, Fret = 5, MidiValue = 60 });
        full[8].DurationDenominator = 2;
        full[8].Notes.Add(new TabNote { StringIndex = 0, Fret = 5, MidiValue = 60 });
        Assert.Equal(new[] { 0, 8 }, CursorPositions.Allowed(full, 16)); // complete: no extra stop
    }

    [Fact]
    public void ExampleG_ThreeFourAndSixEight()
    {
        var three = Enumerable.Range(0, 16).Select(_ => new TabCell()).ToList();
        foreach (var i in new[] { 0, 4, 8 })
        {
            three[i].DurationDenominator = 4;
            three[i].Notes.Add(new TabNote { StringIndex = 0, Fret = 5, MidiValue = 60 });
        }
        Assert.Equal(new[] { 0, 4, 8 }, CursorPositions.Allowed(three, 12)); // MusicTime.cs:18 BarSlots(3,4)=12
        var six = Enumerable.Range(0, 16).Select(_ => new TabCell()).ToList();
        foreach (var i in new[] { 0, 2, 4, 6, 8, 10 })
        {
            six[i].DurationDenominator = 8;
            six[i].Notes.Add(new TabNote { StringIndex = 0, Fret = 5, MidiValue = 60 });
        }
        Assert.Equal(new[] { 0, 2, 4, 6, 8, 10 }, CursorPositions.Allowed(six, 12)); // BarSlots(6,8)=12
    }
}
