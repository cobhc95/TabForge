using System.IO;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;

namespace TabForge.Diagnostics;

/// <summary>
/// `--write-tutorial-starters &lt;dir&gt;`: the seven synthetic "My first riff" starter songs of the tutorial (four bars, 4/4, A minor, 100 BPM),
/// each written as a .gp with the whole project embedded and verified by reading it back. Original and trivial; they belong to us.
/// </summary>
internal static class TutorialStarterSongs
{
    private const int Guitar = 0;   // string indexes run high to low: 4 = A string, 5 = low E string

    public static IReadOnlyList<(string File, SongProject Song)> All()
    {
        var list = new List<(string, SongProject)>();
        list.Add(("first-riff-05-empty.gp", Song("My first riff (blank start)", withRiff: false, 4)));
        list.Add(("first-riff-05.gp", Song("My first riff", withRiff: true, 4)));
        var s6 = Song("My first riff", true, 4); Techniques(s6.Tracks[Guitar]);
        list.Add(("first-riff-06.gp", s6));
        var s7 = Song("My first riff", true, 4); Techniques(s7.Tracks[Guitar]); s7.Tracks.Add(BassTrack(4, 0));
        list.Add(("first-riff-07.gp", s7));
        var s8 = Song("My first riff", true, 4); Techniques(s8.Tracks[Guitar]); s8.Tracks.Add(BassTrack(4, 0)); s8.Tracks.Add(DrumTrack(4, 0));
        list.Add(("first-riff-08.gp", s8));
        var s9 = Song("My first riff", true, 4); Techniques(s9.Tracks[Guitar]); s9.Tracks.Add(BassTrack(4, 0)); s9.Tracks.Add(DrumTrack(4, 0));
        s9.Tracks[0].Volume = 100; s9.Tracks[0].Pan = 54; s9.Tracks[1].Volume = 90; s9.Tracks[2].Volume = 95;
        list.Add(("first-riff-09.gp", s9));
        // Arranged: Intro (bars 1-4), the riff copied twice as Verse (bars 5-12), one bar of rest (bar 13).
        var s11 = Song("My first riff", false, 13);
        var g = s11.Tracks[Guitar]; var b = BassTrack(13, 0); var d = DrumTrack(13, 0);
        foreach (var offset in new[] { 0, 4, 8 })
        {
            FillGuitar(g, offset); FillBass(b, offset); FillDrums(d, offset);
        }
        Techniques(g);
        s11.Tracks.Add(b); s11.Tracks.Add(d);
        s11.Tracks[0].Volume = 100; s11.Tracks[0].Pan = 54; s11.Tracks[1].Volume = 90; s11.Tracks[2].Volume = 95;
        s11.Markers.Add(new MarkerModel { MeasureIndex = 0, Title = "Intro", ColorHex = "#2E74B5" });
        s11.Markers.Add(new MarkerModel { MeasureIndex = 4, Title = "Verse", ColorHex = "#C0392B" });
        s11.Markers.Add(new MarkerModel { MeasureIndex = 8, Title = "Verse 2", ColorHex = "#C0392B" });
        s11.Markers.Add(new MarkerModel { MeasureIndex = 12, Title = "Rest", ColorHex = "#7F8C8D" });
        list.Add(("first-riff-11.gp", s11));
        return list;
    }

    private static SongProject Song(string title, bool withRiff, int bars)
    {
        var song = new SongProject { Title = title, Artist = "TabForge tutorial", Tempo = 100, TimeSignatureNumerator = 4, TimeSignatureDenominator = 4 };
        var guitar = new TrackModel { Name = "Guitar", Kind = TrackKind.Guitar, MidiProgram = 29, MidiChannel = 0, InstrumentName = "Overdriven Guitar", Measures = TemplateFactory.Measures(bars) };
        if (withRiff) FillGuitar(guitar, 0);
        song.Tracks.Add(guitar);
        return song;
    }

    private static TabNote N(TrackModel t, int s, int fret) => new() { StringIndex = s, Fret = fret, MidiValue = t.PitchOf(s, fret) };

    private static void Put(MeasureModel m, int cell, int den, params TabNote[] notes)
    {
        var c = m.Cells[cell];
        c.DurationDenominator = den;
        c.Notes.AddRange(notes);
    }

    /// <summary>Bar 1: 0 0 3 0 0 5 3 0 and bar 2: 0 0 3 0 0 5 7 5 as eighths on the A string; bar 3: A5, G5 (quarters), F5 (half); bar 4: E5 whole.</summary>
    private static void FillGuitar(TrackModel t, int offset)
    {
        var bar1 = new[] { 0, 0, 3, 0, 0, 5, 3, 0 };
        var bar2 = new[] { 0, 0, 3, 0, 0, 5, 7, 5 };
        for (var i = 0; i < 8; i++)
        {
            Put(t.Measures[offset], i * 2, 8, N(t, 4, bar1[i]));
            Put(t.Measures[offset + 1], i * 2, 8, N(t, 4, bar2[i]));
        }
        Put(t.Measures[offset + 2], 0, 4, N(t, 5, 5), N(t, 4, 7));
        Put(t.Measures[offset + 2], 4, 4, N(t, 5, 3), N(t, 4, 5));
        Put(t.Measures[offset + 2], 8, 2, N(t, 5, 1), N(t, 4, 3));
        Put(t.Measures[offset + 3], 0, 1, N(t, 5, 0), N(t, 4, 2));
    }

    /// <summary>Chapter 6: palm mute on bars 1 and 2, a legato slide from the 5 to the 7 in bar 2, vibrato on the bar 4 chord (every copy of the riff).</summary>
    private static void Techniques(TrackModel t)
    {
        for (var start = 0; start + 3 < t.Measures.Count; start += 4)
        {
            if (t.Measures[start].Cells[0].Notes.Count == 0) continue;
            for (var bar = start; bar < start + 2; bar++)
                foreach (var cell in t.Measures[bar].Cells)
                    foreach (var note in cell.Notes) note.Techniques.Add(TechniqueNames.PalmMute);
            var slide = t.Measures[start + 1].Cells[10].Notes[0];   // the 5 (sixth eighth)
            slide.Techniques.Add(TechniqueNames.LegatoSlide);
            slide.SlideTargetMidi = t.PitchOf(4, 7);
            foreach (var note in t.Measures[start + 3].Cells[0].Notes) note.Techniques.Add(TechniqueNames.Vibrato);
        }
    }

    private static TrackModel BassTrack(int bars, int offset)
    {
        var bass = new TrackModel
        {
            Name = "Bass", Kind = TrackKind.Bass, MidiProgram = 33, MidiChannel = 1, InstrumentName = "Electric Bass (finger)", ColorHex = "#35B954",
            StringTunings = new List<int> { 43, 38, 33, 28 }, NumberOfFrets = 24, Measures = TemplateFactory.Measures(bars)
        };
        FillBass(bass, offset);
        return bass;
    }

    /// <summary>The bass doubles the riff an octave down: same A-string frets in bars 1 and 2, the chord roots on the low E string in bar 3, open E in bar 4.</summary>
    private static void FillBass(TrackModel t, int offset)
    {
        var bar1 = new[] { 0, 0, 3, 0, 0, 5, 3, 0 };
        var bar2 = new[] { 0, 0, 3, 0, 0, 5, 7, 5 };
        for (var i = 0; i < 8; i++)
        {
            Put(t.Measures[offset], i * 2, 8, N(t, 2, bar1[i]));
            Put(t.Measures[offset + 1], i * 2, 8, N(t, 2, bar2[i]));
        }
        Put(t.Measures[offset + 2], 0, 4, N(t, 3, 5));
        Put(t.Measures[offset + 2], 4, 4, N(t, 3, 3));
        Put(t.Measures[offset + 2], 8, 2, N(t, 3, 1));
        Put(t.Measures[offset + 3], 0, 1, N(t, 3, 0));
    }

    private static TrackModel DrumTrack(int bars, int offset)
    {
        var drums = new TrackModel
        {
            Name = "Drums", Kind = TrackKind.Drums, MidiProgram = 0, MidiChannel = 9, ColorHex = "#D850C6", InstrumentName = "Drum Kit (Standard)",
            StringTunings = new List<int> { 49, 42, 48, 38, 43, 36 }, Measures = TemplateFactory.Measures(bars)
        };
        FillDrums(drums, offset);
        return drums;
    }

    private static TabNote Hit(int midi) => new() { StringIndex = GuitarProImporter.DrumLine(midi), Fret = midi, MidiValue = midi, Velocity = 100 };

    /// <summary>A basic rock beat: closed hi-hat on every eighth, kick on beats 1 and 3, snare on beats 2 and 4.</summary>
    private static void FillDrums(TrackModel t, int offset)
    {
        for (var bar = 0; bar < 4; bar++)
            for (var i = 0; i < 8; i++)
            {
                var notes = new List<TabNote> { Hit(42) };
                if (i == 0 || i == 4) notes.Add(Hit(36));
                if (i == 2 || i == 6) notes.Add(Hit(38));
                Put(t.Measures[offset + bar], i * 2, 8, notes.ToArray());
            }
    }

    /// <summary>Writes the seven files into <paramref name="dir"/>; returns false when one does not read back identically.</summary>
    public static bool Write(string dir)
    {
        Directory.CreateDirectory(dir);
        var ok = true;
        foreach (var (file, song) in All())
        {
            var path = FilePathPolicy.OutputFile(Path.Combine(dir, file), "starter song", ".gp");
            GuitarProExporter.Save(song, path, embedProject: true);
            var back = GuitarProExporter.TryReadEmbedded(path);
            var same = back is not null && ProjectService.ContentHash(back).AsSpan().SequenceEqual(ProjectService.ContentHash(song));
            Console.WriteLine($"{(same ? "ok  " : "DIFF")} {file}: {song.Tracks.Count} track(s), {song.Tracks[0].Measures.Count} bars");
            ok &= same;
        }
        return ok;
    }
}
