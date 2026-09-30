using TabForge.Models;
using TabForge.Services;

namespace TabForge.Presets;

/// <summary>
/// Builds the large verification song used by the precision tests and by
/// `TabForge.exe --gendemo`: 5 tracks, 64 bars, mixed time signatures, sections,
/// repeats, simile bars and a mix of empty / sparse / dense bars.
/// </summary>
public static class DemoSongFactory
{
    public static SongProject Create()
    {
        var project = new SongProject { Tempo = 120, Title = "Precision Demo", Artist = "TabForge", Subtitle = "5 tracks · 64 bars · mixed metres" };
        var palette = new[] { "#F61A16", "#2248E8", "#35B954", "#F4E014", "#D850C6" };
        var kinds = new[] { TrackKind.Guitar, TrackKind.Guitar, TrackKind.Bass, TrackKind.Drums, TrackKind.Keys };
        var programs = new[] { 30, 29, 34, 0, 0 };
        var names = new[] { "Lead Guitar", "Rhythm Guitar", "Bass", "Drums", "Keys" };

        for (var t = 0; t < 5; t++)
        {
            var track = new TrackModel
            {
                Name = names[t],
                Kind = kinds[t],
                ColorHex = palette[t],
                MidiProgram = programs[t],
                MidiChannel = t == 3 ? 9 : t,
                InstrumentName = kinds[t] switch
                {
                    TrackKind.Drums => "Drum Kit (Standard)",
                    TrackKind.Bass => "Electric Bass (Finger)",
                    TrackKind.Keys => "Grand Piano",
                    _ => t == 0 ? "Lead Guitar (Distortion)" : "Distortion Guitar"
                },
                StringTunings = kinds[t] switch
                {
                    TrackKind.Bass => new List<int> { 43, 38, 33, 28 },
                    TrackKind.Drums => new List<int> { 49, 46, 42, 38, 36 },
                    _ => new List<int> { 64, 59, 55, 50, 45, 40 }
                },
                Measures = TemplateFactory.Measures(64)
            };

            for (var b = 0; b < 64; b++)
            {
                var measure = track.Measures[b];
                // Mixed metres: 3/4 every 9th bar, 6/8 every 17th bar, otherwise 4/4.
                if (b % 17 == 0) { measure.TimeSigNum = 6; measure.TimeSigDenom = 8; }
                else if (b % 9 == 0) { measure.TimeSigNum = 3; measure.TimeSigDenom = 4; }

                if (b == 40) { measure.SimileOneBar = true; continue; }
                if (b % 5 == 0) continue;                       // deliberately empty bars
                if (b % 7 == 0) { measure.RepeatStart = b % 14 == 0; measure.RepeatEnd = b % 14 == 7; measure.RepeatCount = 2; }

                var notesPerBar = b % 3 == 0 ? 16 : 4;          // dense vs sparse
                var slots = MusicTime.BarSlots(measure.TimeSigNum ?? 4, measure.TimeSigDenom ?? 4);
                for (var n = 0; n < notesPerBar; n++)
                {
                    var slot = n * (slots / notesPerBar);
                    if (slot >= measure.Cells.Count) break;
                    var cell = measure.Cells[slot];
                    cell.DurationDenominator = Math.Max(1, 16 / notesPerBar);
                    if (track.Kind == TrackKind.Drums)
                    {
                        var kick = n % 2 == 0 ? 36 : 38;
                        cell.Notes.Add(new TabNote { StringIndex = 4, Fret = 0, MidiValue = kick, Velocity = n % 4 == 0 ? 110 : 85 });
                    }
                    else if (track.Kind == TrackKind.Keys)
                    {
                        cell.Notes.Add(new TabNote { StringIndex = 0, Fret = 0, MidiValue = 60 + ((n + b) % 12) });
                        if (n % 4 == 0) cell.Notes.Add(new TabNote { StringIndex = 2, Fret = 0, MidiValue = 64 + ((n + b) % 12) });
                    }
                    else
                    {
                        var stringIndex = n % track.StringTunings.Count;
                        var fret = (n + b) % 12;
                        cell.Notes.Add(new TabNote
                        {
                            StringIndex = stringIndex,
                            Fret = fret,
                            MidiValue = track.StringTunings[stringIndex] + fret,
                            Velocity = n % 4 == 0 ? 105 : 88
                        });
                    }
                }
            }
            project.Tracks.Add(track);
        }

        var sections = new (string Name, int Bar)[]
        {
            ("Intro", 0), ("Verse", 8), ("Pre-Chorus", 16), ("Chorus", 24),
            ("Verse", 32), ("Chorus", 40), ("Bridge", 48), ("Solo", 56), ("Outro", 60)
        };
        var sectionColors = new[] { "#2E74B5", "#3FB950", "#D8A032", "#8B5CF6", "#3FB950", "#8B5CF6", "#E5484D", "#00B7C2", "#64748B" };
        for (var i = 0; i < sections.Length; i++)
        {
            project.Markers.Add(new MarkerModel
            {
                MeasureIndex = sections[i].Bar,
                Title = sections[i].Name,
                ColorHex = sectionColors[i % sectionColors.Length]
            });
        }

        project.IsDirty = false;
        return project;
    }
}
