using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TabForge.Models;
using TabForge.Services;

namespace TabForge;

// Owns: the real save/load paths the round-trip checks go through (.tforge and .gp, the export-failure isolation and the song copy), split from SelfTestRoundTrip.cs.
// Does not own: the round-trip checks themselves (SelfTestRoundTrip).
public static partial class SelfTest
{
    // ------------------------------------------------------------------ format round trips (real code paths)

    private static SongProject RtViaTforge(SongProject s, string folder, string name)
    {
        var path = Path.Combine(folder, name + ".tforge");
        ProjectService.Save(path, s);
        return ProjectService.Load(path);
    }

    /// <summary>When the .gp export throws, isolate the track and bar that alphaTab chokes on (reported in the failure text).</summary>
    private static string RtIsolateExportFailure(SongProject s)
    {
        var found = new List<string>();
        for (var t = 0; t < s.Tracks.Count; t++)
        {
            var solo = RtCopy(s);
            solo.Tracks = new List<TrackModel> { solo.Tracks[t] };
            try { GuitarProExporter.ToBytes(solo, false); continue; } catch (Exception) { } // swallow: this solo variant is not exportable; the next variant is tried
            var bars = solo.Tracks[0].Measures.Count;
            for (var b = 0; b < bars; b++)
            {
                var one = RtCopy(s);
                var keep = one.Tracks[t];
                one.Tracks = new List<TrackModel> { keep };
                for (var k = 0; k < keep.Measures.Count; k++) if (k != b) { keep.Measures[k].Cells = Enumerable.Range(0, 16).Select(_ => new TabCell()).ToList(); keep.Measures[k].Voice2Cells = new(); }
                try { GuitarProExporter.ToBytes(one, false); } catch (Exception) { found.Add($"track {t} '{s.Tracks[t].Name}' bar {b}"); }
            }
            if (found.Count == 0) found.Add($"track {t} '{s.Tracks[t].Name}' (only with other bars)");
        }
        return string.Join(", ", found);
    }

    private static SongProject RtViaGp(SongProject s, string folder, string name, bool embed)
    {
        try { return RtViaGpCore(s, folder, name, embed); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { throw new InvalidOperationException($"{ex.GetType().Name}: {ex.Message}; isolated: {RtIsolateExportFailure(s)}", ex); }
    }

    private static SongProject RtViaGpCore(SongProject s, string folder, string name, bool embed)
    {
        var path = Path.Combine(folder, name + (embed ? "-embed" : "-clean") + ".gp");
        GuitarProExporter.Save(s, path, embedProject: embed);
        return GuitarProImporter.Import(path);
    }

    private static SongProject RtCopy(SongProject s) => ProjectService.Restore(ProjectService.Snapshot(s));
}
