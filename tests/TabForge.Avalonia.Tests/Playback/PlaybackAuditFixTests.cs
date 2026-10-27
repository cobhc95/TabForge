using System;
using System.Collections.Generic;
using System.Linq;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: audit fixes in playback: a chased controller added after the notes keeps every note-off index, an audio plug-in track never gets channel 9, a cut tied origin leaves no mid-note wheel reset, and a before-the-beat grace trims a chord-heavy lane's previous beat.
public class PlaybackAuditFixTests
{
    private static TrackModel Guitar(string name, int bars = 1) =>
        new() { Name = name, Kind = TrackKind.Guitar, MidiChannel = 0, StringTunings = new List<int> { 64, 59, 55, 50, 45, 40 }, Measures = TemplateFactory.Measures(bars) };

    private static TabNote Note(TrackModel track, int str, int fret) => new() { StringIndex = str, Fret = fret, MidiValue = track.PitchOf(str, fret) };

    [Fact]
    public void Chased_controller_missing_from_setup_keeps_each_note_off_on_its_own_event()
    {
        var song = new SongProject { Title = "T", Tempo = 120 };
        var track = Guitar("Lead", 3);
        song.Tracks.Add(track);
        track.Measures[0].Cells[0].Mix = new MixChange { Phaser = 3 };   // CC95 is not part of the channel setup
        for (var bar = 1; bar < 3; bar++) { track.Measures[bar].Cells[0].DurationDenominator = 4; track.Measures[bar].Cells[0].Notes.Add(Note(track, 0, 5 + bar)); }
        var timeline = MidiTimelineBuilder.Build(song, new PlaybackOptions { StartBar = 1 });
        Assert.Contains(timeline.ChannelSetup, e => e.IsChased && e.Data1 == 95);
        Assert.NotEmpty(timeline.Notes);
        foreach (var note in timeline.Notes)
            foreach (var index in note.OffEventIndices)
            {
                var off = timeline.Events[index];
                Assert.True(off.Status == (0x80 | (off.Channel & 0x0F)) && off.Data1 == note.Midi, $"note {note.Midi} off index {index} is status {off.Status:X} data {off.Data1}");
            }
    }

    [Fact]
    public void Audio_instrument_track_never_gets_percussion_channel_without_drums()
    {
        var song = new SongProject { Title = "T" };
        for (var i = 0; i < 10; i++) song.Tracks.Add(Guitar("G" + i));
        var audio = new TrackModel { Name = "Audio", Kind = TrackKind.Audio, SoundSource = SoundSources.Plugins, MidiSound = false };
        audio.Rig.Plugins.Add(new TabForge.Plugins.PluginSlot { Type = TabForge.Plugins.PluginSlotType.Instrument, Enabled = true });
        song.Tracks.Add(audio);
        var channels = ChannelAllocator.Assign(song);
        Assert.DoesNotContain(ChannelAllocator.PercussionChannel, channels);
        Assert.True(channels[10] >= 0);
        Assert.Equal(11, channels.Distinct().Count());
    }

    [Fact]
    public void Staccato_origin_of_a_bent_tie_leaves_no_zero_wheel_mid_note()
    {
        var song = new SongProject { Title = "T", Tempo = 120 };
        var track = Guitar("Lead");
        song.Tracks.Add(track);
        for (var i = 0; i < 2; i++)
        {
            var cell = track.Measures[0].Cells[i];
            cell.DurationDenominator = 8;
            cell.Staccato = i == 0;
            var note = Note(track, 0, 5);
            note.Tied = i == 1;
            note.BendPoints.Add(new BendPointModel { Offset = 0, Value = 4 });
            note.BendPoints.Add(new BendPointModel { Offset = 1, Value = 4 });
            cell.Notes.Add(note);
        }
        var timeline = MidiTimelineBuilder.Build(song, new PlaybackOptions { StartBar = 0 });
        Assert.Single(timeline.Notes, n => n.Bar == 0);
        var inside = timeline.Events.Where(e => (e.Status & 0xF0) == 0xE0 && e.TimeMs > 1 && e.TimeMs < 499 && (e.Data2 << 7 | e.Data1) == 8192);
        Assert.Empty(inside);
    }

    [Fact]
    public void Before_the_beat_grace_trims_the_previous_beat_of_a_chord_heavy_lane()
    {
        var song = new SongProject { Title = "T", Tempo = 120 };
        var track = Guitar("Lead");
        song.Tracks.Add(track);
        var cells = new[] { 0, 4, 8 };
        foreach (var at in cells)
        {
            var cell = track.Measures[0].Cells[at];
            cell.DurationDenominator = 4;
            if (at == 0) cell.SoundDurationPercent = 200;   // rings on past the next beat
            for (var s = 0; s < 6; s++) cell.Notes.Add(Note(track, s, 3 + at / 4));
        }
        var grace = Note(track, 0, 12);
        grace.IsGraceNote = true;
        grace.GraceBeforeBeat = true;
        track.Measures[0].Cells[8].Notes.Add(grace);
        var timeline = MidiTimelineBuilder.Build(song, new PlaybackOptions { StartBar = 0 });
        var graceOnset = timeline.Notes.Single(n => n.Fret == 12).OnsetMs;
        var previous = timeline.Notes.Where(n => n.Fret == 3).ToList();
        Assert.Equal(6, previous.Count);
        Assert.All(previous, n => Assert.True(n.EndMs <= graceOnset + 0.01, $"fret 3 string {n.StringIndex} ends {n.EndMs} after grace {graceOnset}"));
    }
}
