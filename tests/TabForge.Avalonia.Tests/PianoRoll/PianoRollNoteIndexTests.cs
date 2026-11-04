// Owns: indexed range-query equivalence across sustains, seeks and overlapping notes.
// Does not own: the editing source or pixel rendering.
using System;
using System.Collections.Generic;
using System.Linq;
using TabForge.Presenters;
using Xunit;

namespace TabForge.Avalonia.Tests;

public sealed class PianoRollNoteIndexTests
{
    [Fact]
    public void ArbitraryRangesMatchLinearScanInOriginalOrder()
    {
        var random = new Random(71);
        var notes = Enumerable.Range(0, 10000).Select(i => new PianoRollNote(random.NextDouble() * 1000, i % 31 == 0 ? 900 : random.NextDouble() * 3, i % 128, 90, Muted: i % 11 == 0)).ToArray();
        var index = new PianoRollNoteIndex(notes);
        var actual = new List<int>();
        for (var query = 0; query < 200; query++)
        {
            var from = random.NextDouble() * 1100;
            var to = query % 2 == 0 ? from : from + 20;
            index.Query(from, to, actual);
            var expected = Enumerable.Range(0, notes.Length).Where(i => notes[i].Start <= to && notes[i].End >= from);
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void EmptyAndBoundaryQueriesAreCorrectAndWarmQueriesAllocateNothing()
    {
        PianoRollNote[] notes = [new(0, 100, 60, 90), new(50, 1, 60, 90), new(100, 1, 61, 90)];
        var index = new PianoRollNoteIndex(notes);
        var result = new List<int>();
        index.Query(100, 100, result);
        Assert.Equal(new[] { 0, 2 }, result);
        for (var i = 0; i < 100; i++) index.Query(50, 50, result);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) index.Query(50, 50, result);
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
        new PianoRollNoteIndex(Array.Empty<PianoRollNote>()).Query(0, 1, result);
        Assert.Empty(result);
    }
}
