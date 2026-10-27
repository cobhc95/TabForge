using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TabForge.Models;

namespace TabForge.Services;

// Owns: saving and loading the .tforge project file, including its bounded read.
// Does not own: the exports, autosave and plug-in state files.
// Tests: no dedicated test class.
public static partial class ProjectService
{
    /// <summary>In-memory form (clipboard snapshots): compacted, see <see cref="LosslessCompactResolver"/>.</summary>
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        MaxDepth = InputLimits.MaxJsonDepth,
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver { Modifiers = { OmitInitialValues, OmitViewState } }
    };

    /// <summary>The unsaved-changes hash: the compact form.</summary>
    private static readonly JsonSerializerOptions HashOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        MaxDepth = InputLimits.MaxJsonDepth,
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver
        {
            Modifiers = { OmitInitialValues, OmitViewState }
        }
    };

    /// <summary>
    /// .tforge files on disk: every property is written (FormatVersion included), so a file never depends on the constructor defaults of
    /// the release that reads it. Not indented, which keeps full files about as small as the old indented compacted ones.
    /// </summary>
    /// <remarks>
    /// One exception to "every property": a beat cell (<see cref="TabCell"/>) is written without the properties that still hold their
    /// constructor value, so an empty cell is <c>{}</c> instead of about 530 bytes. A song keeps 16 cells per bar and track for each of its two
    /// voices, and a real one is mostly empty cells (a 2,039-bar, 8-track song: 523,592 cells, 86,272 with content), which made the
    /// full form 265 MiB and over the 128 MiB limit although the song is a 1 MB file. The cell defaults are frozen like the schema-2 ones
    /// (TestPersistenceSchema); changing one needs a FormatVersion bump and a migration. Files written this way read the same in older releases.
    /// </remarks>
    private static readonly JsonSerializerOptions DiskOptions = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true,
        MaxDepth = InputLimits.MaxJsonDepth,
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver
        {
            Modifiers = { info => { if (info.Type == typeof(TabCell)) OmitInitialValues(info); } }
        },
    };

    /// <summary>
    /// Schema of .tforge files written before full serialization: they omitted FormatVersion (and every value equal to a schema-2
    /// initializer). A file without FormatVersion is read as this version. Its omitted defaults are frozen by the self-test fixture
    /// (TestPersistenceSchema): changing any of those initializers needs a FormatVersion bump and a migration that fills the old values.
    /// </summary>
    internal const int LegacyCompactedFormatVersion = 2;

    /// <summary>
    /// Lossless compaction: a property is written only when it differs from the value a freshly
    /// constructed object already has (its initializer), so reading it back yields the identical model.
    /// Tens of thousands of beat cells and notes mostly hold initial values; skipping them roughly
    /// quarters the JSON, and with it the time of every undo snapshot and unsaved-changes check.
    /// Used only in memory (undo, clipboard, hashing, embedded .gp snapshots), never for the .tforge files on disk.
    /// </summary>
    internal static System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver LosslessCompactResolver() =>
        new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver { Modifiers = { OmitInitialValues } };

    private static void OmitInitialValues(System.Text.Json.Serialization.Metadata.JsonTypeInfo info)
    {
        if (info.Kind != System.Text.Json.Serialization.Metadata.JsonTypeInfoKind.Object || info.CreateObject is null) return;
        object prototype;
        // A type whose constructor cannot run standalone simply keeps default (full) serialization.
        try { prototype = info.CreateObject(); }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or MissingMethodException) { return; } // Not logged: type cannot run standalone: default serialization is kept
        foreach (var property in info.Properties)
        {
            if (property.Get is null || property.Set is null) continue;
            object? initial;
            try { initial = property.Get(prototype); }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException) { continue; } // Not logged: property without a readable initial value: serialized as before
            property.ShouldSerialize = (_, value) => !SameAsInitial(value, initial);
        }
    }

    private static bool SameAsInitial(object? value, object? initial) => value switch
    {
        null => initial is null,
        string text => initial is string start && string.Equals(text, start, StringComparison.Ordinal),
        System.Collections.ICollection collection => collection.Count == 0 && initial is System.Collections.ICollection { Count: 0 },
        _ => value.GetType().IsValueType && value.Equals(initial)
    };

    /// <summary>
    /// Writes the project to <paramref name="path"/> and returns the hash of its content as written. The song's own unsaved state is not touched:
    /// whoever saves the song decides when it is clean.
    /// </summary>
    public static byte[] Save(string path, SongProject project)
    {
        path = FilePathPolicy.OutputFile(path, "TabForge project", ".tforge");
        // Stream to disk (size-checking on the way) instead of building the whole file in
        // one pooled buffer that the shared ArrayPool would keep afterwards.
        FilePathPolicy.WriteAtomically(path, stream => WriteTo(stream, project));
        // The clean baseline for the unsaved-changes check, which hashes the compact in-memory form.
        return ContentHash(project);
    }

    /// <summary>Writes the .tforge bytes of <paramref name="project"/> to <paramref name="stream"/>: startup-template tracks left out, validated, gzip-compressed, JSON size bounded.</summary>
    public static void WriteTo(Stream stream, SongProject project) => WriteTo(stream, project, skipValidation: false);

    /// <summary>
    /// Overload for callers that just validated <paramref name="project"/> (additive): skips the second
    /// <see cref="ProjectValidator.Validate"/> pass. Existing callers are unchanged.
    /// </summary>
    public static void WriteTo(Stream stream, SongProject project, bool skipValidation)
    {
        var toWrite = project.Tracks.Any(t => t.StartupTemplateId is not null) ? project.WithoutStartupTracks() : project;   // startup-template tracks are not saved
        if (!skipValidation) toWrite = CloneProject(toWrite);   // validated (and repaired) on a copy: the open song is never silently changed
        // gzip-compressed on disk (about 35x smaller); the limit applies to the JSON size.
        using var gzip = new GZipStream(stream, CompressionLevel.Optimal, leaveOpen: true);
        using var sink = new HashingLimitStream(gzip, InputLimits.MaxTforgeFileBytes);
        SerializeChunked(sink, toWrite, DiskOptions);
    }

    /// <param name="maxBytes">File and JSON size bound (<see cref="InputLimits.MaxTforgeFileBytes"/> for a project and for a crash-recovery copy).</param>
    public static SongProject Load(string path, long maxBytes = InputLimits.MaxTforgeFileBytes)
    {
        path = FilePathPolicy.ExistingFile(path, "TabForge project", ".tforge");

        try
        {
            var bytes = InputLimits.ReadBoundedBytes(path, maxBytes, "TabForge project");
            // Files written since M-03 are gzip; older ones are plain JSON. Detect by the gzip magic bytes.
            if (bytes.Length >= 2 && bytes[0] == 0x1F && bytes[1] == 0x8B) bytes = GunzipBounded(bytes, maxBytes);
            // Single JSON scan: shape limits + root FormatVersion flag (was two full passes).
            ProjectValidator.ValidateJsonShape(bytes, out var hasFormatVersion);
            var project = JsonSerializer.Deserialize<SongProject>(bytes, DiskOptions)
                          ?? throw new InvalidDataException("The TabForge project is empty or invalid.");
            if (!hasFormatVersion) project.FormatVersion = LegacyCompactedFormatVersion;
            ProjectValidator.Validate(project);
            MigrateRepeatGroupsToOneClip(project);
            MigrateLoopGroupsToSeparateTakes(project);
            RepairGp3To5PalmMuteDurations(project);
            // (a ladder-tuned guitar/bass keeps its Kind and TAB; the instrument panel follows the instrument: InstrumentViewRules)
            project.IsDirty = false;
            return project;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The TabForge project is malformed or exceeds the supported JSON depth.", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new InvalidDataException("TabForge could not read this project file.", ex);
        }
        catch (IOException ex)
        {
            throw new InvalidDataException("TabForge could not read this project file.", ex);
        }
    }

    /// <summary>
    /// Migration: .tforge files saved after per-note duration % landed (P-14) but before the GP3-5 garbage fix hold
    /// SoundDurationPercent = 1 on palm-muted notes. A Guitar Pro 3-5 file has no such value and the editor's own values
    /// are normally 20..200, so a value under 5 in a project imported from .gp3/.gp4/.gp5 is the bug's residue: reset to 100.
    /// </summary>
    internal static void RepairGp3To5PalmMuteDurations(SongProject project)
    {
        var source = project.ImportedFrom;
        if (string.IsNullOrEmpty(source)) return;
        var ext = Path.GetExtension(source);
        if (!(ext.Equals(".gp3", StringComparison.OrdinalIgnoreCase) || ext.Equals(".gp4", StringComparison.OrdinalIgnoreCase) ||
              ext.Equals(".gp5", StringComparison.OrdinalIgnoreCase))) return;
        foreach (var track in project.Tracks)
            foreach (var measure in track.Measures)
                foreach (var cell in measure.Cells.Concat(measure.Voice2Cells))
                    if (cell.SoundDurationPercent < 5) cell.SoundDurationPercent = 100;
    }

    // The loop-group to separate-takes migration lives in ProjectService.TakeGroups.cs (partial) to keep this file within its size budget.
    /// <summary>The JSON text of a .tforge file, gzip or legacy plain (diagnostics and tests).</summary>
    internal static string ReadJsonText(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 2 && bytes[0] == 0x1F && bytes[1] == 0x8B) bytes = GunzipBounded(bytes);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    /// <summary>Decompresses a gzip .tforge, refusing anything that expands past the project size limit.</summary>
    internal static byte[] GunzipBounded(byte[] data, long maxBytes = InputLimits.MaxTforgeFileBytes)
    {
        try
        {
            using var input = new MemoryStream(data, writable: false);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            var buffer = new byte[64 * 1024];
            while (true)
            {
                var read = gzip.Read(buffer, 0, buffer.Length);
                if (read == 0) break;
                if (output.Length + read > maxBytes)
                    throw new InvalidDataException("The TabForge project exceeds the decompressed size limit.");
                output.Write(buffer, 0, read);
            }
            return output.ToArray();
        }
        catch (InvalidDataException) { throw; }
        catch (Exception ex) when (ex is IOException or EndOfStreamException)
        {
            throw new InvalidDataException("The compressed TabForge project is damaged.", ex);
        }
    }

    /// <summary>True when the root JSON object names <paramref name="name"/> (case-insensitive, like the deserializer); nested values are skipped.</summary>
    internal static bool HasRootProperty(ReadOnlySpan<byte> json, string name)
    {
        var reader = new Utf8JsonReader(json, new JsonReaderOptions { MaxDepth = InputLimits.MaxJsonDepth });
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) return false;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (string.Equals(reader.GetString(), name, StringComparison.OrdinalIgnoreCase)) return true;
            reader.Read();
            reader.Skip();
        }
        return false;
    }

    /// <summary>SHA-256 of the compact in-memory serialization (the unsaved-changes baseline), computed without buffering it.</summary>
    public static byte[] ContentHash(SongProject project)
    {
        using var sink = new HashingLimitStream(Stream.Null, long.MaxValue);
        SerializeChunked(sink, project, HashOptions);
        return sink.Finish();
    }

    /// <summary>Self-test: the JSON of one beat cell as the disk form writes it.</summary>
    internal static byte[] MeasureCell(TabCell cell) => JsonSerializer.SerializeToUtf8Bytes(cell, DiskOptions);

    /// <summary>Diagnostics (`--import-measure`): the JSON size of the project in the disk form (every property) and in the compact in-memory form.</summary>
    internal static (long Full, long Compact) MeasureJsonBytes(SongProject project)
    {
        using var full = new HashingLimitStream(Stream.Null, long.MaxValue, hash: false);
        SerializeChunked(full, project, DiskOptions);
        using var compact = new HashingLimitStream(Stream.Null, long.MaxValue, hash: false);
        SerializeChunked(compact, project, CompactOptions);
        return (full.Length, compact.Length);
    }

    public static string Snapshot(SongProject project)
    {
        var json = JsonSerializer.Serialize(CloneProject(project), Options);   // the copy is validated; the open song is left as it is
        if (Encoding.UTF8.GetByteCount(json) > InputLimits.MaxTforgeFileBytes)
            throw new InvalidDataException("The project snapshot exceeds the 128 MiB size limit.");
        return json;
    }

    /// <summary>
    /// Trusted in-memory clone for Duplicate/undo helpers: compact serialize + deserialize + model validate of the COPY (the source is never repaired),
    /// without the untrusted-input JSON-shape scan (<see cref="ProjectValidator.ValidateJsonShape"/>). Additive;
    /// <see cref="Snapshot"/>/<see cref="Restore(string)"/> are unchanged for untrusted inputs.
    /// </summary>
    public static SongProject CloneProject(SongProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(project, CompactOptions);
        if (bytes.LongLength > InputLimits.MaxTforgeFileBytes)
            throw new InvalidDataException("The project snapshot exceeds the 128 MiB size limit.");
        try
        {
            var clone = JsonSerializer.Deserialize<SongProject>(bytes, CompactOptions)
                ?? throw new InvalidDataException("The project clone is empty or invalid.");
            ProjectValidator.Validate(clone);
            return clone;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The project clone is malformed.", ex);
        }
    }

    public static SongProject Restore(string snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Length > InputLimits.MaxTforgeFileBytes ||
            Encoding.UTF8.GetByteCount(snapshot) > InputLimits.MaxTforgeFileBytes)
            throw new InvalidDataException("The project snapshot exceeds the 128 MiB size limit.");
        return DeserializeAndValidate(Encoding.UTF8.GetBytes(snapshot), "project snapshot");
    }

    private static readonly JsonSerializerOptions CompactOptions = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true,
        MaxDepth = InputLimits.MaxJsonDepth,
        TypeInfoResolver = LosslessCompactResolver()
    };

    /// <summary>
    /// Whole-song snapshot for the memory report and self-tests. Undo keeps per-bar <c>ProjectState</c>s instead.
    /// Compact UTF-8 JSON, gzip-compressed. It runs on the CALLER's thread: the serialising happens on a pool
    /// thread but the caller waits for it, so on the UI thread it blocks the UI for the whole serialise + compress. Autosave no
    /// longer uses it: it captures a cheap immutable ProjectState on the UI thread and serialises that on a worker. A realistic score serialises to
    /// megabytes of text (and twice that as a .NET string), so keeping it raw would let the history
    /// grow to gigabytes; compressed it is ~1% of that.
    /// </summary>
    public static byte[] SnapshotBytes(SongProject project)
    {
        // Stream straight into gzip: the uncompressed JSON (tens of MB for a full song) never exists as
        // one buffer. SerializeToUtf8Bytes rented it from the shared ArrayPool, which then kept it.
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
            SerializeChunked(gzip, project, CompactOptions);
        return output.ToArray();
    }

    /// <summary>
    /// Writes JSON to a stream in ~16 KB chunks. The synchronous stream overload builds the whole payload
    /// (tens of MB for a full song) in one ArrayPool buffer before writing, and the shared pool then keeps
    /// every size it grew through (measured: 128 MB retained). The async path flushes as it goes; its
    /// internal awaits do not capture the UI context, so blocking on it here cannot deadlock.
    /// </summary>
    private static void SerializeChunked<T>(Stream stream, T value, JsonSerializerOptions options) =>
        Task.Run(() => JsonSerializer.SerializeAsync(stream, value, options)).GetAwaiter().GetResult();

    /// <summary>
    /// The project embedded in a TabForge-written .gp: gzip of the full disk JSON (FormatVersion always written, no compaction).
    /// The JSON is held to <see cref="InputLimits.MaxTforgeFileBytes"/>, the same limit the .tforge save and the embedded-project
    /// reader (<see cref="RestorePersistedBytes"/>) apply, so a .gp TabForge writes can always be read back. Throws
    /// an <see cref="InvalidDataException"/> with <see cref="SizeLimitMessage"/> when it is over.
    /// </summary>
    internal const string SizeLimitMessage = "The expanded project data would be larger than the 128 MiB limit (the limit applies to the data inside the project, not to the size of the file).";

    /// <summary>
    /// The hand-off of an imported song from the import worker process to the app: the project in the compact in-memory form (properties
    /// at their initial value left out, see <see cref="LosslessCompactResolver"/>), gzip-compressed. Both ends are the same build, so
    /// nothing depends on a file schema. The JSON is held to <see cref="InputLimits.MaxTforgeFileBytes"/> and the reader applies the
    /// same bound; <paramref name="what"/> names the data in the message when it is over.
    /// </summary>
    internal static byte[] TransferBytes(SongProject project, string what = "imported song")
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
        using (var sink = new HashingLimitStream(gzip, InputLimits.MaxTforgeFileBytes, hash: false, what: what))
            SerializeChunked(sink, project, CompactOptions);
        return output.ToArray();
    }

    /// <summary>Reads <see cref="TransferBytes"/> output (bounded, validated like any project).</summary>
    internal static SongProject RestoreTransferBytes(byte[] transfer)
    {
        ArgumentNullException.ThrowIfNull(transfer);
        if (transfer.LongLength > InputLimits.MaxTforgeFileBytes)
            throw new InvalidDataException("The imported song's compressed data is larger than the 128 MiB limit.");
        using var input = new MemoryStream(transfer, writable: false);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = gzip.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (output.Length + read > InputLimits.MaxTforgeFileBytes)
                throw new InvalidDataException("The imported song's expanded data is larger than the 128 MiB limit.");
            output.Write(buffer, 0, read);
        }
        return DeserializeAndValidate(output.ToArray(), "imported song", CompactOptions);
    }

    /// <param name="maxJsonBytes">The JSON size bound; a crash-recovery copy uses the same bound as a project (<see cref="InputLimits.MaxTforgeFileBytes"/>).</param>
    public static byte[] PersistBytes(SongProject project, long maxJsonBytes = InputLimits.MaxTforgeFileBytes)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
        using (var sink = new HashingLimitStream(gzip, maxJsonBytes, hash: false))
            SerializeChunked(sink, project, DiskOptions);
        return output.ToArray();
    }

    /// <summary>Reads <see cref="PersistBytes"/> output, and the compacted form older releases embedded (no FormatVersion: schema 2).</summary>
    public static SongProject RestorePersistedBytes(byte[] snapshot)
    {
        var project = RestoreBytes(snapshot, out var json, out var hasFormatVersion);
        if (!hasFormatVersion) project.FormatVersion = LegacyCompactedFormatVersion;
        return project;
    }

    public static SongProject RestoreBytes(byte[] snapshot) => RestoreBytes(snapshot, out _);

    /// <summary>Overload that also reports the fused root flag so callers skip the second scan (additive).</summary>
    internal static SongProject RestoreBytes(byte[] snapshot, out byte[] json, out bool hasFormatVersion)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.LongLength > InputLimits.MaxTforgeFileBytes)
            throw new InvalidDataException("The undo snapshot exceeds the compressed size limit.");
        using var input = new MemoryStream(snapshot, writable: false);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var read = gzip.Read(buffer, 0, buffer.Length);
            if (read == 0) break;
            if (output.Length + read > InputLimits.MaxTforgeFileBytes)
                throw new InvalidDataException("The undo snapshot exceeds the decompressed size limit.");
            output.Write(buffer, 0, read);
        }
        json = output.ToArray();
        return DeserializeAndValidate(json, "undo snapshot", CompactOptions, out hasFormatVersion);
    }

    private static SongProject RestoreBytes(byte[] snapshot, out byte[] json)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.LongLength > InputLimits.MaxTforgeFileBytes)
            throw new InvalidDataException("The undo snapshot exceeds the compressed size limit.");
        using var input = new MemoryStream(snapshot, writable: false);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var read = gzip.Read(buffer, 0, buffer.Length);
            if (read == 0) break;
            if (output.Length + read > InputLimits.MaxTforgeFileBytes)
                throw new InvalidDataException("The undo snapshot exceeds the decompressed size limit.");
            output.Write(buffer, 0, read);
        }
        json = output.ToArray();
        return DeserializeAndValidate(json, "undo snapshot", CompactOptions);
    }

    private static SongProject DeserializeAndValidate(byte[] json, string description,
        JsonSerializerOptions? options = null) =>
        DeserializeAndValidate(json, description, options, out _);

    /// <summary>Validating deserialize that also reports the fused root flag (additive; skips the second scan).</summary>
    private static SongProject DeserializeAndValidate(byte[] json, string description,
        JsonSerializerOptions? options, out bool hasFormatVersion)
    {
        try
        {
            ProjectValidator.ValidateJsonShape(json, out hasFormatVersion);
            var project = JsonSerializer.Deserialize<SongProject>(json, options ?? Options)
                          ?? throw new InvalidDataException($"The {description} is empty or invalid.");
            ProjectValidator.Validate(project);
            return project;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"The {description} is malformed or exceeds the supported JSON depth.", ex);
        }
    }

    public static MeasureModel CloneMeasure(MeasureModel measure)
    {
        ArgumentNullException.ThrowIfNull(measure);
        // Manual deep copy (no JSON round-trip on the UI thread); falls back to JSON only if a clone fails.
        try { return measure.Clone(); }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            return JsonSerializer.Deserialize<MeasureModel>(JsonSerializer.SerializeToUtf8Bytes(measure, CompactOptions), CompactOptions)
                ?? throw new InvalidDataException("Measure clone failed.");
        }
    }
}
