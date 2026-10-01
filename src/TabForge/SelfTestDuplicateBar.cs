using TabForge.Controllers;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;

namespace TabForge;

// Duplicate bar: every track gets the copy, one undo step. Synthetic songs only.
public static partial class SelfTest
{
    private static SongProject SignatureSong(int tracks, int bars)
    {
        var song = new SongProject { Tempo = 120 };
        for (var t = 0; t < tracks; t++)
            song.Tracks.Add(new TrackModel { Name = "T" + (t + 1), Measures = TemplateFactory.Measures(bars) });
        return song;
    }

    private static void TestDuplicateBarAllTracks()
    {
        var song = SignatureSong(3, 6);
        song.Markers.Add(new MarkerModel { MeasureIndex = 0, Title = "A" });
        song.Markers.Add(new MarkerModel { MeasureIndex = 3, Title = "B" });
        foreach (var track in song.Tracks)
        {
            track.Measures[2].TempoChange = 140;
            track.Measures[2].SectionName = "Label";
            track.Measures[2].RepeatStart = true;
            track.Measures[2].TimeSigNum = 3;   // bar 3 is a 3/4 bar: the copy must be 3/4 too
            track.Measures[2].TimeSigDenom = 4;
        }
        Beat(song, 0, 2, 0, 4, 60);
        Beat(song, 1, 2, 4, 8, 64);

        var map = new ArrangementController().DuplicateBars(song, 2, 2);
        Check("duplicate bar adds one bar to EVERY track right after the source (all tracks stay aligned)",
            map is not null && song.Tracks.All(t => t.Measures.Count == 7) && BarRangeEditor.MaxMeasures(song) == 7,
            string.Join(",", song.Tracks.Select(t => t.Measures.Count)));
        Check("duplicate bar maps old bars to new ones and shifts later sections with their bars",
            map is not null && map.SequenceEqual(new[] { 0, 1, 2, 4, 5, 6 }) &&
            song.Markers.Single(m => m.Title == "A").MeasureIndex == 0 && song.Markers.Single(m => m.Title == "B").MeasureIndex == 4);
        Check("duplicate bar copies each track's own content (notes, tempo, meter, repeat) and leaves the source alone",
            song.Tracks[0].Measures[3].Cells[0].Notes.Count == 1 && song.Tracks[0].Measures[3].Cells[0].Notes[0].MidiValue == 60 &&
            song.Tracks[1].Measures[3].Cells[4].Notes.Count == 1 && song.Tracks[1].Measures[3].Cells[0].Notes.Count == 0 &&
            song.Tracks[2].Measures[3].Cells.All(c => c.Notes.Count == 0) &&
            song.Tracks.All(t => t.Measures[3].TempoChange == 140 && t.Measures[3].RepeatStart && t.Measures[3].TimeSigNum == 3 && t.Measures[3].TimeSigDenom == 4) &&
            song.Tracks.All(t => t.Measures[2].TempoChange == 140 && t.Measures[2].SectionName == "Label") &&
            MusicTime.BarSlots(song, 2) == 12 && MusicTime.BarSlots(song, 3) == 12 && MusicTime.BarSlots(song, 4) == 16);
        Check("the copy is independent of the source and does not repeat its section label",
            song.Tracks.All(t => !ReferenceEquals(t.Measures[2], t.Measures[3]) && !ReferenceEquals(t.Measures[2].Cells, t.Measures[3].Cells) && t.Measures[3].SectionName == ""));
        Check("duplicate bar renumbers every track's bars 1..n",
            song.Tracks.All(t => t.Measures.Select(m => m.Number).SequenceEqual(Enumerable.Range(1, 7))));

        // A bar range: copied as a block, after its last bar, on every track.
        var range = SignatureSong(2, 8);
        for (var b = 0; b < 8; b++) range.Tracks[1].Measures[b].TempoChange = 100 + b;
        range.Markers.Add(new MarkerModel { MeasureIndex = 5, Title = "Chorus" });
        var rangeMap = BarRangeEditor.Duplicate(range, 2, 4);
        Check("duplicating a bar range copies all its bars on every track after the last selected bar",
            rangeMap is not null && range.Tracks.All(t => t.Measures.Count == 11) &&
            range.Tracks[1].Measures.Select(m => m.TempoChange ?? 0).SequenceEqual(new[] { 100, 101, 102, 103, 104, 102, 103, 104, 105, 106, 107 }) &&
            rangeMap.SequenceEqual(new[] { 0, 1, 2, 3, 4, 8, 9, 10 }) && range.Markers.Single().MeasureIndex == 8);

        // Tracks that are shorter than the others (never expected, but a file can have them) are padded, not left behind.
        var ragged = SignatureSong(2, 6);
        ragged.Tracks[1].Measures.RemoveRange(4, 2);
        Check("duplicate bar keeps a shorter track aligned",
            BarRangeEditor.Duplicate(ragged, 1, 1) is not null && ragged.Tracks.All(t => t.Measures.Count == 7));
        var pickup = SignatureSong(2, 3);
        foreach (var t in pickup.Tracks) t.Measures[0].Anacrusis = true;
        Check("a duplicated pickup bar is not a second pickup (only bar 1 keeps the mark)",
            BarRangeEditor.Duplicate(pickup, 0, 0) is not null && pickup.Tracks.All(t => t.Measures[0].Anacrusis && !t.Measures[1].Anacrusis));
        Check("duplicate bar refuses a bar that does not exist and an empty song",
            BarRangeEditor.Duplicate(SignatureSong(1, 3), 5, 6) is null && BarRangeEditor.Duplicate(new SongProject(), 0, 0) is null);

        // One undo step, and redo brings it back.
        var undoSong = SignatureSong(2, 4);
        Beat(undoSong, 0, 1, 0, 4, 60);
        var before = ProjectService.Snapshot(undoSong);
        var history = new UndoController();
        var transaction = history.BeginTransaction(undoSong);
        BarRangeEditor.Duplicate(undoSong, 1, 1);
        var committed = history.Commit(transaction);
        var changed = ProjectService.Snapshot(undoSong);
        var undone = history.TryUndo(history.Snapshot(undoSong), out var target) ? history.Restore(target, undoSong) : null;
        Check("duplicate bar is one undo step: a single undo removes the copy from every track",
            committed.Stored && history.RedoCount == 1 && history.UndoCount == 0 && undone is not null &&
            ProjectService.Snapshot(undone) == before && changed != before && undone.Tracks.All(t => t.Measures.Count == 4));
        var redone = history.TryRedo(history.Snapshot(undone!), out var redoTarget) ? history.Restore(redoTarget, undone!) : null;
        Check("redo puts the duplicated bar back on every track", redone is not null && ProjectService.Snapshot(redone) == changed);
    }
}
