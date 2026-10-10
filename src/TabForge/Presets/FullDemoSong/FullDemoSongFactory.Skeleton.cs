using System.Linq;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Services;

namespace TabForge.Presets;

// Owns: everything in the demo song that is not a note: the performed bar order, time signatures, tempo, keys, repeats, directions, section markers, line breaks and bar flags, written on every track.
// Does not own: any note (FullDemoSongFactory.Drums.cs, FullDemoSongFactory.LeadKeys.cs, FullDemoSongFactory.Rhythm.cs).
// Tests: TestFullDemoSong.

// Owner (a): everything that is not a note (plan 1.2–1.5, 2.1–2.4, 5.1, 6.3). Structure is written on EVERY track here and
// nowhere else, so the builders never touch time signatures, tempo, keys, repeats, directions or bar flags.
internal static partial class FullDemoSongFactory
{
    /// <summary>The performed order of plan 2.2, as 1-based source bars (154 bars).</summary>
    internal static IReadOnlyList<int> ExpectedPerformedBars { get; } = BuildExpectedOrder();

    private static List<int> BuildExpectedOrder()
    {
        static IEnumerable<int> R(int a, int b) => Enumerable.Range(a, b - a + 1);
        return R(1, 18)
            .Concat(new[] { 19, 20, 21, 22, 19, 20, 21, 23 }).Concat(R(24, 31))
            .Concat(R(32, 47))
            .Concat(new[] { 48, 49, 48, 49, 48, 50 })
            .Concat(R(51, 117))
            .Concat(R(36, 39))
            .Concat(R(118, 144))
            .ToList();
    }

    /// <summary>Double bars (S11), plan 2.4.</summary>
    internal static readonly int[] DoubleBars = { 9, 10, 18, 31, 47, 50, 62, 78, 82, 90, 101, 117, 134, 140, 144 };
    /// <summary>Forced line breaks (S38), plan 2.4.</summary>
    internal static readonly int[] LineBreaks = { 11, 19, 24, 32, 40, 48, 51, 63, 71, 79, 83, 91, 102, 110, 118, 135, 141 };
    /// <summary>Prevented line breaks (S39), plan 2.4.</summary>
    internal static readonly int[] KeepOnLine = { 10, 119 };

    /// <summary>Markers (S37): 0-based bar index, title, colour, lock, length (plan 2.4).</summary>
    internal static readonly (int Index, string Title, string Color, bool Lock, int? Length)[] SectionMarkers =
    {
        (0, "Intro: Embers", "#5A8CDC", false, null),
        (9, "Swell", "#5A8CDC", false, 1),
        (10, "Intro: Meridian", "#D9423A", false, null),
        (18, "Verse 1", "#5AB46E", false, null),
        (31, "Pre-chorus 1", "#E6B43C", false, null),
        (39, "Chorus 1", "#E6962A", false, null),
        (47, "Post-chorus", "#C8643C", false, null),
        (50, "Verse 2", "#5AB46E", false, null),
        (62, "Pre-chorus 2", "#E6B43C", false, null),
        (70, "Chorus 2", "#E6962A", false, null),
        (78, "Interlude: Glass", "#58B8D8", false, null),
        (82, "Bridge", "#8B5CF6", false, null),
        (90, "Breakdown: Meridian Falls", "#D23C3C", false, null),
        (98, "Bounce", "#D23C3C", false, null),
        (100, "Wind-up", "#D23C3C", false, null),
        (101, "Solo", "#F0A830", true, null),
        (117, "Coda: Lift", "#E6962A", false, null),
        (118, "Final Chorus", "#E6962A", false, null),
        (134, "Outro", "#966EC8", false, null),
        (140, "Fake-out", "#8A8A8A", false, null),
    };

    /// <summary>Directions (S22–S33), 1-based bar → token (plan 2.4).</summary>
    internal static readonly (int Bar, string Direction)[] BarDirections =
    {
        (36, "Segno"), (39, "ToCoda"), (117, "DalSegnoAlCoda"), (118, "Coda"), (134, "ToDoubleCoda"), (135, "DoubleCoda"), (144, "Fine"),
    };

    /// <summary>Bar time signatures (S02, S48): 1-based bar → signature; every other bar is the song's 4/4.</summary>
    internal static readonly (int Bar, int Num, int Den)[] BarSignatures =
    {
        (1, 1, 4), (10, 3, 4), (83, 7, 8), (84, 7, 8), (85, 7, 8), (86, 7, 8), (118, 2, 4),
    };

    internal const string SongLyrics =
        "[Chorus 1]\n" +
        "Hold the last light as it fades,\n" +
        "we are the ash of the sun that burned us\n" +
        "down to the end of the long meridian.\n" +
        "\n" +
        "[Chorus 2]\n" +
        "Sing through the smoke till it clears,\n" +
        "we are the spark in the dark that remains\n" +
        "when the world falls to the cold meridian.\n" +
        "\n" +
        "[Post-chorus] HEY!\n" +
        "[Breakdown] RISE!";

    private static SongProject BuildSkeleton()
    {
        var song = new SongProject
        {
            // S41 title block (plan 6.3)
            Title = "Ashen Meridian",
            Subtitle = "Feature test song: not meant to be taken seriously",
            Artist = "TabForge Demo",
            Album = "Examples",
            MusicAuthor = "TabForge contributors",
            LyricsAuthor = "TabForge contributors",
            Copyright = "(c) 2026 TabForge contributors, CC0 1.0",
            TabAuthor = "TabForge",
            Instructions = "Original composition written for TabForge. Public domain (CC0 1.0). RhyL 7-string drop A, RhyR 8-string, Clean capo 5.",
            Notice = "A demo and test song: it deliberately uses every technique, notation and playback option TabForge supports, so it is not meant to be taken seriously as music. Every riff, melody, lyric and drum part was written for TabForge.",
            Lyrics = SongLyrics,                                   // S42
            Tempo = 150,                                           // S12
            TimeSignatureNumerator = 4, TimeSignatureDenominator = 4,   // S01
            KeySignature = 0, KeySignatureMinor = true,            // S03, S05: A minor
            GrayInactiveVoice = true,                              // S43
        };

        AddTracks(song);
        foreach (var t in song.Tracks) t.Measures = TemplateFactory.Measures(BarCount);
        WriteStructure(song);
        WriteClefs(song);
        WriteMarkers(song);
        return song;
    }

    // ------------------------------------------------------------------ tracks (plan 1.4, 1.5; T01–T27)

    private static void AddTracks(SongProject song)
    {
        var dropA7 = new List<int> { 64, 59, 55, 50, 45, 40, 33 };
        var eight = new List<int> { 64, 59, 55, 50, 45, 40, 33, 28 };
        var eStd = new List<int> { 64, 59, 55, 50, 45, 40 };
        var keys = new List<int> { 96, 84, 72, 60, 48, 36, 24 };

        TrackModel T(string name, TrackKind kind, List<int> tuning, int program, int channel, int volume, int pan, int reverb, int chorus,
                     string group, string color, string rig, string map, int frets = 24) => new()
        {
            Name = name, Kind = kind, StringTunings = new List<int>(tuning), MidiProgram = program, MidiChannel = channel,
            InstrumentName = kind == TrackKind.Drums ? "Drum Kit (Standard)" : GeneralMidi.NameOf(program),
            Volume = volume, Pan = pan, Reverb = reverb, Chorus = chorus, MixerGroup = group, ColorHex = color, NumberOfFrets = frets,
            Performer = "TabForge Demo", SoundSource = SoundSources.Midi, Rig = new RigPreset { Name = rig, ArticulationMap = map },
        };

        song.Tracks.Add(T(RhyL, TrackKind.Guitar, dropA7, 30, 0, 100, 0, 16, 0, "Guitars", "#D9423A", "High Gain", "Generic Guitar"));
        var rhyR = T(RhyR, TrackKind.Guitar, eight, 30, 1, 100, 127, 16, 0, "Guitars", "#B8322C", "High Gain", "Generic Guitar");
        rhyR.TrackNotes = "8-string: the low E1 is used only for the breakdown drop.";
        song.Tracks.Add(rhyR);
        song.Tracks.Add(T(Lead, TrackKind.Guitar, eStd, 29, 2, 96, 64, 40, 8, "Guitars", "#F0A830", "Lead", "Generic Guitar"));
        song.Tracks.Add(T(LeadHarm, TrackKind.Guitar, eStd, 29, 3, 86, 50, 40, 8, "Guitars", "#E0C050", "Lead", "Generic Guitar"));
        var clean = T(Clean, TrackKind.Guitar, eStd, 27, 4, 84, 84, 72, 48, "Guitars", "#58B8D8", "Clean", "Generic Guitar", frets: 22);
        clean.Capo = 5;
        clean.TrackNotes = "Capo 5. Frets are relative to the capo.";
        song.Tracks.Add(clean);
        song.Tracks.Add(T(Bass, TrackKind.Bass, new List<int> { 43, 38, 33, 28, 21 }, 34, 5, 104, 64, 8, 0, "Rhythm section", "#3C78DC", "Bass DI", "Generic Bass"));
        var sub = T(Sub, TrackKind.Bass, new List<int> { 50, 45, 40, 33 }, 38, 6, 92, 64, 0, 0, "Rhythm section", "#6A4CC8", "Sub", "Generic Bass");
        sub.Transpose = -12;
        sub.TrackNotes = "Written an octave up; sounds an octave down (Transpose -12).";
        song.Tracks.Add(sub);
        var drums = T(Drums, TrackKind.Drums, new List<int> { 49, 42, 48, 38, 43, 36 }, 0, 9, 108, 64, 28, 0, "Rhythm section", "#8A8A8A", "Kit", "GM Drums");
        drums.DrumMapPreset = DrumMaps.Custom;                     // T14, T15: the ride bell as a diamond; the rest falls back to GP5
        drums.CustomDrumMap = new List<DrumMapEntry>
        {
            new() { Midi = 53, TabLine = 0, Label = "RB", StaffStep = DrumMaps.Default(DrumMaps.GuitarPro5, 53).StaffStep, Head = "diamond" },
        };
        song.Tracks.Add(drums);
        song.Tracks.Add(T(Pad, TrackKind.Keys, keys, 89, 7, 70, 64, 80, 40, "Keys", "#7FC8A0", "Pad", "Piano", frets: 36));   // chord voicings push notes down an octave "string": up to fret 33
        song.Tracks.Add(T(Piano, TrackKind.Keys, keys, 0, 8, 80, 44, 64, 0, "Keys", "#E8E0D0", "Grand", "Piano", frets: 36));

        // T25 group levels; the explicit MixerGroup names above override the by-instrument grouping.
        song.Mixer.Grouping = MixerGrouping.ByInstrument;
        foreach (var (group, volume) in new[] { ("Guitars", 100), ("Rhythm section", 100), ("Keys", 85) })
        {
            var g = song.Mixer.Edit(group);
            g.Volume = volume; g.Pan = 0; g.Pitch = 0;
        }
    }

    // ------------------------------------------------------------------ structure on every track (plan 1.2, 1.3, 2.1, 2.4)

    private static void WriteStructure(SongProject song)
    {
        // S02 / S48 time signatures: the short bars hold exactly their slots, the bar after each change states 4/4 again.
        foreach (var (bar, num, den) in BarSignatures)
            ForAllTracks(song, bar, m =>
            {
                m.TimeSigNum = num; m.TimeSigDenom = den;
                var slots = MusicTime.BarSlots(num, den);
                if (m.Cells.Count > slots && m.Cells.All(c => c.Notes.Count == 0)) m.Cells = Enumerable.Range(0, slots).Select(_ => new TabCell()).ToList();
                EnsureSlots(m);
            });
        foreach (var bar in new[] { 2, 11, 87, 119 })
            ForAllTracks(song, bar, m => { m.TimeSigNum = 4; m.TimeSigDenom = 4; });
        ForAllTracks(song, 1, m => m.Anacrusis = true);                                      // S19

        // Tempo map (plan 1.2): S13, S14, S15.
        ForAllTracks(song, 90, m => m.MidBarTempos = new List<TempoPoint> { new(12, 112) });
        ForAllTracks(song, 91, m => m.TempoChange = 112);
        ForAllTracks(song, 101, m => m.MidBarTempos = new List<TempoPoint> { new(0, 150, 16) });
        ForAllTracks(song, 102, m => m.TempoChange = 150);
        ForAllTracks(song, 139, m => m.MidBarTempos = new List<TempoPoint> { new(0, 141, 16) });
        ForAllTracks(song, 140, m => m.MidBarTempos = new List<TempoPoint> { new(0, 132, 16) });

        // S04 / S05 key change to B minor from bar 118 (a bar without a key falls back to the song key, so every bar states it).
        for (var bar = 118; bar <= BarCount; bar++)
            ForAllTracks(song, bar, m => { m.KeySignature = 2; m.KeySignatureMinor = true; });

        // S07–S10 repeats and endings.
        ForAllTracks(song, 19, m => m.RepeatStart = true);
        ForAllTracks(song, 22, m => { m.AlternateEnding = 1; m.RepeatEnd = true; m.RepeatCount = 2; });
        ForAllTracks(song, 23, m => m.AlternateEnding = 2);
        ForAllTracks(song, 48, m => m.RepeatStart = true);
        ForAllTracks(song, 49, m => { m.AlternateEndingMask = 0b011; m.RepeatEnd = true; m.RepeatCount = 3; });
        ForAllTracks(song, 50, m => m.AlternateEnding = 3);

        foreach (var (bar, direction) in BarDirections) ForAllTracks(song, bar, m => m.Directions = direction);   // S22–S33

        for (var bar = 79; bar <= 82; bar++) ForAllTracks(song, bar, m => m.TripletFeelKind = TripletFeels.Eighth);      // S16
        for (var bar = 99; bar <= 100; bar++) ForAllTracks(song, bar, m => m.TripletFeelKind = TripletFeels.Sixteenth);  // S17
        ForAllTracks(song, 142, m => m.FreeTime = true);                                                                    // S18

        foreach (var bar in DoubleBars) ForAllTracks(song, bar, m => m.IsDoubleBar = true);          // S11
        foreach (var bar in LineBreaks) ForAllTracks(song, bar, m => m.ForceLineBreak = true);       // S38
        foreach (var bar in KeepOnLine) ForAllTracks(song, bar, m => m.PreventLineBreak = true);     // S39

        // S20 / S21 simile (per track, not structure): the builders fill these bars with a copy of the repeated bar(s).
        foreach (var name in new[] { RhyL, RhyR, Bass, Drums })
            foreach (var bar in new[] { 25, 29 }) Track(song, name).Measures[bar - 1].SimileOneBar = true;
        foreach (var name in new[] { RhyL, RhyR, Bass })
            foreach (var bar in new[] { 57, 58 }) Track(song, name).Measures[bar - 1].SimileTwoBar = true;
    }

    /// <summary>S06: guitar clef on guitars, bass clef on Bass and Sub, treble on the keys (Piano bass clef on 91 and 95, Pad alto on 135–140).</summary>
    private static void WriteClefs(SongProject song)
    {
        foreach (var t in song.Tracks)
            for (var bar = 1; bar <= BarCount; bar++)
            {
                var m = t.Measures[bar - 1];
                m.Clef = t.Name switch
                {
                    Bass or Sub => Clefs.Bass,
                    Piano => bar is 91 or 95 ? Clefs.Bass : Clefs.Treble,
                    Pad => bar is >= 135 and <= 140 ? Clefs.Alto : Clefs.Treble,
                    _ => Clefs.Guitar,
                };
            }
    }

    /// <summary>S37 markers with the same title in every track's bar.SectionName.</summary>
    private static void WriteMarkers(SongProject song)
    {
        foreach (var (index, title, color, locked, length) in SectionMarkers)
        {
            song.Markers.Add(new MarkerModel { MeasureIndex = index, Title = title, ColorHex = color, LockPosition = locked, LengthBars = length });
            ForAllTracks(song, index + 1, m => m.SectionName = title);
        }
    }

    // ------------------------------------------------------------------ mix-table automation (plan 5.1; B30)

    /// <summary>
    /// Every mix-table change of plan 5.1 as (track, 1-based bar, slot, change). Applied in Finalise, after the parts are written,
    /// so each lands on a real beat (a mix on a cell without a beat would shift the compiler's timing); empty bars get rests to carry it.
    /// </summary>
    internal static IEnumerable<(string Track, int Bar, double Slot, MixChange Mix)> MixTable()
    {
        yield return (Pad, 1, 0, new MixChange { Volume = 2 });
        yield return (Pad, 2, 0, new MixChange { Volume = 10, TransitionBeats = 16 });
        yield return (Drums, 1, 0, new MixChange { Program = 25 });
        yield return (Drums, 11, 0, new MixChange { Program = 0 });
        yield return (Clean, 40, 0, new MixChange { Volume = 9 });
        yield return (Clean, 79, 0, new MixChange { Volume = 11, Chorus = 10, Phaser = 5 });
        yield return (Clean, 83, 0, new MixChange { Volume = 9, Chorus = 4, Phaser = 0 });
        yield return (Pad, 79, 0, new MixChange { Tremolo = 8 });
        yield return (Pad, 83, 0, new MixChange { Tremolo = 0 });
        yield return (Piano, 91, 0, new MixChange { Reverb = 12 });
        yield return (Piano, 102, 0, new MixChange { Reverb = 8 });
        yield return (Piano, 135, 0, new MixChange { Volume = 16 });   // the music box sits two octaves up, where the piano is thin: lift it (A7-A04)
        yield return (Piano, 141, 0, new MixChange { Volume = 10 });   // back to the track level
        yield return (Lead, 102, 0, new MixChange { Reverb = 6, Pan = 0 });
        yield return (Lead, 110, 0, new MixChange { Pan = -3 });
        yield return (Lead, 110, 12, new MixChange { Pan = 3, TransitionBeats = 12 });
        yield return (Lead, 114, 0, new MixChange { Pan = 0, TransitionBeats = 4 });
        yield return (Lead, 117, 12, new MixChange { Reverb = 12, TransitionBeats = 2 });
        yield return (Lead, 118, 0, new MixChange { Reverb = 6 });
        yield return (Pad, 119, 0, new MixChange { Program = 48, Volume = 11 });
        yield return (RhyL, 141, 0, new MixChange { Reverb = 14, AllTracks = true });
        yield return (RhyL, 143, 0, new MixChange { Reverb = 3, AllTracks = true });
    }
}
