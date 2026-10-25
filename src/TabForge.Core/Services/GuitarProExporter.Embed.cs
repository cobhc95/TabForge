using System.IO;
using TabForge.Models;

namespace TabForge.Services;

// Owns: the TabForge project embedded in a .gp: writing its bytes, reading it back (bounded) and the zip directory reads both sides need.
// Does not own: the score writer (GuitarProExporter.cs) and the integrity record (EmbeddedBinding).
// Tests: no dedicated test class.
public static partial class GuitarProExporter
{
    /// <summary>
    /// The gzip project bytes for the embedded entry, held to the same limit the reader applies (<see cref="InputLimits.MaxTforgeFileBytes"/>).
    /// Over it: an <see cref="InvalidDataException"/> naming the largest plug-in states, in the style of the .tfaudio size message.
    /// </summary>
    internal static byte[] EmbeddedProjectBytes(SongProject project)
    {
        try { return ProjectService.PersistBytes(project); }
        catch (InvalidDataException ex) when (ex.Message == ProjectService.SizeLimitMessage)
        {
            var largest = AudioDataFile.LargestPluginStates(project);
            var beatCells = project.Tracks.Sum(t => t.Measures.Sum(m => (long)m.Cells.Count + m.Voice2Cells.Count));
            var notes = project.Tracks.Sum(t => t.Measures.Sum(m => (long)m.Cells.Concat(m.Voice2Cells).Sum(c => c.Notes.Count)));
            throw new InvalidDataException(
                $"The TabForge project saved inside this .gp file would be over its {AudioDataFile.Mb(InputLimits.MaxTforgeFileBytes)} limit, so nothing was saved. "
                + $"The song has {project.Tracks.Count} tracks, {beatCells:N0} beat cells and {notes:N0} notes"
                + (largest.Count > 0 ? $"; the largest plug-in states are {string.Join(", ", largest)}. Remove or reset those plug-ins (or unload large sample sets), then save again." : ". Save it as a clean .gp file instead, or split the song."));
        }
    }

    internal readonly record struct ZipPart(string Name, uint StoredCrc, byte[] Data);

    /// <summary>
    /// Reads a zip's entries straight from its central directory and local headers, inflating each one itself,
    /// without the CRC validation a stock zip reader may apply (alphaTab's writer stores wrong CRCs).
    /// Entries come back in archive order, directory entries with empty data.
    /// </summary>
    internal static List<ZipPart> ReadZip(byte[] zip)
    {
        static int U16(byte[] b, int at) => b[at] | b[at + 1] << 8;
        static int U32(byte[] b, int at) => b[at] | b[at + 1] << 8 | b[at + 2] << 16 | b[at + 3] << 24;
        var eocd = -1;
        for (var i = zip.Length - 22; i >= Math.Max(0, zip.Length - 22 - 65535); i--)
            if (U32(zip, i) == 0x06054b50) { eocd = i; break; }
        if (eocd < 0) throw new InvalidDataException("No zip end-of-directory record.");
        var count = U16(zip, eocd + 10);
        var pos = U32(zip, eocd + 16);
        var parts = new List<ZipPart>();
        for (var n = 0; n < count; n++)
        {
            if (pos < 0 || pos + 46 > zip.Length || U32(zip, pos) != 0x02014b50) throw new InvalidDataException("Bad zip central directory.");
            var method = U16(zip, pos + 10);
            var storedCrc = (uint)U32(zip, pos + 16);
            var compressedSize = U32(zip, pos + 20);
            var nameLength = U16(zip, pos + 28);
            var extraLength = U16(zip, pos + 30);
            var commentLength = U16(zip, pos + 32);
            var local = U32(zip, pos + 42);
            var name = System.Text.Encoding.UTF8.GetString(zip, pos + 46, nameLength);
            pos += 46 + nameLength + extraLength + commentLength;
            if (local < 0 || local + 30 > zip.Length || U32(zip, local) != 0x04034b50) throw new InvalidDataException("Bad zip local header.");
            var dataStart = local + 30 + U16(zip, local + 26) + U16(zip, local + 28);
            if (compressedSize < 0 || dataStart + (long)compressedSize > zip.Length) throw new InvalidDataException("Zip entry runs past the end of the file.");
            byte[] data;
            if (name.EndsWith('/')) data = Array.Empty<byte>();
            else if (method == 0) data = zip.AsSpan(dataStart, compressedSize).ToArray();
            else if (method == 8) data = GuitarProPreParse.InflateEntry(zip, dataStart, compressedSize);
            else throw new InvalidDataException($"Unsupported zip compression method {method}.");
            parts.Add(new ZipPart(name, storedCrc, data));
        }
        return parts;
    }

    public const string EmbeddedProjectEntry = "TabForge/project.tforge.gz";
    /// <summary>The integrity record beside the embedded project (<see cref="EmbeddedBinding"/>). Absent in files written before it existed.</summary>
    public const string EmbeddedBindingEntry = "TabForge/binding.json";
    /// <summary>The Guitar Pro score part the binding hashes.</summary>
    public const string ScoreEntry = "Content/score.gpif";

    /// <summary>True when the .gp carries an embedded TabForge project entry (used or not); only the zip directory is read.</summary>
    internal static bool HasEmbeddedEntry(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > InputLimits.MaxGuitarProFileBytes) return false;
            using var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read);
            return zip.GetEntry(EmbeddedProjectEntry) is not null;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { return false; } // Not logged: embedded project probe: false means none
    }

    /// <summary>The TabForge project embedded in a .gp written by TabForge, or null for other .gp files.</summary>
    public static SongProject? TryReadEmbedded(string path)
    {
        // Untrusted input: the same bounded read as any Guitar Pro file, and the zip entry is inflated
        // with a hard cap (its declared size can lie), so a small crafted .gp cannot expand to gigabytes.
        byte[] file;
        try { file = InputLimits.ReadBoundedBytes(path, InputLimits.MaxGuitarProFileBytes, "score file"); }
        catch (InvalidDataException) { return null; } // Not logged: the plain import reports the damage
        catch (IOException) { return null; } // Not logged: the plain import reports the damage
        catch (UnauthorizedAccessException) { return null; } // Not logged: the plain import reports the damage
        return TryReadEmbedded(file);
    }

    /// <summary>The embedded TabForge project of a .gp already read into memory (bounded), or null.</summary>
    internal static SongProject? TryReadEmbedded(byte[] file) => ReadEmbedded(file, out _);

    /// <summary>
    /// The embedded project of a score file in memory. Null with <paramref name="rejected"/> null: there is none (not a TabForge-written score file;
    /// the normal import applies, silently). Null with a reason in <paramref name="rejected"/>: one is present but cannot be used
    /// (over the size limit, damaged, unsupported version); the caller opens the file as plain Guitar Pro and tells the user why.
    /// </summary>
    internal static SongProject? ReadEmbedded(byte[] file, out string? rejected)
    {
        rejected = null;
        System.IO.Compression.ZipArchive zip;
        try { zip = new System.IO.Compression.ZipArchive(new MemoryStream(file, writable: false), System.IO.Compression.ZipArchiveMode.Read); }
        catch (InvalidDataException) { return null; }   // not a zip at all (Guitar Pro 3-5 ...): no embedded project // Not logged: not a zip: no embedded project
        using (zip)
        {
            System.IO.Compression.ZipArchiveEntry? entry;
            try { entry = zip.GetEntry(EmbeddedProjectEntry); }
            catch (InvalidDataException) { return null; }   // directory unreadable: the plain import reports the damage // Not logged: directory unreadable: the plain import reports the damage
            if (entry is null) return null;
            try
            {
                if (entry.Length > InputLimits.MaxTforgeFileBytes) { rejected = TooBigReason; return null; }
                using var stream = entry.Open();
                using var buffer = new MemoryStream();
                var chunk = new byte[64 * 1024];
                int read;
                while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
                {
                    if (buffer.Length + read > InputLimits.MaxTforgeFileBytes) { rejected = TooBigReason; return null; }
                    buffer.Write(chunk, 0, read);
                }
                var projectBytes = buffer.ToArray();
                var stale = EmbeddedBinding.Check(zip, projectBytes);
                if (stale is not null) { rejected = stale; return null; }
                var restored = ProjectService.RestorePersistedBytes(projectBytes);
                if (EmbeddedBinding.LegacyBarCountMismatch(zip, restored)) { rejected = EmbeddedBinding.StaleReason; return null; }
                return restored;
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException)
            {
                Services.Trace.Error(Services.Trace.Import, "embedded project: restore: " + ex.Message);
                rejected = ex.Message.Contains("size limit", StringComparison.Ordinal) ? TooBigReason
                    : ex.Message.Contains("unsupported format version", StringComparison.Ordinal) ? "it was saved by a newer or unknown version of TabForge"
                    : "the data is damaged or invalid";
                return null;
            }
        }
    }

    private static readonly string TooBigReason = $"it is over the {AudioDataFile.Mb(InputLimits.MaxTforgeFileBytes)} limit";

    /// <summary>The open notice for an embedded project that was present but rejected (<see cref="ReadEmbedded"/>).</summary>
    internal static string RejectedNotice(string reason) =>
        $"This file's TabForge project data could not be read ({reason}); it was opened as a plain score file"
        + (ReferenceEquals(reason, EmbeddedBinding.StaleReason) ? ", so the score is shown exactly as the file holds it and the older TabForge-only settings were not applied" : "");
}
