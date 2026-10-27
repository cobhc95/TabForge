using System;
using System.Linq;
using TabForge.Playback;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the checks of the Core lit-note lists (now, recent, next, upcoming) on a small hand-made note stream.
/// Does not own: the drawing of the lists (the instrument pane) or the selection they will replace.
/// Tests: this class.
/// </summary>
public class InstrumentPlaybackListsTests
{
    private static NoteEvent Note(double onset, double duration, int stringIndex) =>
        new() { OnsetMs = onset, DurationMs = duration, StringIndex = stringIndex, Midi = 40 + stringIndex };

    [Fact]
    public void SoundingNotesAreNowAndRecentlyReleasedOnesAreRecent()
    {
        // A note ended at 400; a chord at 1000 (lasting 500) and a single note at 1200 (lasting 800); the song time is 1600.
        var notes = new[] { Note(0, 400, 0), Note(1000, 500, 1), Note(1000, 500, 2), Note(1200, 800, 3) };
        var snapshot = new InstrumentPlaybackSnapshot();

        InstrumentPlaybackLists.Fill(snapshot, notes, 1600, previewHorizon: 6);

        Assert.Equal(new[] { 3 }, snapshot.Now.Select(n => n.StringIndex).ToArray());
        Assert.Equal(new[] { 2, 1, 0 }, snapshot.Recent.Select(n => n.StringIndex).ToArray());
    }

    [Fact]
    public void SameNotesHoldBetweenNoteBoundariesAndChangeAtOne()
    {
        // The chord at 1000 (ends 1500) stays lit from 1100 to 1200; at 1600 it has been released and sits in Recent.
        var notes = new[] { Note(1000, 500, 0), Note(2000, 100, 1) };
        var first = new InstrumentPlaybackSnapshot();
        var second = new InstrumentPlaybackSnapshot();
        var third = new InstrumentPlaybackSnapshot();

        InstrumentPlaybackLists.Fill(first, notes, 1100, previewHorizon: 6);
        InstrumentPlaybackLists.Fill(second, notes, 1200, previewHorizon: 6);
        InstrumentPlaybackLists.Fill(third, notes, 1600, previewHorizon: 6);

        Assert.True(first.SameNotesAs(second));
        Assert.False(first.SameNotesAs(third));
    }

    [Fact]
    public void RestKeepsTheLastStruckNoteAndUpcomingCarriesItsMovementDepth()
    {
        // Nothing sounds at 1500 (the note at 1000 ended at 1100): the last struck note stays as the released position.
        // Movements: 2000 (next, the chord), then 3000 (depth 1).
        var notes = new[] { Note(1000, 100, 0), Note(2000, 100, 1), Note(2000.3, 100, 2), Note(3000, 100, 3) };
        var snapshot = new InstrumentPlaybackSnapshot();

        InstrumentPlaybackLists.Fill(snapshot, notes, 1500, previewHorizon: 6);

        Assert.Empty(snapshot.Now);
        Assert.Equal(new[] { 0 }, snapshot.Released.Select(n => n.StringIndex).ToArray());
        Assert.Equal(new[] { 3 }, snapshot.Upcoming.Select(n => n.StringIndex).ToArray());
        Assert.Equal(new[] { 1 }, snapshot.UpcomingDepth.ToArray());
    }

    [Fact]
    public void NextIsTheFirstOnsetGroupAndUpcomingIsTheRest()
    {
        var notes = new[] { Note(1000, 100, 0), Note(2000, 100, 1), Note(2000.3, 100, 2), Note(3000, 100, 3) };
        var snapshot = new InstrumentPlaybackSnapshot();

        InstrumentPlaybackLists.Fill(snapshot, notes, 500, previewHorizon: 6);

        Assert.Equal(new[] { 0 }, snapshot.Next.Select(n => n.StringIndex).ToArray());
        Assert.Equal(new[] { 1, 2, 3 }, snapshot.Upcoming.Select(n => n.StringIndex).ToArray());
    }

    [Fact]
    public void UpcomingStopsAfterThePreviewHorizonPlusTwoMovements()
    {
        // Ten single-note movements; horizon 1 reads three movements: next plus two more.
        var notes = Enumerable.Range(0, 10).Select(i => Note(1000 + i * 100, 50, i % 6)).ToArray();
        var snapshot = new InstrumentPlaybackSnapshot();

        InstrumentPlaybackLists.Fill(snapshot, notes, 0, previewHorizon: 1);

        Assert.Single(snapshot.Next);
        Assert.Equal(2, snapshot.Upcoming.Count);
    }

    [Fact]
    public void ReleasedNotesOutsideTheHoldWindowAreDropped()
    {
        var notes = new[] { Note(0, 100, 0) };
        var snapshot = new InstrumentPlaybackSnapshot();

        InstrumentPlaybackLists.Fill(snapshot, notes, InstrumentPlaybackLists.DefaultHoldMs + 500, previewHorizon: 6);

        Assert.Empty(snapshot.Recent);
        Assert.Empty(snapshot.Now);
    }

    [Fact]
    public void FillAllocatesNothingOnceWarmOverAThousandFrames()
    {
        var notes = Enumerable.Range(0, 200).Select(i => Note(i * 250.0, 400, i % 6)).ToArray();
        var snapshot = new InstrumentPlaybackSnapshot();
        // One full pass grows each list to its ceiling; the measured pass must then reuse that capacity.
        for (var frame = 0; frame < 1000; frame++)
            InstrumentPlaybackLists.Fill(snapshot, notes, frame * 16.7, previewHorizon: 6);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var frame = 0; frame < 1000; frame++)
            InstrumentPlaybackLists.Fill(snapshot, notes, frame * 16.7, previewHorizon: 6);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }
}
