using System.Collections.Generic;
using System.Linq;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: a legato slide merges only its immediate destination; later same-pitch notes attack, a tied destination still sustains.
public class LegatoSlideTieCoreTests
{
    private static List<int> Attacks(params (int Fret, bool Tied, bool Slide)[] cells)
    {
        var song = new SongProject { Title = "T", Tempo = 120 };
        var track = new TrackModel { Name = "Lead", Kind = TrackKind.Guitar, MidiChannel = 0, StringTunings = new List<int> { 64, 59, 55, 50, 45, 40 }, Measures = TemplateFactory.Measures(1) };
        song.Tracks.Add(track);
        for (var i = 0; i < cells.Length; i++)
        {
            var c = track.Measures[0].Cells[i];
            c.DurationDenominator = 8;
            var n = new TabNote { StringIndex = 0, Fret = cells[i].Fret, MidiValue = track.PitchOf(0, cells[i].Fret), Tied = cells[i].Tied };
            if (cells[i].Slide) n.Techniques.Add("LegatoSlide");
            c.Notes.Add(n);
        }
        return MidiTimelineBuilder.Build(song, new PlaybackOptions { StartBar = 0 }).Notes.Where(n => n.Bar == 0).Select(n => n.Cell).OrderBy(c => c).ToList();
    }

    [Fact]
    public void Legato_slide_target_is_not_re_picked_but_later_same_pitch_notes_are()
        => Assert.Equal(new[] { 0, 2, 3, 4, 5 }, Attacks((5, false, true), (7, false, false), (7, false, false), (7, false, false), (7, false, false), (7, false, false)));

    [Fact]
    public void Legato_slide_into_a_tied_note_stays_one_attack()
        => Assert.Equal(new[] { 0 }, Attacks((5, false, true), (7, false, false), (7, true, false)));
}
