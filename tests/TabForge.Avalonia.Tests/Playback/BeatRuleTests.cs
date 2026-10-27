using System.Collections.Generic;
using System.Linq;
using TabForge.Models;
using TabForge.Views;
using TabForge.Views.Score;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the one beat rule (MusicTime.IsBeat / IsGraceOnly / Onsets): grid, paste anchor, clipboard and drum stems start every cell where playback does.
public class BeatRuleTests
{
    private static TabCell Note(int denominator = 4, bool grace = false) => new()
    {
        DurationDenominator = denominator, IsGrace = grace,
        Notes = { new TabNote { StringIndex = 0, Fret = 3, IsGraceNote = grace } },
    };

    private static TabCell TieOnly() => new() { DurationDenominator = 4, IsTied = true };

    private static List<TabCell> Quarters(bool tieThird)
        => new() { Note(), Note(), tieThird ? TieOnly() : Note(), Note() };

    [Fact]
    public void A_tie_only_cell_holds_its_time_on_the_grid_like_playback()   // before: 0, 4, 2, 8
    {
        var cells = Quarters(tieThird: true);
        Assert.Equal(new[] { 0.0, 4, 8, 12 }, BarGrid.Onsets(cells));
        Assert.Equal(MusicTime.Onsets(cells), BarGrid.Onsets(cells));
    }

    [Fact]
    public void A_grace_only_beat_takes_no_time_on_the_grid()   // before: 0, 4, 5, 9, 13
    {
        var cells = new List<TabCell> { Note(), Note(16, grace: true), Note(), Note(), Note() };
        Assert.Equal(new[] { 0.0, 4, 4, 8, 12 }, BarGrid.Onsets(cells));
    }

    [Fact]
    public void A_plain_bar_of_eighths_is_unchanged()
    {
        var cells = Enumerable.Range(0, 8).Select(_ => Note(8)).ToList();
        Assert.Equal(new[] { 0.0, 2, 4, 6, 8, 10, 12, 14 }, BarGrid.Onsets(cells));
        Assert.Equal(new[] { 0, 2, 4, 6, 8, 10, 12, 14 }, MusicTime.BeatOnsets(cells).Select(b => (int)b.Start));
    }

    [Fact]
    public void Clipboard_and_drum_stems_follow_the_same_onsets()
    {
        var cells = Quarters(tieThird: true);
        Assert.Equal(new[] { 0.0, 4, 8, 12 }, MusicTime.BeatOnsets(cells).Select(b => b.Start));
        Assert.Equal(new[] { 0.0, 4, 12 }, StaffDrumStems.NoteOnsets(cells));
    }

    [Fact]
    public void A_paste_anchor_after_a_tie_only_cell_is_where_playback_starts_the_next_beat()
    {
        var cells = Quarters(tieThird: true);
        var track = new TrackModel { Measures = { new MeasureModel { Cells = cells } } };
        var project = new SongProject { Tracks = { track } };
        Assert.Equal(MusicTime.Onsets(cells)[3], BarGrid.AnchorAt(project, 0, 0, 0, 3));
        Assert.Equal(12.0, BarGrid.AnchorAt(project, 0, 0, 0, 3));
    }

    private static TabCell Chord(int denominator = 4) => new() { DurationDenominator = denominator, ChordName = "Am" };

    [Fact]
    public void A_chord_name_after_a_half_note_does_not_push_the_later_beats()   // before: 0, 8, 12, 16
    {
        var cells = new List<TabCell> { Note(2), Chord(), Note(), Note() };
        Assert.Equal(new[] { 0.0, 8, 8, 12 }, MusicTime.Onsets(cells));
    }

    [Fact]
    public void An_annotated_empty_bar_is_not_underfull_and_a_chord_name_after_a_full_bar_is_not_overfull()
    {
        var empty = new MeasureModel { Cells = new List<TabCell> { Chord() } };
        Assert.Equal(BarTimingStatus.Complete, BarTiming.Status(empty, 4, 4));
        var track = new TrackModel { Measures = { new MeasureModel { Cells = new List<TabCell> { Note(), Note(), Note(), Note(), Chord() } } } };
        Assert.False(BarLocalPaste.IsOverfull(new SongProject { Tracks = { track } }, 0, 0, 0));
    }

    [Fact]
    public void Retiming_after_a_grace_gives_the_grace_no_time()   // before: the beat after the grace got a written position at slot 5
    {
        var cells = new List<TabCell> { Note(), Note(16, grace: true), Note(), Note() };
        EditCommands.RetimeFrom(cells, 1);
        Assert.Equal(new[] { 0.0, 4, 4, 8 }, MusicTime.Onsets(cells));
        Assert.Null(cells[2].RhythmicPosition);
    }

    [Fact]
    public void The_cursor_can_stop_on_a_tie_only_beat()
    {
        var cells = Quarters(tieThird: true);
        Assert.True(CursorPositions.IsBeatCell(cells[2]));
        Assert.Contains(2, MusicTime.BeatSlots(cells));
    }
}
