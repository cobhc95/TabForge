using System.IO;

namespace TabForge.Services;

// Owns: the safe file operations: atomic writes, leftover sweeps, reserved-name checks and bounded reads.
// Does not own: the contents of the files written.
// Tests: TestSecurityInputBoundaries.
/// <summary>Validates user-selected file boundaries without narrowing Unicode or long-path support.</summary>
public static partial class FilePathPolicy
{
    public static string ExistingFile(string path, string description, params string[] extensions)
    {
        var fullPath = FullPath(path, description);
        RequireExtension(fullPath, description, extensions);
        if (!File.Exists(fullPath)) throw new FileNotFoundException($"The selected {description} could not be found.");
        return fullPath;
    }

    public static string OutputFile(string path, string description, params string[] extensions)
    {
        var fullPath = FullPath(path, description);
        RequireExtension(fullPath, description, extensions);
        return fullPath;
    }

    public static string OutputFile(string path, string description)
    {
        return FullPath(path, description);
    }

    /// <summary>A typed output folder as a full path; a device path (\\.\ or \\?\) is refused before anything is created through it.</summary>
    public static string OutputDirectory(string path, string description)
    {
        var full = FullPath(path, description);
        if (full.StartsWith(@"\\?\", StringComparison.Ordinal) || full.StartsWith(@"\\.\", StringComparison.Ordinal))
            throw new InvalidDataException($"The selected {description} is a device path, which is not allowed.");
        return full;
    }

    /// <summary>One clause appended to a folder message when access was denied (Controlled folder access, OneDrive backup, or folder permissions).</summary>
    public const string AccessDeniedHint = " Check Controlled folder access, OneDrive backup, or folder permissions. You can also choose another folder in Settings.";

    /// <summary>True when <paramref name="error"/> is an access-denied failure (looking through inner errors and Win32 codes).</summary>
    public static bool IsAccessDenied(Exception error)
    {
        for (var e = error; e is not null; e = e.InnerException)
        {
            if (e is UnauthorizedAccessException) return true;
            if (e is IOException io && (io.HResult & 0xFFFF) == 5) return true;
        }
        return false;
    }

    /// <summary>
    /// A typed output folder that must already be usable: a device path is refused and a leaf that is a link (symbolic link or
    /// junction) is refused. Creates nothing (the caller creates the folder and then probes it with <see cref="TryProbeWritableFolder"/>).
    /// </summary>
    public static string ValidateWritableFolder(string path, string description)
    {
        var full = OutputDirectory(path, description);
        var link = false;
        try
        {
            if (Directory.Exists(full))
            {
                var info = new DirectoryInfo(full);
                link = info.LinkTarget is not null || (info.Attributes & FileAttributes.ReparsePoint) != 0;
            }
        }
        catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && ex is not InvalidDataException)
        {
            throw new InvalidDataException($"The selected {description} could not be checked: {ex.Message}", ex);
        }
        if (link) throw new InvalidDataException($"The selected {description} is a link, which is not allowed. Choose a normal folder.");
        return full;
    }

    /// <summary>
    /// Proves <paramref name="folder"/> can be created and written: creates it, writes and removes one small file opened
    /// create-new with no sharing. Returns false with <paramref name="failure"/> (never throws) when it cannot.
    /// </summary>
    public static bool TryProbeWritableFolder(string folder, out Exception? failure)
    {
        failure = null;
        string? probe = null;
        try
        {
            Directory.CreateDirectory(folder);
            probe = Path.Combine(folder, ".tabforge-write-test-" + Guid.NewGuid().ToString("N")[..8] + ".tmp");
            using (var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                stream.WriteByte(0);
                stream.Flush();   // plain flush: a 1-byte probe needs no write-through durability (and Flush(true) fails on some redirected stacks)
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            failure = ex;
            return false;
        }
        finally { DeleteQuietly(probe); }
    }

    /// <summary>
    /// Fallback when Windows (e.g. Controlled Folder Access) blocks a media folder: the ordered
    /// <see cref="FallbackWritableChain"/> (LocalAppData, then Temp), or null when neither probes writable.
    /// </summary>
    public static string? FallbackWritableFolder(string kind) => FallbackWritableChain(kind, null, out _);

    /// <summary>True when <paramref name="error"/> looks like disk-full/quota rather than access-denied (Win32 39/112 or full/quota/space wording).</summary>
    public static bool IsDiskFull(Exception error)
    {
        for (var e = error; e is not null; e = e.InnerException)
        {
            if (e is IOException io && ((io.HResult & 0xFFFF) is 39 or 112)) return true;
            if (e.Message.Contains("disk full", StringComparison.OrdinalIgnoreCase)
                || e.Message.Contains("not enough space", StringComparison.OrdinalIgnoreCase)
                || e.Message.Contains("quota", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>
    /// Ordered fallback chain when Windows blocks a media folder: level 1 %LocalAppData%\TabForge\<paramref name="kind"/>,
    /// level 2 %Temp%\TabForge\<paramref name="kind"/>, level 3 <paramref name="songAdjacent"/> (audio takes: the song's
    /// folder; video passes null and skips it). A level with an empty root is skipped, never relative; duplicate levels
    /// are probed once. Each level is write-probed; <paramref name="level"/> is the winning level (1-based), 0 when none.
    /// Wins and the exhausted chain are traced (fallback-1/2/3) for support; never throws.
    /// </summary>
    public static string? FallbackWritableChain(string kind, string? songAdjacent, out int level)
    {
        level = 0;
        var tried = new List<(int Number, string Path)>();
        void Add(int number, string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            string full;
            try { full = Path.GetFullPath(path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException) { return; }
            if (!Path.IsPathFullyQualified(full)) return;
            if (tried.Any(t => string.Equals(t.Path, full, TabForge.Platform.PathComparer.Comparison))) return;
            tried.Add((number, full));
        }
        string RootOf(Environment.SpecialFolder folder)
        {
            try { return Environment.GetFolderPath(folder); }
            catch (Exception ex) when (ex is ArgumentException or System.Security.SecurityException) { return ""; }
        }
        var localRoot = RootOf(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localRoot)) Add(1, Path.Combine(localRoot, "TabForge", kind));
        string tempRoot;
        try { tempRoot = Path.GetTempPath(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException) { tempRoot = ""; }
        if (!string.IsNullOrWhiteSpace(tempRoot)) Add(2, Path.Combine(tempRoot, "TabForge", kind));
        if (!string.IsNullOrWhiteSpace(songAdjacent) && Path.IsPathFullyQualified(songAdjacent)) Add(3, songAdjacent);
        foreach (var (number, path) in tried)
        {
            if (TryProbeWritableFolder(path, out var failure) && failure is null)
            {
                Services.Trace.Error(Services.Trace.Ui, $"fallback-{number}: {path}");
                level = number;
                return path;
            }
            Services.Trace.Error(Services.Trace.Ui, $"fallback-{number}: fail: {path}: {failure?.Message}");
        }
        Services.Trace.Error(Services.Trace.Ui, $"fallback-exhausted: {kind}");
        return null;
    }

    public static string DefaultDiagnosticsPath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName)
            throw new ArgumentException("A diagnostics file name must be a single file name.", nameof(fileName));
        var folder = UserPaths.Diagnostics;
        if (string.IsNullOrWhiteSpace(folder)) throw new IOException("The user diagnostics folder is not available.");
        return Path.Combine(folder, fileName);
    }

    /// <summary>Test seam: called at each write stage ("staged:&lt;file name&gt;", "commit:&lt;file name&gt;"); a throw simulates a failure there.</summary>
    internal static Action<string>? FaultInjection { get; set; }

    /// <summary>Writes beside the destination and replaces it only after a complete flushed write.</summary>
    public static void WriteAtomically(string path, Action<Stream> write, bool createDirectory = false)
    {
        ArgumentNullException.ThrowIfNull(write);
        var fullPath = FullPath(path, "output file");
        var temporaryPath = Stage(fullPath, write, createDirectory);
        try { Commit(temporaryPath, fullPath, backupPath: null); }
        finally { DeleteQuietly(temporaryPath); }
    }

    private static string Stage(string fullPath, Action<Stream> write, bool createDirectory, string? id = null)
    {
        var directory = Path.GetDirectoryName(fullPath)
                        ?? throw new IOException("The output folder is not available.");
        if (createDirectory) Directory.CreateDirectory(directory);
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("The output folder does not exist.");
        SweepStaleLeftovers(directory);

        // A short random leaf also works when the destination file name is already near the component limit.
        var temporaryPath = OwnFile(directory, id ?? NewId(), ".tmp");
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                       bufferSize: 64 * 1024, FileOptions.WriteThrough))
            {
                write(stream);
                FaultInjection?.Invoke("staged:" + Path.GetFileName(fullPath));
                stream.Flush(flushToDisk: true);
            }
            return temporaryPath;
        }
        catch
        {
            DeleteQuietly(temporaryPath);
            throw;
        }
    }

    private static void Commit(string temporaryPath, string fullPath, string? backupPath)
    {
        FaultInjection?.Invoke("commit:" + Path.GetFileName(fullPath));
        if (File.Exists(fullPath))
        {
            try { File.Replace(temporaryPath, fullPath, backupPath, ignoreMetadataErrors: true); }
            catch (PlatformNotSupportedException) // Not logged: platform without File.Replace: the copy path is used
            {
                if (backupPath is not null) File.Copy(fullPath, backupPath, overwrite: true);
                File.Move(temporaryPath, fullPath, overwrite: true);
            }
        }
        else
        {
            File.Move(temporaryPath, fullPath);
        }
    }

    /// <summary>Leftovers of a write interrupted by a crash or power loss are removed after this age (a running save is far younger).</summary>
    internal static readonly TimeSpan StaleLeftoverAge = TimeSpan.FromHours(24);

    /// <summary>
    /// Deletes this class's own staging / backup files (".tabforge-&lt;32 hex&gt;.tmp" / ".bak") older than <see cref="StaleLeftoverAge"/> in
    /// <paramref name="directory"/>: a crash between stage and commit would otherwise leave them in the user's folder forever. Anything
    /// not matching that exact name shape is never touched, nor is a file an unresolved pair save's marker still needs (a backup keeps the
    /// old file's date, so age alone does not prove it is abandoned); when a marker there cannot be read, nothing is swept. Returns the number deleted.
    /// </summary>
    internal static int SweepStaleLeftovers(string directory)
    {
        var deleted = 0;
        try
        {
            var cutoff = DateTime.UtcNow - StaleLeftoverAge;
            // Single directory scan: collect our files once, derive the pending ids from the markers among them.
            List<string> own;
            try { own = Directory.EnumerateFiles(directory, ".tabforge-*").ToList(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Services.Trace.Error(Services.Trace.Import, "temp sweep: scan folder: " + ex.Message); return 0; }
            var markers = own.Where(f => Path.GetFileName(f).StartsWith(".tabforge-save-", StringComparison.Ordinal) && f.EndsWith(".pending", StringComparison.OrdinalIgnoreCase)).ToList();
            if (PendingIds(markers) is not { } pending) return 0;
            foreach (var file in own)
            {
                var name = Path.GetFileName(file);
                if (!IsOwnLeftover(name) || pending.Contains(name.Substring(".tabforge-".Length, 32))) continue;
                try
                {
                    if (File.GetLastWriteTimeUtc(file) >= cutoff) continue;
                    File.Delete(file);
                    deleted++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } // Not logged: sweep: a locked leftover is kept for the next sweep.
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Services.Trace.Error(Services.Trace.Import, "temp sweep: scan folder: " + ex.Message); }
        return deleted;
    }

    private static bool IsOwnLeftover(string name)
    {
        const string prefix = ".tabforge-";
        if (name.Length != prefix.Length + 32 + 4 || !name.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var extension = name[^4..];
        if (!extension.Equals(".tmp", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".bak", StringComparison.OrdinalIgnoreCase)) return false;
        for (var i = prefix.Length; i < prefix.Length + 32; i++)
            if (!char.IsAsciiHexDigit(name[i])) return false;
        return true;
    }

    private static void DeleteQuietly(string? path)
    {
        if (path is null) return;
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { } // swallow: best-effort delete of a leftover.
        catch (UnauthorizedAccessException) { } // swallow: best-effort delete of a leftover.
    }

    /// <summary>True for a relative, forward-normalised archive entry name with no "..", drive, rooted or control-character parts; entries are only read in memory, but a name that could escape is refused anyway.</summary>
    public static bool IsSafeArchiveEntryName(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Any(c => c < 32)) return false;
        var normal = name.Replace('\\', '/');
        if (normal.StartsWith('/') || normal.Contains(':')) return false;
        return normal.Split('/').All(part => part != "..");
    }

    private static string FullPath(string path, string description)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidDataException($"Choose a valid {description} path.");
        try { return Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or System.Security.SecurityException)
        {
            throw new InvalidDataException($"The selected {description} path is invalid.", ex);
        }
    }

    private static void RequireExtension(string path, string description, string[] extensions)
    {
        if (extensions.Length == 0) return;
        var extension = Path.GetExtension(path);
        if (!extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException($"Choose a {description} with one of these extensions: {string.Join(", ", extensions)}.");
    }
}
