using System.Diagnostics;
using System.IO;
using System.Text;
using TabForge.Audio.Contracts;
using TabForge.Plugins;

namespace TabForge.AudioEngine.Isolation;

// Owns: locating the bundled Wine loader and the native helper, the private prefix, the child environment from the
// Linux Wine plan (2.6), first-run prefix setup and Linux -> Z:\ path conversion.
// Does not own: the helper protocol (RemotePlugin) or the launch record (Vst32Bridge).
// Tests: Vst32WineSeamTests (paths/environment only; no Wine on this machine).

/// <summary>The bundled Wine runtime and its private prefix for the Windows VST2 bridge.</summary>
public sealed class WineRuntime
{
    public const string TokenVariable = "TABFORGE_HOST_TOKEN";
    private const string MarkerName = ".tabforge-wine", LockName = ".tabforge-setup.lock", FailedName = ".tabforge-failed";

    public string WineExecutable { get; }
    /// <summary>The x86 helper (32-bit VST2 under Wine WoW64).</summary>
    public string HelperPath { get; }
    /// <summary>The x64 helper (64-bit Windows VST2 under Wine).</summary>
    public string HelperPath64 { get; }
    public string PrefixPath { get; }
    /// <summary>The folder holding <c>bin</c> and <c>lib</c> (the bundle root, for WINELOADER/WINESERVER/WINEDLLPATH).</summary>
    public string BundleRoot { get; }
    public string? Version { get; private set; }

    public WineRuntime(string wineExecutable, string helperPath, string prefixPath, string? version = null, string? helperPath64 = null)
    {
        WineExecutable = Path.GetFullPath(wineExecutable);
        HelperPath = Path.GetFullPath(helperPath);
        HelperPath64 = Path.GetFullPath(helperPath64 ?? Path.Combine(Path.GetDirectoryName(HelperPath)!, Vst32Bridge.HelperName64));
        PrefixPath = Path.GetFullPath(prefixPath);
        BundleRoot = Path.GetDirectoryName(Path.GetDirectoryName(WineExecutable)!) ?? Path.GetDirectoryName(WineExecutable)!;
        Version = version;
    }

    /// <summary>The runtime on this Linux build, or null when Wine or the helper is not shipped. Never starts a process.</summary>
    public static WineRuntime? Locate(string? appFolder = null, string? helperFolder = null)
    {
        if (!OperatingSystem.IsLinux()) return null;
        var wine = WineBridgeSupport.WineExecutable(appFolder);
        if (wine is null) return null;
        var folder = string.IsNullOrWhiteSpace(helperFolder) ? AppContext.BaseDirectory : helperFolder;
        return new WineRuntime(wine, Path.Combine(folder, Vst32Bridge.HelperName), WineBridgeSupport.PrefixPath,
            helperPath64: Path.Combine(folder, Vst32Bridge.HelperName64));
    }

    /// <summary>The private shared-memory file for one session; the Wine-plan folder, never a network or system temp name.</summary>
    public static string BlockPath(string id, string? runtimeFolder = null) =>
        Path.Combine(runtimeFolder ?? SharedMemoryPaths.Folder(TabForge.Platform.AppFolders.Current.Runtime), $"tf-wine-{id}.shm");

    /// <summary>Deletes stale <c>tf-wine-*.shm</c> files left by crashed helpers (Stage 9). Active sessions hold their file open, so only files older than <paramref name="maxAge"/> are removed; returns how many were deleted.</summary>
    public static int SweepStaleBlocks(TimeSpan? maxAge = null, string? runtimeFolder = null)
    {
        var age = maxAge ?? TimeSpan.FromHours(2);
        string folder;
        try { folder = runtimeFolder ?? SharedMemoryPaths.Folder(TabForge.Platform.AppFolders.Current.Runtime); }
        catch (Exception) { return 0; }
        string[] files;
        try
        {
            if (!Directory.Exists(folder)) return 0;
            files = Directory.GetFiles(folder, "tf-wine-*.shm");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
        var deleted = 0;
        var cutoff = DateTime.UtcNow - age;
        foreach (var file in files)
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) > cutoff) continue;
                // A live session holds the block open; a delete probe with no sharing refuses instead of unlinking under it.
                // (On Unix an open mapping could still be unlinked, so the age check remains the primary guard.)
                using (new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                File.Delete(file);
                deleted++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return deleted;
    }

    /// <summary>The helper's crash dump on Linux: the Wine prefix's Windows diagnostics folder mapped back to a Linux path (null when absent).</summary>
    public static string? LinuxCrashDump(string prefixPath, string helperName, int pid)
    {
        try
        {
            var candidates = new[]
            {
                Path.Combine(prefixPath, "drive_c", "users", Environment.UserName, "Local Settings", "Application Data", "TabForge", "Diagnostics", $"{helperName}-{pid}.dmp"),
                Path.Combine(prefixPath, "drive_c", "users", Environment.UserName, "AppData", "Local", "TabForge", "Diagnostics", $"{helperName}-{pid}.dmp"),
            };
            foreach (var c in candidates)
                if (File.Exists(c)) return c;
            var users = Path.Combine(prefixPath, "drive_c", "users");
            if (Directory.Exists(users))
                foreach (var dir in Directory.GetDirectories(users))
                    foreach (var name in new[] { $"{helperName}-{pid}.dmp" })
                    {
                        var a = Path.Combine(dir, "Local Settings", "Application Data", "TabForge", "Diagnostics", name);
                        if (File.Exists(a)) return a;
                        var b = Path.Combine(dir, "AppData", "Local", "TabForge", "Diagnostics", name);
                        if (File.Exists(b)) return b;
                    }
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>A Linux path as Wine's Z: drive sees it (Z: maps to / in the private prefix).</summary>
    public static string ToWindowsPath(string path)
    {
        // Normalise first on every branch: /a/../b must become Z:\b, never a verbatim ".." in the helper's argument.
        var full = Path.GetFullPath(path);
        if (Path.DirectorySeparatorChar == '\\')
        {
            if (!path.StartsWith('/')) return full;
            // A Wine-style absolute path on a Windows test host: keep the Z: view while still normalising the dots.
            var root = Path.GetPathRoot(full) ?? "";
            return root.Length > 0 ? "Z:" + full[(root.Length - 1)..] : "Z:" + full;
        }
        if (full.StartsWith('/')) return "Z:" + full.Replace('/', '\\');
        if (full.Length >= 3 && char.IsLetter(full[0]) && full[1] == ':' && (full[2] == '\\' || full[2] == '/')) return full;
        return "Z:" + full.Replace('/', '\\');
    }

    /// <summary>
    /// The child environment: the private prefix, no Mono/Gecko prompts, quiet Wine logging unless TABFORGE_WINE_LOG=1,
    /// the bundled loader paths, a fixed PATH and the AppImage/.NET variables cleared. DISPLAY is inherited.
    /// </summary>
    public IReadOnlyDictionary<string, string> EnvironmentFor()
    {
        // Use the real loader folder first, not a guessed layout: an AppImage mounts under a random path, and a user may move
        // the extracted tar.gz anywhere. Everything below is derived from the loader that was actually found, never a
        // compile-time or installation path.
        var loaderDir = Path.GetDirectoryName(WineExecutable) ?? Path.Combine(BundleRoot, "bin");
        var env = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["WINEPREFIX"] = PrefixPath,
            ["WINEDLLOVERRIDES"] = "mscoree,mshtml=",
            ["WINEDEBUG"] = Environment.GetEnvironmentVariable("TABFORGE_WINE_LOG") == "1" ? "+err,+warn" : "-all",
            ["WINELOADER"] = WineExecutable,
            ["PATH"] = string.Join(':', new[] { loaderDir, Path.Combine(BundleRoot, "bin"), "/usr/local/bin", "/usr/bin", "/bin" }
                .Distinct(StringComparer.Ordinal)),
            ["LD_LIBRARY_PATH"] = "",
            ["LD_PRELOAD"] = "",
            ["PYTHONHOME"] = "",
            ["PYTHONPATH"] = "",
            ["APPDIR"] = "",
            ["APPIMAGE"] = "",
            ["ARGV0"] = "",
        };
        var server = Path.Combine(loaderDir, "wineserver");
        if (!File.Exists(server)) server = Path.Combine(BundleRoot, "bin", "wineserver");
        if (File.Exists(server)) env["WINESERVER"] = server;
        var dllPaths = new[]
        {
            Path.Combine(BundleRoot, "lib", "wine", "x86_64-unix"),
            Path.Combine(BundleRoot, "lib", "wine", "x86_64-windows"),
            Path.Combine(BundleRoot, "lib", "wine", "i386-unix"),
            Path.Combine(BundleRoot, "lib", "wine", "i386-windows"),
        }.Where(Directory.Exists).ToArray();
        if (dllPaths.Length > 0) env["WINEDLLPATH"] = string.Join(':', dllPaths);
        if (Version is { Length: > 0 }) env["TABFORGE_WINE_VERSION"] = Version;
        return env;
    }

    /// <summary>
    /// Lazy first-run setup: checks the loader (5 s), creates the private prefix with wineboot -u (180 s) when the marker
    /// is missing or names another Wine build, turns off Wine's crash dialog, and writes the marker. Serialised by a lock
    /// file. Never called from audio. A broken, read-only or full prefix becomes one readable exception and one failure
    /// marker; it never leaves the setup lock behind.
    /// </summary>
    public void EnsureReady(int versionTimeoutMs = 5000, int bootTimeoutMs = 180000)
    {
        if (!File.Exists(WineExecutable)) throw new FileNotFoundException("The bundled Wine loader is missing.", WineExecutable);
        try
        {
            CreatePrefixFolder();
            using var setupLock = AcquireSetupLock(TimeSpan.FromMilliseconds(bootTimeoutMs + 30000));
            var version = RunVersion(versionTimeoutMs);
            Version = version;
            var marker = Path.Combine(PrefixPath, MarkerName);
            if (Directory.Exists(marker))
                throw new InvalidOperationException($"The Wine prefix marker at {marker} is a folder. Remove it so TabForge can create a fresh marker.");
            var markerLines = ReadMarkerLines(marker);
            var ready = markerLines.Contains($"wineVersion={version}", StringComparer.Ordinal) &&
                        markerLines.Contains("crashDialog=off", StringComparer.Ordinal) &&
                        Directory.Exists(Path.Combine(PrefixPath, "drive_c"));
            if (ready)
            {
                // A prefix that looks ready but cannot be written fails later inside Wine with an unrelated message. Probe
                // the folder once per setup so a read-only prefix (or a full disk) is named before a helper is started.
                ProbeWritable();
                return;
            }
            Prepare(version, marker, bootTimeoutMs);
            try { File.Delete(Path.Combine(PrefixPath, FailedName)); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* swallow: a stale failure marker is optional. */ }
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw Fail(ex, versionTimeoutMs);
        }
        catch (InvalidOperationException ex) when (!ex.Message.Contains("could not be prepared", StringComparison.Ordinal))
        {
            throw Fail(ex, versionTimeoutMs);
        }
    }

    private void Prepare(string version, string marker, int bootTimeoutMs)
    {
        var boot = RunWine(["wineboot", "-u"], bootTimeoutMs, firstRun: true);
        if (boot.ExitCode != 0) throw new InvalidOperationException($"wineboot -u failed (exit {boot.ExitCode}): {boot.Error}".Trim());
        // A crashing plug-in must exit, not leave Wine's debugger dialog on screen (the plan's 2.6 first-run rule).
        var crash = RunWine(["reg", "add", @"HKCU\Software\Wine\WineDbg", "/v", "ShowCrashDialog", "/t", "REG_DWORD", "/d", "0", "/f"],
            Math.Min(bootTimeoutMs, 30000), firstRun: true);
        if (crash.ExitCode != 0)
            throw new InvalidOperationException($"could not turn off Wine's crash dialog (exit {crash.ExitCode}): {crash.Error}".Trim());
        WriteMarker(marker, version);
    }

    private InvalidOperationException Fail(Exception error, int bootTimeoutMs)
    {
        try { File.WriteAllText(Path.Combine(PrefixPath, FailedName), error.Message); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* swallow: a read-only prefix cannot hold the note. */ }
        return new InvalidOperationException(
            $"TabForge's Wine prefix could not be prepared: {error.Message} (see {Path.Combine(PrefixPath, FailedName)})", error);
    }

    private void CreatePrefixFolder()
    {
        try
        {
            if (OperatingSystem.IsWindows()) Directory.CreateDirectory(PrefixPath);
            else Directory.CreateDirectory(PrefixPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"The Wine prefix folder {PrefixPath} could not be created: {ex.Message}. Check that the folder is writable by this user.", ex);
        }
    }

    // A corrupted marker is only a hint that setup is needed; a failed or oversized read must not stop the repair.
    private static string[] ReadMarkerLines(string marker)
    {
        try
        {
            if (!File.Exists(marker)) return [];
            if (new FileInfo(marker).Length > 64 * 1024) return [];   // garbage, not a marker: run first-run setup again
            return File.ReadAllLines(marker);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    // One create/delete in the prefix: a read-only prefix or a full disk is reported here, before Wine is asked to run.
    private void ProbeWritable()
    {
        var probe = Path.Combine(PrefixPath, $".tabforge-write-{Environment.ProcessId}-{Guid.NewGuid():N}");
        try
        {
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
            File.Delete(probe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"The Wine prefix {PrefixPath} is not writable by this user (the folder may be read-only or the disk full): {ex.Message}. " +
                "Fix the folder permissions, free some space, or remove the prefix so TabForge can create a fresh one.", ex);
        }
    }

    private string RunVersion(int timeoutMs)
    {
        var result = RunWine(["--version"], timeoutMs, firstRun: false);
        var line = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";
        if (result.ExitCode != 0 || line.Length == 0) throw new InvalidOperationException($"The bundled Wine did not answer --version: {result.Error}".Trim());
        return line;
    }

    private void WriteMarker(string marker, string version)
    {
        var text = new StringBuilder()
            .AppendLine($"wineVersion={version}")
            .AppendLine("arch=win64")
            .AppendLine("helperProtocol=1")
            .AppendLine("crashDialog=off")
            .AppendLine($"createdUtc={DateTime.UtcNow:O}")
            .ToString();
        File.WriteAllText(marker, text);
    }

    private (int ExitCode, string Output, string Error) RunWine(string[] arguments, int timeoutMs, bool firstRun)
    {
        var start = new ProcessStartInfo(WineExecutable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        foreach (var (key, value) in EnvironmentFor()) start.Environment[key] = value;
        if (firstRun) start.Environment["WINEARCH"] = "win64";
        using var process = Process.Start(start) ?? throw new InvalidOperationException("The bundled Wine did not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeoutMs))
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { /* swallow: Wine already exited. */ }
            return (-1, "", $"Wine did not finish within {timeoutMs / 1000} s");
        }
        return (process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
    }

    private IDisposable AcquireSetupLock(TimeSpan timeout)
    {
        // A create-exclusive lock file is the atomic claim. Directory.CreateDirectory is idempotent on .NET, so two copies
        // could both believe they owned a directory lock and both run wineboot (Stage 15 CI found exactly that). The file
        // content is diagnostic only; a lock older than the setup timeout is treated as stale and removed once.
        var path = Path.Combine(PrefixPath, LockName);
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            try
            {
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream))
                    writer.Write($"{Environment.ProcessId} {DateTime.UtcNow:O}");
                return new SetupLock(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (File.Exists(path))
                {
                    if (File.GetLastWriteTimeUtc(path) < DateTime.UtcNow - timeout)
                    {
                        try { File.Delete(path); }
                        catch (Exception remove) when (remove is IOException or UnauthorizedAccessException) { /* another process may hold it. */ }
                        continue;
                    }
                    if (DateTime.UtcNow >= deadline)
                        throw new IOException("Another TabForge process is preparing the Wine prefix and did not finish in time.");
                    Thread.Sleep(200);
                    continue;
                }
                if (Directory.Exists(path))
                {
                    // A leftover directory lock from an earlier build: remove it once stale, otherwise wait for its owner.
                    if (Directory.GetCreationTimeUtc(path) < DateTime.UtcNow - timeout)
                    {
                        try { Directory.Delete(path, recursive: true); }
                        catch (Exception remove) when (remove is IOException or UnauthorizedAccessException) { /* another process may hold it. */ }
                        continue;
                    }
                    if (DateTime.UtcNow >= deadline)
                        throw new IOException("Another TabForge process is preparing the Wine prefix and did not finish in time.");
                    Thread.Sleep(200);
                    continue;
                }
                throw new InvalidOperationException(
                    $"The Wine prefix {PrefixPath} is not writable by this user (the folder may be read-only or the disk full): {ex.Message}. " +
                    "Fix the folder permissions, free some space, or remove the prefix so TabForge can create a fresh one.", ex);
            }
        }
    }

    private sealed class SetupLock(string path) : IDisposable
    {
        public void Dispose()
        {
            try { File.Delete(path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* swallow: best-effort lock release. */ }
        }
    }
}
