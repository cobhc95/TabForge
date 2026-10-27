using System.Linq;
using TabForge.Audio;
using TabForge.Features.Recording.Rules;
using TabForge.Models;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: looped recording landing: one separate take per lap on its own lane with only the newest audible (audio and MIDI),
//   and a second session stacking above the first. Directly built groups (repeat takes) still share one lane.
// Does not own: the folded drawing, pass rotation or whole-group gestures (later stages).
// Tests: this class.
public sealed class LoopPassGroupTests
{
    private static TrackModel Track() => new() { Name = "Audio", Kind = TrackKind.Audio };

    private static AudioClip Clip(string? group, int pass, double start, double length, int lane = 0) => new()
    {
        File = "take.wav", Name = "take", StartSec = start, SourceLengthSec = length, FileLengthSec = length,
        Lane = lane, TakeGroupId = group, TakeGroupPass = pass,
    };

    [Fact]
    public void ThreeAudioLapsOfOneLoopedRecordingLandAsSeparateTakesOnSeparateLanes()
    {
        var track = Track();
        var takes = RecordedTakes.FromRecording("take.wav", 0, 12, loop: (0, 4));

        Assert.Equal(3, takes.Count);
        Assert.All(takes, take => Assert.Null(take.TakeGroupId));
        Assert.Equal(new[] { 0d, 0d, 0d }, takes.Select(t => t.StartSec));
        Assert.Equal(new[] { 4d, 4d, 4d }, takes.Select(t => t.LengthSec));

        RecordedTakes.Place(track, takes, midi: false);

        Assert.Equal(new[] { 0, 1, 2 }, takes.Select(t => t.Lane));
        Assert.Equal(3, ClipLanes.Count(track));   // one lane per lap
        Assert.Equal(new[] { true, true, false }, takes.Select(t => t.Muted));
        Assert.True(ClipLanes.Audible(track, takes[^1]));
        Assert.False(ClipLanes.Audible(track, takes[0]));
    }

    [Fact]
    public void MidiLapsDropNoteLessOnesThenLandAsSeparateTakesOnSeparateLanes()
    {
        var take = new MidiTake();
        var loop = (Start: 0.0, End: 4.0);
        take.Keep(0x90, 60, 100, 1.0, 0, loop, sec => sec);
        take.Keep(0x80, 60, 0, 2.0, 0, loop, sec => sec);
        // Lap 1 stays empty.
        take.Keep(0x90, 64, 100, 1.0, 2, loop, sec => sec);
        take.Keep(0x80, 64, 0, 2.0, 2, loop, sec => sec);
        var clips = take.Finish("Keys", 4.0);

        Assert.Equal(2, clips.Count);   // the note-less lap was dropped
        Assert.All(clips, clip => Assert.Null(clip.TakeGroupId));

        var track = Track();
        RecordedTakes.Place(track, clips, midi: true);

        Assert.Equal(new[] { 0, 1 }, clips.Select(c => c.Lane));
        Assert.Equal(2, ClipLanes.Count(track));
        Assert.Equal(new[] { true, false }, clips.Select(c => c.Muted));
        Assert.True(ClipLanes.Audible(track, clips[^1]));
    }

    [Fact]
    public void ASinglePassRecordingStaysUngroupedAndPlaysAsBefore()
    {
        var track = Track();
        var audio = RecordedTakes.FromRecording("solo.wav", 0, 1.5, loop: null);
        var one = Assert.Single(audio);
        Assert.Null(one.TakeGroupId);
        Assert.Equal(0, one.TakeGroupPass);

        RecordedTakes.Place(track, audio, midi: false);

        Assert.Equal(0, one.Lane);
        Assert.False(one.Muted);
        Assert.Equal(1, ClipLanes.Count(track));

        var take = new MidiTake();
        take.Keep(0x90, 60, 100, 1.0, 0, null, sec => sec);
        take.Keep(0x80, 60, 0, 1.5, 0, null, sec => sec);
        var midi = Assert.Single(take.Finish("Keys", 2));
        Assert.Null(midi.TakeGroupId);
        Assert.Equal(0, midi.TakeGroupPass);
        Assert.False(midi.Muted);
    }

    [Fact]
    public void FreeLaneIgnoresTheGroupsOwnPassesButNotForeignClips()
    {
        var track = Track();
        var own = Clip("group-a", 1, 0, 4, lane: 0);
        track.AudioClips.Add(own);
        track.AudioClips.Add(Clip("group-a", 2, 0, 4, lane: 1));
        var plain = Clip(null, 0, 10, 2, lane: 0);

        Assert.Equal(0, ClipLanes.FreeLane(track, 0, 4, except: null, takeGroupId: "group-a"));    // its own passes do not conflict
        Assert.True(ClipLanes.SameTakeGroup(own, track.AudioClips[1]));
        Assert.False(ClipLanes.SameTakeGroup(own, plain));

        track.AudioClips.Add(plain);
        Assert.Equal(1, ClipLanes.FreeLane(track, 10, 12, except: null, takeGroupId: "group-a")); // a null-group clip still does

        track.AudioClips.Add(Clip("group-b", 1, 0, 4, lane: 0));
        Assert.Equal(1, ClipLanes.FreeLane(track, 0, 4, except: null, takeGroupId: "group-a")); // another group conflicts too
        Assert.Equal(2, ClipLanes.FreeLane(track, 0, 4, except: null, takeGroupId: null));      // no group ignored, as before
    }

    [Fact]
    public void ASecondLoopedSessionStacksItsOwnLanesAboveTheFirstWithoutColliding()
    {
        var track = Track();
        var first = RecordedTakes.FromRecording("one.wav", 0, 12, loop: (0, 4));
        var second = RecordedTakes.FromRecording("two.wav", 0, 12, loop: (0, 4));
        RecordedTakes.Place(track, first, midi: false);
        RecordedTakes.Place(track, second, midi: false);

        Assert.All(first, take => Assert.Null(take.TakeGroupId));
        Assert.All(second, take => Assert.Null(take.TakeGroupId));
        Assert.Equal(new[] { 0, 1, 2 }, first.Select(t => t.Lane));
        Assert.Equal(new[] { 3, 4, 5 }, second.Select(t => t.Lane));
        Assert.Equal(6, ClipLanes.Count(track));
        Assert.Equal(new[] { true, true, false }, second.Select(t => t.Muted));
        Assert.True(ClipLanes.Audible(track, second[^1]));
    }
}
