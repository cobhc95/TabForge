using System.Collections.Generic;
using System.Linq;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Services;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: a MIDI clip on a take lane that does not play is left out of the compiled timeline (the one timeline playback and render both use).
/// Does not own: the audio path (AudioEngineClient sync) or the take lane rules (RecordingLaneLifecycleTests).
/// Tests: this class.
/// </summary>
public sealed class MidiTakeLanePlaysTests
{
    private static (SongProject Song, TrackModel Track) TwoTakes()
    {
        var song = DemoSongFactory.Create();
        var track = NewTrackFactory.CreateTrack(song, TrackKind.Keys);
        song.Tracks.Add(track);
        track.AudioClips.Add(new AudioClip { Name = "old", StartSec = 0, SourceLengthSec = 2, FileLengthSec = 2, Lane = 0, Notes = new List<ClipNote> { new(0.1, 0.2, 60, 100) } });
        track.AudioClips.Add(new AudioClip { Name = "new", StartSec = 0, SourceLengthSec = 2, FileLengthSec = 2, Lane = 1, Notes = new List<ClipNote> { new(0.1, 0.2, 72, 100) } });
        ClipLanes.PlayNewTake(track, 1, midi: true);
        return (song, track);
    }

    private static List<int> Pitches(SongProject song, TrackModel track)
    {
        var index = song.Tracks.IndexOf(track);
        var timeline = MidiTimelineBuilder.Build(song, new PlaybackOptions { RespectMuteSolo = false });
        return timeline.Events.Where(e => e.TrackIndex == index && (e.Status & 0xF0) == 0x90).Select(e => e.Data1).ToList();
    }

    [Fact]
    public void OnlyTheNewerMidiTakeIsInTheCompiledTimeline()
    {
        var (song, track) = TwoTakes();
        Assert.Equal(new[] { 72 }, Pitches(song, track));
    }

    [Fact]
    public void PickingTheOldTakeSwitchesTheCompiledNotesToIt()
    {
        var (song, track) = TwoTakes();
        ClipLanes.SelectTake(track, 0, midi: true, add: false);
        Assert.Equal(new[] { 60 }, Pitches(song, track));
    }
}
