using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presets;
using TabForge.Services;
using TabForge.Views.EffectEditors;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: per-note vibrato depth and speed: the pitch-wheel amplitude, the null default, the edit helper and the save/load round trip.
public class VibratoSettingsTests
{
    private static SongProject Song(TabNote note)
    {
        var song = new SongProject { Title = "T", Tempo = 120 };
        var track = new TrackModel { Name = "Lead", Kind = TrackKind.Guitar, MidiChannel = 0, StringTunings = new List<int> { 64, 59, 55, 50, 45, 40 }, Measures = TemplateFactory.Measures(1) };
        song.Tracks.Add(track);
        var cell = track.Measures[0].Cells[0];
        cell.DurationDenominator = 1;
        note.StringIndex = 0; note.Fret = 15; note.MidiValue = track.PitchOf(0, 15);
        cell.Notes.Add(note);
        return song;
    }

    private static int MaxWheelOffset(SongProject song, PlaybackOptions? options = null) =>
        MidiTimelineBuilder.Build(song, options ?? new PlaybackOptions()).Events
            .Where(e => (e.Status & 0xF0) == 0xE0).Select(e => Math.Abs(((e.Data2 << 7) | e.Data1) - 8192)).DefaultIfEmpty(0).Max();

    private static TabNote Vibrato(double? depth = null, double? speed = null)
    {
        var note = new TabNote { VibratoDepth = depth, VibratoSpeed = speed };
        note.Techniques.Add("Vibrato");
        return note;
    }

    [Fact]
    public void Custom_depth_changes_the_pitch_wheel_amplitude_and_null_keeps_the_default()
    {
        var normal = MaxWheelOffset(Song(Vibrato()));
        var deeper = MaxWheelOffset(Song(Vibrato(depth: 0.94)));
        var wideDefault = new TabNote(); wideDefault.Techniques.Add("WideVibrato");
        Assert.Equal(MaxWheelOffset(Song(wideDefault)), deeper);   // 0.94 is the wide default
        Assert.True(deeper > normal * 2);
        Assert.Equal(normal, MaxWheelOffset(Song(Vibrato()), new PlaybackOptions { NormalVibratoDepth = PlaybackOptions.DefaultNormalVibrato }));
        Assert.True(MaxWheelOffset(Song(Vibrato()), new PlaybackOptions { NormalVibratoDepth = 0.94 }) > normal * 2);   // the global default applies to a note with none
    }

    [Fact]
    public void Edit_stores_null_for_the_defaults_and_values_otherwise()
    {
        var note = Vibrato();
        Assert.True(VibratoEdits.Apply(new[] { note }, wide: true, depth: 0.94, speed: 5.5, defaultDepth: 0.94, defaultSpeed: 5.5));
        Assert.True(note.Techniques.Contains("WideVibrato") && !note.Techniques.Contains("Vibrato"));
        Assert.Null(note.VibratoDepth);
        Assert.True(VibratoEdits.Apply(new[] { note }, wide: false, depth: 0.6, speed: 7, defaultDepth: 0.38, defaultSpeed: 5.5));
        Assert.Equal((0.6, 7.0), (note.VibratoDepth, note.VibratoSpeed));
        Assert.True(VibratoEdits.Clean(new[] { note }));
        Assert.False(VibratoEdits.Has(note));
        Assert.Null(note.VibratoDepth);
    }

    [Fact]
    public void Depth_and_speed_survive_save_and_load()
    {
        var song = Song(Vibrato(0.7, 6.5));
        var path = Path.Combine(Path.GetTempPath(), "vibrato-" + Guid.NewGuid().ToString("N") + ".tforge");
        try
        {
            ProjectService.Save(path, song);
            var back = ProjectService.Load(path).Tracks[0].Measures[0].Cells[0].Notes[0];
            Assert.Equal((0.7, 6.5), (back.VibratoDepth, back.VibratoSpeed));
        }
        finally { File.Delete(path); }

        var writer = new BarWriter();
        BarCodec.Write(writer, song.Tracks[0].Measures[0]);
        var decoded = BarCodec.Read(writer.Written)!.Cells[0].Notes[0];
        Assert.Equal((0.7, 6.5), (decoded.VibratoDepth, decoded.VibratoSpeed));
        Assert.Null(Song(Vibrato()).Tracks[0].Measures[0].Cells[0].Notes[0].VibratoDepth);
    }
}
