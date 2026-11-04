using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TabForge.AudioEngine.Isolation;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the Linux first-run state machine of WineRuntime without a real Wine: lazy prefix creation, the success marker,
// the failure marker, and the crash-dialog registry command. Wine itself is exercised by the wine-stage6 workflow.
// Does not own: the loader environment (Vst32WineSeamTests) or the helper protocol.
public sealed class WineRuntimeLinuxTests
{
    [Fact]
    public void FirstRunCreatesThePrefixTurnsOffTheCrashDialogAndWritesTheMarker()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var folder = new Vst32TestAssets.TempFolder();
        var log = Path.Combine(folder.Path, "wine-calls.txt");
        var runtime = Runtime(folder, Script(folder, log, winebootExit: 0));
        var marker = Path.Combine(runtime.PrefixPath, ".tabforge-wine");
        var failed = Path.Combine(runtime.PrefixPath, ".tabforge-failed");

        Assert.Null(runtime.Version);
        Assert.False(Directory.Exists(runtime.PrefixPath), "constructing WineRuntime must not create the prefix");

        runtime.EnsureReady(5000, 5000);

        Assert.Equal("wine-9.21-fake", runtime.Version);
        Assert.True(File.Exists(marker), "a successful first run must write the marker");
        var lines = File.ReadAllLines(marker);
        Assert.Contains("wineVersion=wine-9.21-fake", lines);
        Assert.Contains("arch=win64", lines);
        Assert.Contains("helperProtocol=1", lines);
        Assert.Contains("crashDialog=off", lines);
        Assert.False(File.Exists(failed), "a successful first run must leave no failure marker");
        var calls = File.ReadAllText(log);
        Assert.Contains("wineboot -u", calls);
        Assert.Contains("ShowCrashDialog", calls);
        Assert.Contains("REG_DWORD", calls);
        Assert.Contains("reg add HKCU\\Software\\Wine\\WineDbg", calls);
    }

    [Fact]
    public void AFailedWinebootWritesTheFailureMarkerAndDoesNotWriteTheSuccessMarker()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var folder = new Vst32TestAssets.TempFolder();
        var runtime = Runtime(folder, Script(folder, Path.Combine(folder.Path, "wine-calls.txt"), winebootExit: 7));
        var marker = Path.Combine(runtime.PrefixPath, ".tabforge-wine");
        var failed = Path.Combine(runtime.PrefixPath, ".tabforge-failed");

        var error = Assert.Throws<InvalidOperationException>(() => runtime.EnsureReady(5000, 5000));

        Assert.Contains("could not be prepared", error.Message);
        Assert.True(File.Exists(failed), "a failed first run must write the failure marker");
        Assert.Contains("wineboot -u failed (exit 7)", File.ReadAllText(failed));
        Assert.False(File.Exists(marker), "a failed first run must not write the success marker");
    }

    [Fact]
    public void ACorruptedOrOversizedMarkerIsRepaired()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var folder = new Vst32TestAssets.TempFolder();
        var log = Path.Combine(folder.Path, "wine-calls.txt");
        var runtime = Runtime(folder, Script(folder, log, winebootExit: 0));
        runtime.EnsureReady(5000, 5000);
        var marker = Path.Combine(runtime.PrefixPath, ".tabforge-wine");
        Assert.True(File.Exists(marker));

        File.WriteAllText(marker, "wineVersion=wine-9.21-fake\nnot a marker\n");
        runtime.EnsureReady(5000, 5000);
        Assert.Contains("crashDialog=off", File.ReadAllLines(marker));

        File.WriteAllText(marker, new string('x', 100 * 1024));
        runtime.EnsureReady(5000, 5000);
        Assert.Contains("wineVersion=wine-9.21-fake", File.ReadAllLines(marker));
        Assert.Equal(3, File.ReadAllText(log).Split('\n').Count(line => line.Contains("wineboot -u", StringComparison.Ordinal)));
    }

    [Fact]
    public void ABrokenOrReadOnlyPrefixFailsReadablyAndLeavesNoLock()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var folder = new Vst32TestAssets.TempFolder();
        var runtime = Runtime(folder, Script(folder, Path.Combine(folder.Path, "wine-calls.txt"), winebootExit: 0));
        Directory.CreateDirectory(runtime.PrefixPath);
        File.WriteAllText(Path.Combine(runtime.PrefixPath, ".tabforge-wine"), "wineVersion=wine-9.21-fake\ncrashDialog=off\n");
        File.WriteAllText(Path.Combine(runtime.PrefixPath, "drive_c"), "not a directory");
        var broken = Assert.Throws<InvalidOperationException>(() => runtime.EnsureReady(5000, 5000));
        Assert.Contains("could not be prepared", broken.Message);
        Assert.False(Path.Exists(Path.Combine(runtime.PrefixPath, ".tabforge-setup.lock")), "a failed setup must not leave the lock");

        File.Delete(Path.Combine(runtime.PrefixPath, "drive_c"));
        runtime.EnsureReady(5000, 5000);   // repair the broken prefix through wineboot before the read-only case
        var mode = File.GetUnixFileMode(runtime.PrefixPath);
        try
        {
            File.SetUnixFileMode(runtime.PrefixPath, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            var readOnly = Assert.Throws<InvalidOperationException>(() => runtime.EnsureReady(5000, 5000));
            Assert.Contains("not writable", readOnly.Message);
            Assert.Contains("read-only", readOnly.Message);
            Assert.False(Path.Exists(Path.Combine(runtime.PrefixPath, ".tabforge-setup.lock")), "a read-only prefix must not leave a lock");
        }
        finally { File.SetUnixFileMode(runtime.PrefixPath, mode); }
    }

    [Fact]
    public void APrefixFolderThatCannotBeCreatedIsReadable()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var folder = new Vst32TestAssets.TempFolder();
        var runtime = Runtime(folder, Script(folder, Path.Combine(folder.Path, "wine-calls.txt"), winebootExit: 0));
        File.WriteAllText(runtime.PrefixPath, "a file where the prefix folder belongs");
        var error = Assert.Throws<InvalidOperationException>(() => runtime.EnsureReady(5000, 5000));
        Assert.Contains("prefix folder", error.Message);
        Assert.Contains(runtime.PrefixPath, error.Message);
    }

    [Fact]
    public async Task TwoCopiesShareOnePrefixWithOneWineboot()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var folder = new Vst32TestAssets.TempFolder();
        var log = Path.Combine(folder.Path, "wine-calls.txt");
        var script = Script(folder, log, winebootExit: 0, bootSleepMs: 400);
        var marker = Path.Combine(folder.Path, "prefix", ".tabforge-wine");
        var prefix = Path.Combine(folder.Path, "prefix");
        var probe = Environment.GetEnvironmentVariable("TABFORGE_STAGE15_PROBE");
        if (!string.IsNullOrWhiteSpace(probe) && File.Exists(probe))
        {
            // Two real processes running the production WineRuntime code against one prefix.
            using var first = StartProbe(probe, script, prefix);
            using var second = StartProbe(probe, script, prefix);
            Assert.True(first.WaitForExit(30000), "the first probe did not finish prefix setup");
            Assert.True(second.WaitForExit(30000), "the second probe did not finish prefix setup");
            Assert.Equal(0, first.ExitCode);
            Assert.Equal(0, second.ExitCode);
        }
        else
        {
            var first = Task.Run(() => Runtime(folder, script).EnsureReady(5000, 10000));
            var second = Task.Run(() => Runtime(folder, script).EnsureReady(5000, 10000));
            await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(20));
            Assert.True(first.IsCompletedSuccessfully && second.IsCompletedSuccessfully,
                $"first={first.Exception?.GetBaseException().Message} second={second.Exception?.GetBaseException().Message}");
        }
        Assert.True(File.Exists(marker));
        Assert.Equal(1, File.ReadAllText(log).Split('\n').Count(line => line.StartsWith("boot-start", StringComparison.Ordinal)));
        Assert.False(Path.Exists(Path.Combine(prefix, ".tabforge-setup.lock")));
    }

    private static System.Diagnostics.Process StartProbe(string probe, string wine, string prefix)
    {
        var start = new System.Diagnostics.ProcessStartInfo
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        if (probe.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            start.FileName = "dotnet";
            start.ArgumentList.Add(probe);
        }
        else start.FileName = probe;
        start.ArgumentList.Add("prepare");
        start.ArgumentList.Add(wine);
        start.ArgumentList.Add(prefix);
        return System.Diagnostics.Process.Start(start) ?? throw new InvalidOperationException("the prefix probe did not start");
    }

    private static WineRuntime Runtime(Vst32TestAssets.TempFolder folder, string script)
    {
        var helper = Path.Combine(folder.Path, "tfvst32.exe");
        var helper64 = Path.Combine(folder.Path, "tfvst64.exe");
        File.WriteAllBytes(helper, []);
        File.WriteAllBytes(helper64, []);
        return new WineRuntime(script, helper, Path.Combine(folder.Path, "prefix"), helperPath64: helper64);
    }

    private static string Script(Vst32TestAssets.TempFolder folder, string log, int winebootExit, int bootSleepMs = 0)
    {
        var wine = Path.Combine(folder.Path, "bundle", "wine", "bin", "wine");
        Directory.CreateDirectory(Path.GetDirectoryName(wine)!);
        var sleep = bootSleepMs > 0 ? "  sleep " + (bootSleepMs / 1000.0).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "\n" : "";
        var text = string.Join("\n", new[]
        {
            "#!/bin/sh",
            "printf '%s\\n' \"$*\" >> '" + log + "'",
            "if [ \"$1\" = \"--version\" ]; then echo wine-9.21-fake; exit 0; fi",
            "if [ \"$1\" = \"wineboot\" ]; then",
            "  echo boot-start >> '" + log + "'",
            sleep.TrimEnd('\n'),
            "  if [ " + winebootExit + " -eq 0 ]; then mkdir -p \"$WINEPREFIX/drive_c\" || exit 7; echo boot-end >> '" + log + "'; exit 0; fi",
            "  echo 'mock wineboot failure' >&2; exit " + winebootExit,
            "fi",
            "if [ \"$1\" = \"reg\" ]; then exit 0; fi",
            "exit 0",
            "",
        });
        File.WriteAllText(wine, text);
        if (OperatingSystem.IsLinux())
            File.SetUnixFileMode(wine, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return wine;
    }
}
