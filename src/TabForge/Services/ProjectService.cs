using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TabForge.Models;

namespace TabForge.Services;

public static class ProjectService
{
    /// <summary>In-memory form (clipboard snapshots, the unsaved-changes hash): compacted, see <see cref="LosslessCompactResolver"/>.</summary>
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        MaxDepth = InputLimits.MaxJsonDepth,
        TypeInfoResolver = LosslessCompactResolver()
    };

    /// <summary>
    /// .tforge files on disk: every property is written (FormatVersion included), so a file never depends on the constructor defaults of
    /// the release that reads it. Not indented, which keeps full files about as small as the old indented compacted ones.
    /// </summary>
    private static readonly JsonSerializerOptions DiskOptions = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true,
        MaxDepth = InputLimits.MaxJsonDepth,
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
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or MissingMethodException) { return; }
        foreach (var property in info.Properties)
        {
            if (property.Get is null || property.Set is null) continue;
            object? initial;
            try { initial = property.Get(prototype); }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException) { continue; }
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

    public static byte[] Save(string path, SongProject project)
    {
        path = FilePathPolicy.OutputFile(path, "TabForge project", ".tforge");
        var wasDirty = project.IsDirty;
        project.IsDirty = false;
        try
        {
            var toWrite = project.Tracks.Any(t => t.StartupTemplateId is not null) ? project.WithoutStartupTracks() : project;   // startup-template tracks are not saved
            ProjectValidator.Validate(toWrite);
            // Stream to disk (size-checking on the way) instead of building the whole file in
            // one pooled buffer that the shared ArrayPool would keep afterwards.
            FilePathPolicy.WriteAtomically(path, stream =>
            {
                // gzip-compressed on disk (Audit 3 M-03: ~35x smaller); the limit applies to the JSON size.
                using var gzip = new GZipStream(stream, CompressionLevel.Optimal, leaveOpen: true);
                using var sink = new HashingLimitStream(gzip, InputLimits.MaxTforgeFileBytes);
                SerializeChunked(sink, toWrite, DiskOptions);
            });
            // The clean baseline for the unsaved-changes check, which hashes the compact in-memory form.
            return ContentHash(project);
        }
        catch
        {
            project.IsDirty = wasDirty;
            throw;
        }
    }

    public static SongProject Load(string path)
    {
        path = FilePathPolicy.ExistingFile(path, "TabForge project", ".tforge");

        try
        {
            var bytes = InputLimits.ReadBoundedBytes(path, InputLimits.MaxTforgeFileBytes, "TabForge project");
            // Files written since M-03 are gzip; older ones are plain JSON. Detect by the gzip magic bytes.
            if (bytes.Length >= 2 && bytes[0] == 0x1F && bytes[1] == 0x8B) bytes = GunzipBounded(bytes);
            ProjectValidator.ValidateJsonShape(bytes);
            var project = JsonSerializer.Deserialize<SongProject>(bytes, DiskOptions)
                          ?? throw new InvalidDataException("The TabForge project is empty or invalid.");
            if (!HasRootProperty(bytes, nameof(SongProject.FormatVersion))) project.FormatVersion = LegacyCompactedFormatVersion;
            ProjectValidator.Validate(project);
            RepairGp3To5PalmMuteDurations(project);
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

    /// <summary>The JSON text of a .tforge file, gzip or legacy plain (diagnostics and tests).</summary>
    internal static string ReadJsonText(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 2 && bytes[0] == 0x1F && bytes[1] == 0x8B) bytes = GunzipBounded(bytes);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    /// <summary>Decompresses a gzip .tforge, refusing anything that expands past the project size limit.</summary>
    internal static byte[] GunzipBounded(byte[] data)
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
                if (output.Length + read > InputLimits.MaxTforgeFileBytes)
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
        SerializeChunked(sink, project, Options);
        return sink.Finish();
    }

    public static string Snapshot(SongProject project)
    {
        ProjectValidator.Validate(project);
        var json = JsonSerializer.Serialize(project, Options);
        if (Encoding.UTF8.GetByteCount(json) > InputLimits.MaxTforgeFileBytes)
            throw new InvalidDataException("The project snapshot exceeds the 128 MiB size limit.");
        return json;
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
    /// Whole-song snapshot (the memory report and self-tests; undo keeps per-bar <see cref="TabForge.Documents.ProjectState"/>s
    /// since Audit 3 M-06): compact UTF-8 JSON, gzip-compressed. It runs on the CALLER's thread: the serialising happens on a pool
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

    /// <summary>The project embedded in a TabForge-written .gp: gzip of the full disk JSON (FormatVersion always written, no compaction).</summary>
    public static byte[] PersistBytes(SongProject project)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
            SerializeChunked(gzip, project, DiskOptions);
        return output.ToArray();
    }

    /// <summary>Reads <see cref="PersistBytes"/> output, and the compacted form older releases embedded (no FormatVersion: schema 2).</summary>
    public static SongProject RestorePersistedBytes(byte[] snapshot)
    {
        var project = RestoreBytes(snapshot, out var json);
        if (!HasRootProperty(json, nameof(SongProject.FormatVersion))) project.FormatVersion = LegacyCompactedFormatVersion;
        return project;
    }

    public static SongProject RestoreBytes(byte[] snapshot) => RestoreBytes(snapshot, out _);

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
        JsonSerializerOptions? options = null)
    {
        try
        {
            ProjectValidator.ValidateJsonShape(json);
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

    public static MeasureModel CloneMeasure(MeasureModel measure) =>
        JsonSerializer.Deserialize<MeasureModel>(JsonSerializer.SerializeToUtf8Bytes(measure, CompactOptions), CompactOptions)
        ?? throw new InvalidDataException("Measure clone failed.");

    /// <summary>
    /// Undo state headers (see <see cref="TabForge.Documents.ProjectState"/>): the compact in-memory JSON of the song without its
    /// track list, and of a track without its bars. The bars are kept as separate shared chunks, so an edit re-stores only what changed.
    /// </summary>
    private static readonly JsonSerializerOptions UndoHeaderOptions = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true,
        MaxDepth = InputLimits.MaxJsonDepth,
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver { Modifiers = { OmitInitialValues, OmitScoreBody } }
    };

    private static void OmitScoreBody(System.Text.Json.Serialization.Metadata.JsonTypeInfo info)
    {
        var body = info.Type == typeof(SongProject) ? nameof(SongProject.Tracks)
            : info.Type == typeof(TrackModel) ? nameof(TrackModel.Measures) : null;
        if (body is null) return;
        for (var index = info.Properties.Count - 1; index >= 0; index--)
            if (info.Properties[index].Name == body) info.Properties.RemoveAt(index);
    }

    internal static byte[] UndoSongHeader(SongProject project) => JsonSerializer.SerializeToUtf8Bytes(project, UndoHeaderOptions);
    internal static byte[] UndoTrackHeader(TrackModel track) => JsonSerializer.SerializeToUtf8Bytes(track, UndoHeaderOptions);

    internal static SongProject RestoreUndoSongHeader(byte[] json) =>
        JsonSerializer.Deserialize<SongProject>(json, UndoHeaderOptions) ?? throw new InvalidDataException("The undo state is damaged.");

    internal static TrackModel RestoreUndoTrackHeader(byte[] json) =>
        JsonSerializer.Deserialize<TrackModel>(json, UndoHeaderOptions) ?? throw new InvalidDataException("The undo state is damaged.");

    /// <summary>Compact in-memory JSON of any model object (self-tests compare the undo bar codec against it).</summary>
    internal static byte[] CompactJson<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, CompactOptions);

    /// <summary>The JSON property names the in-memory form writes for <paramref name="type"/> (the undo bar codec must cover every one).</summary>
    internal static IReadOnlyList<string> CompactPropertyNames(Type type) =>
        CompactOptions.GetTypeInfo(type).Properties
            .Where(property => property.AttributeProvider?.GetCustomAttributes(typeof(System.Text.Json.Serialization.JsonIgnoreAttribute), true)
                .OfType<System.Text.Json.Serialization.JsonIgnoreAttribute>()
                .Any(ignore => ignore.Condition == System.Text.Json.Serialization.JsonIgnoreCondition.Always) != true)
            .Select(property => property.Name).ToList();
}

/// <summary>Write-only pass-through that SHA-256 hashes and bounds what is written to the inner stream.</summary>
internal sealed class HashingLimitStream : Stream
{
    private readonly Stream _inner;
    private readonly long _limit;
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private long _written;

    public HashingLimitStream(Stream inner, long limit) { _inner = inner; _limit = limit; }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        _written += buffer.Length;
        if (_written > _limit) throw new InvalidDataException("The TabForge project exceeds the 128 MiB size limit.");
        _hash.AppendData(buffer);
        _inner.Write(buffer);
    }

    public byte[] Finish() => _hash.GetHashAndReset();

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => _written;
    public override long Position { get => _written; set => throw new NotSupportedException(); }
    public override void Flush() => _inner.Flush();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    protected override void Dispose(bool disposing) { if (disposing) _hash.Dispose(); base.Dispose(disposing); }
}
