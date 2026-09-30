using System.Linq;
using TabForge.Models;
using TabForge.Plugins;

namespace TabForge.Presets;

public static class TemplateFactory
{
    public static SongProject Create(string name) => name switch
    {
        "Modern Metal" => ModernMetal(),
        "Rock Band" => RockBand(),
        "Acoustic Song" => Acoustic(),
        _ => Blank()
    };

    public static SongProject Blank()
    {
        var p = Base("Untitled", 120);
        p.Tracks.Add(Guitar("Guitar", 0, 29, "Clean"));
        return p;
    }

    public static SongProject ModernMetal()
    {
        var p = Base("Modern Metal", 145);
        Sections(p, ("Intro", 0), ("Verse", 4), ("Pre-Chorus", 12), ("Chorus", 16), ("Verse", 24), ("Chorus", 32));
        p.Tracks.Add(Guitar("Rhythm Guitar L", 0, 30, "Modern Rhythm L", new[] { 64, 59, 55, 50, 45, 36 }));
        p.Tracks.Add(Guitar("Rhythm Guitar R", 1, 30, "Modern Rhythm R", new[] { 64, 59, 55, 50, 45, 36 }));
        p.Tracks.Add(Guitar("Lead Guitar", 2, 29, "Lead"));
        p.Tracks.Add(Bass("Bass", 3));
        p.Tracks.Add(Drums("Drums", 9));
        return p;
    }

    public static SongProject RockBand()
    {
        var p = Base("Rock Band", 125);
        Sections(p, ("Intro", 0), ("Verse", 4), ("Chorus", 12), ("Solo", 20), ("Chorus", 26));
        p.Tracks.Add(Guitar("Rhythm Guitar", 0, 30, "Crunch"));
        p.Tracks.Add(Guitar("Lead Guitar", 1, 29, "Lead"));
        p.Tracks.Add(Bass("Bass", 2));
        p.Tracks.Add(Drums("Drums", 9));
        return p;
    }

    public static SongProject Acoustic()
    {
        var p = Base("Acoustic Song", 96);
        Sections(p, ("Intro", 0), ("Verse", 4), ("Bridge", 12), ("Outro", 20));
        p.Tracks.Add(Guitar("Acoustic Guitar", 0, 25, "Acoustic"));
        return p;
    }

    /// <summary>Adds default song sections so the arrangement overview shows structure immediately.</summary>
    private static void Sections(SongProject project, params (string Name, int Bar)[] sections)
    {
        var palette = new[] { "#2E74B5", "#3FB950", "#D8A032", "#8B5CF6", "#E5484D", "#00B7C3", "#D850C6" };
        for (var i = 0; i < sections.Length; i++)
        {
            project.Markers.Add(new MarkerModel
            {
                MeasureIndex = sections[i].Bar,
                Title = sections[i].Name,
                ColorHex = palette[i % palette.Length]
            });
        }
    }

    private static SongProject Base(string title, int bpm) => new()
    {
        Title = title,
        Tempo = bpm,
        Tracks = new List<TrackModel>()
    };

    private static TrackModel Guitar(string name, int channel, int program, string rig, int[]? tuning = null)
    {
        return new TrackModel
        {
            Name = name,
            Kind = TrackKind.Guitar,
            MidiChannel = channel,
            MidiProgram = program,
            StringTunings = (tuning ?? new[] { 64, 59, 55, 50, 45, 40 }).ToList(),
            Measures = Measures(32),
            Rig = new RigPreset { Name = rig, ArticulationMap = "Generic Guitar" }
        };
    }

    private static TrackModel Bass(string name, int channel) => new()
    {
        Name = name,
        Kind = TrackKind.Bass,
        MidiChannel = channel,
        MidiProgram = 34,
        StringTunings = new List<int> { 43, 38, 33, 28 },
        Measures = Measures(32),
        Rig = new RigPreset { Name = "Bass DI + Amp", ArticulationMap = "Generic Bass" }
    };

    private static TrackModel Drums(string name, int channel) => new()
    {
        Name = name,
        Kind = TrackKind.Drums,
        MidiChannel = channel,
        MidiProgram = 0,
        StringTunings = new List<int> { 49, 46, 42, 38, 36 },
        Measures = Measures(32),
        Rig = new RigPreset { Name = "Acoustic Kit", ArticulationMap = "GM Drums" }
    };

    public static List<MeasureModel> Measures(int count) => Enumerable.Range(1, count)
        .Select(i => new MeasureModel { Number = i })
        .ToList();
}
