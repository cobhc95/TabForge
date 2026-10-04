using System.IO;
using TabForge.Services;

namespace TabForge;

// Owns: the emergency-copy naming check.
// Does not own: the naming (AutosaveService.EmergencyFileFor) or the crash handler (App).
// Tests: TestEmergencyRecoveryNames.
public static partial class SelfTest
{
    private static void TestEmergencyRecoveryNames()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"tf-emergency-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(folder);
            // Two dirty songs with the same file name (from different folders) written in the same second.
            var a = AutosaveService.EmergencyFileFor(folder, "song", "20260101-000000");
            var b = AutosaveService.EmergencyFileFor(folder, "song", "20260101-000000");
            App.WriteRecoveryCopy(RichSong(1, 4, 9), a);
            App.WriteRecoveryCopy(RichSong(1, 4, 10), b);
            Check("emergency copies: two songs with the same name get two distinct files, both written", a != b && File.Exists(a) && File.Exists(b));
            Check("emergency copies: the names stay readable (song name and timestamp)", Path.GetFileName(a).Contains("song-20260101-000000"));
            var orphans = AutosaveService.FindOrphans(folder);
            Check("emergency copies: both are listed for recovery at the next start and recognised as recovery copies",
                orphans.Contains(a) && orphans.Contains(b) && AutosaveService.IsRecoveryCopy(a, folder) && AutosaveService.IsRecoveryCopy(b, folder));
            AutosaveService.DeleteOwn(folder, Environment.ProcessId);
            Check("emergency copies: a normal exit of this process leaves them in place", File.Exists(a) && File.Exists(b));
            var first = ProjectService.Load(a, InputLimits.MaxRecoveryProjectBytes);
            var second = ProjectService.Load(b, InputLimits.MaxRecoveryProjectBytes);
            Check("emergency copies: both restore", first.Tracks.Count > 0 && second.Tracks.Count > 0);
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }
}
