using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Diagnostics;
using System.Threading.Tasks;
using TabForge.Documents;
using TabForge.Playback;
using TabForge.Presets;
using Xunit;
using Xunit.Abstractions;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: playback never reads the live song off the UI thread. Pool compiles run on the immutable song copy while the UI thread keeps editing the
///     live song (no torn read: each compile equals the compile of its own copy taken before the edits), and a playing engine fed by seeks and
///     background refreshes over many edits keeps playing with a holder whose track ids and loop bounds come from the copy.
/// Does not own: the copy's cost (SongSnapshotCostTests) or what is played (golden/parity tests).
/// Tests: this class.
/// </summary>
[Collection(TimingCollection.Name)]
public class PlaybackSnapshotIsolationTests
{
    private const int Edits = 200;

    private readonly ITestOutputHelper _output;

    public PlaybackSnapshotIsolationTests(ITestOutputHelper output) => _output = output;

    private sealed class Sink : IMidiOutput
    {
        public IReadOnlyList<MidiOutputDeviceInfo> Devices { get; } = Array.Empty<MidiOutputDeviceInfo>();
        public void Send(int deviceId, int status, int data1, int data2) { }
        public void ResetAll() { }
        public void Close() { }
        public void Dispose() { }
    }

    private static long Signature(ScoreTimeline timeline)
    {
        long hash = timeline.Events.Count;
        foreach (var e in timeline.Events) hash = unchecked(hash * 31 + (long)(e.TimeMs * 8) + e.Status * 7 + e.Data1 * 13 + e.Data2 * 17 + e.TrackIndex);
        return hash;
    }

    private static void Edit(Models.SongProject song, int i)
    {
        var track = song.Tracks.First(t => t.Measures.Any(m => m.Cells.Any(b => b.Notes.Count > 0)));
        var note = track.Measures.SelectMany(m => m.Cells).First(b => b.Notes.Count > 0).Notes[0];
        note.Fret = (note.Fret + 1) % 20;
        song.Tracks[i % song.Tracks.Count].Volume = 40 + i % 60;
        song.IsDirty = true;
    }

    [Fact]
    public void TheCopyOnTheCallerThreadIsMeasuredAndASeekOnAnUnchangedSongReusesTheLastCopy()
    {
        var song = FullDemoSongFactory.Create();
        var rebuild = new PlaybackSeekRebuild();
        var clock = Stopwatch.StartNew();
        var first = rebuild.TakeSnapshot(song);
        var firstMs = clock.Elapsed.TotalMilliseconds;
        clock.Restart();
        var again = rebuild.TakeSnapshot(song);
        var againMs = clock.Elapsed.TotalMilliseconds;
        Edit(song, 0);
        clock.Restart();
        SongSnapshot.Take(song);
        var freshMs = clock.Elapsed.TotalMilliseconds;
        clock.Restart();
        var afterEdit = rebuild.TakeSnapshot(song);
        var editMs = clock.Elapsed.TotalMilliseconds;
        _output.WriteLine($"copy on the caller thread: first {firstMs:F2} ms, a fresh Take (a Start's) {freshMs:F2} ms, unchanged song {againMs:F3} ms, after one edit {editMs:F2} ms");
        Assert.Same(first, again);
        Assert.NotSame(first, afterEdit);
        Assert.True(againMs < Math.Max(1.0, firstMs / 4), "an unchanged song costs no encode");
    }

    [Fact]
    public void AMuteSetWithoutAnEditMarkIsInTheNextCachedCopy()
    {
        var song = FullDemoSongFactory.Create();
        var rebuild = new PlaybackSeekRebuild();
        var before = rebuild.TakeSnapshot(song);
        Assert.False(before.Song.Tracks[0].Mute);
        song.Tracks[0].Mute = true;
        var after = rebuild.TakeSnapshot(song);
        Assert.NotSame(before, after);
        Assert.True(after.Song.Tracks[0].Mute);
    }

    private static (Models.SongProject Song, Models.TrackModel Track, Plugins.PluginSlot Plugin) PluginSong()
    {
        var song = FullDemoSongFactory.Create();
        var track = song.Tracks.First(t => t.HasNotation);
        var plugin = new Plugins.PluginSlot { Type = Plugins.PluginSlotType.Instrument, Enabled = true };
        track.Rig.Plugins.Add(plugin);
        track.SoundSource = Models.SoundSources.Plugins;
        track.MidiSound = false;
        song.IsDirty = true;
        return (song, track, plugin);
    }

    [Fact]
    public void APlayingPluginInstrumentCompilesToThePluginRouteFromTheCopy()
    {
        var (song, track, _) = PluginSong();
        var copyTrack = SongSnapshot.Take(song).Song.Tracks.First(t => t.Id == track.Id);
        Assert.True(Models.MixerGroups.InstrumentPlays(copyTrack));
        Assert.Equal(Models.TrackRoute.Instrument, Models.MixerGroups.RouteOf(copyTrack, new Models.MixerOptions()));
    }

    [Fact]
    public void AnUnavailablePluginInstrumentCompilesToTheFallbackFromTheCopy()
    {
        var (song, track, plugin) = PluginSong();
        plugin.Unavailable = true;
        var copyTrack = SongSnapshot.Take(song).Song.Tracks.First(t => t.Id == track.Id);
        Assert.False(Models.MixerGroups.InstrumentPlays(copyTrack));
        Assert.True(copyTrack.Rig.Plugins[0].Unavailable);
        Assert.Equal(Models.TrackRoute.Silent, Models.MixerGroups.RouteOf(copyTrack, new Models.MixerOptions()));
    }

    [Fact]
    public void AManualGmOffSilencesTheGmRouteInTheCopy()
    {
        var song = FullDemoSongFactory.Create();
        var track = song.Tracks.First(t => t.HasNotation);
        track.Rig.Plugins.Add(new Plugins.PluginSlot { Type = Plugins.PluginSlotType.Effect, Enabled = true });
        track.SoundSource = Models.SoundSources.Midi;
        track.MidiSound = false;
        track.MidiSoundManualOff = true;
        var copyTrack = SongSnapshot.Take(song).Song.Tracks.First(t => t.Id == track.Id);
        Assert.True(Models.MixerGroups.IsSilentRoute(copyTrack));
    }

    [Fact]
    public void TogglingARuntimeFlagInvalidatesTheCachedCopy()
    {
        var (song, track, plugin) = PluginSong();
        var rebuild = new PlaybackSeekRebuild();
        var first = rebuild.TakeSnapshot(song);
        plugin.Unavailable = true;
        var second = rebuild.TakeSnapshot(song);
        Assert.NotSame(first, second);
        plugin.GroupBypassed = true;
        var third = rebuild.TakeSnapshot(song);
        Assert.NotSame(second, third);
        track.MidiSoundManualOff = true;
        Assert.NotSame(third, rebuild.TakeSnapshot(song));
    }

    [Fact]
    public async Task CompilesOnTheCopyAreNotTornByEditsToTheLiveSong()
    {
        var song = FullDemoSongFactory.Create();
        var encoder = new ProjectStateEncoder();
        var pending = new List<(Task<long> Compile, long Expected)>();
        for (var i = 0; i < Edits; i++)
        {
            var snapshot = SongSnapshot.Take(song, encoder);
            var expected = Signature(MidiTimelineBuilder.Build(snapshot.Song, new PlaybackOptions()));
            pending.Add((Task.Run(() => Signature(MidiTimelineBuilder.Build(snapshot.Song, new PlaybackOptions()))), expected));
            Edit(song, i);   // the UI thread edits the live song while the pool compiles the copy
        }
        foreach (var (compile, expected) in pending) Assert.Equal(expected, await compile);
    }

    [Fact]
    public async Task APlayingEngineSurvivesEditsSeeksAndBackgroundRefreshesAndKeepsItsHolderOnTheCopy()
    {
        var song = FullDemoSongFactory.Create();
        var bars = song.Tracks.Max(t => t.Measures.Count);
        using var engine = new PlaybackEngine(new Sink());
        engine.Start(song, new PlaybackOptions { Loop = true, LoopStartBar = 0, LoopEndBar = bars - 1 }, _ => { }, () => { });
        var identity = Enumerable.Range(0, bars).ToArray();
        var refreshes = new List<Task>();
        for (var i = 0; i < Edits; i++)
        {
            Edit(song, i);
            if (i % 5 == 0) engine.Seek(song, i % bars, 0);
            if (i % 7 == 0) refreshes.Add(engine.RefreshArrangementInBackground(song, identity));
            Thread.Sleep(2);
        }
        await Task.WhenAll(refreshes).WaitAsync(TimeSpan.FromSeconds(30));
        var playing = engine.IsPlaying;
        var timeline = engine.Timeline;
        var copy = engine.PublishedSongCopy;
        var trackIds = engine.PublishedTrackIds;
        var held = engine.LoopBoundsHold.Hold(double.MaxValue, looping: true, wrapped: false);
        var options = new PlaybackOptions { Loop = true, LoopStartBar = 0, LoopEndBar = bars - 1 };
        engine.Stop();
        Assert.True(playing, "the engine kept playing through the edits");
        Assert.NotNull(timeline);
        Assert.All(refreshes, t => Assert.False(t.IsFaulted));
        Assert.NotNull(copy);
        Assert.NotSame(song, copy);
        Assert.Equal(copy!.Tracks.Select(t => t.Id).ToArray(), trackIds);
        var (_, endMs, available) = PlaybackOrder.LoopBounds(timeline!, copy, options);
        Assert.True(available);
        Assert.Equal(endMs - LoopPlayheadHold.EndHoldMs, held, 6);   // the loop bounds the scheduler holds come from the copy
    }

    [Fact]
    public void ARunningEngineKeepsTheTrackIdsAndSongOfItsCopyWhenTheLiveSongGrowsAndShrinks()
    {
        var song = FullDemoSongFactory.Create();
        var bars = song.Tracks.Max(t => t.Measures.Count);
        using var engine = new PlaybackEngine(new Sink());
        engine.Start(song, new PlaybackOptions { Loop = true, LoopStartBar = 0, LoopEndBar = bars - 1 }, _ => { }, () => { });
        var idsAtStart = song.Tracks.Select(t => t.Id).ToArray();
        var heldAtStart = engine.LoopBoundsHold.Hold(double.MaxValue, looping: true, wrapped: false);
        var removed = song.Tracks[^1];
        song.Tracks.Remove(removed);   // the live song loses a track and its last bar: the playing holder must not follow
        foreach (var track in song.Tracks) track.Measures.RemoveAt(track.Measures.Count - 1);
        song.IsDirty = true;
        Thread.Sleep(50);   // the scheduler ticks, the pool threads run: none of them may read the edited live song
        var copy = engine.PublishedSongCopy;
        var trackIds = engine.PublishedTrackIds;
        var held = engine.LoopBoundsHold.Hold(double.MaxValue, looping: true, wrapped: false);
        engine.Stop();
        Assert.Equal(idsAtStart, trackIds);
        Assert.Equal(idsAtStart, copy!.Tracks.Select(t => t.Id).ToArray());
        Assert.Contains(removed.Id, trackIds);
        Assert.Equal(bars, copy.Tracks.Max(t => t.Measures.Count));
        Assert.Equal(heldAtStart, held, 6);
    }
}
