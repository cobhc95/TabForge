using System.Collections.Generic;
using System.Linq;
using TabForge.Documents;
using TabForge.Features.Recording.Rules;
using TabForge.Menus;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: loop recording takes: one take per loop pass, the last one active, the Takes menu rows and switching, and the whole recording as one undo step.
/// Does not own: the engine capture or the window command wiring.
/// Tests: this class.
/// </summary>
public sealed class LoopTakesTests
{
    private static (SongDocument Document, TrackModel Track) Landed()
    {
        var document = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        var track = NewTrackFactory.CreateTrack(document.Project, TrackKind.Audio);
        document.Project.Tracks.Add(track);
        var takes = RecordedTakes.FromRecording("pass.wav", 2.0, 5.0, (2.0, 4.0));   // 2 s loop, 5 s of recording: 3 passes (2 + 2 + partial 1)
        DocumentEdits.Run(document, _ => { RecordedTakes.Place(track, takes, midi: false); return true; }, label: "Record");
        return (document, track);
    }

    [Fact]
    public void EveryLoopPassIsATakeAndTheLastIsActive()
    {
        var (_, track) = Landed();
        var takes = LoopTakes.Of(track, track.AudioClips[0]);
        Assert.Equal(3, takes.Count);
        Assert.True(LoopTakes.IsActive(track, takes[2]));
        Assert.False(LoopTakes.IsActive(track, takes[0]));
        Assert.Equal(1.0, takes[2].SourceLengthSec, 6);   // the partial last pass is kept
    }

    [Fact]
    public void TheMenuListsTakesWithATickOnTheActiveOne()
    {
        var (_, track) = Landed();
        var takes = LoopTakes.Of(track, track.AudioClips[0]);
        var state = new ClipMenuState(true, false, false, false, Takes: takes.Select((t, i) => ($"Take {i + 1}", LoopTakes.IsActive(track, t))).ToList());
        var sub = TimelineMenus.Clip(state, _ => "").Single(row => row.Header == "Takes").Children!;
        Assert.Equal(new[] { "Take 1", "Take 2", "Take 3" }, sub.Select(r => r.Header));
        Assert.Equal(new[] { false, false, true }, sub.Select(r => r.Checked));
    }

    [Fact]
    public void ChoosingATakeMakesItActiveInOneUndoStep()
    {
        var (document, track) = Landed();
        var takes = LoopTakes.Of(track, track.AudioClips[0]);
        var depth = document.Undo.UndoCount;
        DocumentEdits.Run(document, _ => LoopTakes.Activate(track, takes, takes[0]), label: "Choose take");
        Assert.True(LoopTakes.IsActive(track, takes[0]));
        Assert.False(LoopTakes.IsActive(track, takes[2]));
        Assert.Equal(depth + 1, document.Undo.UndoCount);
    }

    [Fact]
    public void TheWholeRecordingIsOneUndoStep()
    {
        var (document, _) = Landed();
        Assert.Equal(1, document.Undo.UndoCount);
        DocumentEdits.Undo(document);
        Assert.Empty(document.Project.Tracks.Last().AudioClips);
    }

    private static List<AudioClip> MidiLaps()
    {
        var take = new MidiTake();
        var loop = ((double Start, double End)?)(0.0, 2.0);
        take.Keep(0x90, 60, 100, 0.1, 0, loop, s => s);
        take.Keep(0x80, 60, 0, 0.5, 0, loop, s => s);
        take.Keep(0x90, 62, 100, 0.1, 1, loop, s => s);
        take.Keep(0x80, 62, 0, 0.5, 1, loop, s => s);
        return take.Finish("Keys", 4.0, looped: true);
    }

    [Fact]
    public void TwoMidiLoopRecordingsOnOneTrackKeepSeparateGroups()
    {
        var track = NewTrackFactory.CreateTrack(DemoSongFactory.Create(), TrackKind.Audio);
        var first = MidiLaps();
        var second = MidiLaps();
        RecordedTakes.Place(track, first, midi: true);
        RecordedTakes.Place(track, second, midi: true);
        Assert.Equal(2, LoopTakes.Of(track, first[0]).Count);
        Assert.All(LoopTakes.Of(track, first[0]), c => Assert.Contains(c, first));
        Assert.All(LoopTakes.Of(track, second[1]), c => Assert.Contains(c, second));
        var before = second.Select(c => c.Muted).ToList();
        LoopTakes.Activate(track, LoopTakes.Of(track, first[0]), first[0]);
        Assert.Equal(before, second.Select(c => c.Muted).ToList());
    }

    [Fact]
    public void ChoosingATakeLeavesOtherLanesAlone()
    {
        var (_, track) = Landed();
        var other = new AudioClip { File = "other.wav", Name = "other", StartSec = 2, SourceLengthSec = 1 };
        RecordedTakes.Place(track, new List<AudioClip> { other }, midi: false);
        var takes = LoopTakes.Of(track, track.AudioClips[0]);
        var plays = track.Lanes[other.Lane].Plays;
        LoopTakes.Activate(track, takes, takes[0]);
        Assert.Equal(plays, track.Lanes[other.Lane].Plays);
        Assert.False(other.Muted);
    }
}
