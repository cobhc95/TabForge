using System.Collections.Generic;
using System.Linq;
using TabForge.Avalonia.Transport;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the checks of the app hook (SynthHumanizePass through PlaybackOptions.Humanizer): only TabForge Synth with the option on
//   changes the timeline, melodic and drum tracks change (drums use the drum family: crashes under snare, human untouched),
//   nothing is lost, and a start mid-song plays the later bars exactly as the whole song.
// Does not own: the model's checks (the engine's HumanizerTests).
// Tests: SynthHumanizePass, ITimelineHumanizer, ScoreToMidiCompiler hook.
public sealed class HumanizerAppTests
{
    private static SongProject AppSong(int bars = 8)
    {
        var song = new SongProject { Title = "Feel", Tempo = 120, TimeSignatureNumerator = 4, TimeSignatureDenominator = 4 };
        song.Tracks.Add(new TrackModel { Name = "Gtr", Kind = TrackKind.Guitar, MidiChannel = 0, MidiProgram = 30,
            StringTunings = new List<int> { 64, 59, 55, 50, 45, 40 }, Measures = TemplateFactory.Measures(bars) });
        song.Tracks.Add(new TrackModel { Name = "Drums", Kind = TrackKind.Drums, MidiChannel = 9, Measures = TemplateFactory.Measures(bars) });
        foreach (var track in song.Tracks)
            for (var b = 0; b < bars; b++)
                for (var c = 0; c < 16; c++)
                {
                    var cell = track.Measures[b].Cells[c];
                    cell.DurationDenominator = 16;
                    var (str, fret) = track.Kind == TrackKind.Drums ? (0, 0) : (5 - c / 4 % 3, c % 4 * 2);
                    cell.Notes.Add(new TabNote { StringIndex = str, Fret = fret, MidiValue = track.Kind == TrackKind.Drums ? 42 : track.PitchOf(str, fret) });
                }
        return song;
    }

    private static List<(double At, int Status, int D1, int D2)> NoteEvents(ScoreTimeline t, int channel) =>
        t.Events.Where(e => e.Channel == channel && (e.IsNoteOn || e.IsNoteOff)).Select(e => (e.TimeMs, e.Status, e.Data1, e.Data2)).ToList();

    [Fact]
    public void TheAppPassChangesOnlyTabForgeSynthMelodicTracksAndKeepsTheirKeysAfterASeek()
    {
        var song = AppSong();
        var synth = new SynthHumanizePass(new PluginSettings { SoundEngine = SoundEngines.TabForgeSynth });
        var plain = MidiTimelineBuilder.Build(song, new PlaybackOptions());
        var human = MidiTimelineBuilder.Build(song, new PlaybackOptions { Humanizer = synth });
        Assert.Equal("Natural", synth.Key);
        var (drumsPlain, drumsHuman) = (NoteEvents(plain, 9), NoteEvents(human, 9));
        Assert.NotEqual(drumsPlain, drumsHuman);   // the drum channel is humanized too (as DrumProgram)
        Assert.Equal(drumsPlain.Where(e => e.D2 > 0).Select(e => e.D1).OrderBy(k => k), drumsHuman.Where(e => e.D2 > 0).Select(e => e.D1).OrderBy(k => k));
        Assert.Equal(plain.Events.Count, human.Events.Count);
        var (a, b) = (NoteEvents(plain, 0), NoteEvents(human, 0));
        Assert.NotEqual(a, b);
        Assert.Equal(a.Where(e => e.D2 > 0).Select(e => e.D1).OrderBy(k => k), b.Where(e => e.D2 > 0).Select(e => e.D1).OrderBy(k => k));
        Assert.True(human.Events.Zip(human.Events.Skip(1)).All(p => p.Second.TimeMs >= p.First.TimeMs));

        foreach (var off in new[] { new PluginSettings { SoundEngine = SoundEngines.WindowsGm }, new PluginSettings { SoundEngine = SoundEngines.TabForgeSynth, SynthHumanize = false } })
        {
            var pass = new SynthHumanizePass(off);
            Assert.Null(pass.Key);
            Assert.Equal(a, NoteEvents(MidiTimelineBuilder.Build(song, new PlaybackOptions { Humanizer = pass }), 0));
        }

        // Started at bar 2: from bar 2 + Before on, the same played notes as the whole song (times relative to the start).
        var late = MidiTimelineBuilder.Build(song, new PlaybackOptions { Humanizer = synth, StartBar = 2 });
        var from = 2 + Sf2Synth.Humanize.HumanTable.Before;
        var whole = NoteEvents(human, 0).Where(e => e.At >= from * 2000 + 50).Select(e => (System.Math.Round(e.At - 4000, 6), e.Status, e.D1, e.D2)).ToList();
        var part = NoteEvents(late, 0).Where(e => e.At >= (from - 2) * 2000 + 50).Select(e => (System.Math.Round(e.At, 6), e.Status, e.D1, e.D2)).ToList();
        Assert.Equal(whole, part);
    }
}
