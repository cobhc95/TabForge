using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Plugins;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the Export MIDI Techniques section: the default export is unchanged, a map writes its keyswitch before the note and replaces the
//   technique's own wheel/cut, Keep and Skip for unmapped techniques, a per-export preset never edits the track, and the report counts.
// Does not own: the compile (ScoreToMidiCompiler), the map files (ArticulationPresets) or the dialog layout.
// Tests: this class. Headless; files live under Path.GetTempPath() and are deleted in Dispose.
public sealed class MidiExportArticulationTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"tf-midi-artic-{Guid.NewGuid():N}");

    public MidiExportArticulationTests() => Directory.CreateDirectory(_folder);
    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private static SongProject BuildSong()
    {
        var song = new SongProject { Title = "Map", Tempo = 120, TimeSignatureNumerator = 4, TimeSignatureDenominator = 4 };
        song.Tracks.Add(new TrackModel
        {
            Name = "Lead", Kind = TrackKind.Guitar, MidiChannel = 0,
            StringTunings = new List<int> { 64, 59, 55, 50, 45, 40 }, Measures = TemplateFactory.Measures(1),
        });
        var track = song.Tracks[0];
        track.SoundSource = SoundSources.Plugins;
        track.MidiSound = false;
        track.Rig.Plugins.Add(new PluginSlot { Name = "Guitar", Type = PluginSlotType.Instrument });
        return song;
    }

    private static TabNote PutNote(TrackModel track, int cell, int fret, int denom, params string[] techniques)
    {
        var beat = track.Measures[0].Cells[cell];
        beat.DurationDenominator = denom;
        var note = new TabNote { StringIndex = 0, Fret = fret, MidiValue = track.PitchOf(0, fret), Velocity = 90 };
        foreach (var technique in techniques) note.Techniques.Add(technique);
        beat.Notes.Add(note);
        return note;
    }

    private static ArticulationMap Map(params (string Id, ArticulationRule Rule)[] rules)
    {
        var map = new ArticulationMap();
        foreach (var (id, rule) in rules) map.Rules[id] = rule;
        return map;
    }

    private static ArticulationRule Key(int note) => new(ArticulationAction.Keyswitch, note, 0, ArticulationMode.Latched);

    private sealed record Ev(int Tick, int Status, int D1, int D2);
    private sealed record Note(int Pitch, int Start, int End);

    private static int ReadBe32(byte[] b, int at) => (b[at] << 24) | (b[at + 1] << 16) | (b[at + 2] << 8) | b[at + 3];
    private static int ReadVarLen(byte[] b, ref int p)
    {
        var value = 0;
        for (var i = 0; i < 4; i++)
        {
            var x = b[p++];
            value = (value << 7) | (x & 0x7F);
            if ((x & 0x80) == 0) return value;
        }
        return value;
    }

    private static List<Ev> TrackEvents(string path)
    {
        var b = File.ReadAllBytes(path);
        var count = (b[10] << 8) | b[11];
        var events = new List<Ev>();
        var p = 14;
        for (var chunk = 0; chunk < count; chunk++)
        {
            var length = ReadBe32(b, p + 4);
            var end = p + 8 + length;
            var q = p + 8;
            var tick = 0;
            var list = new List<Ev>();
            while (q < end)
            {
                tick += ReadVarLen(b, ref q);
                var status = b[q];
                if (status == 0xFF)
                {
                    q += 2;
                    var len = ReadVarLen(b, ref q);
                    q += len;
                    continue;
                }
                q++;
                if ((status & 0xF0) is 0xC0 or 0xD0) { q++; continue; }
                list.Add(new Ev(tick, status, b[q], b[q + 1]));
                q += 2;
            }
            if (chunk > 0) events.AddRange(list);
            p = end;
        }
        return events;
    }

    private static List<Note> Notes(List<Ev> events)
    {
        var notes = new List<Note>();
        var pending = new Dictionary<(int, int), Queue<int>>();
        foreach (var e in events.OrderBy(e => e.Tick))
        {
            var kind = e.Status & 0xF0;
            if (kind == 0x90 && e.D2 > 0)
            {
                if (!pending.TryGetValue((e.Status & 0x0F, e.D1), out var queue)) pending[(e.Status & 0x0F, e.D1)] = queue = new Queue<int>();
                queue.Enqueue(e.Tick);
            }
            else if ((kind == 0x80 || kind == 0x90) && pending.TryGetValue((e.Status & 0x0F, e.D1), out var queue) && queue.Count > 0)
                notes.Add(new Note(e.D1, queue.Dequeue(), e.Tick));
        }
        return notes;
    }

    private string Export(SongProject song, ArticulationExportOptions? options, string name)
    {
        var path = Path.Combine(_folder, name);
        var (_, slice) = MidiExportRange.Resolve(song, new Rendering.MidiExportSettings(), null, null);
        MidiExportService.Export(song, path, slice, options);
        return path;
    }

    [Fact]
    public void DefaultOptionIsIdenticalExceptSectionMarkers()
    {
        var song = BuildSong();
        var note = PutNote(song.Tracks[0], 0, 5, 4);
        note.Techniques.Add("Bend");
        var oldPath = Path.Combine(_folder, "old.mid");
        MidiExportService.Export(song, oldPath);
        var newPath = Export(song, null, "new.mid");
        Assert.Equal(File.ReadAllBytes(oldPath), File.ReadAllBytes(newPath));
        // With sections the default export only adds the section markers.
        song.Markers.Add(new MarkerModel { MeasureIndex = 0, Title = "Intro" });
        var offPath = Path.Combine(_folder, "off.mid");
        MidiExportService.Export(song, offPath, (ArticulationExportOptions?)null, writeSections: false);
        Assert.Equal(File.ReadAllBytes(oldPath), File.ReadAllBytes(offPath));
        var onPath = Export(song, null, "on.mid");
        var onBytes = File.ReadAllBytes(onPath);
        Assert.Contains(System.Text.Encoding.UTF8.GetBytes("Intro"), onBytes);
        Assert.DoesNotContain(System.Text.Encoding.UTF8.GetBytes("Intro"), File.ReadAllBytes(offPath));
        Assert.Equal(TrackEvents(offPath), TrackEvents(onPath));
    }

    [Fact]
    public void MapWritesKeyswitchBeforeTheNoteAndDropsTheBendsOwnWheel()
    {
        var song = BuildSong();
        var track = song.Tracks[0];
        var note = PutNote(track, 0, 5, 4, "Bend");
        note.BendPoints = new List<BendPointModel> { new() { Offset = 0, Value = 0 }, new() { Offset = 60, Value = 4 } };
        var map = Map(("Bend", Key(27)), (ArticulationTechniques.Normal, Key(24)));
        var options = new ArticulationExportOptions();
        options.Overrides[0] = map;
        var events = TrackEvents(Export(song, options, "bend.mid"));

        var keyswitch = events.FirstOrDefault(e => (e.Status & 0xF0) == 0x90 && e.D1 == 27);
        var attack = events.FirstOrDefault(e => (e.Status & 0xF0) == 0x90 && e.D1 == note.MidiValue);
        Assert.True(keyswitch is not null, "events: " + string.Join("; ", events.Select(e => $"{e.Tick}:{e.Status:X2}:{e.D1}:{e.D2}")));
        Assert.NotNull(attack);
        Assert.True(keyswitch!.Tick <= attack!.Tick, $"keyswitch {keyswitch.Tick} after note {attack.Tick}");
        Assert.DoesNotContain(events, e => (e.Status & 0xF0) == 0xE0 && Math.Abs(((e.D2 << 7) | e.D1) - 8192) > 200);   // the map replaces the bend curve
    }

    [Fact]
    public void MapCoversPalmMuteSoTheNoteIsNotCutShort()
    {
        var song = BuildSong();
        PutNote(song.Tracks[0], 0, 5, 2, "PalmMute");
        var options = new ArticulationExportOptions();
        options.Overrides[0] = Map((TechniqueNames.PalmMute, Key(26)));
        var mapped = Notes(TrackEvents(Export(song, options, "mapped.mid"))).Single(n => n.Pitch == song.Tracks[0].PitchOf(0, 5));

        var plain = BuildSong();
        PutNote(plain.Tracks[0], 0, 5, 2, "PalmMute");
        var unmapped = Notes(TrackEvents(Export(plain, null, "plain.mid"))).Single(n => n.Pitch == plain.Tracks[0].PitchOf(0, 5));

        Assert.Equal(2 * 480, mapped.End - mapped.Start);   // the half note keeps its written length
        Assert.True(unmapped.End - unmapped.Start < mapped.End - mapped.Start, "the uncounted palm-mute cut is still there");
    }

    [Fact]
    public void UnmappedTechniquesKeepTheirOwnSoundOrAreSkipped()
    {
        var keepSong = BuildSong();
        var keepNote = PutNote(keepSong.Tracks[0], 0, 5, 4, "Bend");
        keepNote.BendPoints = new List<BendPointModel> { new() { Offset = 0, Value = 0 }, new() { Offset = 60, Value = 4 } };
        var keepOptions = new ArticulationExportOptions();
        keepOptions.Overrides[0] = Map((ArticulationTechniques.Normal, Key(24)));
        var kept = TrackEvents(Export(keepSong, keepOptions, "keep.mid"));

        var skipSong = BuildSong();
        var skipNote = PutNote(skipSong.Tracks[0], 0, 5, 4, "Bend");
        skipNote.BendPoints = new List<BendPointModel> { new() { Offset = 0, Value = 0 }, new() { Offset = 60, Value = 4 } };
        var skipOptions = new ArticulationExportOptions { SkipUnmapped = true };
        skipOptions.Overrides[0] = Map((ArticulationTechniques.Normal, Key(24)));
        var skipped = TrackEvents(Export(skipSong, skipOptions, "skip.mid"));

        Assert.Contains(kept, e => (e.Status & 0xF0) == 0xE0 && Math.Abs(((e.D2 << 7) | e.D1) - 8192) > 200);
        Assert.DoesNotContain(skipped, e => (e.Status & 0xF0) == 0xE0 && Math.Abs(((e.D2 << 7) | e.D1) - 8192) > 200);
        Assert.Equal(1, keepOptions.Kept);
        Assert.Equal(1, skipOptions.Skipped);
    }

    [Fact]
    public void PerTrackPresetOverrideDoesNotChangeTheTracksMap()
    {
        var song = BuildSong();
        var track = song.Tracks[0];
        PutNote(track, 0, 5, 4, "PalmMute");
        var slot = track.Rig.Plugins[0];
        slot.ArticulationBindings = Map((TechniqueNames.PalmMute, Key(26))).ToBindings();
        var before = slot.ArticulationBindings.ToDictionary(p => p.Key, p => p.Value);

        var options = new ArticulationExportOptions();
        options.Overrides[0] = Map((TechniqueNames.PalmMute, Key(30)));
        var events = TrackEvents(Export(song, options, "override.mid"));

        Assert.Contains(events, e => (e.Status & 0xF0) == 0x90 && e.D1 == 30);
        Assert.DoesNotContain(events, e => (e.Status & 0xF0) == 0x90 && e.D1 == 26);
        Assert.Equal(before, slot.ArticulationBindings);
    }

    [Fact]
    public void ReportCountsMappedKeptAndSkipped()
    {
        var song = BuildSong();
        PutNote(song.Tracks[0], 0, 5, 4, "PalmMute");
        var options = new ArticulationExportOptions();
        options.Overrides[0] = Map((TechniqueNames.PalmMute, Key(26)));
        _ = Export(song, options, "counts.mid");
        Assert.Equal(1, options.Mapped);
        Assert.Equal(0, options.Kept);
        Assert.Equal(0, options.Skipped);
    }
}
