using System.IO;

namespace TabForge.Services;

// Owns: the two-file pair save (staged writes, the pending marker, crash recovery of an interrupted pair).
// Does not own: single-file atomic writes, staging and commit (FilePathPolicy.cs) or the contents of the files written.
// Tests: TestSecurityInputBoundaries.
public static partial class FilePathPolicy
{
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
                { Services.Trace.Error(Services.Trace.Ui, "pair restore: " + restoreError.Message); outcome = PairOutcome.Unresolved(restoreError.Message); }
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
    private static readonly IReadOnlyList<string> MarkerKeys = new[] { "first", "second", "firstTemp", "secondTemp", "backup", "restore", "oldFirst", "newFirst", "oldSecond", "newSecond" };

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
        for (var i = 0; i < MarkerKeys.Count; i++) body.Append(MarkerKeys[i]).Append('=').Append(values[i]).Append('\n');
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
        catch (ArgumentException) { problem = "the marker is not valid text"; return null; } // Not logged: marker text check: the problem text is returned to the caller
        if (text.StartsWith("TabForge pair save v1", StringComparison.Ordinal)) { problem = "the marker is in the older format that stored file paths, which is no longer trusted"; return null; }
        problem = "the marker is incomplete or malformed";
        if (!text.EndsWith('\n') || text.Contains('\r') || text.Contains('\0')) return null;
        var lines = text[..^1].Split('\n');
        if (lines.Length != MarkerKeys.Count + 2 || lines[0] != PairMarkerMagic) return null;
        var checkAt = text.LastIndexOf("check=", StringComparison.Ordinal);
        if (lines[^1] != "check=" + Sha256Hex(System.Text.Encoding.UTF8.GetBytes(text[..checkAt]))) { problem = "the marker's checksum does not match (incomplete or altered)"; return null; }
        var v = new string[MarkerKeys.Count];
        for (var i = 0; i < MarkerKeys.Count; i++)
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
    public static string? RecoverInterruptedPair(string firstPath, string secondPath) => RecoverInterruptedPairCore(firstPath, secondPath);

    /// <summary>
    /// <see cref="RecoverInterruptedPair(string, string)"/> for a first file whose partner is named by the marker itself: a .gp saved with a
    /// full copy (.tforge) or with its audio data (.tfaudio). Only those two partner kinds are accepted from a marker.
    /// </summary>
    public static string? RecoverInterruptedPair(string firstPath)
    {
        string first;
        try
        {
            first = Path.GetFullPath(firstPath);
            if (Path.GetDirectoryName(first) is not { } folder || !File.Exists(PairMarkerPath(first))) return null;
            var partner = Path.ChangeExtension(first, ".tfaudio");
            if (ReadMarkerBytes(PairMarkerPath(first), out _) is { } bytes && ParseMarker(bytes, Path.GetFileName(first), null, out _) is { } info
                && (info.SecondName.EndsWith(".tfaudio", StringComparison.OrdinalIgnoreCase) || info.SecondName.EndsWith(".tforge", StringComparison.OrdinalIgnoreCase)))
                partner = Path.Combine(folder, info.SecondName);
            return RecoverInterruptedPairCore(first, partner);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            Services.Trace.Error(Services.Trace.Ui, "interrupted save: recover: " + ex.Message);
            return null;
        }
    }

    private static string? RecoverInterruptedPairCore(string firstPath, string secondPath)
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException) // Not logged: no marker: nothing to recover
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
            Services.Trace.Error(Services.Trace.Ui, "interrupted save: report: " + ex.Message);
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } // Not logged: best-effort delete of a file whose content changed; it is kept.
    }

    /// <summary>The ids of files still needed by pending pair saves in <paramref name="directory"/>; null when any marker there cannot be read (then nothing is swept).</summary>
    private static HashSet<string>? PendingIds(string directory)
    {
        try
        {
            return PendingIds(Directory.EnumerateFiles(directory, ".tabforge-save-*.pending"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Overload over an already-enumerated marker list (additive): lets the sweep use one directory scan.</summary>
    private static HashSet<string>? PendingIds(IEnumerable<string> markers)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var marker in markers)
        {
            try
            {
                if (ReadMarkerBytes(marker, out _) is not { } bytes || ParseMarker(bytes, null, null, out _) is not { } info) return null;
                ids.UnionWith(info.Ids);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; } // Not logged: best-effort id collection: the caller treats null as none
        }
        return ids;
    }
}
