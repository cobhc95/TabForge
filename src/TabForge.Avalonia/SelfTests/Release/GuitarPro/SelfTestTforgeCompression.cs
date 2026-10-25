using System;
using System.IO;
using System.Linq;
using TabForge.Services;

namespace TabForge;

// Owns: gzip .tforge save/load plus controlled-error check.

/// <summary>The gzip .tforge: compressed on save, older plain JSON still loads, invalid UTF-8 and a truncated stream are controlled errors.</summary>
public static partial class SelfTest
{
    private static void TestTforgeCompression()
    {
        var folder = Path.Combine(Path.GetTempPath(), "tabforge-gz-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var project = BuildSyntheticGpSong();
            var path = Path.Combine(folder, "song.tforge");
            var hash = ProjectService.Save(path, project);
            var bytes = File.ReadAllBytes(path);
            Check("a saved .tforge is gzip", bytes.Length > 2 && bytes[0] == 0x1F && bytes[1] == 0x8B);
            var plain = ProjectService.GunzipBounded(bytes);
            Check("the gzip .tforge is much smaller than its JSON", bytes.Length * 4 < plain.Length, $"{bytes.Length} vs {plain.Length}");
            Check("a gzip .tforge loads back identically", ProjectService.ContentHash(ProjectService.Load(path)).SequenceEqual(hash));

            var legacy = Path.Combine(folder, "legacy.tforge");
            File.WriteAllBytes(legacy, plain);
            Check("an old uncompressed .tforge still loads", ProjectService.ContentHash(ProjectService.Load(legacy)).SequenceEqual(hash));

            // M-07: invalid UTF-8 is a malformed file, not an unexpected exception type.
            var damaged = (byte[])plain.Clone();
            damaged[2] = 0xFF;
            var damagedPath = Path.Combine(folder, "damaged.tforge");
            File.WriteAllBytes(damagedPath, damaged);
            var kind = "no exception";
            try { ProjectService.Load(damagedPath); }
            catch (InvalidDataException) { kind = "InvalidDataException"; }
            catch (Exception ex) { kind = ex.GetType().Name; }
            Eq("invalid UTF-8 in a .tforge is an InvalidDataException", "InvalidDataException", kind);

            var truncated = Path.Combine(folder, "truncated.tforge");
            File.WriteAllBytes(truncated, bytes.Take(bytes.Length / 2).ToArray());
            kind = "no exception";
            try { ProjectService.Load(truncated); }
            catch (InvalidDataException) { kind = "InvalidDataException"; }
            catch (Exception ex) { kind = ex.GetType().Name; }
            Eq("a truncated gzip .tforge is an InvalidDataException", "InvalidDataException", kind);
        }
        finally { TryDelete(folder);
 }
    }
}
