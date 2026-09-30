using System.IO;

namespace TabForge.Services;

/// <summary>Validates user-selected file boundaries without narrowing Unicode or long-path support.</summary>
public static class FilePathPolicy
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

    public static string OutputDirectory(string path, string description)
    {
        return FullPath(path, description);
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

    /// <summary>
    /// Writes two files that belong together (a clean .gp and its .tfaudio): both are staged completely beside their destinations first,
    /// then committed first-then-second. When the second commit fails the first destination is restored from its backup, so the pair on
    /// disk is either the old pair or the new pair.
    /// </summary>
    public static void WritePairAtomically(string firstPath, Action<Stream> writeFirst, string secondPath, Action<Stream> writeSecond)
    {
        ArgumentNullException.ThrowIfNull(writeFirst);
        ArgumentNullException.ThrowIfNull(writeSecond);
        var first = FullPath(firstPath, "output file");
        var second = FullPath(secondPath, "output file");
        var directory = Path.GetDirectoryName(first) ?? throw new IOException("The output folder is not available.");
        if (!string.Equals(directory, Path.GetDirectoryName(second), StringComparison.OrdinalIgnoreCase) || string.Equals(first, second, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The two files of a pair save must be different files in the same folder.");
        var marker = PairMarkerPath(first);
        // An earlier save of this pair that was never resolved: resolve it first; if that is not possible, write nothing (its backup is still needed).
        if (File.Exists(marker))
        {
            var pending = RecoverInterruptedPair(first, second);
            if (File.Exists(marker))
                throw new IOException($"An earlier save of '{first}' was interrupted and could not be resolved, so nothing was written. {pending}");
        }

        var info = new PairMarker(Path.GetFileName(first), Path.GetFileName(second), NewId(), NewId(), NewId(), NewId(), null, "", null, "");
        string? firstTemp = null, secondTemp = null;
        var markerWritten = false;
        var keep = false;
        try
        {
            firstTemp = Stage(first, writeFirst, false, info.FirstTempId);
            secondTemp = Stage(second, writeSecond, false, info.SecondTempId);
            var oldFirst = HashOrNull(first);
            info = info with { OldFirst = oldFirst, BackupId = oldFirst is null ? null : info.BackupId, NewFirst = HashOrNull(firstTemp)!, OldSecond = HashOrNull(second), NewSecond = HashOrNull(secondTemp)! };
            var backup = info.BackupId is null ? null : OwnFile(directory, info.BackupId, ".bak");
            // "Save in progress" marker: complete and flushed before either commit, removed last, so an interruption is recoverable on the next open.
            FaultInjection?.Invoke("marker:" + info.FirstName);
            WriteMarker(marker, info);
            markerWritten = true;
            FaultInjection?.Invoke("marker-written:" + info.FirstName);
            try
            {
                Commit(firstTemp, first, backup);
                FaultInjection?.Invoke("committed:" + info.FirstName);
                Commit(secondTemp, second, backupPath: null);
                FaultInjection?.Invoke("committed:" + info.SecondName);
                // Both commits done: a failed cleanup here is harmless (the next open sees the new pair and finishes it).
                if (backup is not null) DeleteQuietly(backup);
                FaultInjection?.Invoke("cleanup:" + info.FirstName);
                DeleteQuietly(marker);
                markerWritten = false;
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                // Bring the pair back to a state proven by content (old pair restored, or the new pair already complete) with the same logic as recovery.
                PairOutcome outcome;
                try { outcome = ResolvePair(directory, first, second, marker, info, "restore:"); }
                catch (Exception restoreError) when (restoreError is IOException or UnauthorizedAccessException)
                { outcome = PairOutcome.Unresolved(restoreError.Message); }
                if (!outcome.Resolved)
                {
                    keep = true;
                    var message = $"Saving '{second}' failed ({error.Message}) and '{first}' could not be restored from its backup '{backup ?? "(none: the file is new)"}' ({outcome.Detail}). " +
                                  $"The backup and the recovery marker '{marker}' were kept; the next open of the file retries.";
                    System.Diagnostics.Trace.TraceError(message);
                    throw new IOException(message, error);
                }
                markerWritten = false;
                throw;
            }
        }
        finally
        {
            if (!keep)
            {
                DeleteQuietly(firstTemp);
                DeleteQuietly(secondTemp);
                if (markerWritten) DeleteQuietly(marker);
            }
        }
    }

    private static string PairMarkerPath(string firstFullPath)
    {
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(firstFullPath.ToUpperInvariant())))[..16].ToLowerInvariant();
        return Path.Combine(Path.GetDirectoryName(firstFullPath)!, $".tabforge-save-{hash}.pending");
    }

    // ---- The pair-save marker: untrusted input. It names no paths, only this transaction's ids and content hashes. ----

    private const string PairMarkerMagic = "TabForge pair save v2";
    /// <summary>A valid marker is ~700 bytes; anything larger is refused before it is read.</summary>
    internal const int MaxPairMarkerBytes = 4096;
    private static readonly string[] MarkerKeys = { "first", "second", "firstTemp", "secondTemp", "backup", "restore", "oldFirst", "newFirst", "oldSecond", "newSecond" };

    /// <param name="BackupId">Null when the first file did not exist before (nothing to back up).</param>
    /// <param name="OldFirst">SHA-256 of the first file before the save; null when it did not exist. Same for <paramref name="OldSecond"/>.</param>
    private sealed record PairMarker(string FirstName, string SecondName, string FirstTempId, string SecondTempId, string? BackupId, string RestoreId,
        string? OldFirst, string NewFirst, string? OldSecond, string NewSecond)
    {
        public IEnumerable<string> Ids => new[] { FirstTempId, SecondTempId, BackupId, RestoreId }.OfType<string>();
    }

    private readonly record struct PairOutcome(bool Resolved, bool Restored, string Detail)
    {
        public static PairOutcome Unresolved(string detail) => new(false, false, detail);
    }

    private static string NewId() => Guid.NewGuid().ToString("N");

    /// <summary>This class's own leftover name shape (see <see cref="IsOwnLeftover"/>), always in the song's own folder: the marker supplies only the id.</summary>
    private static string OwnFile(string directory, string id, string extension) => Path.Combine(directory, $".tabforge-{id}{extension}");

    private static string Sha256Hex(ReadOnlySpan<byte> bytes) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();

    /// <summary>Content hash of a file, or null when it does not exist. A locked file throws (the caller keeps the recovery state and retries later).</summary>
    private static string? HashOrNull(string path)
    {
        if (!File.Exists(path)) return null;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static bool IsReparsePoint(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static string MarkerText(PairMarker m)
    {
        var values = new[] { m.FirstName, m.SecondName, m.FirstTempId, m.SecondTempId, m.BackupId ?? "-", m.RestoreId, m.OldFirst ?? "-", m.NewFirst, m.OldSecond ?? "-", m.NewSecond };
        var body = new System.Text.StringBuilder(PairMarkerMagic).Append('\n');
        for (var i = 0; i < MarkerKeys.Length; i++) body.Append(MarkerKeys[i]).Append('=').Append(values[i]).Append('\n');
        var text = body.ToString();
        return text + "check=" + Sha256Hex(System.Text.Encoding.UTF8.GetBytes(text)) + "\n";
    }

    /// <summary>Written to a flushed (write-through) temporary file and then renamed, so the marker is either absent or complete.</summary>
    private static void WriteMarker(string marker, PairMarker info)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(MarkerText(info));
        var temporary = OwnFile(Path.GetDirectoryName(marker)!, NewId(), ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, marker, overwrite: false);
        }
        finally { DeleteQuietly(temporary); }
    }

    /// <summary>Reads a marker with a size bound; refuses links. Returns null (with the reason) when it cannot be read.</summary>
    private static byte[]? ReadMarkerBytes(string marker, out string problem)
    {
        problem = "";
        if (IsReparsePoint(marker)) { problem = "the marker is a link, not a file TabForge wrote"; return null; }
        using var stream = new FileStream(marker, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaxPairMarkerBytes) { problem = $"the marker is {stream.Length} bytes (a valid one is under {MaxPairMarkerBytes})"; return null; }
        var buffer = new byte[MaxPairMarkerBytes + 1];
        var total = 0;
        for (int read; total < buffer.Length && (read = stream.Read(buffer, total, buffer.Length - total)) > 0;) total += read;
        if (total > MaxPairMarkerBytes) { problem = "the marker grew while it was read"; return null; }
        return buffer[..total];
    }

    /// <summary>
    /// Validates the marker's complete structure (magic, exact keys in order, id and hash shapes, checksum). With expected names, the recorded
    /// file names must equal them exactly. Returns null with the reason for anything else, including the older v1 format that stored paths.
    /// </summary>
    private static PairMarker? ParseMarker(byte[] bytes, string? expectedFirst, string? expectedSecond, out string problem)
    {
        string text;
        try { text = new System.Text.UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes); }
        catch (ArgumentException) { problem = "the marker is not valid text"; return null; }
        if (text.StartsWith("TabForge pair save v1", StringComparison.Ordinal)) { problem = "the marker is in the older format that stored file paths, which is no longer trusted"; return null; }
        problem = "the marker is incomplete or malformed";
        if (!text.EndsWith('\n') || text.Contains('\r') || text.Contains('\0')) return null;
        var lines = text[..^1].Split('\n');
        if (lines.Length != MarkerKeys.Length + 2 || lines[0] != PairMarkerMagic) return null;
        var checkAt = text.LastIndexOf("check=", StringComparison.Ordinal);
        if (lines[^1] != "check=" + Sha256Hex(System.Text.Encoding.UTF8.GetBytes(text[..checkAt]))) { problem = "the marker's checksum does not match (incomplete or altered)"; return null; }
        var v = new string[MarkerKeys.Length];
        for (var i = 0; i < MarkerKeys.Length; i++)
        {
            var prefix = MarkerKeys[i] + "=";
            if (!lines[i + 1].StartsWith(prefix, StringComparison.Ordinal)) return null;
            v[i] = lines[i + 1][prefix.Length..];
        }
        static bool Hex(string s, int length) => s.Length == length && s.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
        static string? Optional(string s) => s == "-" ? null : s;
        var m = new PairMarker(v[0], v[1], v[2], v[3], Optional(v[4]), v[5], Optional(v[6]), v[7], Optional(v[8]), v[9]);
        if (!IsPlainFileName(m.FirstName) || !IsPlainFileName(m.SecondName) || string.Equals(m.FirstName, m.SecondName, StringComparison.OrdinalIgnoreCase)) return null;
        if (!m.Ids.All(id => Hex(id, 32)) || m.Ids.Distinct().Count() != m.Ids.Count()) return null;
        if (!Hex(m.NewFirst, 64) || !Hex(m.NewSecond, 64) || (m.OldFirst is { } of && !Hex(of, 64)) || (m.OldSecond is { } os && !Hex(os, 64))) return null;
        if ((m.BackupId is null) != (m.OldFirst is null)) return null;
        if ((expectedFirst is not null && !string.Equals(m.FirstName, expectedFirst, StringComparison.OrdinalIgnoreCase))
            || (expectedSecond is not null && !string.Equals(m.SecondName, expectedSecond, StringComparison.OrdinalIgnoreCase)))
        { problem = "the marker belongs to other files"; return null; }
        problem = "";
        return m;
    }

    /// <summary>A single file name: no folder, drive, stream or device part, no traversal.</summary>
    private static bool IsPlainFileName(string name)
    {
        if (name.Length is 0 or > 255 || name is "." or ".." || name.EndsWith(' ') || name.EndsWith('.')) return false;
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains(':') || name.Contains('/') || name.Contains('\\')) return false;
        var stem = name.Split('.')[0].TrimEnd().ToUpperInvariant();
        return stem is not ("CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$")
               && !(stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && char.IsAsciiDigit(stem[3]));
    }

    /// <summary>
    /// Call when a file that may have been written by <see cref="WritePairAtomically"/> is opened, with the second file's path derived from it
    /// (e.g. the .tfaudio beside a .gp). If a "save in progress" marker exists, the pair is brought back to a state proven by content hashes:
    /// the new pair when both commits happened, otherwise the old pair (the first file restored from this transaction's own backup). Anything
    /// that cannot be proven (a malformed or foreign marker, a mismatching backup, a locked file) changes nothing, keeps every file and the
    /// marker, and is reported so the next open can retry. Returns a message for the user, or null when nothing was pending.
    /// </summary>
    public static string? RecoverInterruptedPair(string firstPath, string secondPath)
    {
        string first, second, directory, marker;
        try
        {
            first = Path.GetFullPath(firstPath);
            second = Path.GetFullPath(secondPath);
            if (Path.GetDirectoryName(first) is not { } folder) return null;
            directory = folder;
            marker = PairMarkerPath(first);
            if (!File.Exists(marker)) return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            return null;   // no marker can be located for a path that does not resolve
        }
        try
        {
            string Kept(string why) =>
                $"An interrupted save of '{first}' could not be resolved automatically: {why}. Nothing was changed; the recovery marker '{marker}' and any backup beside it were kept.";
            if (first.StartsWith(@"\\?\", StringComparison.Ordinal) || first.StartsWith(@"\\.\", StringComparison.Ordinal))
                return Kept("the song was opened through a device path");
            if (!string.Equals(directory, Path.GetDirectoryName(second), StringComparison.OrdinalIgnoreCase))
                return Kept("the audio data file is not in the song's folder");
            if (new DirectoryInfo(directory).LinkTarget is not null)
                return Kept("the song's folder is a link (junction or symbolic link)");
            if (ReadMarkerBytes(marker, out var problem) is not { } bytes || ParseMarker(bytes, Path.GetFileName(first), Path.GetFileName(second), out problem) is not { } info)
                return Kept(problem);
            FaultInjection?.Invoke("recover:" + info.FirstName);
            var outcome = ResolvePair(directory, first, second, marker, info, "recover:");
            if (!outcome.Resolved) return Kept(outcome.Detail);
            return outcome.Restored ? $"A previous save of '{Path.GetFileName(first)}' was interrupted; the last complete version was restored." : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            return $"An interrupted save of '{first}' could not be resolved now ({ex.Message}). Nothing further was changed; the recovery marker '{marker}' was kept and the next open retries.";
        }
    }

    /// <summary>
    /// Brings a marked pair to a content-proven consistent state and then removes this transaction's own files (each only when its content
    /// is the recorded one) and finally the marker. Never overwrites or deletes anything whose content it cannot account for.
    /// </summary>
    private static PairOutcome ResolvePair(string directory, string first, string second, string marker, PairMarker info, string faultPrefix)
    {
        var firstTemp = OwnFile(directory, info.FirstTempId, ".tmp");
        var secondTemp = OwnFile(directory, info.SecondTempId, ".tmp");
        var restoreTemp = OwnFile(directory, info.RestoreId, ".tmp");
        var backup = info.BackupId is null ? null : OwnFile(directory, info.BackupId, ".bak");
        foreach (var own in new[] { firstTemp, secondTemp, restoreTemp, backup })
            if (own is not null && File.Exists(own) && IsReparsePoint(own)) return PairOutcome.Unresolved($"'{own}' is a link, not a file TabForge wrote");

        var hFirst = HashOrNull(first);
        var hSecond = HashOrNull(second);
        var newPair = hFirst == info.NewFirst && hSecond == info.NewSecond;
        var oldPair = hFirst == info.OldFirst && hSecond == info.OldSecond;
        var restored = false;
        if (!newPair && !oldPair)
        {
            // Only one known mixed state: the first file was committed and the second was not. Anything else is not ours to decide.
            if (hFirst != info.NewFirst || hSecond != info.OldSecond)
                return PairOutcome.Unresolved("the song and its audio data match neither the previous save nor the new one");
            FaultInjection?.Invoke(faultPrefix + info.FirstName);
            if (info.OldFirst is null) File.Delete(first);   // the song did not exist before this save (its content is the new one, checked above)
            else
            {
                if (backup is null || HashOrNull(backup) != info.OldFirst)
                    return PairOutcome.Unresolved($"its backup '{backup}' is missing or does not hold the previous version");
                if (File.Exists(restoreTemp) && HashOrNull(restoreTemp) != info.OldFirst)
                    return PairOutcome.Unresolved($"'{restoreTemp}' exists and is not this save's file");
                DeleteQuietly(restoreTemp);
                using (var source = new FileStream(backup, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var target = new FileStream(restoreTemp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.WriteThrough))
                {
                    source.CopyTo(target);
                    target.Flush(flushToDisk: true);
                }
                if (HashOrNull(restoreTemp) != info.OldFirst) return PairOutcome.Unresolved("the restored copy did not match the backup");
                File.Move(restoreTemp, first, overwrite: true);
            }
            if (HashOrNull(first) != info.OldFirst) return PairOutcome.Unresolved("the song could not be put back to its previous version");
            restored = true;
        }
        DeleteIfContent(firstTemp, info.NewFirst);
        DeleteIfContent(secondTemp, info.NewSecond);
        if (info.OldFirst is not null) DeleteIfContent(restoreTemp, info.OldFirst);
        if (backup is not null) DeleteIfContent(backup, info.OldFirst!);
        File.Delete(marker);
        return new PairOutcome(true, restored, "");
    }

    // Self-test seams: locate a marker, write a crafted one, hash a file.
    internal static string PairMarkerPathFor(string firstPath) => PairMarkerPath(Path.GetFullPath(firstPath));
    internal static string CraftPairMarker(string firstName, string secondName, string firstTempId, string secondTempId, string? backupId, string restoreId,
        string? oldFirst, string newFirst, string? oldSecond, string newSecond) =>
        MarkerText(new PairMarker(firstName, secondName, firstTempId, secondTempId, backupId, restoreId, oldFirst, newFirst, oldSecond, newSecond));
    internal static string? ContentHash(string path) => HashOrNull(path);

    private static void DeleteIfContent(string path, string sha256)
    {
        try { if (HashOrNull(path) == sha256) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>The ids of files still needed by pending pair saves in <paramref name="directory"/>; null when any marker there cannot be read (then nothing is swept).</summary>
    private static HashSet<string>? PendingIds(string directory)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var marker in Directory.EnumerateFiles(directory, ".tabforge-save-*.pending"))
        {
            try
            {
                if (ReadMarkerBytes(marker, out _) is not { } bytes || ParseMarker(bytes, null, null, out _) is not { } info) return null;
                ids.UnionWith(info.Ids);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
        }
        return ids;
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
            catch (PlatformNotSupportedException)
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
            if (PendingIds(directory) is not { } pending) return 0;
            foreach (var file in Directory.EnumerateFiles(directory, ".tabforge-*"))
            {
                var name = Path.GetFileName(file);
                if (!IsOwnLeftover(name) || pending.Contains(name.Substring(".tabforge-".Length, 32))) continue;
                try
                {
                    if (File.GetLastWriteTimeUtc(file) >= cutoff) continue;
                    File.Delete(file);
                    deleted++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
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
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
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
