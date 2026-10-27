using System;
using System.Diagnostics;
using System.Linq;
using TabForge.Documents;
using TabForge.Playback;
using TabForge.Presets;
using Xunit;
using Xunit.Abstractions;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the cost and the safety of the song copy a seek or a start takes on the UI thread: a warm copy (no edit since the last) stays small, and a
///     marked edit still shows up in the next copy.
/// Does not own: the compile from the copy (PlaybackSeekRebuild) or undo states (TestUndoDeltaStates).
/// Tests: this class.
/// </summary>
[Collection(TimingCollection.Name)]
public class SongSnapshotCostTests
{
    private const double MaxWarmMs = 2.0;
    private const int WarmRuns = 60;
    private readonly ITestOutputHelper _log;

    public SongSnapshotCostTests(ITestOutputHelper log) => _log = log;

    [Fact]
    public void AWarmCopyIsCheapAndADirectEditStillShowsUp()
    {
        var song = FullDemoSongFactory.Create();
        var rebuild = new PlaybackSeekRebuild();
        var clock = Stopwatch.StartNew();
        rebuild.TakeSnapshot(song);
        var coldMs = clock.Elapsed.TotalMilliseconds;
        var warmMs = double.MaxValue;
        for (var run = 0; run < WarmRuns; run++)
        {
            clock.Restart();
            rebuild.TakeSnapshot(song);
            warmMs = Math.Min(warmMs, clock.Elapsed.TotalMilliseconds);
        }
        _log.WriteLine($"TakeSnapshot on the demo song: cold {coldMs:0.00} ms, warm {warmMs:0.00} ms ({song.Tracks.Sum(t => t.Measures.Count)} bars)");

        var track = song.Tracks.First(t => t.Measures.Any(m => m.Cells.Any(b => b.Notes.Count > 0)));
        var bar = track.Measures.FindIndex(m => m.Cells.Any(b => b.Notes.Count > 0));
        var note = track.Measures[bar].Cells.First(b => b.Notes.Count > 0).Notes[0];
        note.Fret += 1;
        song.IsDirty = true;   // an edit marks the song changed, as the app's edit paths do; that moves the revision the copy is keyed on
        var copy = rebuild.TakeSnapshot(song).Song;
        var copied = copy.Tracks[song.Tracks.IndexOf(track)].Measures[bar].Cells.First(b => b.Notes.Count > 0).Notes[0];
        Assert.Equal(note.Fret, copied.Fret);
        Assert.True(warmMs < MaxWarmMs, $"a warm copy took {warmMs:0.00} ms");
    }
}
