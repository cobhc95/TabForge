using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the byte-for-byte characterization of what the compiler emits: for generated songs (the demo song, the full demo song, a song with Mix
//   Table points, ramps, mid-bar tempo ramps and let-ring runs) and the Guitar Pro sample fixtures under samples\Tests (skipped when absent), a
//   SHA-256 of every compiled event (time, device, channel, status, data, flags), every sounding note, the bars and the let-ring counters, at
//   several let-ring settings and start positions, plus the bytes of the MIDI file export. Golden values live in CompiledMidiGolden.txt
//   (regenerate only with TABFORGE_MIDI_GOLDEN_WRITE=1 after a deliberate change, with the reason in the commit message; TABFORGE_MIDI_GOLDEN_DUMP=<folder>
//   writes each hashed text there, so two runs can be diffed).
// Does not own: the compiler or the scheduler (ScoreToMidiCompiler, PlaybackEngine) or MIDI export options (MidiExportArticulationTests).
// Tests: this class.
public sealed class CompiledMidiGoldenTests
{
    /// <summary>The let-ring settings compiled for every song (the default 2000 and the setting's ends).</summary>
    private static readonly double[] GoldenCaps = { 600, 1000, 2000, 6000 };

    /// <summary>Let-ring settings the setting allows below 600 ms.</summary>
    private static readonly double[] LowCaps = { 400, 500, 599 };

    private static string GoldenPath => Path.Combine(ArchitectureScan.RepoRoot(), "Source", "tests", "TabForge.Avalonia.Tests", "Playback", "CompiledMidiGolden.txt");

    private static readonly Lazy<IReadOnlyList<(string Name, SongProject Song)>> Songs = new(LoadSongs, true);

    private static IReadOnlyList<(string Name, SongProject Song)> LoadSongs()
    {
        var songs = new List<(string, SongProject)>
        {
            ("demo", DemoSongFactory.Create()),
            ("full-demo", FullDemoSongFactory.Create()),
            ("mix-points", MixSong()),
        };
        var folder = Path.Combine(ArchitectureScan.RepoRoot(), "samples", "Tests");
        if (Directory.Exists(folder))
        {
            AlphaTabWarmUp.EnsureReady();
            foreach (var file in Directory.EnumerateFiles(folder).Where(f => f.EndsWith(".gp", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".gp5", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".gp4", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".gpx", StringComparison.OrdinalIgnoreCase)).OrderBy(f => f, StringComparer.Ordinal))
                songs.Add(("sample:" + Path.GetFileName(file), GuitarProImporter.Import(file, new ImportContext())));
        }
        // Channels are handed out in track-id order (ChannelAllocator), and a new track gets a random id: fixed ids make the compile repeatable.
        foreach (var (_, song) in songs)
            for (var i = 0; i < song.Tracks.Count; i++)
                song.Tracks[i].Id = new Guid(i + 1, 0, 0, new byte[8]);
        return songs;
    }

    /// <summary>The demo song with Mix Table points on beats (volume ramps, pan, program, reverb, chorus, an all-tracks tempo ramp), a mid-bar tempo
    /// ramp, and let-ring runs of short and long notes on the first track.</summary>
    internal static SongProject MixSong()
    {
        var song = DemoSongFactory.Create();
        var first = song.Tracks[0];
        first.Measures[1].Cells[0].Mix = new MixChange { Volume = 6, TransitionBeats = 4 };
        first.Measures[2].Cells[4].Mix = new MixChange { Pan = -6, Reverb = 12, Chorus = 3 };
        first.Measures[3].Cells[0].Mix = new MixChange { Volume = 14, Pan = 5, TransitionBeats = 2 };
        first.Measures[4].Cells[8].Mix = new MixChange { Program = 30 };
        first.Measures[5].Cells[0].Mix = new MixChange { Tempo = 90, TransitionBeats = 4, AllTracks = true };
        first.Measures[6].MidBarTempos = new List<TempoPoint> { new(8, 140, 4) };
        if (song.Tracks.Count > 1) song.Tracks[1].Measures[2].Cells[0].Mix = new MixChange { Volume = 3, Phaser = 4, Tremolo = 2, TransitionBeats = 3 };
        for (var bar = 0; bar < Math.Min(8, first.Measures.Count); bar++)
            foreach (var cell in first.Measures[bar].Cells)
                foreach (var note in cell.Notes)
                    note.Techniques.Add("LetRing");
        return song;
    }

    private static string Hash(ScoreTimeline t)
    {
        var s = new StringBuilder();
        static string D(double v) => v.ToString("R", CultureInfo.InvariantCulture);
        void Event(ScoreEvent e) => s.Append(D(e.TimeMs)).Append(',').Append(e.DeviceId).Append(',').Append(e.Channel).Append(',').Append(e.Status).Append(',')
            .Append(e.Data1).Append(',').Append(e.Data2).Append(',').Append(e.TrackIndex).Append(',').Append(e.IsSetup ? 1 : 0).Append(e.IsChased ? 1 : 0)
            .Append(e.IsMetronome ? 1 : 0).Append(e.IsCountInClick ? 1 : 0).Append(e.IsMetronomeAccent ? 1 : 0).Append(e.IsArticulation ? 1 : 0).Append(',')
            .Append(e.MetronomeTick).Append(',').Append(e.MetronomePairId).Append('\n');
        s.Append("events\n");
        foreach (var e in t.Events) Event(e);
        s.Append("setup\n");
        foreach (var e in t.ChannelSetup) Event(e);
        s.Append("notes\n");
        foreach (var n in t.Notes)
            s.Append(D(n.OnsetMs)).Append(',').Append(D(n.DurationMs)).Append(',').Append(n.TrackIndex).Append(',').Append(n.Bar).Append(',').Append(n.Cell).Append(',')
                .Append(n.VoiceIndex).Append(',').Append(n.StringIndex).Append(',').Append(n.Fret).Append(',').Append(n.Midi).Append(',').Append(n.Velocity).Append(',')
                .Append(n.Channel).Append(',').Append(n.LetRing ? 1 : 0).Append('\n');
        s.Append("bars\n");
        foreach (var b in t.Bars) s.Append(b.Bar).Append(',').Append(D(b.StartMs)).Append(',').Append(D(b.EndMs)).Append(',').Append(b.Slots).Append(',').Append(b.Tempo).Append('\n');
        s.Append(D(t.TotalMs)).Append(',').Append(D(t.CountInMs)).Append(',').Append(D(t.PlayFromMs)).Append(',').Append(t.LetRingExtensions).Append(',')
            .Append(D(t.MaxLetRingExtensionMs)).Append(',').Append(t.RetriggerCollisions).Append(',').Append(t.TieMerges).Append(',').Append(t.TieOrphans);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s.ToString())))[..24];
        if (Environment.GetEnvironmentVariable("TABFORGE_MIDI_GOLDEN_DUMP") is { Length: > 0 } dump) File.WriteAllText(Path.Combine(dump, hash + ".txt"), s.ToString());
        return hash;
    }

    private static string Compile(SongProject song, Action<PlaybackOptions> setup)
    {
        var options = new PlaybackOptions();
        setup(options);
        return Hash(MidiTimelineBuilder.Build(song, options));
    }

    private static string ExportBytes(SongProject song)
    {
        var path = Path.Combine(Path.GetTempPath(), "tf-midi-golden-" + Guid.NewGuid().ToString("N") + ".mid");
        try
        {
            MidiExportService.Export(song, path);
            return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))[..24];
        }
        finally { File.Delete(path); }
    }

    /// <summary>Every golden line: song | variant | hash.</summary>
    private static IEnumerable<(string Key, string Hash)> Lines()
    {
        foreach (var (name, song) in Songs.Value)
        {
            foreach (var cap in GoldenCaps)
                yield return ($"{name}|letring={cap.ToString(CultureInfo.InvariantCulture)}", Compile(song, o => o.LetRingCapMs = cap));
            yield return ($"{name}|metronome+countin", Compile(song, o => { o.Metronome = true; o.CountIn = true; o.CountInBars = 2; }));
            if (BarCount(song) > 5) yield return ($"{name}|start=bar5", Compile(song, o => o.StartBar = 5));
            yield return ($"{name}|export", ExportBytes(song));
        }
    }

    private static int BarCount(SongProject song) => song.Tracks.Count == 0 ? 0 : song.Tracks.Max(t => t.Measures.Count);

    [Fact]
    public void CompiledMidiMatchesTheGolden()
    {
        var actual = Lines().ToList();
        if (Environment.GetEnvironmentVariable("TABFORGE_MIDI_GOLDEN_WRITE") == "1")
        {
            File.WriteAllLines(GoldenPath, actual.Select(l => l.Key + "|" + l.Hash));
            return;
        }
        Assert.True(File.Exists(GoldenPath), "missing golden " + GoldenPath);
        var golden = File.ReadAllLines(GoldenPath).Where(l => l.Length > 0).Select(l => l.Split('|')).ToDictionary(p => p[0] + "|" + p[1], p => p[2]);
        var problems = new List<string>();
        foreach (var (key, hash) in actual)
        {
            if (!golden.TryGetValue(key, out var expected)) problems.Add(key + ": not in the golden");
            else if (expected != hash) problems.Add($"{key}: {hash}, golden {expected}");
        }
        // A sample fixture that is absent on this machine is not checked; a generated song always is.
        foreach (var key in golden.Keys.Where(k => !k.StartsWith("sample:", StringComparison.Ordinal) && actual.All(a => a.Key != k))) problems.Add(key + ": no longer produced");
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void LetRingSettingsBelow600TakeEffect()
    {
        // The whole setting range 400-6000 ms takes effect (until 0.9.6 SustainResolver floored it at 600, so 400-599 sounded like 600). Settings of
        // 600 and more are pinned by the golden above. A song with let-ring tails rings shorter under 600, never longer; one without is unchanged.
        foreach (var (name, song) in Songs.Value)
        {
            var at600 = MidiTimelineBuilder.Build(song, new PlaybackOptions { LetRingCapMs = 600 });
            foreach (var cap in LowCaps)
            {
                var low = MidiTimelineBuilder.Build(song, new PlaybackOptions { LetRingCapMs = cap });
                if (at600.LetRingExtensions == 0) Assert.True(Hash(at600) == Hash(low), $"{name}: no let-ring tail, yet let-ring {cap} differs from 600");
                else
                {
                    Assert.True(Hash(at600) != Hash(low), $"{name}: let-ring {cap} still sounds like 600");
                    Assert.True(low.Notes.Max(n => n.DurationMs) <= at600.Notes.Max(n => n.DurationMs), $"{name}: let-ring {cap} rings longer than 600");
                    Assert.True(low.MaxLetRingExtensionMs <= at600.MaxLetRingExtensionMs, $"{name}: let-ring {cap} extends more than 600");
                }
            }
        }
    }

    [Fact]
    public void ALoneLetRingNoteRingsForTheSettingBelow600()
    {
        // One short let-ring note alone on its string: it rings for exactly the setting when that is under 600 ms (600 before 0.9.6).
        var song = new SongProject { Tempo = 120 };
        var track = new TrackModel { Name = "Guitar", Measures = Enumerable.Range(0, 2).Select(bar => new MeasureModel { Number = bar + 1 }).ToList() };
        var note = new TabNote { StringIndex = 0, Fret = 3 };
        note.Techniques.Add("LetRing");
        track.Measures[0].Cells[0].Notes.Add(note);
        song.Tracks.Add(track);
        foreach (var (cap, ring) in new[] { (400.0, 400.0), (500.0, 500.0), (600.0, 600.0), (2000.0, 600.0) })
        {
            var compiled = MidiTimelineBuilder.Build(song, new PlaybackOptions { LetRingCapMs = cap }).Notes.Single();
            Assert.Equal(ring, compiled.DurationMs, 3);
        }
    }
}
