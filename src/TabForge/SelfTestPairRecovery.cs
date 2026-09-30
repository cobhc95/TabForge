using System.Diagnostics;
using System.IO;
using TabForge.Services;

namespace TabForge;

/// <summary>
/// Two-file (.gp + .tfaudio) save transaction: the recovery marker is untrusted input, and every interruption point (exception or a killed
/// process) leaves the old pair, the new pair, or a kept recovery marker that the next open resolves by content, never a silent mixed pair.
/// Only disposable folders under the temp directory are used.
/// </summary>
public static partial class SelfTest
{
    private static readonly string[] PairStages =
    {
        "staged:song.gp", "staged:song.tfaudio", "marker:song.gp", "marker-written:song.gp", "commit:song.gp",
        "committed:song.gp", "commit:song.tfaudio", "committed:song.tfaudio", "cleanup:song.gp",
    };

    /// <summary>The pair a save interrupted at <paramref name="stage"/> must end as once resolved: new only when both commits happened.</summary>
    private static string ExpectedAfter(string stage) => stage is "committed:song.tfaudio" or "cleanup:song.gp" ? "new" : "old";

    private static byte[] PairBytes(string tag) => System.Text.Encoding.UTF8.GetBytes(tag + Guid.NewGuid().ToString("N"));

    private static void WriteRawPair(string gp, byte[] gpBytes, string tfaudio, byte[] audioBytes) =>
        FilePathPolicy.WritePairAtomically(gp, s => s.Write(gpBytes), tfaudio, s => s.Write(audioBytes));

    private static bool SameBytes(string path, byte[] bytes) => File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes);

    private static string FileNames(string folder) => string.Join(", ", Directory.GetFiles(folder).Select(Path.GetFileName));

    private static string PairState(string gp, string tf, byte[] oldGp, byte[] oldTf, byte[] newGp, byte[] newTf) =>
        SameBytes(gp, oldGp) && SameBytes(tf, oldTf) ? "old" : SameBytes(gp, newGp) && SameBytes(tf, newTf) ? "new" : "mixed";

    /// <summary>A real interrupted save: the .gp is committed, the .tfaudio commit fails and so does the restore, so backup + marker are kept.</summary>
    private static void InterruptPairSave(string gp, string tf, byte[] newGp, byte[] newTf)
    {
        FilePathPolicy.FaultInjection = s => { if (s is "commit:song.tfaudio" or "restore:song.gp") throw new IOException("simulated failure"); };
        try { WriteRawPair(gp, newGp, tf, newTf); } catch (IOException) { }
        finally { FilePathPolicy.FaultInjection = null; }
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();

    // Item 1: a crafted, damaged or foreign marker never copies, overwrites or deletes anything; links are refused; locks and missing backups stay recoverable.
    private static void TestPairMarkerIsUntrusted()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tf-marker-{Guid.NewGuid():N}");
        var folder = Path.Combine(root, "songs");
        var outside = Path.Combine(root, "outside");
        Directory.CreateDirectory(folder);
        Directory.CreateDirectory(outside);
        try
        {
            var gp = Path.Combine(folder, "song.gp");
            var tf = Path.Combine(folder, "song.tfaudio");
            var (oldGp, oldTf, newGp, newTf) = (PairBytes("old gp "), PairBytes("old audio "), PairBytes("new gp "), PairBytes("new audio "));
            WriteRawPair(gp, oldGp, tf, oldTf);
            var marker = FilePathPolicy.PairMarkerPathFor(gp);
            var (bakId, tmpId) = (Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"));
            var victims = new Dictionary<string, byte[]>
            {
                [Path.Combine(outside, "victim.txt")] = PairBytes("outside "),
                [Path.Combine(folder, "victim.txt")] = PairBytes("beside "),
                [Path.Combine(folder, $".tabforge-{bakId}.bak")] = PairBytes("foreign backup "),
                [Path.Combine(folder, $".tabforge-{tmpId}.tmp")] = PairBytes("foreign temp "),
            };
            foreach (var (path, bytes) in victims) File.WriteAllBytes(path, bytes);
            bool Untouched() => victims.All(v => SameBytes(v.Key, v.Value)) && SameBytes(gp, oldGp) && SameBytes(tf, oldTf);

            void Crafted(string name, byte[] markerBytes)
            {
                File.WriteAllBytes(marker, markerBytes);
                string? message = null;
                var threw = false;
                try { message = FilePathPolicy.RecoverInterruptedPair(gp, tf); } catch (Exception) { threw = true; }
                Check($"marker {name}: nothing is copied, overwritten or deleted, the marker is kept and the problem reported",
                    !threw && message is not null && Untouched() && SameBytes(marker, markerBytes), message);
                File.Delete(marker);
            }
            string Id() => Guid.NewGuid().ToString("N");
            byte[] Text(string s) => System.Text.Encoding.UTF8.GetBytes(s);
            var hGp = Hash(oldGp);
            var hTf = Hash(oldTf);
            // Looks like "the .gp was committed, the .tfaudio not" so it asks for a restore, but its backup is a foreign file with other content.
            var mixedForeign = FilePathPolicy.CraftPairMarker("song.gp", "song.tfaudio", Id(), tmpId, bakId, Id(), Hash(newGp), hGp, hTf, Hash(newTf));

            Crafted("in the old path format pointing at unrelated files", Text(
                $"TabForge pair save v1\n{gp}\n{tf}\n{Path.Combine(outside, "victim.txt")}\n{Path.Combine(folder, $".tabforge-{bakId}.bak")}\n"));
            Crafted("over the size limit", Text(mixedForeign + new string('x', FilePathPolicy.MaxPairMarkerBytes)));
            Crafted("truncated", Text(mixedForeign[..(mixedForeign.Length / 2)]));
            Crafted("that is not text", new byte[] { 0xFF, 0xFE, 0x00, 0xC3, 0x28, 0x0A });
            Crafted("with an altered checksum", Text(mixedForeign.Replace("backup=" + bakId, "backup=" + tmpId, StringComparison.Ordinal)));
            Crafted("with a traversal name", Text(FilePathPolicy.CraftPairMarker(@"..\song.gp", "song.tfaudio", Id(), tmpId, bakId, Id(), Hash(newGp), hGp, hTf, Hash(newTf))));
            Crafted("with an absolute path", Text(FilePathPolicy.CraftPairMarker(Path.Combine(outside, "victim.txt"), "song.tfaudio", Id(), tmpId, bakId, Id(), Hash(newGp), hGp, hTf, Hash(newTf))));
            Crafted("with a device name", Text(FilePathPolicy.CraftPairMarker("song.gp", "CON.tfaudio", Id(), tmpId, bakId, Id(), Hash(newGp), hGp, hTf, Hash(newTf))));
            Crafted("naming another file as the second one", Text(FilePathPolicy.CraftPairMarker("song.gp", "victim.txt", Id(), tmpId, bakId, Id(), Hash(newGp), hGp, hTf, Hash(newTf))));
            Crafted("whose backup id names a foreign file (matching name, other content)", Text(mixedForeign));

            // A well-formed marker that finds the pair consistent still deletes only files whose content it recorded, not foreign ones with matching names.
            File.WriteAllText(marker, FilePathPolicy.CraftPairMarker("song.gp", "song.tfaudio", Id(), tmpId, bakId, Id(), hGp, hGp, hTf, hTf));
            var consistent = FilePathPolicy.RecoverInterruptedPair(gp, tf);
            Check("a marker finding the pair consistent removes itself but leaves foreign same-shaped files byte-identical",
                consistent is null && !File.Exists(marker) && Untouched(), consistent);

            Check("malformed song paths never throw from recovery",
                FilePathPolicy.RecoverInterruptedPair("bad\0path.gp", "x.tfaudio") is null && FilePathPolicy.RecoverInterruptedPair(@"\\.\CON", @"\\.\CON.tfaudio") is null
                && FilePathPolicy.RecoverInterruptedPair("", "") is null);
            foreach (var victim in victims.Keys.Where(v => v.StartsWith(folder, StringComparison.Ordinal))) File.Delete(victim);

            // A valid interrupted save still recovers, but not while the song is locked; repeated attempts keep everything and the last one succeeds.
            InterruptPairSave(gp, tf, newGp, newTf);
            string?[] lockedMessages;
            using (new FileStream(gp, FileMode.Open, FileAccess.Read, FileShare.None))
                lockedMessages = new[] { FilePathPolicy.RecoverInterruptedPair(gp, tf), FilePathPolicy.RecoverInterruptedPair(gp, tf) };
            Check("recovery while the song is locked changes nothing, keeps the marker and says so, every attempt",
                lockedMessages.All(m => m?.Contains("kept", StringComparison.Ordinal) == true) && File.Exists(marker)
                && PairState(gp, tf, oldGp, oldTf, newGp, newTf) == "mixed", string.Join(" | ", lockedMessages));
            File.SetAttributes(gp, FileAttributes.ReadOnly);
            var denied = FilePathPolicy.RecoverInterruptedPair(gp, tf);
            File.SetAttributes(gp, FileAttributes.Normal);
            Check("recovery refused by the file system (read-only song) keeps the marker and backup and says so",
                denied is not null && File.Exists(marker) && Directory.GetFiles(folder, "*.bak").Length == 1, denied);
            var restored = FilePathPolicy.RecoverInterruptedPair(gp, tf);
            Check("a valid interrupted save is restored once the file is free, and every leftover is removed",
                restored?.Contains("restored", StringComparison.Ordinal) == true && PairState(gp, tf, oldGp, oldTf, newGp, newTf) == "old"
                && Directory.GetFiles(folder).Length == 2, $"{restored}; {FileNames(folder)}");

            // A missing backup: nothing can be proven, so nothing is changed.
            InterruptPairSave(gp, tf, newGp, newTf);
            foreach (var bak in Directory.GetFiles(folder, "*.bak")) File.Delete(bak);
            var missing = FilePathPolicy.RecoverInterruptedPair(gp, tf);
            Check("a missing backup leaves the song as it is, keeps the marker and reports it (never a false 'restored')",
                missing?.Contains("backup", StringComparison.Ordinal) == true && File.Exists(marker) && SameBytes(gp, newGp) && SameBytes(tf, oldTf), missing);
            File.Delete(marker);
            foreach (var tmp in Directory.GetFiles(folder, "*.tmp")) File.Delete(tmp);
            File.WriteAllBytes(gp, oldGp);

            // Links: a backup that is a symbolic link (even to the right content) and a song folder reached through a junction are refused.
            InterruptPairSave(gp, tf, newGp, newTf);
            var realBackup = Directory.GetFiles(folder, "*.bak").Single();
            var linkTarget = Path.Combine(outside, "old-copy.gp");
            File.Copy(realBackup, linkTarget);
            File.Move(realBackup, realBackup + ".held");
            var symlinkMade = false;
            try { File.CreateSymbolicLink(realBackup, linkTarget); symlinkMade = true; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            if (symlinkMade)
            {
                var viaLink = FilePathPolicy.RecoverInterruptedPair(gp, tf);
                Check("a backup that is a symbolic link is refused: nothing is changed and it is reported",
                    viaLink?.Contains("link", StringComparison.Ordinal) == true && SameBytes(gp, newGp) && SameBytes(linkTarget, oldGp) && File.Exists(marker), viaLink);
                File.Delete(realBackup);
            }
            else Skip("a backup that is a symbolic link is refused", "creating symbolic links needs Developer Mode or elevation here");
            File.Move(realBackup + ".held", realBackup);

            var junction = Path.Combine(root, "junction");
            var mklink = Process.Start(new ProcessStartInfo("cmd.exe") { ArgumentList = { "/c", "mklink", "/J", junction, folder }, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true });
            var junctionMade = mklink is not null && mklink.WaitForExit(15000) && mklink.ExitCode == 0 && Directory.Exists(junction);
            if (!junctionMade) { try { mklink?.Kill(); } catch (InvalidOperationException) { } }
            if (junctionMade)
            {
                var gpViaJunction = Path.Combine(junction, "song.gp");
                var junctionMarker = FilePathPolicy.PairMarkerPathFor(gpViaJunction);
                File.Copy(marker, junctionMarker);
                var viaJunction = FilePathPolicy.RecoverInterruptedPair(gpViaJunction, Path.Combine(junction, "song.tfaudio"));
                Check("a song folder reached through a junction is refused: nothing is changed and it is reported",
                    viaJunction?.Contains("link", StringComparison.Ordinal) == true && SameBytes(gp, newGp) && File.Exists(realBackup) && File.Exists(junctionMarker), viaJunction);
                File.Delete(junctionMarker);
                Directory.Delete(junction);
            }
            else Skip("a song folder reached through a junction is refused", "mklink /J could not create a junction here");
            var direct = FilePathPolicy.RecoverInterruptedPair(gp, tf);
            Check("the same interrupted save opened directly (no link) is restored",
                direct?.Contains("restored", StringComparison.Ordinal) == true && PairState(gp, tf, oldGp, oldTf, newGp, newTf) == "old" && Directory.GetFiles(folder).Length == 2,
                $"{direct}; {FileNames(folder)}");
        }
        finally
        {
            FilePathPolicy.FaultInjection = null;
            try { foreach (var f in Directory.GetFiles(Path.Combine(root, "songs"))) File.SetAttributes(f, FileAttributes.Normal); } catch (IOException) { }
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    // Item 2: an exception at every stage (with and without a failed rollback), a vanished staged file, an external change, and stale-file sweeping.
    private static void TestPairSaveEveryStage()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tf-stages-{Guid.NewGuid():N}");
        try
        {
            var run = 0;
            foreach (var stage in PairStages)
            foreach (var restoreFails in new[] { false, true })
            {
                var folder = Path.Combine(root, $"{run++}");
                Directory.CreateDirectory(folder);
                var gp = Path.Combine(folder, "song.gp");
                var tf = Path.Combine(folder, "song.tfaudio");
                var (oldGp, oldTf, newGp, newTf) = (PairBytes("old gp "), PairBytes("old audio "), PairBytes("new gp "), PairBytes("new audio "));
                WriteRawPair(gp, oldGp, tf, oldTf);
                FilePathPolicy.FaultInjection = s => { if (s == stage || (restoreFails && s == "restore:song.gp")) throw new IOException("injected"); };
                var threw = false;
                try { WriteRawPair(gp, newGp, tf, newTf); } catch (IOException) { threw = true; }
                finally { FilePathPolicy.FaultInjection = null; }
                var label = $"exception at {stage}{(restoreFails ? " with a failed rollback" : "")}";
                var state = PairState(gp, tf, oldGp, oldTf, newGp, newTf);
                var markerLeft = File.Exists(FilePathPolicy.PairMarkerPathFor(gp));
                Check($"{label}: the save fails and leaves a consistent pair with nothing else, or a kept recovery marker",
                    threw && (markerLeft ? state is "mixed" or "old" or "new" : state is "old" or "new" && Directory.GetFiles(folder).Length == 2),
                    $"state {state}, files {FileNames(folder)}");
                FilePathPolicy.RecoverInterruptedPair(gp, tf);
                var after = PairState(gp, tf, oldGp, oldTf, newGp, newTf);
                Check($"{label}: the next open leaves exactly the {ExpectedAfter(stage)} pair and no leftovers",
                    after == ExpectedAfter(stage) && Directory.GetFiles(folder).Length == 2, $"state {after}, files {FileNames(folder)}");
            }

            // "The second commit completed" versus "its staged file vanished": decided by content, not by the temp file's absence.
            var dir = Path.Combine(root, "vanished");
            Directory.CreateDirectory(dir);
            var g = Path.Combine(dir, "song.gp");
            var t = Path.Combine(dir, "song.tfaudio");
            var (og, ot, ng, nt) = (PairBytes("old gp "), PairBytes("old audio "), PairBytes("new gp "), PairBytes("new audio "));
            WriteRawPair(g, og, t, ot);
            InterruptPairSave(g, t, ng, nt);
            foreach (var tmp in Directory.GetFiles(dir, "*.tmp")) File.Delete(tmp);
            var vanished = FilePathPolicy.RecoverInterruptedPair(g, t);
            Check("a staged .tfaudio that vanished (not committed) is not mistaken for a finished save: the old pair is restored",
                vanished?.Contains("restored", StringComparison.Ordinal) == true && PairState(g, t, og, ot, ng, nt) == "old" && Directory.GetFiles(dir).Length == 2, vanished);

            InterruptPairSave(g, t, ng, nt);
            var external = PairBytes("changed elsewhere ");
            File.WriteAllBytes(t, external);
            var unknown = FilePathPolicy.RecoverInterruptedPair(g, t);
            Check("a pair matching neither save is not 'restored': nothing is changed, the backup and marker are kept, and it is reported",
                unknown?.Contains("neither", StringComparison.Ordinal) == true && SameBytes(g, ng) && SameBytes(t, external)
                && Directory.GetFiles(dir, "*.bak").Length == 1 && File.Exists(FilePathPolicy.PairMarkerPathFor(g)), unknown);
            File.WriteAllBytes(t, ot);

            // Stale sweeping: a backup keeps the old file's date, so it can look days old at once; files a pending marker needs are never swept.
            var old = DateTime.UtcNow.AddDays(-3);
            foreach (var f in Directory.GetFiles(dir, ".tabforge-*")) File.SetLastWriteTimeUtc(f, old);
            var unrelated = Path.Combine(dir, $".tabforge-{Guid.NewGuid():N}.tmp");
            File.WriteAllBytes(unrelated, PairBytes("abandoned "));
            File.SetLastWriteTimeUtc(unrelated, old);
            var swept = FilePathPolicy.SweepStaleLeftovers(dir);
            Check("the stale sweep removes an abandoned leftover but keeps every file a pending save still needs",
                swept == 1 && !File.Exists(unrelated) && Directory.GetFiles(dir, "*.bak").Length == 1, $"swept {swept}, files {FileNames(dir)}");
            var marker = FilePathPolicy.PairMarkerPathFor(g);
            var valid = File.ReadAllBytes(marker);
            File.WriteAllBytes(marker, valid[..(valid.Length / 2)]);
            File.WriteAllBytes(unrelated, PairBytes("abandoned "));
            File.SetLastWriteTimeUtc(unrelated, old);
            Check("with an unreadable marker in the folder nothing is swept", FilePathPolicy.SweepStaleLeftovers(dir) == 0 && File.Exists(unrelated));
            File.Delete(unrelated);
            File.WriteAllBytes(marker, valid);
            var sweptThenRecovered = FilePathPolicy.RecoverInterruptedPair(g, t);
            Check("after the sweep the interrupted save still recovers",
                sweptThenRecovered?.Contains("restored", StringComparison.Ordinal) == true && PairState(g, t, og, ot, ng, nt) == "old" && Directory.GetFiles(dir).Length == 2, sweptThenRecovered);

            // A new save refuses to overwrite an unresolved marker (its backup would be lost).
            InterruptPairSave(g, t, ng, nt);
            File.WriteAllBytes(t, external);
            var refused = Throws(() => WriteRawPair(g, PairBytes("third "), t, PairBytes("third audio ")));
            Check("a new save over an unresolved interrupted one writes nothing and keeps its backup and marker",
                refused && SameBytes(g, ng) && SameBytes(t, external) && Directory.GetFiles(dir, "*.bak").Length == 1 && File.Exists(marker));
        }
        finally
        {
            FilePathPolicy.FaultInjection = null;
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    /// <summary>
    /// Child process for <see cref="TestPairSaveProcessKill"/>: <c>--pair-save-probe &lt;gp&gt; &lt;tfaudio&gt; &lt;stage&gt; &lt;signal&gt; &lt;new gp bytes&gt; &lt;new audio bytes&gt;</c>.
    /// Runs the pair save and, on reaching the stage, writes the signal file and waits to be killed.
    /// </summary>
    internal static int PairSaveProbe(string[] args)
    {
        if (args.Length < 7) return 2;
        var (gp, tf, stage, signal) = (args[1], args[2], args[3], args[4]);
        var (newGp, newTf) = (File.ReadAllBytes(args[5]), File.ReadAllBytes(args[6]));
        FilePathPolicy.FaultInjection = s =>
        {
            if (s != stage) return;
            File.WriteAllText(signal, s);
            Thread.Sleep(TimeSpan.FromSeconds(60));   // killed long before; the bound keeps an orphan from living forever
            Environment.Exit(3);
        };
        WriteRawPair(gp, newGp, tf, newTf);
        return 0;
    }

    // Item 2: a real process performing the save is killed at every stage (TerminateProcess: no finally blocks run).
    private static void TestPairSaveProcessKill()
    {
        var exe = Environment.ProcessPath;
        if (exe is null || !Path.GetFileNameWithoutExtension(exe).Equals("TabForge", StringComparison.OrdinalIgnoreCase))
        {
            Skip("pair save killed at every stage", "not running as TabForge.exe");
            return;
        }
        var root = Path.Combine(Path.GetTempPath(), $"tf-kill-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            for (var i = 0; i < PairStages.Length; i++)
            {
                var stage = PairStages[i];
                var folder = Path.Combine(root, $"song{i}");
                Directory.CreateDirectory(folder);
                var gp = Path.Combine(folder, "song.gp");
                var tf = Path.Combine(folder, "song.tfaudio");
                var (oldGp, oldTf, newGp, newTf) = (PairBytes("old gp "), PairBytes("old audio "), PairBytes("new gp "), PairBytes("new audio "));
                WriteRawPair(gp, oldGp, tf, oldTf);
                var (newGpFile, newTfFile, signal) = (Path.Combine(root, $"new{i}.gp"), Path.Combine(root, $"new{i}.tfaudio"), Path.Combine(root, $"signal{i}"));
                File.WriteAllBytes(newGpFile, newGp);
                File.WriteAllBytes(newTfFile, newTf);
                var start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true };
                foreach (var a in new[] { "--pair-save-probe", gp, tf, stage, signal, newGpFile, newTfFile }) start.ArgumentList.Add(a);
                using var child = Process.Start(start);
                var watch = Stopwatch.StartNew();
                while (child is not null && !File.Exists(signal) && !child.HasExited && watch.ElapsedMilliseconds < 30000) Thread.Sleep(20);
                var reached = File.Exists(signal);
                try { child?.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                var ended = child?.WaitForExit(10000) ?? false;
                var state = PairState(gp, tf, oldGp, oldTf, newGp, newTf);
                var marker = FilePathPolicy.PairMarkerPathFor(gp);
                var markerLeft = File.Exists(marker);
                Check($"process killed at {stage}: the pair is consistent, or a recovery marker is left",
                    reached && ended && (state is "old" or "new" || markerLeft), $"reached {reached}, state {state}, files {FileNames(folder)}");
                var message = FilePathPolicy.RecoverInterruptedPair(gp, tf);
                var after = PairState(gp, tf, oldGp, oldTf, newGp, newTf);
                // Killed before the marker existed: only this class's own staged files may remain (swept after a day); from the marker on, recovery removes everything.
                var extras = Directory.GetFiles(folder).Select(Path.GetFileName).Where(n => n is not ("song.gp" or "song.tfaudio")).ToList();
                var extrasOk = markerLeft || stage is "cleanup:song.gp" or "committed:song.tfaudio"
                    ? extras.Count == 0
                    : extras.All(n => n!.StartsWith(".tabforge-", StringComparison.Ordinal) && n.EndsWith(".tmp", StringComparison.Ordinal) && n.Length == ".tabforge-".Length + 36);
                Check($"process killed at {stage}: the next open leaves exactly the {ExpectedAfter(stage)} pair",
                    after == ExpectedAfter(stage) && !File.Exists(marker) && extrasOk, $"state {after}, message {message}, files {FileNames(folder)}");
            }
        }
        finally { try { Directory.Delete(root, true); } catch (IOException) { } }
    }
}
