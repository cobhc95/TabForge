using System;
using System.IO;
using System.Linq;
using System.Text;
using TabForge.Playback;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Opt-in probe (TABFORGE_MIX_AUDIT=folder): dumps every imported mix point and the compiled program/volume/pan changes per sample.
public class MixTableAuditProbe
{
    public MixTableAuditProbe() { AlphaTabWarmUp.EnsureReady(); }

    [Fact]
    public void Opening_mix_table_sets_the_tracks_starting_sound()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "TabForge", "samples", "iron_maiden", "no_more_lies.gp3"));
        if (!File.Exists(path)) return;   // the sample is local only (a copyrighted song, not in the repository)
        var song = GuitarProImporter.Import(path, new ImportContext());
        Assert.Equal(26, song.Tracks[0].MidiProgram);   // header says 30 (distortion); the bar-1 mix table says 26 (clean)
        Assert.Equal(27, song.Tracks[2].MidiProgram);
        Assert.Equal(26, song.Tracks[0].Measures[0].Cells[0].Mix?.Program);   // bar 1 keeps its Mix Table (timeline marker)
        Assert.Null(song.Tracks[0].Measures[0].Cells[0].Mix?.Volume);       // its volume repeats the track's own: not a change, not ticked
        Assert.Null(song.Tracks[3].Measures[0].Cells.FirstOrDefault(c => c.Mix is not null)?.Mix);   // Keyboards' bar 1 table repeats Strings 48: nothing to tick
        Assert.Equal(26, song.Tracks[0].Measures[24].Cells[0].Mix?.Program);
        Assert.Equal(30, song.Tracks[0].Measures[49].Cells[0].Mix?.Program);
    }

    [Fact]
    public void Dump_mix_points()
    {
        var dir = Environment.GetEnvironmentVariable("TABFORGE_MIX_AUDIT");
        if (string.IsNullOrWhiteSpace(dir)) return;
        var sb = new StringBuilder();
        var one = Environment.GetEnvironmentVariable("TABFORGE_MIX_RAW");
        if (!string.IsNullOrEmpty(one))
        {
            var score = AlphaTab.Importer.ScoreLoader.LoadScoreFromBytes(File.ReadAllBytes(one), null);
            foreach (var tr in score.Tracks)
            {
                sb.AppendLine($"T{tr.Index} {tr.Name} prog={tr.PlaybackInfo.Program} vol={tr.PlaybackInfo.Volume}");
                foreach (var st in tr.Staves) foreach (var bar in st.Bars) foreach (var v in bar.Voices) foreach (var be in v.Beats)
                    if (be.Automations.Count > 0)
                        sb.AppendLine($"  bar{bar.Index + 1} v{v.Index} beat{be.Index}: " + string.Join(", ", be.Automations.Select(a => a.Type + "=" + a.Value)));
            }
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "mixraw.txt"), sb.ToString());
            return;
        }
        foreach (var path in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                     .Where(f => new[] { ".gp3", ".gp4", ".gp5", ".gpx", ".gp" }.Contains(Path.GetExtension(f).ToLowerInvariant())).OrderBy(f => f))
        {
            sb.AppendLine("=== " + Path.GetRelativePath(dir, path));
            try
            {
                var song = GuitarProImporter.Import(path, new ImportContext());
                for (var t = 0; t < song.Tracks.Count; t++)
                {
                    var tr = song.Tracks[t];
                    sb.AppendLine($"  T{t} '{tr.Name}' prog={tr.MidiProgram} vol={tr.Volume} pan={tr.Pan}");
                    for (var b = 0; b < tr.Measures.Count; b++)
                        foreach (var c in tr.Measures[b].Cells.Concat(tr.Measures[b].Voice2Cells))
                            if (c.Mix is { IsEmpty: false } m)
                                sb.AppendLine($"    bar{b + 1}: prog={m.Program} vol={m.Volume} pan={m.Pan} ch={m.Chorus} rv={m.Reverb} ph={m.Phaser} tr={m.Tremolo} tempo={m.Tempo} trans={m.TransitionBeats} all={m.AllTracks}");
                }
                var tl = MidiTimelineBuilder.Build(song, new PlaybackOptions { RespectMuteSolo = false, RepeatExpansion = true, StartBar = 0, Speed = 1, CountIn = false, Metronome = false, SkipClips = true });
                foreach (var e in tl.Events.Where(e => (e.Status & 0xF0) == 0xC0))
                    sb.AppendLine($"  PC t={e.TimeMs:0} trk={e.TrackIndex} ch={e.Channel} prog={e.Data1} setup={e.IsSetup}");
            }
            catch (Exception ex) { sb.AppendLine("  FAIL " + ex.Message); }
        }
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "mixaudit.txt"), sb.ToString());
    }
}
