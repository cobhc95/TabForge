using System.Diagnostics;
using System.IO;

namespace TabForge;

/// <summary>`--capture`: a tiny script photographs the main window off-screen (own process, scratch profile) and writes a non-empty 2x PNG of the requested size.</summary>
public static partial class SelfTest
{
    private static void TestCaptureMainWindowOffscreen()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) { Log.Add("  skip  capture: no executable path"); return; }
        var work = Path.Combine(Path.GetTempPath(), $"tf-capture-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(work);
            var script = Path.Combine(work, "script.json");
            File.WriteAllText(script, "[{\"size\":[1280,800]},{\"theme\":\"dark\"},{\"shot\":\"main\",\"target\":\"window\"},{\"menu\":\"File\"},{\"shot\":\"file-menu\",\"target\":\"menu\"}]");
            var outDir = Path.Combine(work, "out");
            var info = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var a in new[] { "--profile", Path.Combine(work, "profile"), "--software-render", "--capture", script, outDir }) info.ArgumentList.Add(a);
            using var process = Process.Start(info)!;
            try { process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch (InvalidOperationException) { }
            var finished = process.WaitForExit(150_000);
            if (!finished) { try { process.Kill(true); } catch (InvalidOperationException) { } }
            Check("capture: the off-screen run finishes and exits cleanly", finished && process.ExitCode == 0);
            var png = Path.Combine(outDir, "main.png");
            var ok = File.Exists(png) && new FileInfo(png).Length > 10_000;
            Check("capture: a non-empty main-window PNG is written", ok);
            if (!ok) return;
            var head = File.ReadAllBytes(png);
            int Be(int at) => (head[at] << 24) | (head[at + 1] << 16) | (head[at + 2] << 8) | head[at + 3];
            Check("capture: the PNG is 2x the requested 1280x800 logical size", Be(16) == 2560 && Be(20) == 1600);
            Check("capture: a menu is photographed without opening a popup", File.Exists(Path.Combine(outDir, "file-menu.png")));

            // Missing script / folder: refused in App.OnStartup before settings load or a window exists (exit 2, nothing written).
            var refusedProfile = Path.Combine(work, "refused");
            var bare = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var a in new[] { "--profile", refusedProfile, "--capture" }) bare.ArgumentList.Add(a);
            using var refused = Process.Start(bare)!;
            try { refused.PriorityClass = ProcessPriorityClass.BelowNormal; } catch (InvalidOperationException) { }
            var refusedDone = refused.WaitForExit(60_000);
            if (!refusedDone) { try { refused.Kill(true); } catch (InvalidOperationException) { } }
            Check("capture: an incomplete --capture is refused before any window or settings (exit 2, no files in the profile)",
                refusedDone && refused.ExitCode == 2 && (!Directory.Exists(refusedProfile) || !Directory.EnumerateFiles(refusedProfile, "*", SearchOption.AllDirectories).Any()));
        }
        finally { try { Directory.Delete(work, true); } catch (IOException) { } }
    }
}
