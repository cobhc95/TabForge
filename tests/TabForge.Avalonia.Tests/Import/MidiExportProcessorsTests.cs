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

// Owns: the MIDI export processor checks (per-track processors are applied in the file, articulation
//   keyswitches stay out of it while their pitch effects survive).
// Does not own: the exporter, the compiler or the processor chain.
// Tests: this class. Headless, no sleeps; files live under Path.GetTempPath() and are deleted in Dispose.
public sealed class MidiExportProcessorsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"tf-export-proc-{Guid.NewGuid():N}");

    public MidiExportProcessorsTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private static SongProject BuildSong(int bars = 1)
    {
        var song = new SongProject
        {
            Title = "ExportProcessors",
            Tempo = 120,
            TimeSignatureNumerator = 4,
            TimeSignatureDenominator = 4,
        };
        song.Tracks.Add(new TrackModel
        {
            Name = "Lead",
            Kind = TrackKind.Guitar,
            MidiChannel = 0,
            StringTunings = new List<int> { 64, 59, 55, 50, 45, 40 },
            Measures = TemplateFactory.Measures(bars),
        });
        return song;
    }

    private static TabNote PutNote(TrackModel track, int bar, int cell, int stringIndex, int fret, int denom = 4)
    {
        var beat = track.Measures[bar].Cells[cell];
        beat.DurationDenominator = denom;
        var note = new TabNote
        {
            StringIndex = stringIndex,
            Fret = fret,
            MidiValue = track.PitchOf(stringIndex, fret),
        };
        beat.Notes.Add(note);
        return note;
    }

    private static PluginMidiProcessor Processor(string type, string key, double value)
    {
        var info = MidiProcessorCatalog.Find(type)!;
        var made = MidiProcessorCatalog.Create(info);
        var o = MidiProcessorCatalog.ParseParams(made, info);
        o[key] = value;
        made.Params = o.ToJsonString();
        return made;
    }

    private static void AddProcessors(TrackModel track, params PluginMidiProcessor[] processors) =>
        track.Rig.Plugins.Add(new PluginSlot { Name = "Chain", Type = PluginSlotType.Effect, MidiProcessors = processors.ToList() });

    private sealed record MidiNote(int Pitch, int StartTick, int EndTick, int Channel);
    private sealed record MidiFileData(int Division, List<MidiNote> Notes, int TrackChunks);

    private static int ReadBe16(byte[] b, int at) => (b[at] << 8) | b[at + 1];
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

    // Minimal SMF reader for the exporter's shape (MThd header, MTrk framing, full status bytes, no running status).
    private static MidiFileData ParseMidi(byte[] b)
    {
        Assert.True(b.Length > 14 && b[0] == (byte)'M' && b[1] == (byte)'T' && b[2] == (byte)'h' && b[3] == (byte)'d');
        var trackCount = ReadBe16(b, 10);
        var division = ReadBe16(b, 12);
        var notes = new List<MidiNote>();
        var pending = new Dictionary<(int, int), Queue<int>>();
        var p = 14;
        for (var chunk = 0; chunk < trackCount; chunk++)
        {
            var length = ReadBe32(b, p + 4);
            var end = p + 8 + length;
            var q = p + 8;
            var tick = 0;
            var status = -1;
            while (q < end)
            {
                tick += ReadVarLen(b, ref q);
                var s = b[q];
                if (s == 0xFF)
                {
                    q++;
                    q++;   // type
                    var len = ReadVarLen(b, ref q);
                    q += len;
                    status = -1;
                    continue;
                }
                if (s >= 0x80) { status = s; q++; }
                var kind = status & 0xF0;
                var channel = status & 0x0F;
                if (kind == 0xC0 || kind == 0xD0) { q++; status = -1; continue; }
                var first = s >= 0x80 ? b[q++] : s;
                var second = b[q++];
                var key = (channel, (int)first);
                if (kind == 0x90 && second > 0)
                {
                    if (!pending.TryGetValue(key, out var queue)) pending[key] = queue = new Queue<int>();
                    queue.Enqueue(tick);
                }
                else if (kind == 0x80 || kind == 0x90)
                {
                    if (pending.TryGetValue(key, out var queue) && queue.Count > 0)
                        notes.Add(new MidiNote(first, queue.Dequeue(), tick, channel));
                }
            }
            p = end;
        }
        return new MidiFileData(division, notes, trackCount);
    }

    [Fact]
    public void ExportAppliesTransposeToTheFile()
    {
        var song = BuildSong();
        var track = song.Tracks[0];
        PutNote(track, 0, 0, 0, 0);   // pitch 64
        AddProcessors(track, Processor("transpose", "Semitones", 12));

        var path = Path.Combine(_folder, "transpose.mid");
        MidiExportService.Export(song, path);
        var midi = ParseMidi(File.ReadAllBytes(path));

        Assert.Contains(midi.Notes, n => n.Pitch == 76);
        Assert.DoesNotContain(midi.Notes, n => n.Pitch == 64);
    }

    [Fact]
    public void ExportWithoutProcessorsLeavesNotesAlone()
    {
        var song = BuildSong();
        PutNote(song.Tracks[0], 0, 0, 0, 0);   // pitch 64

        var path = Path.Combine(_folder, "plain.mid");
        MidiExportService.Export(song, path);
        var midi = ParseMidi(File.ReadAllBytes(path));

        Assert.Contains(midi.Notes, n => n.Pitch == 64);
    }

    [Fact]
    public void ExportAppliesArpeggiatorToTheFile()
    {
        var song = BuildSong();
        PutNote(song.Tracks[0], 0, 0, 0, 0, denom: 1);   // a whole note: the arp has four beats to run on
        AddProcessors(song.Tracks[0], Processor("arp", "Rate", 2));

        var path = Path.Combine(_folder, "arp.mid");
        MidiExportService.Export(song, path);
        var midi = ParseMidi(File.ReadAllBytes(path));

        Assert.True(midi.Notes.Count(n => n.Pitch == 64) >= 4, $"expected an arpeggiated run, got {midi.Notes.Count} notes");
        Assert.All(midi.Notes, n => Assert.True(n.EndTick > n.StartTick));
    }

    [Fact]
    public void ExportStripsKeyswitchesButKeepsReplacePitch()
    {
        var song = BuildSong();
        var track = song.Tracks[0];
        track.SoundSource = SoundSources.Plugins;
        track.MidiSound = false;
        var map = new ArticulationMap();
        map.Rules["PalmMute"] = new ArticulationRule(ArticulationAction.Keyswitch, 26, 0, ArticulationMode.PerNote);
        map.Rules["Dead"] = new ArticulationRule(ArticulationAction.Replace, 40, 0, ArticulationMode.PerNote);
        track.Rig.Plugins.Add(new PluginSlot
        {
            Name = "Guitar",
            Type = PluginSlotType.Instrument,
            ArticulationBindings = map.ToBindings(),
        });
        PutNote(track, 0, 0, 0, 0);   // pitch 64
        PutNote(track, 0, 4, 0, 0).Techniques.Add("PalmMute");   // keyswitch 26, stripped below
        var dead = PutNote(track, 0, 8, 0, 0);
        dead.Techniques.Add("Dead");
        dead.Dead = true;   // replaced pitch 40 survives

        var path = Path.Combine(_folder, "articulation.mid");
        MidiExportService.Export(song, path);
        var midi = ParseMidi(File.ReadAllBytes(path));

        Assert.Contains(midi.Notes, n => n.Pitch == 64);
        Assert.Contains(midi.Notes, n => n.Pitch == 40);
        Assert.DoesNotContain(midi.Notes, n => n.Pitch == 26);
    }
}
