using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Plugins;
using TabForge.Presets;
using TabForge.Services;
using Xunit;
using Xunit.Abstractions;

namespace TabForge.Avalonia.Tests;

// Owns: the GM-track articulation-map guard, the "Keep techniques for TabForge" marker round trip (constructed and real song), the
//   controller/pitch-bend import lane and the keyswitch-note import rule, plus hostile-marker tolerance.
// Does not own: the export encoding or the importer internals it exercises.
public sealed class MidiImportTechniqueMarkerTests : IDisposable
{
    private const int Div = 480;
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"tf-midi-marker-{Guid.NewGuid():N}");
    private readonly ITestOutputHelper _out;

    public MidiImportTechniqueMarkerTests(ITestOutputHelper output)
    {
        _out = output;
        Directory.CreateDirectory(_folder);
        AlphaTabWarmUp.EnsureReady();
    }

    public void Dispose() { try { Directory.Delete(_folder, true); } catch (IOException) { } }

    private static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);

    private static byte[] Chunk(IEnumerable<(int Tick, byte[] Bytes)> events)
    {
        var body = new List<byte>();
        var last = 0;
        foreach (var (tick, bytes) in events.OrderBy(e => e.Tick))
        {
            Vlq(body, tick - last); last = tick; body.AddRange(bytes);
        }
        body.AddRange(new byte[] { 0, 0xFF, 0x2F, 0 });
        return Ascii("MTrk").Concat(new byte[] { 0, 0, (byte)(body.Count >> 8), (byte)body.Count }).Concat(body).ToArray();
    }

    private static void Vlq(List<byte> o, int v)
    {
        var parts = new Stack<byte>(); parts.Push((byte)(v & 0x7F));
        for (v >>= 7; v > 0; v >>= 7) parts.Push((byte)(v & 0x7F | 0x80));
        o.AddRange(parts);
    }

    private static byte[] File1(params byte[][] chunks) =>
        Ascii("MThd").Concat(new byte[] { 0, 0, 0, 6, 0, 1, 0, (byte)chunks.Length, Div >> 8, Div & 0xFF }).Concat(chunks.SelectMany(c => c)).ToArray();

    private static (int, byte[]) Tempo(int tick, int bpm) { var mpq = 60_000_000 / bpm; return (tick, new byte[] { 0xFF, 0x51, 3, (byte)(mpq >> 16), (byte)(mpq >> 8), (byte)mpq }); }
    private static (int, byte[]) On(int tick, int pitch, int ch = 0, int vel = 90) => (tick, new byte[] { (byte)(0x90 | ch), (byte)pitch, (byte)vel });
    private static (int, byte[]) Off(int tick, int pitch, int ch = 0) => (tick, new byte[] { (byte)(0x80 | ch), (byte)pitch, 0 });
    private static (int, byte[]) Cc(int tick, int controller, int value, int ch = 0) => (tick, new byte[] { (byte)(0xB0 | ch), (byte)controller, (byte)value });
    private static (int, byte[]) Bend(int tick, int value, int ch = 0) => (tick, new byte[] { (byte)(0xE0 | ch), (byte)(value & 0x7F), (byte)((value >> 7) & 0x7F) });
    private static (int, byte[]) Marker(int tick, string text) => (tick, new byte[] { 0xFF, 0x06, (byte)text.Length }.Concat(Ascii(text)).ToArray());

    private SongProject Song(bool plugin)
    {
        var song = new SongProject { Title = "M", Tempo = 120, TimeSignatureNumerator = 4, TimeSignatureDenominator = 4 };
        var track = new TrackModel
        {
            Name = "Lead", Kind = TrackKind.Guitar, MidiChannel = 0,
            StringTunings = new List<int> { 64, 59, 55, 50, 45, 40 }, Measures = TemplateFactory.Measures(2),
            MidiSound = !plugin, SoundSource = plugin ? SoundSources.Plugins : SoundSources.Midi,
        };
        if (plugin) track.Rig.Plugins.Add(new PluginSlot { Name = "Guitar", Type = PluginSlotType.Instrument });
        song.Tracks.Add(track);
        return song;
    }

    private static TabNote Put(TrackModel track, int bar, int cell, int fret, int denom, params string[] techniques)
    {
        var beat = track.Measures[bar].Cells[cell];
        beat.DurationDenominator = denom;
        var note = new TabNote { StringIndex = 0, Fret = fret, MidiValue = track.PitchOf(0, fret), Velocity = 90 };
        foreach (var technique in techniques) note.Techniques.Add(technique);
        beat.Notes.Add(note);
        return note;
    }

    private string Export(SongProject song, ArticulationExportOptions? options, string name)
    {
        var path = Path.Combine(_folder, name);
        var (_, slice) = MidiExportRange.Resolve(song, new Rendering.MidiExportSettings(), null, null);
        MidiExportService.Export(song, path, slice, options);
        return path;
    }

    [Fact]
    public void GmTrackKeepsItsDefaultAndUsesAMapTheExportPicks()
    {
        var song = Song(false);
        var note = Put(song.Tracks[0], 0, 0, 5, 4, "Bend", "PalmMute");
        note.BendPoints = new List<BendPointModel> { new() { Offset = 0, Value = 0 }, new() { Offset = 60, Value = 4 } };
        var map = new ArticulationMap();
        map.Rules[TechniqueNames.Bend] = new ArticulationRule(ArticulationAction.Keyswitch, 27, 0, ArticulationMode.Latched);
        map.Rules[TechniqueNames.PalmMute] = new ArticulationRule(ArticulationAction.Keyswitch, 26, 0, ArticulationMode.Latched);
        map.Rules[ArticulationTechniques.Normal] = new ArticulationRule(ArticulationAction.Keyswitch, 24, 0, ArticulationMode.Latched);

        // Default None: the built-in track exports exactly as before (no map applied).
        var plainPath = Path.Combine(_folder, "gm_plain.mid");
        MidiExportService.Export(song, plainPath);
        var mappedDefault = File.ReadAllBytes(Export(song, new ArticulationExportOptions(), "gm_default.mid"));
        Assert.Equal(File.ReadAllBytes(plainPath), mappedDefault);

        // An explicit map for that same built-in track is applied like a plug-in track's map.
        var options = new ArticulationExportOptions();
        options.Overrides[0] = map;
        var path = Export(song, options, "gm_map.mid");
        var midi = MidiArticulationVerifyTests.ParseMidi(File.ReadAllBytes(path));
        _out.WriteLine($"GM_MAP mapped={options.Mapped} kept={options.Kept} notes={midi.Notes.Count} bends={midi.Bends.Count}");
        Assert.True(options.Mapped > 0);
        Assert.Contains(midi.Notes, n => n.Pitch == 26 || n.Pitch == 27);
        Assert.DoesNotContain(midi.Bends, b => Math.Abs(b.Value - 8192) > 200);   // the map replaces the bend curve
    }

    [Fact]
    public void PluginTrackStillWritesItsMap()
    {
        var song = Song(true);
        Put(song.Tracks[0], 0, 0, 5, 4, "PalmMute");
        var map = new ArticulationMap();
        map.Rules[TechniqueNames.PalmMute] = new ArticulationRule(ArticulationAction.Keyswitch, 26, 0, ArticulationMode.Latched);
        var options = new ArticulationExportOptions();
        options.Overrides[0] = map;
        _ = Export(song, options, "plugin_map.mid");
        Assert.True(options.Mapped > 0);
    }

    [Fact]
    public void MarkerRoundTripRestoresTechniquesAndNumbers()
    {
        var song = Song(true);
        var bend = Put(song.Tracks[0], 0, 0, 7, 4, "Bend", "Vibrato");
        bend.BendPoints = new List<BendPointModel> { new() { Offset = 0, Value = 0 }, new() { Offset = 30, Value = 2 }, new() { Offset = 60, Value = 4 } };
        bend.VibratoDepth = 0.6; bend.VibratoSpeed = 6.5;
        var slide = Put(song.Tracks[0], 0, 4, 9, 4, "LegatoSlide", "ShiftSlide");
        slide.SlideTargetMidi = 71;
        var harmonic = Put(song.Tracks[0], 0, 8, 12, 4, "Harmonic");
        harmonic.HarmonicFret = 12;
        var trill = Put(song.Tracks[0], 0, 12, 5, 4, "Trill");
        trill.TrillTargetMidi = 67; trill.TrillDurationDenominator = 32;
        var ghost = Put(song.Tracks[0], 1, 0, 3, 4, "PalmMute", "Ghost");
        ghost.Ghost = true;

        var withMarkers = Export(song, new ArticulationExportOptions { KeepTechniques = true, MapsEnabled = false }, "markers.mid");
        var smf = SmfReader.Read(File.ReadAllBytes(withMarkers));
        _out.WriteLine("MARKER_PARSED=" + smf.Tracks.Sum(t => t.Markers.Count) + " notesWithMarker=" + smf.Tracks.Sum(t => t.Notes.Count(n => n.Marker is not null)));
        var legacy = Path.Combine(_folder, "legacy.mid");
        MidiExportService.Export(song, legacy);
        Assert.Equal(File.ReadAllBytes(legacy), File.ReadAllBytes(Export(song, null, "no_markers.mid")));
        _out.WriteLine($"MARKER_COST plain={new FileInfo(legacy).Length} withMarkers={new FileInfo(withMarkers).Length}");

        var back = GuitarProImporter.Import(withMarkers);
        var notes = back.Tracks.SelectMany(t => t.Measures).SelectMany(m => m.Cells.Concat(m.Voice2Cells ?? new List<TabCell>())).SelectMany(c => c.Notes).ToList();
        Assert.Contains(notes, n => n.Techniques.Contains("Bend") && n.BendPoints.Count == 3);
        Assert.Contains(notes, n => n.Techniques.Contains("Vibrato") && Math.Abs((n.VibratoDepth ?? 0) - 0.6) < 0.01);
        Assert.Contains(notes, n => n.Techniques.Contains("LegatoSlide") && n.SlideTargetMidi == 71);
        Assert.Contains(notes, n => n.Techniques.Contains("Harmonic") && Math.Abs((n.HarmonicFret ?? 0) - 12) < 0.01);
        Assert.Contains(notes, n => n.Techniques.Contains("Trill") && n.TrillTargetMidi == 67 && n.TrillDurationDenominator == 32);
        Assert.Contains(notes, n => n.Techniques.Contains("PalmMute") && n.Ghost);
    }

    [Fact]
    public void RealSongTechniqueCountsSurviveTheMarkerRoundTrip()
    {
        var sourcePath = MidiArticulationVerifyTests.PickSong();
        var source = GuitarProImporter.Import(sourcePath);
        var before = MidiArticulationVerifyTests.TechniqueCounts(source);
        var path = Export(source, new ArticulationExportOptions { KeepTechniques = true, MapsEnabled = false }, "real_markers.mid");
        var plainPath = Path.Combine(_folder, "real_plain.mid");
        MidiExportService.Export(source, plainPath);
        var back = GuitarProImporter.Import(path);
        var after = MidiArticulationVerifyTests.TechniqueCounts(back);
        var markerCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var marker in SmfReader.Read(File.ReadAllBytes(path)).Tracks.SelectMany(t => t.Markers))
            foreach (var id in marker.Payload.Split('|')[0].Split(',', StringSplitOptions.RemoveEmptyEntries))
                markerCounts[id] = markerCounts.GetValueOrDefault(id) + 1;
        var lost = before.Where(kv => !after.ContainsKey(kv.Key) || after[kv.Key] != kv.Value)
            .Select(kv => $"{kv.Key} {kv.Value}->{after.GetValueOrDefault(kv.Key)}").ToList();
        // These techniques sit on beats the export never attacks (merged ties/slides or silent notes), so there is no note-on to mark.
        var unmarkable = lost.Where(l => l.StartsWith("TremBar", StringComparison.Ordinal)).ToList();
        var restoreable = lost.Except(unmarkable).ToList();
        var markerLost = before.Where(kv => !kv.Key.StartsWith("TremBar", StringComparison.Ordinal) && markerCounts.GetValueOrDefault(kv.Key) != kv.Value)
            .Select(kv => $"{kv.Key} {kv.Value}->{markerCounts.GetValueOrDefault(kv.Key)}").ToList();
        _out.WriteLine($"REAL_MARKERS srcBytes={new FileInfo(plainPath).Length} markedBytes={new FileInfo(path).Length} markerLost=[" + string.Join(",", markerLost) + "] importLost=[" + string.Join(",", restoreable) + "] unmarkable=[" + string.Join(",", unmarkable) + "]");
        Assert.DoesNotContain(markerLost, l => l.Contains("->0") && !l.StartsWith("TremBar", StringComparison.Ordinal));
        // Markers ride on note-ons: a technique on a tie/slide continuation beat has no attack to mark, so a small share is out of reach.
        Assert.True(markerCounts.Values.Sum() >= before.Values.Sum() * 0.9, "marker coverage too low: " + string.Join(",", markerLost));
        Assert.DoesNotContain(lost, l => l.Contains("->0") && !l.StartsWith("TremBar", StringComparison.Ordinal));
        Assert.True(after.Values.Sum() >= before.Values.Sum() * 0.9, "too many technique occurrences lost: " + string.Join(",", restoreable));
    }

    [Fact]
    public void GarbageMarkersNeverCrashTheImport()
    {
        var random = new Random(7);
        const string alphabet = "TF0123456789:;=|,.-abcXYZ \t\u00ff";
        var events = new List<(int, byte[])> { Tempo(0, 120) };
        for (var i = 0; i < 40; i++)
        {
            var text = "TF" + random.Next(0, 9) + ":" + new string(Enumerable.Range(0, random.Next(0, 40)).Select(_ => alphabet[random.Next(alphabet.Length)]).ToArray());
            events.Add(Marker(i * 20, text));
            events.Add(On(i * 20, 40 + i % 12));
            events.Add(Off(i * 20 + 60, 40 + i % 12));
        }
        var path = Path.Combine(_folder, "fuzz.mid");
        System.IO.File.WriteAllBytes(path, File1(Chunk(events)));
        var song = GuitarProImporter.Import(path);
        Assert.NotEmpty(song.Tracks);
    }

    [Fact]
    public void ControllersAndPitchBendComeBackOnAMidiTracksClip()
    {
        var events = new List<(int, byte[])>
        {
            Tempo(0, 120), Cc(0, 7, 100), Cc(0, 10, 64), Cc(0, 11, 110), Cc(0, 64, 127), Cc(0, 1, 30), Bend(0, 8192),
            On(0, 60), Off(480, 60), Bend(240, 9000), Cc(240, 11, 80),
        };
        var path = Path.Combine(_folder, "cc.mid");
        System.IO.File.WriteAllBytes(path, File1(Chunk(events)));
        var song = GuitarProImporter.Import(path, new ImportContext { MidiMode = MidiImportMode.Midi });
        var track = song.Tracks[0];
        var clip = track.AudioClips.SingleOrDefault(c => c.Controllers is { Count: > 0 });
        Assert.NotNull(clip);
        Assert.Contains(clip!.Controllers!, p => p.Controller == 7 && p.Value == 100);
        Assert.Contains(clip.Controllers!, p => p.Controller == 64 && p.Value == 127);
        Assert.Contains(clip.Controllers!, p => p.Controller == ClipController.PitchBend && p.Value == 808);
        var outPath = Path.Combine(_folder, "cc_back.mid");
        MidiExportService.Export(song, outPath);
        var data = MidiArticulationVerifyTests.ParseMidi(System.IO.File.ReadAllBytes(outPath));
        Assert.Contains(data.Ccs, c => c.Controller == 7 && c.Value == 100);
        Assert.Contains(data.Ccs, c => c.Controller == 11 && c.Value == 80);
        Assert.Contains(data.Bends, b => b.Value == 9000);
    }

    [Fact]
    public void KeyswitchNotesAreKeptOutOfTheScoreAndReported()
    {
        var events = new List<(int, byte[])>
        {
            Tempo(0, 120),
            On(0, 20, 0, 100), Off(1, 20), On(12, 60), Off(480, 60),
            On(480, 21, 0, 100), Off(481, 21), On(492, 62), Off(960, 62),
            On(960, 40), Off(960 + 480, 40),   // a real low note: long, so kept
        };
        var path = Path.Combine(_folder, "ks.mid");
        System.IO.File.WriteAllBytes(path, File1(Chunk(events)));
        var context = new ImportContext();
        var song = MidiToSong.Convert(SmfReader.Read(System.IO.File.ReadAllBytes(path), context), path, context);
        var notes = song.Tracks.SelectMany(t => t.Measures).SelectMany(m => m.Cells.Concat(m.Voice2Cells ?? new List<TabCell>())).SelectMany(c => c.Notes).ToList();
        _out.WriteLine($"KEYSWITCH moved={context.KeyswitchNotesMoved} notice={context.KeyswitchNotice} notes={string.Join(",", notes.Select(n => n.MidiValue))}");
        Assert.Equal(2, context.KeyswitchNotesMoved);
        Assert.DoesNotContain(notes, n => n.MidiValue is 20 or 21);
        Assert.Contains(notes, n => n.MidiValue == 40);
        Assert.NotNull(context.KeyswitchNotice);
    }
}
