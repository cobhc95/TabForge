using TabForge.Models;
using TabForge.Services;

namespace TabForge.Presets;

/// <summary>
/// Small purpose-built songs that isolate one musical/timing behaviour each. They are used by the
/// audio verification tooling (render to WAV and analyse onsets), by the manual regression session
/// and by anyone debugging playback. Written by `TabForge.exe --gendiag &lt;dir&gt;`.
/// </summary>
public static class DiagnosticSongFactory
{
    public static IReadOnlyList<(string Name, SongProject Song)> All() => new List<(string, SongProject)>
    {
        ("01-quarters-120", Quarters(120)),
        ("02-eighths-120", Eighths(120)),
        ("03-sixteenths-120", Sixteenths(120)),
        ("04-chords-every-beat", ChordsEveryBeat()),
        ("05-dense-chords", DenseChords()),
        ("06-multitrack-sync", MultiTrackSync()),
        ("07-band", Band()),
        ("08-sustain-under-melody", SustainUnderMelody()),
        ("09-triplets", Triplets()),
        ("10-straight-vs-triplets", StraightVsTriplets()),
        ("11-dotted", Dotted()),
        ("12-ties-across-beats", TiesAcrossBeats()),
        ("13-ties-across-bars", TiesAcrossBars()),
        ("14-tempo-changes", TempoChanges()),
        ("15-rapid-repeats", RapidRepeats()),
        ("16-dense-polyphony", DensePolyphony()),
        ("17-frequent-rests", FrequentRests()),
        ("18-fast-260", Sixteenths(260)),
        ("19-seek-target", Quarters(120, 8)),
    };

    // ---------- helpers ----------

    private static SongProject New(string title, int bpm, int bars)
    {
        var p = new SongProject { Tempo = bpm, Title = title };
        p.Tracks.Add(new TrackModel { Name = "Guitar", Kind = TrackKind.Guitar, Measures = TemplateFactory.Measures(bars) });
        return p;
    }

    private static TabCell Beat(SongProject p, int bar, int cell, int midi, int denominator, int str = 0, int fret = 0)
    {
        var c = p.Tracks[0].Measures[bar].Cells[cell];
        c.DurationDenominator = denominator;
        c.Notes.Add(new TabNote { StringIndex = str, Fret = fret, MidiValue = midi, Velocity = 96 });
        return c;
    }

    private static TabCell Rest(SongProject p, int bar, int cell, int denominator)
    {
        var c = p.Tracks[0].Measures[bar].Cells[cell];
        c.DurationDenominator = denominator;
        c.IsRest = true;
        return c;
    }

    /// <summary>One note per subdivision, stepped through a scale so each attack is easy to hear.</summary>
    private static SongProject Pulse(int bpm, int denominator, int perBar, int bars)
    {
        var p = New($"{perBar} per bar at {bpm} bpm", bpm, bars);
        var scale = new[] { 64, 66, 68, 69, 71, 73, 75, 76 };
        for (var b = 0; b < bars; b++)
            for (var n = 0; n < perBar; n++)
            {
                var step = 16 / perBar;
                Beat(p, b, n * step, scale[(b * perBar + n) % scale.Length], denominator);
            }
        return p;
    }

    private static SongProject Quarters(int bpm, int bars = 4) => Pulse(bpm, 4, 4, bars);
    private static SongProject Eighths(int bpm, int bars = 4) => Pulse(bpm, 8, 8, bars);
    private static SongProject Sixteenths(int bpm, int bars = 4) => Pulse(bpm, 16, 16, bars);

    private static SongProject ChordsEveryBeat()
    {
        var p = New("Chords on every beat", 120, 4);
        for (var b = 0; b < 4; b++)
            for (var beat = 0; beat < 4; beat++)
            {
                var cell = p.Tracks[0].Measures[b].Cells[beat * 4];
                cell.DurationDenominator = 4;
                foreach (var (str, semitone) in new[] { (0, 0), (1, 4), (2, 7) })
                    cell.Notes.Add(new TabNote { StringIndex = str, Fret = semitone, MidiValue = 60 + semitone, Velocity = 96 });
            }
        return p;
    }

    private static SongProject DenseChords()
    {
        var p = New("Dense six-note chords", 120, 4);
        for (var b = 0; b < 4; b++)
            for (var n = 0; n < 8; n++)
            {
                var cell = p.Tracks[0].Measures[b].Cells[n * 2];
                cell.DurationDenominator = 8;
                for (var str = 0; str < 6; str++)
                    cell.Notes.Add(new TabNote
                    {
                        StringIndex = str, Fret = str + n % 3,
                        MidiValue = 64 - str + (str + n) % 5, Velocity = 92
                    });
            }
        return p;
    }

    private static SongProject MultiTrackSync()
    {
        var p = new SongProject { Tempo = 120, Title = "Four tracks starting together" };
        var names = new[] { "Lead", "Rhythm", "Bass", "Keys" };
        var programs = new[] { 30, 29, 34, 0 };
        for (var t = 0; t < 4; t++)
        {
            var track = new TrackModel
            {
                Name = names[t], Kind = t == 2 ? TrackKind.Bass : t == 3 ? TrackKind.Keys : TrackKind.Guitar,
                MidiChannel = t, MidiProgram = programs[t], Measures = TemplateFactory.Measures(4)
            };
            for (var b = 0; b < 4; b++)
                for (var beat = 0; beat < 4; beat++)
                {
                    var cell = track.Measures[b].Cells[beat * 4];
                    cell.DurationDenominator = 4;
                    cell.Notes.Add(new TabNote { StringIndex = 0, Fret = (b + beat) % 5, MidiValue = 52 + t * 5 + (b + beat) % 5, Velocity = 96 });
                }
            p.Tracks.Add(track);
        }
        return p;
    }

    private static SongProject Band()
    {
        var p = new SongProject { Tempo = 140, Title = "Guitar + bass + drums" };
        var guitar = new TrackModel { Name = "Guitar", Kind = TrackKind.Guitar, MidiChannel = 0, MidiProgram = 30, Measures = TemplateFactory.Measures(4) };
        var bass = new TrackModel { Name = "Bass", Kind = TrackKind.Bass, MidiChannel = 1, MidiProgram = 34, Measures = TemplateFactory.Measures(4) };
        var drums = new TrackModel { Name = "Drums", Kind = TrackKind.Drums, MidiChannel = 9, MidiProgram = 0, Measures = TemplateFactory.Measures(4) };
        p.Tracks.AddRange(new[] { guitar, bass, drums });

        for (var b = 0; b < 4; b++)
            for (var n = 0; n < 8; n++)
            {
                var cell = guitar.Measures[b].Cells[n * 2];
                cell.DurationDenominator = 8;
                cell.Notes.Add(new TabNote { StringIndex = n % 3, Fret = (n + b) % 7, MidiValue = 45 + (n + b) % 12, Velocity = 100 });

                var bassCell = bass.Measures[b].Cells[n * 2];
                bassCell.DurationDenominator = 8;
                bassCell.Notes.Add(new TabNote { StringIndex = 0, Fret = (b + n) % 5, MidiValue = 33 + (b + n) % 5, Velocity = 100 });

                var kick = n % 2 == 0 ? 36 : 38;
                var drumCell = drums.Measures[b].Cells[n * 2];
                drumCell.DurationDenominator = 8;
                drumCell.Notes.Add(new TabNote { StringIndex = 4, Fret = 0, MidiValue = kick, Velocity = n % 4 == 0 ? 110 : 88 });
            }
        return p;
    }

    private static SongProject SustainUnderMelody()
    {
        var p = new SongProject { Tempo = 120, Title = "Sustained pad under a melody" };
        var pad = new TrackModel { Name = "Pad", Kind = TrackKind.Keys, MidiChannel = 1, MidiProgram = 89, Measures = TemplateFactory.Measures(4) };
        var melody = new TrackModel { Name = "Melody", Kind = TrackKind.Guitar, MidiChannel = 0, MidiProgram = 30, Measures = TemplateFactory.Measures(4) };
        p.Tracks.Add(pad);
        p.Tracks.Add(melody);
        for (var b = 0; b < 4; b++)
        {
            var whole = pad.Measures[b].Cells[0];
            whole.DurationDenominator = 1;              // whole-bar sustained note
            whole.Notes.Add(new TabNote { StringIndex = 5, Fret = b, MidiValue = 40 + b, Velocity = 84 });

            for (var n = 0; n < 8; n++)
            {
                var cell = melody.Measures[b].Cells[n * 2];
                cell.DurationDenominator = 8;
                cell.Notes.Add(new TabNote { StringIndex = 0, Fret = n % 4, MidiValue = 66 + n % 5, Velocity = 104 });
            }
        }
        return p;
    }

    private static SongProject Triplets()
    {
        var p = New("Eighth-note triplets", 120, 4);
        for (var b = 0; b < 4; b++)
            for (var group = 0; group < 4; group++)
                for (var n = 0; n < 3; n++)
                {
                    var cell = p.Tracks[0].Measures[b].Cells[group * 4 + n];
                    cell.DurationDenominator = 8;
                    cell.IsTriplet = true;
                    cell.Notes.Add(new TabNote { StringIndex = 0, Fret = (group + n) % 5, MidiValue = 64 + (group * 3 + n) % 8, Velocity = 96 });
                }
        return p;
    }

    private static SongProject StraightVsTriplets()
    {
        var p = New("Straight eighths against triplets", 120, 4);
        for (var b = 0; b < 4; b++)
        {
            for (var n = 0; n < 4; n++) Beat(p, b, n * 2, 64 + n % 5, 8, 0);            // straight eighths (beats 1-2)
            for (var group = 0; group < 2; group++)                                      // triplets on beats 3-4
                for (var n = 0; n < 3; n++)
                {
                    var cell = p.Tracks[0].Measures[b].Cells[8 + group * 4 + n];
                    cell.DurationDenominator = 8;
                    cell.IsTriplet = true;
                    cell.Notes.Add(new TabNote { StringIndex = 1, Fret = group + n, MidiValue = 55 + group * 3 + n, Velocity = 96 });
                }
        }
        return p;
    }

    private static SongProject Dotted()
    {
        var p = New("Dotted rhythms", 120, 4);
        for (var b = 0; b < 4; b++)
        {
            var a = Beat(p, b, 0, 64 + b % 4, 4);
            a.Dots = 1;      // dotted quarter (6 slots)
            var c = Beat(p, b, 6, 66 + b % 4, 4);
            c.Dots = 1;      // dotted quarter
            Beat(p, b, 12, 67 + b % 4, 4);
        }
        return p;
    }

    private static SongProject TiesAcrossBeats()
    {
        var p = New("Ties across beats", 120, 4);
        for (var b = 0; b < 4; b++)
        {
            Beat(p, b, 0, 64 + b % 4, 8);
            Beat(p, b, 2, 64 + b % 4, 8).IsTied = true;      // tied into beat 2
            Beat(p, b, 4, 67 + b % 4, 4);
            Beat(p, b, 8, 69 + b % 4, 8);
            Beat(p, b, 10, 69 + b % 4, 8).IsTied = true;
            Beat(p, b, 12, 71 + b % 4, 4);
        }
        return p;
    }

    private static SongProject TiesAcrossBars()
    {
        var p = New("Ties across bars", 120, 4);
        for (var b = 0; b < 4; b++)
        {
            Beat(p, b, 12, 60 + b, 4);
            if (b + 1 >= 4) continue;
            var next = p.Tracks[0].Measures[b + 1].Cells[0];
            next.DurationDenominator = 4;
            next.Notes.Add(new TabNote { StringIndex = 0, Fret = b, MidiValue = 60 + b, Velocity = 96, Tied = true });
        }
        return p;
    }

    private static SongProject TempoChanges()
    {
        var p = New("Tempo changes 120-60-180", 120, 3);
        p.Tracks[0].Measures[1].TempoChange = 60;
        p.Tracks[0].Measures[2].TempoChange = 180;
        for (var b = 0; b < 3; b++)
            for (var n = 0; n < 4; n++) Beat(p, b, n * 4, 60 + n + b, 4);
        return p;
    }

    private static SongProject RapidRepeats()
    {
        var p = New("Rapid repeated sixteenths", 120, 2);
        for (var b = 0; b < 2; b++)
            for (var n = 0; n < 16; n++) Beat(p, b, n, 69, 16);
        return p;
    }

    private static SongProject DensePolyphony()
    {
        var p = New("Dense polyphony", 120, 4);
        var programs = new[] { 30, 29, 34, 0, 0, 27 };
        var names = new[] { "Gtr 1", "Gtr 2", "Bass", "Keys L", "Keys R", "Lead" };
        for (var t = 0; t < 6; t++)
        {
            var track = new TrackModel
            {
                Name = names[t], Kind = t == 2 ? TrackKind.Bass : t >= 5 ? TrackKind.Other : TrackKind.Guitar,
                MidiChannel = t, MidiProgram = programs[t], Measures = TemplateFactory.Measures(4)
            };
            for (var b = 0; b < 4; b++)
                for (var n = 0; n < 8; n++)
                {
                    var cell = track.Measures[b].Cells[n * 2];
                    cell.DurationDenominator = 8;
                    for (var v = 0; v < 3; v++)
                        cell.Notes.Add(new TabNote
                        {
                            StringIndex = v, Fret = (n + v) % 5,
                            MidiValue = 48 + t * 3 + v * 4 + (b + n) % 5, Velocity = 90
                        });
                }
            p.Tracks.Add(track);
        }
        return p;
    }

    private static SongProject FrequentRests()
    {
        var p = New("Frequent rests", 120, 4);
        for (var b = 0; b < 4; b++)
            for (var n = 0; n < 8; n++)
            {
                if (n % 2 == 0) Beat(p, b, n * 2, 64 + n % 5, 8);
                else Rest(p, b, n * 2, 8);
            }
        return p;
    }
}
