using System.IO;
using System.Security.Cryptography;
using TabForge.Plugins;
using TabForge.Services;

namespace TabForge;

/// <summary>Audit 5 H-5: <c>--approve-night-plugins</c> approves exactly the night plug-ins, only in a profile, and is refused without <c>--profile</c>.</summary>
public static partial class SelfTest
{
    private static void TestNightPluginApproval()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tf-night-{Guid.NewGuid():N}");
        var previous = UserPaths.ProfileRoot;
        var realRoaming = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TabForge");
        var realBefore = FolderFingerprint(realRoaming);
        try
        {
            string Make(string relative, string content = "MZ-night")
            {
                var full = Path.Combine(root, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllText(full, content + relative);
                return PluginTrust.Normalize(full);
            }
            var nexus2 = Make(@"vst2\Nexus.dll");
            var sd3 = Make(@"vst2\Toontrack\Superior Drummer 3.dll");
            Make(@"vst2\Other Synth.dll");
            Make(@"vst2\Toontrack\EZdrummer.dll");
            var nexus3 = Make(@"vst3\Nexus.vst3");
            Make(@"vst3\Evil.vst3");
            var eq = Make(@"reaper\Plugins\FX\reaeq.dll");
            var comp = Make(@"reaper\Plugins\FX\reacomp.dll");
            Make(@"reaper\Plugins\reaper_wave.dll");
            Make(@"reaper\Plugins\FX\notes.txt");
            var crash = Make(@"repo\native\crashtest\bin\TabForgeCrashTest.dll");
            Make(@"repo\native\crashtest\bin\Other.dll");
            var roots = new NightRoots(new[] { Path.Combine(root, "vst2") }, new[] { Path.Combine(root, "vst3") }, new[] { Path.Combine(root, "reaper") },
                new[] { Path.Combine(root, "repo", "build", "TabForge") });
            var expected = new[] { nexus2, sd3, nexus3, eq, comp, crash }.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
            var flag = new[] { "--approve-night-plugins" };

            // Without --profile the flag is refused and approves nothing.
            UserPaths.SetProfile(null);
            var refused = new PluginSettings();
            var refusedResult = NightPluginApproval.Apply(flag, refused, out var refusedList, roots);
            Check("H-5: without --profile the flag is refused and nothing is approved",
                refusedResult == NightPluginApproval.Outcome.Refused && refusedList.Count == 0 && refused.ApprovedPluginPaths.Count == 0 && refused.TrustRecords.Count == 0);

            // With --profile but without the flag nothing happens.
            UserPaths.SetProfile(Path.Combine(root, "profile"));
            var quiet = new PluginSettings();
            Check("H-5: with --profile but without the flag nothing is approved",
                NightPluginApproval.Apply(Array.Empty<string>(), quiet, out _, roots) == NightPluginApproval.Outcome.NotRequested && quiet.ApprovedPluginPaths.Count == 0);

            // With both: exactly the night plug-ins, by path and hash, in the profile's settings object.
            var profileSettings = new PluginSettings();
            var result = NightPluginApproval.Apply(flag, profileSettings, out _, roots);
            var approved = profileSettings.ApprovedPluginPaths.Select(PluginTrust.Normalize).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
            Check("H-5: with --profile the flag approves Nexus, Superior Drummer 3, REAPER's bundled FX and the crash-test DLL, and nothing else",
                result == NightPluginApproval.Outcome.Approved && approved.SequenceEqual(expected, StringComparer.OrdinalIgnoreCase),
                $"approved {approved.Count}, expected {expected.Count}: {string.Join(" | ", approved.Select(Path.GetFileName))}");
            var hashOk = expected.All(p =>
                profileSettings.TrustRecords.FirstOrDefault(r => string.Equals(PluginTrust.Normalize(r.Path), p, StringComparison.OrdinalIgnoreCase)) is { } rec
                && rec.Sha256 == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))));
            Check("H-5: each approval records the file's SHA-256 (trust by path plus hash)", hashOk);
            var other = Path.Combine(root, "vst2", "Other Synth.dll");
            Check("H-5: a plug-in that is not on the list stays untrusted, the listed ones are trusted",
                !PluginTrust.IsTrusted(other, profileSettings) && expected.All(p => PluginTrust.IsTrusted(p, profileSettings)));

            // Argument helpers, and the real user folder is untouched.
            Check("H-5: the flag is found and stripped from the arguments",
                NightPluginApproval.Requested(new[] { "--profile", "x", "--approve-night-plugins" })
                && NightPluginApproval.Without(new[] { "a", "--approve-night-plugins", "b" }).SequenceEqual(new[] { "a", "b" }));
            Check("H-5: approving leaves the real %APPDATA%\\TabForge byte-identical", FolderFingerprint(realRoaming) == realBefore);

            // Read-only search on this machine finds only allowed things.
            var real = NightPluginApproval.Find(NightRoots.Default());
            bool Allowed(string p)
            {
                var name = Path.GetFileName(p);
                return name.Equals("Nexus.dll", StringComparison.OrdinalIgnoreCase) || name.Equals("Nexus.vst3", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("Superior Drummer 3.", StringComparison.OrdinalIgnoreCase) || name.Equals(NightPluginApproval.CrashTestDll, StringComparison.OrdinalIgnoreCase)
                    || (p.Contains(@"REAPER", StringComparison.OrdinalIgnoreCase) && p.Contains(@"\FX\", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
            }
            Check("H-5: the machine search returns only Nexus, Superior Drummer 3, REAPER FX and the crash-test DLL", real.All(Allowed), string.Join(" | ", real.Where(p => !Allowed(p))));
        }
        finally
        {
            UserPaths.SetProfile(previous);
            try { Directory.Delete(root, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
