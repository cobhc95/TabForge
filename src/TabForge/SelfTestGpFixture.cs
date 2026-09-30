using System.IO;
using System.Linq;
using TabForge.Diagnostics;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;

namespace TabForge;

// Synthetic Guitar Pro compatibility fixture. Generated from code at test time, so the check never
// depends on local (commercial) songs in Tabs/ and can run on a clean public checkout / CI runner.
public static partial class SelfTest
{
    /// <summary>Requirement name for --require / TABFORGE_SELFTEST_REQUIRE: the synthetic .gp round trip must run.</summary>
    private const string RequireGpFixtures = "gp-fixtures";

    internal static SongProject BuildSyntheticGpSong()
    {
        var project = new SongProject { Tempo = 96, Title = "Synthetic GP fixture", Artist = "TabForge self-test" };
        var guitar = new TrackModel { Name = "Fixture Guitar", Kind = TrackKind.Guitar, MidiProgram = 29, MidiChannel = 0, Measures = TemplateFactory.Measures(4) };
        var bass = new TrackModel
        {
            Name = "Fixture Bass", Kind = TrackKind.Bass, MidiProgram = 33, MidiChannel = 1,
            StringTunings = new List<int> { 43, 38, 33, 28 }, Measures = TemplateFactory.Measures(4)
        };
        for (var bar = 0; bar < 4; bar++)
        {
            for (var beat = 0; beat < 4; beat++)
            {
                var g = guitar.Measures[bar].Cells[beat * 4];
                g.DurationDenominator = 4;
                var gs = (bar + beat) % guitar.StringTunings.Count;
                var gf = (bar * 3 + beat * 2) % 12;
                g.Notes.Add(new TabNote { StringIndex = gs, Fret = gf, MidiValue = guitar.StringTunings[gs] + gf });

                var b = bass.Measures[bar].Cells[beat * 4];
                b.DurationDenominator = 4;
                var bs = beat % bass.StringTunings.Count;
                var bf = (bar + beat) % 7;
                b.Notes.Add(new TabNote { StringIndex = bs, Fret = bf, MidiValue = bass.StringTunings[bs] + bf });
            }
        }
        project.Tracks.Add(guitar);
        project.Tracks.Add(bass);
        return project;
    }

    // ---- Showcase song: every technique and structure feature, generated in code (redistributable) ----

    private static void Put(MeasureModel m, TrackModel t, double slot, int den, int dots, bool trip, params TabNote[] notes)
    {
        var idx = (int)slot;
        while (idx < m.Cells.Count - 1 && (m.Cells[idx].Notes.Count > 0 || m.Cells[idx].IsRest)) idx++;
        var c = m.Cells[idx];
        c.DurationDenominator = den; c.Dots = dots; c.IsTriplet = trip;
        if (Math.Abs(slot - Math.Round(slot)) > 0.01 || idx != (int)slot) c.RhythmicPosition = slot;
        if (notes.Length == 0) c.IsRest = true; else c.Notes.AddRange(notes);
    }

    private static TabNote FretNote(TrackModel t, int s, int fret, params string[] tech)
    {
        var n = new TabNote { StringIndex = s, Fret = fret, MidiValue = t.StringTunings[s] + fret };
        foreach (var x in tech) if (x.Length > 0) n.Techniques.Add(x);
        return n;
    }

    private static TabNote Drum(int midi, int velocity = 100, bool ghost = false) =>
        new() { StringIndex = GuitarProImporter.DrumLine(midi), Fret = midi, MidiValue = midi, Velocity = velocity, Ghost = ghost };

    internal static SongProject BuildShowcaseSong()
    {
        var song = new SongProject
        {
            Title = "TabForge - Showcase fixture", Artist = "TabForge", Tempo = 120, KeySignature = 2,
            Subtitle = "Every technique, generated in code", Copyright = "Public domain / CC0 (generated)",
            Lyrics = "Show-case ver-se one\nEv-ery tech-nique here",
        };
        var tech = GmSongAudit.TechniqueSong();
        var names = GmSongAudit.TechniqueBars.ToList();
        var soloStart = 8; var outro = soloStart + names.Count; var total = outro + 2;

        var lead = new TrackModel { Name = "Lead Guitar", Kind = TrackKind.Guitar, MidiProgram = 29, MidiChannel = 0, ColorHex = "#F61A16", Measures = TemplateFactory.Measures(total) };
        var rhythm = new TrackModel
        {
            Name = "Rhythm Guitar (Drop D)", Kind = TrackKind.Guitar, MidiProgram = 30, MidiChannel = 1, ColorHex = "#2248E8",
            StringTunings = new List<int> { 64, 59, 55, 50, 45, 38 }, Measures = TemplateFactory.Measures(total)
        };
        var bass = new TrackModel
        {
            Name = "Bass", Kind = TrackKind.Bass, MidiProgram = 33, MidiChannel = 2, ColorHex = "#35B954",
            StringTunings = new List<int> { 43, 38, 33, 28 }, Measures = TemplateFactory.Measures(total)
        };
        var drums = new TrackModel
        {
            Name = "Drums", Kind = TrackKind.Drums, MidiProgram = 0, MidiChannel = 9, ColorHex = "#D850C6", InstrumentName = "Drum Kit (Standard)",
            StringTunings = new List<int> { 49, 42, 48, 38, 43, 36 }, Measures = TemplateFactory.Measures(total)   // the six drum lines the importer creates (DrumLine 0..5): a kick is on line 5
        };
        song.Tracks.AddRange(new[] { lead, rhythm, bass, drums });

        // Structure on every track: tempo change, repeat with alternate endings, 7/8 bar.
        foreach (var t in song.Tracks)
        {
            var ms = t.Measures;
            ms[4].RepeatStart = true; ms[4].TempoChange = 140;
            ms[5].TimeSigNum = 7; ms[5].TimeSigDenom = 8;
            ms[6].TimeSigNum = 4; ms[6].TimeSigDenom = 4; ms[6].RepeatEnd = true; ms[6].RepeatCount = 2; ms[6].AlternateEnding = 1;
            ms[7].AlternateEnding = 2;
            ms[soloStart].TempoChange = 120;
            ms[outro].TempoChange = 90;
        }
        var palette = new[] { "#2E74B5", "#3FB950", "#8B5CF6", "#64748B" };
        var sections = new[] { ("Intro", 0), ("Verse", 4), ("Solo", soloStart), ("Outro", outro) };
        for (var i = 0; i < sections.Length; i++)
            song.Markers.Add(new MarkerModel { MeasureIndex = sections[i].Item2, Title = sections[i].Item1, ColorHex = palette[i] });

        // Lead: melody with triplets / dots / rests, then one bar per technique, then an outro.
        for (var b = 0; b < 8; b++)
        {
            var m = lead.Measures[b];
            var slots = b == 5 ? 14 : 16;
            switch (b)
            {
                case 0: foreach (var (s, f) in new[] { (0, 3), (4, 5), (8, 7), (12, 5) }) Put(m, lead, s, 4, 0, false, FretNote(lead, 1, f)); break;
                case 1:
                    for (var k = 0; k < 3; k++) Put(m, lead, k * 4 / 3.0, 8, 0, true, FretNote(lead, 1, 5 + k));
                    Put(m, lead, 4, 4, 1, false, FretNote(lead, 1, 7)); Put(m, lead, 10, 8, 0, false, FretNote(lead, 1, 5)); Put(m, lead, 12, 4, 0, false, FretNote(lead, 1, 3)); break;
                case 2: Put(m, lead, 0, 1, 0, false, FretNote(lead, 0, 7, "Vibrato")); break;
                case 3: Put(m, lead, 0, 2, 0, false); Put(m, lead, 8, 2, 0, false, FretNote(lead, 1, 5)); break;
                case 6: Put(m, lead, 0, 1, 0, false, FretNote(lead, 1, 5)); break;
                case 7: Put(m, lead, 0, 1, 0, false, FretNote(lead, 0, 7, "FadeOut")); break;
                default:
                    for (var k = 0; k < slots / 2; k++)
                    {
                        Put(m, lead, k * 2, 8, 0, false, FretNote(lead, 2, b == 5 ? 7 : new[] { 5, 5, 7, 5, 8, 5, 7, 5 }[k]));
                        if (b == 4 && k < 2) m.Cells[k * 2].Lyrics = k == 0 ? "Show-" : "case";
                    }
                    break;
            }
        }
        for (var i = 0; i < names.Count; i++)
        {
            var m = lead.Measures[soloStart + i];
            m.Cells = tech.Tracks[0].Measures[i].Cells;
            var n = names[i];
            foreach (var c in m.Cells.Where(c => c.Notes.Count > 0))
            {
                if (n == "Accent") c.Accent = 1; else if (n == "HeavyAccent") c.Accent = 2;
                else if (n == "Staccato") c.Staccato = true; else if (n == "Tenuto") c.Tenuto = true; else if (n == "Fermata") c.Fermata = true;
                foreach (var note in c.Notes)
                {
                    if (n == "Ghost") note.Ghost = true;
                    if (n is "Dead" or "DeadSlapped") note.Dead = true;
                }
                if (n.StartsWith("TremBar")) c.WhammyPoints = new List<BendPointModel> { new() { Offset = 0, Value = 0 }, new() { Offset = 30, Value = -8 }, new() { Offset = 60, Value = 0 } };
            }
            if (n == "Tie") m.Cells[4].Notes[0].Tied = true;
        }
        Put(lead.Measures[outro], lead, 0, 1, 0, false, FretNote(lead, 1, 5, "LetRing"));
        Put(lead.Measures[outro + 1], lead, 0, 1, 0, false, FretNote(lead, 1, 3, "Vibrato"));

        // Rhythm (drop D): palm-muted chugs and power chords.
        for (var b = 0; b < 8; b++)
        {
            var m = rhythm.Measures[b];
            var steps = (b == 5 ? 14 : 16) / 2;
            for (var k = 0; k < steps; k++)
            {
                var chord = k >= steps - 2 && b % 2 == 0;
                if (chord) Put(m, rhythm, k * 2, 8, 0, false, FretNote(rhythm, 5, 3, k == steps - 1 ? "" : "Accent"), FretNote(rhythm, 4, 5));
                else Put(m, rhythm, k * 2, 8, 0, false, FretNote(rhythm, 5, 0, "PalmMute"));
            }
        }
        Put(rhythm.Measures[outro], rhythm, 0, 1, 0, false, FretNote(rhythm, 5, 0, "LetRing"), FretNote(rhythm, 4, 0, "LetRing"));
        Put(rhythm.Measures[outro + 1], rhythm, 0, 1, 0, false, FretNote(rhythm, 5, 0, "LetRing"), FretNote(rhythm, 4, 0, "LetRing"));

        // Bass: root eighths, one slap/pop bar.
        for (var b = 0; b < 8; b++)
        {
            var m = bass.Measures[b];
            if (b == 1)
            {
                for (var k = 0; k < 4; k++) Put(m, bass, k * 4, 4, 0, false, k % 2 == 0 ? FretNote(bass, 3, 0, "Slap") : FretNote(bass, 1, 2, "Pop"));
                continue;
            }
            for (var k = 0; k < (b == 5 ? 7 : 8); k++) Put(m, bass, k * 2, 8, 0, false, FretNote(bass, 3, k % 4 == 3 ? 2 : 0));
        }
        Put(bass.Measures[outro], bass, 0, 1, 0, false, FretNote(bass, 3, 0));
        Put(bass.Measures[outro + 1], bass, 0, 1, 0, false, FretNote(bass, 3, 0));

        // Drums: groove with every kit piece, a 7/8 bar, ride groove and a tom fill.
        for (var b = 0; b < 8; b++)
        {
            var m = drums.Measures[b];
            var steps = (b == 5 ? 14 : 16) / 2;
            for (var k = 0; k < steps; k++)
            {
                var hits = new List<TabNote>();
                var slot = k * 2;
                if (b == 7 && k >= 4) { hits.Add(Drum(new[] { 48, 47, 45, 43 }[k - 4])); Put(m, drums, slot, 8, 0, false, hits.ToArray()); continue; }
                if (b is 2 or 3 or 6) hits.Add(Drum(b == 6 && k % 2 == 1 ? 53 : 51));       // ride, ride bell
                else hits.Add(Drum(k == steps - 1 && b % 2 == 0 ? 46 : 42));               // closed hat, open hat
                if (k == 0) hits.Add(Drum(b == 0 ? 49 : b == 1 ? 52 : 55, 110));            // crash, china, splash
                if (k % 4 == 0 || k == 3) hits.Add(Drum(36, 110));
                if (k % 4 == 2) hits.Add(Drum(38, 105));
                if (k == 3 && b != 5) hits.Add(Drum(38, 40, ghost: true));                 // ghost snare
                if (k == 1 && b == 4) hits.Add(Drum(44, 70));                              // pedal hat
                Put(m, drums, slot, 8, 0, false, hits.ToArray());
            }
        }
        Put(drums.Measures[outro], drums, 0, 1, 0, false, Drum(49, 115), Drum(36, 115));
        Put(drums.Measures[outro + 1], drums, 0, 1, 0, false, Drum(51));
        song.IsDirty = false;
        return song;
    }

    private static readonly string[] GpStorable =
    {
        "PalmMute", "LetRing", "HOPO", "Bend", "LegatoSlide", "ShiftSlide", "Vibrato", "WideVibrato", "Harmonic", "ArtificialHarmonic",
        "PinchHarmonic", "TapHarmonic", "Ghost", "Dead", "SlideInBelow", "SlideInAbove", "SlideOutUp", "SlideOutDown",
        "TremBar", "TremoloPick", "Trill", "Tapping", "Slap", "Pop", "FadeIn", "FadeOut", "WahOpen", "WahClose", "BrushDown", "BrushUp",
        "ArpeggioDown", "ArpeggioUp", "GraceBefore", "GraceOnBeat",
    };

    private static HashSet<string> TechniqueSet(SongProject p)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in p.Tracks) foreach (var m in t.Measures) foreach (var c in m.Cells)
        {
            if (c.Accent == 1) set.Add("Accent"); else if (c.Accent == 2) set.Add("HeavyAccent");
            if (c.Staccato) set.Add("Staccato");
            foreach (var n in c.Notes)
            {
                foreach (var x in n.Techniques) set.Add(x == "Slide" ? "LegatoSlide" : x);
                if (n.Ghost) set.Add("Ghost");
                if (n.Dead) set.Add("Dead");
            }
        }
        return set;
    }

    private static void TestShowcaseGuitarProFixture()
    {
        var folder = Path.Combine(Path.GetTempPath(), "tabforge-gpshowcase-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var source = BuildShowcaseSong();
            var path = Path.Combine(folder, "showcase.gp");
            GuitarProExporter.Save(source, path, embedProject: false);
            Check("showcase .gp fixture is written", File.Exists(path) && new FileInfo(path).Length > 0);
            var imported = GuitarProImporter.Import(path);
            var bars = source.Tracks.Max(t => t.Measures.Count);
            Eq("showcase .gp: track count survives", source.Tracks.Count, imported.Tracks.Count);
            Check("showcase .gp: bar count survives", imported.Tracks.All(t => t.Measures.Count == bars),
                $"expected {bars}, got {string.Join("/", imported.Tracks.Select(t => t.Measures.Count))}");
            Check("showcase .gp: drum track present", imported.Tracks.Any(t => t.Kind == TrackKind.Drums && t.Measures.Any(m => m.Cells.Any(c => c.Notes.Count > 0))));
            Check("showcase .gp: repeat, alternate endings, 7/8 bar and tempo change survive",
                imported.Tracks[0].Measures[4].RepeatStart && imported.Tracks[0].Measures[6].RepeatEnd &&
                imported.Tracks[0].Measures[6].EndingPasses != 0 && imported.Tracks[0].Measures[5].TimeSigNum == 7 && imported.Tracks[0].Measures[4].TempoChange == 140);
            Check("showcase .gp: section markers survive", imported.Markers.Count >= 4);
            Check("showcase .gp: subtitle and copyright survive",
                imported.Subtitle == source.Subtitle && imported.Copyright == source.Copyright,
                $"subtitle '{imported.Subtitle}', copyright '{imported.Copyright}'");
            var before = TechniqueSet(source); var after = TechniqueSet(imported);
            foreach (var name in GpStorable.Where(before.Contains))
                Check($"showcase .gp: technique {name} survives", after.Contains(name));
            var lost = before.Where(x => !GpStorable.Contains(x, StringComparer.OrdinalIgnoreCase) && !after.Contains(x)).OrderBy(x => x).ToList();
            Log.Add($"  info  showcase .gp: techniques Guitar Pro export does not keep ({lost.Count}): {string.Join(", ", lost)}");
            _gpFixtureRan = true;
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    private static uint Crc32Of(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++) crc = (crc & 1) != 0 ? crc >> 1 ^ 0xEDB88320u : crc >> 1;
        }
        return ~crc;
    }

    /// <summary>alphaTab's zip writer stores bogus CRC-32s; every entry of a saved .gp must carry the real one.</summary>
    private static void TestGpZipCrcs(SongProject source)
    {
        foreach (var embed in new[] { false, true })
        {
            var label = embed ? "with embedded project" : "clean";
            var bytes = GuitarProExporter.ToBytes(source, embedProject: embed);
            var parts = GuitarProExporter.ReadZip(bytes);
            var bad = parts.Where(p => p.StoredCrc != Crc32Of(p.Data)).Select(p => $"{p.Name} stored {p.StoredCrc:x8} actual {Crc32Of(p.Data):x8}").ToList();
            Check($"exported .gp ({label}): every zip entry has a correct CRC-32 ({parts.Count} entries)", parts.Count > 0 && bad.Count == 0, string.Join("; ", bad));
            Check($"exported .gp ({label}): VERSION entry present, embedded project entry {(embed ? "present" : "absent")}",
                parts.Any(p => p.Name == "VERSION") && parts.Any(p => p.Name == GuitarProExporter.EmbeddedProjectEntry) == embed);
            using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(bytes), System.IO.Compression.ZipArchiveMode.Read);
            Check($"exported .gp ({label}): .NET zip reader lists the same entries in order",
                zip.Entries.Select(e => e.FullName).SequenceEqual(parts.Select(p => p.Name)));
            var path = Path.Combine(Path.GetTempPath(), "tabforge-gpcrc-" + Guid.NewGuid().ToString("N") + ".gp");
            try
            {
                File.WriteAllBytes(path, bytes);
                var again = embed ? GuitarProExporter.TryReadEmbedded(path) ?? throw new InvalidDataException("embedded project missing") : GuitarProImporter.Import(path);
                Check($"exported .gp ({label}): re-imports with the same notes",
                    NoteSignature(source).SequenceEqual(NoteSignature(again)));
            }
            finally { try { File.Delete(path); } catch (IOException) { } }
        }
    }

    private static List<(int Track, int Bar, int Midi)> NoteSignature(SongProject project) =>
        project.Tracks.SelectMany((track, t) => track.Measures.SelectMany((measure, bar) =>
                measure.Cells.SelectMany(cell => cell.Notes.Select(note => (t, bar, note.MidiValue)))))
            .OrderBy(x => x.t).ThenBy(x => x.bar).ThenBy(x => x.MidiValue).ToList();

    /// <summary>
    /// Builds a small song in code, exports it as a clean .gp (no embedded TabForge project, so the
    /// real Guitar Pro reader is exercised), re-imports it and compares structure and pitches.
    /// </summary>
    private static void TestSyntheticGuitarProFixture()
    {
        var folder = Path.Combine(Path.GetTempPath(), "tabforge-gpfixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var source = BuildSyntheticGpSong();
            var path = Path.Combine(folder, "synthetic.gp");
            GuitarProExporter.Save(source, path, embedProject: false);
            Check("synthetic .gp fixture is written", File.Exists(path) && new FileInfo(path).Length > 0);
            Check("synthetic .gp fixture has no embedded TabForge project", GuitarProExporter.TryReadEmbedded(path) is null);

            var imported = GuitarProImporter.Import(path);
            Eq("synthetic .gp: track count survives", source.Tracks.Count, imported.Tracks.Count);
            Eq("synthetic .gp: tempo survives", 96, imported.Tempo);
            Check("synthetic .gp: bar count survives",
                imported.Tracks.All(t => t.Measures.Count == 4), string.Join("/", imported.Tracks.Select(t => t.Measures.Count)));
            var expected = NoteSignature(source);
            var actual = NoteSignature(imported);
            Check("synthetic .gp: every note (track, bar, pitch) survives export and import",
                expected.SequenceEqual(actual), $"expected {expected.Count} notes, got {actual.Count}");
            Check("synthetic .gp: bass keeps its four-string tuning",
                imported.Tracks.Count > 1 && imported.Tracks[1].StringTunings.Count == 4);

            var restored = ProjectService.Restore(ProjectService.Snapshot(imported));
            var timeline = Playback.MidiTimelineBuilder.Build(restored, new Playback.PlaybackOptions());
            Check("synthetic .gp: imported song compiles a playback timeline", timeline.TotalMs > 0 && timeline.Events.Count > 0);
            _gpFixtureRan = true;
            TestGpZipCrcs(source);
            TestShowcaseGuitarProFixture();
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
}
