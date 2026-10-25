using TabForge.Models;
using TabForge.Presets;

namespace TabForge;

// Owns: playback song-building helpers (one-track song, one-beat note).

/// <summary>
/// Song building helpers of the playback checks: a one-track song and a one-beat note (part of <see cref="SelfTest"/>).
/// </summary>
public static partial class SelfTest
{
    private static SongProject SingleTrack(int bars = 1, int bpm = 120)
    {
        var p = new SongProject { Tempo = bpm };
        p.Tracks.Add(new TrackModel { Name = "Gtr", Measures = TemplateFactory.Measures(bars) });
        return p;
    }

    private static TabCell Beat(SongProject p, int track, int bar, int cell, int denominator, int midi, int str = 0, int fret = 0)
    {
        var c = p.Tracks[track].Measures[bar].Cells[cell];
        c.DurationDenominator = denominator;
        c.Notes.Add(new TabNote { StringIndex = str, Fret = fret, MidiValue = midi });
        return c;
    }
}
