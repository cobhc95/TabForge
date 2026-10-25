using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Presenters;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the MIDI clip mini piano roll: the pitch rows over the clip height, velocity as opacity, the 2 px minimum width, and the notes of a clip folded over a repeat.
/// Does not own: the live take layer (RecordingLiveTakeTests) or the clip box drawing (TimelineAudioClipTests).
/// Tests: this class.
/// </summary>
public sealed class MidiNoteLayoutTests
{
    [Fact]
    public void VelocityMapsToOpacityFromAThirdToFull()
    {
        Assert.Equal(0.35, MidiNoteLayout.OpacityOf(0), 6);
        Assert.Equal(1.0, MidiNoteLayout.OpacityOf(127), 6);
        Assert.Equal(1.0, MidiNoteLayout.OpacityOf(300), 6);
        Assert.InRange(MidiNoteLayout.OpacityOf(40), 0.5, 0.6);
    }

    [Fact]
    public void PitchRangeSpreadsItsSemitoneRowsOverTheHeightHighPitchesOnTop()
    {
        var range = new MidiPitchRange(48, 55);   // C3..G3 and the semitone above: 8 rows
        Assert.Equal(8, range.Rows);
        var (topY, rowHeight) = MidiNoteLayout.RowOf(55, range, 40);
        Assert.Equal(0, topY, 6);
        Assert.Equal(5, rowHeight, 6);
        Assert.Equal(35, MidiNoteLayout.RowOf(48, range, 40).Y, 6);
        Assert.Equal(1, MidiNoteLayout.RowOf(60, new MidiPitchRange(60, 60), 0.5).Height, 6);   // never under a pixel
    }

    [Fact]
    public void ANoteIsAtLeastTwoPixelsWide()
    {
        Assert.Equal(2, MidiNoteLayout.WidthOf(10, 10.2), 6);
        Assert.Equal(30, MidiNoteLayout.WidthOf(10, 40), 6);
    }

    private static (TimelineView View, TrackModel Track, AudioClip Clip) ViewWith(Func<SongProject, AudioClip> make, SongProject? song = null)
    {
        song ??= DemoSongFactory.Create();
        var track = NewTrackFactory.CreateTrack(song, TrackKind.Audio);
        song.Tracks.Add(track);
        var clip = make(song);
        track.AudioClips.Add(clip);
        return (new TimelineView(song), track, clip);
    }

    [AvaloniaFact]
    public void AMidiClipDrawsEightSemitoneRowsOverItsHeightWithTwoVelocityOpacities()
    {
        var (view, track, _) = ViewWith(_ => new AudioClip
        {
            Name = "Keys", StartSec = 0, SourceLengthSec = 8, FileLengthSec = 8,
            Notes = new List<ClipNote> { new(0, 1, 48, 40), new(1, 1, 55, 127) },
        });
        var draw = new InstrumentFretNumberTests.RecordingDrawContext();

        var row = view.Song.Tracks.Count - 1;
        TimelineAudioClips.DrawRow(draw, key => AvaloniaPalette.Get(key, false), view, track, row, view.LaneBandTopOf(row), AvaloniaPalette.Get("InstrumentAccent", false));

        var expectedRow = (ClipLanes.AudioLaneHeight - 8) / 8;   // the clip box is the lane less 2 x 2 px padding; the notes keep 2 px inside it
        var notes = draw.Rects.Where(r => r.Fill is { A: > 120 } && Math.Abs(r.Height - expectedRow) < 0.01).ToList();
        Assert.Equal(2, notes.Count);
        Assert.NotEqual(notes[0].Fill!.Value.A, notes[1].Fill!.Value.A);
        Assert.Equal(255, notes.Max(n => n.Fill!.Value.A));
        Assert.Equal(7 * expectedRow, Math.Abs(notes[0].Y - notes[1].Y), 4);
    }

    [AvaloniaFact]
    public void ANoteOfAFoldedMidiClipDrawsInTheSliceOfItsPass()
    {
        // Bars 1-2 repeat twice (performed 0,1,2,1,2,3): a clip over bars 0.5-5.5 folds into an ordinary slice, two stacked parts and a tail.
        var song = new TabForge.Documents.SongDocument(() => null) { Project = TabForge.Documents.SongDocument.BlankProject() };
        song.Project.Tracks[0].Measures[1].RepeatStart = true;
        song.Project.Tracks[0].Measures[2].RepeatEnd = true;
        var bar = AudioClipTimelineRules.SecondsAtBar(song.Project, 1, 0);
        var (view, track, clip) = ViewWith(_ => new AudioClip
        {
            Name = "Keys", StartSec = 0.5 * bar, SourceLengthSec = 5 * bar, FileLengthSec = 5 * bar,
            Notes = new List<ClipNote> { new(1.0 * bar, 0.05 * bar, 60, 127), new(2.7 * bar, 0.05 * bar, 60, 127) },   // song bar 1.5 (pass 1), song bar 3.2 = bar 1.2 on pass 2
        }, song.Project);
        var parts = TimelineAudioClips.FoldOf(view, clip)!.Where(slice => slice.IsPart).ToList();
        var laneTop = view.LaneBandTopOf(view.Song.Tracks.IndexOf(track));
        var first = TimelineAudioClips.SliceRect(view, parts[0], laneTop);
        var second = TimelineAudioClips.SliceRect(view, parts[1], laneTop);
        var draw = new InstrumentFretNumberTests.RecordingDrawContext();

        TimelineAudioClips.DrawRow(draw, key => AvaloniaPalette.Get(key, false), view, track, view.Song.Tracks.IndexOf(track), laneTop, AvaloniaPalette.Get("InstrumentAccent", false));

        var notes = draw.Rects.Where(r => r.Fill is { A: 255 }).ToList();
        Assert.True(notes.Count == 2, string.Join(" | ", notes.Select(n => $"{n.X:0.0},{n.Y:0.0},{n.Width:0.0},{n.Height:0.0}")));
        Assert.Contains(notes, n => n.Y >= first.Y - 0.01 && n.Y < first.Bottom && Math.Abs(n.X - (first.X + 0.25 * first.Width)) < 1.5);
        Assert.Contains(notes, n => n.Y >= second.Y - 0.01 && n.Y < second.Bottom && Math.Abs(n.X - (second.X + 0.1 * second.Width)) < 1.5);
    }
}
