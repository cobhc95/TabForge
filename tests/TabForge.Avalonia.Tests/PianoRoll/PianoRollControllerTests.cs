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
    public void LaneListKeepsTheOldLanesFirstAndNamesKnownCcs()
    {
        var ids = PianoRollControllers.Lanes.Select(l => l.Id).ToArray();
        Assert.Equal(new[] { -1, 128, 1, 7, 10, 11, 64, 129, 130 }, ids);   // the menu's order; the ids of the original seven are unchanged
        Assert.Contains(PianoRollControllers.Program, ids);
        Assert.Contains(PianoRollControllers.ChannelAftertouch, ids);
        Assert.Equal("Program change", PianoRollControllers.NameOf(PianoRollControllers.Program));
        Assert.Equal("Channel aftertouch", PianoRollControllers.NameOf(PianoRollControllers.ChannelAftertouch));
        Assert.Equal("CC2 Breath", PianoRollControllers.NameOf(2));
        Assert.Equal("CC4 Foot", PianoRollControllers.NameOf(4));
        Assert.Equal("CC5 Portamento time", PianoRollControllers.NameOf(5));
        Assert.Equal("CC65 Portamento", PianoRollControllers.NameOf(65));
        Assert.Equal("CC66 Sostenuto", PianoRollControllers.NameOf(66));
        Assert.Equal("CC67 Soft pedal", PianoRollControllers.NameOf(67));
        Assert.Equal("CC68 Legato", PianoRollControllers.NameOf(68));
        Assert.Equal("CC69 Hold 2", PianoRollControllers.NameOf(69));
        Assert.Equal("CC84 Portamento control", PianoRollControllers.NameOf(84));
        Assert.Equal("CC3", PianoRollControllers.NameOf(3));
        // A stepped program point replaces the point at its beat; after an erase the previous value holds (example c, pure).
        var points = new List<PianoRollPoint> { new(0, 24), new(8, 25) };
        var stepped = PianoRollControllers.DrawStep(points, PianoRollControllers.Program, 8, 30);
        Assert.Equal(new[] { (0.0, 24), (8.0, 30) }, stepped.Select(p => (p.Beat, p.Value)).ToArray());
        var erased = PianoRollControllers.Erase(stepped, 7.5, 8.5);
        Assert.Equal(new[] { 0.0 }, erased.Select(p => p.Beat).ToArray());
        Assert.Equal(24, PianoRollControllers.ValueAt(erased, PianoRollControllers.Program, 12));
    }

    [Fact]
    public async Task ProgramAndAnyCcPointsPlayAsProgramChangeAndControlChange()
    {
        var document = PianoRollSongs.Midi();
        var presenter = PianoRollSongs.Presenter(document);
        var steps = document.Undo.UndoCount;
        Assert.True(await presenter.CommitPointsAsync(PianoRollControllers.Program, new[] { new PianoRollPoint(0, 24), new PianoRollPoint(8, 25) }, "Draw program"));
        Assert.True(await presenter.CommitPointsAsync(2, new[] { new PianoRollPoint(0, 0), new PianoRollPoint(4, 100) }, "Draw controller"));
        Assert.True(await presenter.CommitPointsAsync(PianoRollControllers.ChannelAftertouch, new[] { new PianoRollPoint(1, 80) }, "Draw controller"));
        Assert.Equal(steps + 3, document.Undo.UndoCount);
        document.Project.MarkTimelineChanged();
        var full = MidiTimelineBuilder.Build(document.Project, new PlaybackOptions { RespectMuteSolo = false }).Events;
        var programs = full.Where(e => (e.Status & 0xF0) == 0xC0 && e.TrackIndex == 0 && !e.IsSetup).OrderBy(e => e.TimeMs).ToList();
        Assert.Equal(new[] { 24, 25 }, programs.Select(e => e.Data1).ToArray());
        Assert.Equal(0, programs[0].TimeMs, 3);
        Assert.Equal(4000, programs[1].TimeMs, 3);   // beat 8 at 120 bpm
        Assert.Equal(new[] { 0, 100 }, full.Where(e => (e.Status & 0xF0) == 0xB0 && e.TrackIndex == 0 && e.Data1 == 2 && !e.IsSetup)
            .OrderBy(e => e.TimeMs).Select(e => e.Data2).ToArray());
        Assert.Contains(full, e => (e.Status & 0xF0) == 0xD0 && e.TrackIndex == 0 && e.Data1 == 80 && !e.IsSetup);
    }

    [Fact]
    public async Task ALaneProgramAndCcSurviveASeekChasedIntoTheSetup()
    {
        var document = PianoRollSongs.Midi();
        var presenter = PianoRollSongs.Presenter(document);
        await presenter.CommitPointsAsync(PianoRollControllers.Program, new[] { new PianoRollPoint(0, 24), new PianoRollPoint(8, 25) }, "Draw program");
        await presenter.CommitPointsAsync(2, new[] { new PianoRollPoint(0, 0), new PianoRollPoint(4, 100) }, "Draw controller");
        document.Project.MarkTimelineChanged();
        // Example (a): program 24 at beat 0, 25 at beat 8; a seek to bar 4 (index 3) sends 25 first.
        var seek = MidiTimelineBuilder.Build(document.Project, new PlaybackOptions { StartBar = 3, RespectMuteSolo = false });
        Assert.True(seek.StartedAfterMixPoints);
        var program = seek.ChannelSetup.Last(e => e.TrackIndex == 0 && (e.Status & 0xF0) == 0xC0);
        Assert.Equal(25, program.Data1);
        Assert.True(program.IsChased);
        // Example (b): CC2 0 at beat 0, 100 at beat 4; the seek holds 100.
        var cc2 = seek.ChannelSetup.Last(e => e.TrackIndex == 0 && (e.Status & 0xF0) == 0xB0 && e.Data1 == 2);
        Assert.Equal(100, cc2.Data2);
        Assert.True(cc2.IsChased);
        Assert.DoesNotContain(seek.Events, e => e.TrackIndex == 0 && !e.IsSetup && e.TimeMs < seek.PlayFromMs - 0.5
            && ((e.Status & 0xF0) == 0xC0 || ((e.Status & 0xF0) == 0xB0 && e.Data1 == 2)));
        // Example (c): deleting the beat-8 program point leaves 24 holding through the seek.
        await presenter.CommitPointsAsync(PianoRollControllers.Program,
            PianoRollControllers.Erase(presenter.Points(PianoRollControllers.Program), 7.5, 8.5), "Erase controller");
        document.Project.MarkTimelineChanged();
        var after = MidiTimelineBuilder.Build(document.Project, new PlaybackOptions { StartBar = 3, RespectMuteSolo = false });
        Assert.Equal(24, after.ChannelSetup.Last(e => e.TrackIndex == 0 && (e.Status & 0xF0) == 0xC0).Data1);
    }

    [Fact]
    public async Task ExportedMidiContainsTheLaneProgramChanges()
    {
        var document = PianoRollSongs.Midi();
        var presenter = PianoRollSongs.Presenter(document);
        await presenter.CommitPointsAsync(PianoRollControllers.Program, new[] { new PianoRollPoint(0, 24), new PianoRollPoint(8, 25) }, "Draw program");
        document.Project.MarkTimelineChanged();
        var folder = Path.Combine(Path.GetTempPath(), "tf-pianoroll-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "song.mid");
            MidiExportService.Export(document.Project, path);
            var found = ScanPrograms(File.ReadAllBytes(path));
            Assert.Contains(24, found);
            Assert.Contains(25, found);
        }
        finally { Directory.Delete(folder, true); }
    }

    /// <summary>Every program change value of an exported file (status byte then one data byte; no running status in our writer).</summary>
    private static List<int> ScanPrograms(byte[] file)
    {
        var found = new List<int>();
        var offset = 14;   // past the MThd header
        while (offset + 8 <= file.Length)
        {
            if (file[offset] != (byte)'M' || file[offset + 1] != (byte)'T' || file[offset + 2] != (byte)'r' || file[offset + 3] != (byte)'k') break;
            var length = (file[offset + 4] << 24) | (file[offset + 5] << 16) | (file[offset + 6] << 8) | file[offset + 7];
            var end = Math.Min(file.Length, offset + 8 + length);
            var i = offset + 8;
            while (i < end)
            {
                i = SkipVarLen(file, i);   // the delta
                if (i >= end) break;
                var status = file[i++];
                if (status == 0xFF) { i++; i = SkipData(file, i, end); }   // meta: type, length, bytes
                else if (status is 0xF0 or 0xF7) { i = SkipData(file, i, end); }   // sysex: length, bytes
                else if ((status & 0xF0) == 0xC0) { if (i < end) found.Add(file[i++]); }
                else if ((status & 0xF0) is 0xD0) i++;
                else i += 2;   // every other voice message carries two data bytes
            }
            offset = end;
        }
        return found;
    }

    private static int SkipVarLen(byte[] file, int i)
    {
        while (i < file.Length && (file[i++] & 0x80) != 0) { }
        return i;
    }

    private static int SkipData(byte[] file, int i, int end)
    {
        var value = 0;
        while (i < end) { var b = file[i++]; value = (value << 7) | (b & 0x7F); if ((b & 0x80) == 0) break; }
        return Math.Min(end, i + value);
    }

    [Fact]
    public void NewLaneIdsRoundTripThroughSaveAndLoad()
    {
        var song = PianoRollSongs.Midi().Project;
        song.Tracks[0].AudioClips[0].Controllers = new List<ClipController>
        {
            new(0, ClipController.ProgramChange, 24), new(4, 2, 100), new(0.5, ClipController.ChannelAftertouch, 80), new(1, 11, 90),
        };
        var folder = Path.Combine(Path.GetTempPath(), "tf-pianoroll-lanes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "song.tforge");
            ProjectService.Save(path, song);
            var loaded = ProjectService.Load(path).Tracks[0].AudioClips[0].Controllers!;
            Assert.Equal(song.Tracks[0].AudioClips[0].Controllers, loaded);
            Assert.Contains(loaded, c => c.Controller == ClipController.ProgramChange && c.Value == 24);
            Assert.Contains(loaded, c => c.Controller == ClipController.ChannelAftertouch && c.Value == 80);
        }
        finally { Directory.Delete(folder, true); }
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
