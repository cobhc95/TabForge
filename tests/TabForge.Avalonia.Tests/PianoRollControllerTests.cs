using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the controller lanes and the per-note channel of a MIDI track: drawing a line of points, erasing a span, the value held between points,
//   storing points in the clips as one undo step, playing them (control change and pitch bend events, a note on its own channel), and saving them.
// Does not own: the lane drawing and gestures (PianoRollViewTests).
// Tests: PianoRollControllers, ClipController, ClipNote.Channel, PianoRollPresenter.CommitPointsAsync.
public sealed class PianoRollControllerTests
{
    [Fact]
    public void ALineDrawsAPointPerStepAndReplacesTheOldOnesInItsSpan()
    {
        var old = new List<PianoRollPoint> { new(0, 10), new(2, 99), new(6, 50) };
        var line = PianoRollControllers.DrawLine(old, 1, 1, 0, 3, 100, 1);
        Assert.Equal(new[] { (0.0, 10), (1.0, 0), (2.0, 50), (3.0, 100), (6.0, 50) }, line.Select(p => (p.Beat, p.Value)).ToArray());
        Assert.Equal(new[] { 0.0, 6.0 }, PianoRollControllers.Erase(line, 0.5, 4).Select(p => p.Beat).ToArray());
        Assert.Equal(50, PianoRollControllers.ValueAt(line, 1, 2.5));
        Assert.Equal(0, PianoRollControllers.ValueAt(Array.Empty<PianoRollPoint>(), PianoRollControllers.PitchBend, 1));
        Assert.Equal(8191, PianoRollControllers.Clamp(PianoRollControllers.PitchBend, 9000));
        Assert.Equal(-8192, PianoRollControllers.Min(PianoRollControllers.PitchBend));
    }

    [Fact]
    public async Task PointsAreStoredInTheClipAsOneStepAndPlayAsControlChangeAndPitchBend()
    {
        var document = PianoRollSongs.Midi();
        var presenter = PianoRollSongs.Presenter(document);
        var steps = document.Undo.UndoCount;
        Assert.True(await presenter.CommitPointsAsync(1, new[] { new PianoRollPoint(1, 64), new PianoRollPoint(2, 127) }, "Draw controller"));
        Assert.True(await presenter.CommitPointsAsync(PianoRollControllers.PitchBend, new[] { new PianoRollPoint(1, -8192) }, "Draw controller"));
        Assert.Equal(steps + 2, document.Undo.UndoCount);
        Assert.Equal(new[] { (1.0, 64), (2.0, 127) }, presenter.Points(1).Select(p => (Math.Round(p.Beat, 6), p.Value)).ToArray());
        var clip = document.Project.Tracks[0].AudioClips[0];
        Assert.Equal(3, clip.Controllers!.Count);
        document.Project.MarkTimelineChanged();
        var events = MidiTimelineBuilder.Build(document.Project, new PlaybackOptions { RespectMuteSolo = false }).Events;
        Assert.Equal(2, events.Count(e => (e.Status & 0xF0) == 0xB0 && e.Data1 == 1 && e.Data2 is 64 or 127));
        Assert.Contains(events, e => (e.Status & 0xF0) == 0xE0 && e.Data1 == 0 && e.Data2 == 0);   // -8192: the lowest bend
        Assert.Empty(presenter.Points(PianoRollControllers.Velocity));
    }

    [Fact]
    public void ANoteOnItsOwnChannelPlaysThereAndTheChannelAndPointsAreSaved()
    {
        var song = PianoRollSongs.Midi().Project;
        var clip = song.Tracks[0].AudioClips[0];
        clip.Notes![0] = clip.Notes[0] with { Channel = 5 };
        clip.Controllers = new List<ClipController> { new(0.5, 11, 80) };
        var events = MidiTimelineBuilder.Build(song, new PlaybackOptions { RespectMuteSolo = false }).Events;
        Assert.Contains(events, e => e.Status == (0x90 | 5) && e.Data1 == 60);
        Assert.Equal(5, PianoRollSources.For(song.Tracks[0]).Read(song, song.Tracks[0], PianoRollTimeMap.Performed(song))[0].Channel);
        var folder = Path.Combine(Path.GetTempPath(), "tf-pianoroll-cc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "song.tforge");
            ProjectService.Save(path, song);
            var loaded = ProjectService.Load(path).Tracks[0].AudioClips[0];
            Assert.Equal(5, loaded.Notes![0].Channel);
            Assert.Null(loaded.Notes[1].Channel);
            Assert.Equal(new ClipController(0.5, 11, 80), loaded.Controllers!.Single());
        }
        finally { Directory.Delete(folder, true); }
        Assert.DoesNotContain("Channel", System.Text.Json.JsonSerializer.Serialize(new ClipNote(0, 1, 60, 90)));
    }
}
