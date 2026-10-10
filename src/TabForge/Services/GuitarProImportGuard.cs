using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;

namespace TabForge.Services;

// Owns: the import guard that stops a runaway import between stages and the context carrying it.
// Does not own: the conversion rules and the worker process (ImportWorker).
// Tests: TestGuitarProImportContainment, TestGuitarProImportWorker.
/// <summary>
/// Cooperative limits for one score import: cancellation, a wall-clock time budget and a managed-memory
/// watermark, checked between import stages and per converted bar. The importer receives it inside its
/// <see cref="ImportContext"/>; headless tools run with a context that has no guard.
/// </summary>
/// <remarks>
/// alphaTab's parse is a single call that cannot be interrupted. The app's background import therefore runs it in a separate
/// process (<see cref="ImportWorker"/>) inside a Job Object with memory and CPU limits, killed on Cancel or at the time budget;
/// only when that process cannot start does the parse run in-process, where a stuck parse is abandoned rather than stopped.
/// </remarks>
public sealed class ImportGuard
{
    public static readonly TimeSpan DefaultTimeBudget = TimeSpan.FromSeconds(60);
    /// <summary>Growth of the managed heap during one import that aborts it (ordinary songs use a few tens of MiB).</summary>
    public const long DefaultMemoryBudgetBytes = 2L * 1024 * 1024 * 1024;

    private readonly CancellationToken _token;
    private readonly long _startTimestamp = Stopwatch.GetTimestamp();
    private readonly long _deadlineTimestamp;
    private readonly long _heapLimit;

    public TimeSpan TimeBudget { get; }
    public long MemoryBudgetBytes { get; }
    /// <summary>The import's cancellation (the out-of-process worker's client kills the worker when it fires).</summary>
    public CancellationToken Token => _token;
    /// <summary>Time left of the budget (zero once it ran out).</summary>
    public TimeSpan Remaining => TimeSpan.FromSeconds(Math.Max(0, (_deadlineTimestamp - Stopwatch.GetTimestamp()) / (double)Stopwatch.Frequency));

    public ImportGuard(CancellationToken token, TimeSpan? timeBudget = null, long? memoryBudgetBytes = null)
    {
        _token = token;
        TimeBudget = timeBudget ?? DefaultTimeBudget;
        MemoryBudgetBytes = memoryBudgetBytes ?? DefaultMemoryBudgetBytes;
        _deadlineTimestamp = _startTimestamp + (long)(TimeBudget.TotalSeconds * Stopwatch.Frequency);
        _heapLimit = GC.GetTotalMemory(false) + MemoryBudgetBytes;
    }

    /// <summary>Throws when the import was cancelled (<see cref="OperationCanceledException"/>) or ran over its time or memory budget.</summary>
    public void Check()
    {
        _token.ThrowIfCancellationRequested();
        if (Stopwatch.GetTimestamp() > _deadlineTimestamp)
            throw new InvalidDataException($"Importing this score file took longer than {TimeBudget.TotalSeconds:0} seconds, so it was stopped.");
        if (GC.GetTotalMemory(false) > _heapLimit)
            throw new InvalidDataException("Importing this score file needed too much memory, so it was stopped.");
    }

}

/// <summary>
/// The state of one Guitar Pro import, passed down the importer's call chain: the cooperative limits of the import
/// (<see cref="Guard"/>, none for headless use), the conversion state of the file being read, and what the import reports
/// once it is done (duplicate notes merged, the embedded-project and damaged-file notices). One context serves one import
/// at a time; the importer resets the reported values when it starts.
/// </summary>
public sealed class ImportContext
{
    public ImportContext(ImportGuard? guard = null) => Guard = guard;

    /// <summary>The import's limits, or null (headless use: no budget checks).</summary>
    public ImportGuard? Guard { get; }

    /// <summary>Notes the last import skipped as duplicates (the same string/pitch merged by voices/staves).</summary>
    public int SkippedDuplicates { get; internal set; }
    /// <summary>Diagnostics: a few of the notes the last import merged as duplicates.</summary>
    public List<string> DuplicateSamples { get; } = new();
    /// <summary>The open notice when the last import found a TabForge project inside the .gp but could not use it (opened as plain Guitar Pro); otherwise null.</summary>
    public string? EmbeddedRejection { get; internal set; }
    /// <summary>The open notice when the last import found far more impossible facts than a valid file holds (see <see cref="ImportPlausibility"/>); otherwise null. The song is opened unchanged.</summary>
    public string? DamageNotice { get; internal set; }

    /// <summary>True while a Guitar Pro 3-5 file is read (alphaTab names its tremolo speeds one step off there).</summary>
    internal bool Gp3To5 { get; set; }
    /// <summary>Mix-table transitions and all-tracks flags alphaTab drops, found in the raw bytes of the file being read.</summary>
    internal Dictionary<object, GuitarProMixTableScanner.RawMix>? RawMixes { get; set; }

    /// <summary>Throws when the import was cancelled or ran over its time or memory budget (cheap: a token read, a timestamp and a heap counter).</summary>
    public void Check() => Guard?.Check();

    /// <summary>Adds the embedded-project and damaged-file notices of the last import, in that order.</summary>
    public void AddNoticesTo(List<string> notices)
    {
        if (EmbeddedRejection is { } rejected) notices.Add(rejected);
        if (DamageNotice is { } damaged) notices.Add(damaged);
    }
}

/// <summary>
/// Checks run on the raw bytes before alphaTab parses them: the container of a zip-based score file is inflated with
/// hard caps, a GPX (BCFZ) declares its unpacked size up front, and a Guitar Pro 3-5 header must look like one.
/// </summary>
public static class GuitarProPreParse
{
    public const int MaxZipEntries = 1_024;
    /// <summary>One unpacked entry: the score XML or an embedded audio asset (stored, so never above the file cap).</summary>
    public const long MaxZipEntryBytes = InputLimits.MaxGuitarProFileBytes;
    public const long MaxZipTotalBytes = 2 * InputLimits.MaxGuitarProFileBytes;
    /// <summary>Unpacked size a GPX (BCFZ) may declare; alphaTab stops decompressing at the declared size.</summary>
    public const long MaxGpxUnpackedBytes = 2 * InputLimits.MaxGuitarProFileBytes;

    private static readonly byte[] Gp3To5Signature = System.Text.Encoding.ASCII.GetBytes("FICHIER GUITAR PRO v");

    /// <summary>Throws <see cref="InvalidDataException"/> when the file must not reach the parser.</summary>
    public static void Validate(byte[] data, ImportContext? context = null)
    {
        if (data.Length >= 4 && data[0] == (byte)'P' && data[1] == (byte)'K' && data[2] == 3 && data[3] == 4) { ValidateZip(data, context); return; }
        if (data.Length >= 4 && data[0] == (byte)'B' && data[1] == (byte)'C' && data[2] == (byte)'F' && data[3] == (byte)'Z') { ValidateGpx(data); return; }
        ValidateGp3To5Header(data);
    }

    private static void ValidateZip(byte[] data, ImportContext? context)
    {
        // The end-of-central-directory record gives the entry count without building any entry objects.
        var eocd = -1;
        for (var i = data.Length - 22; i >= Math.Max(0, data.Length - 22 - 65_535); i--)
            if (BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(i, 4)) == 0x06054b50) { eocd = i; break; }
        if (eocd < 0) throw new InvalidDataException("This score file is a damaged archive (no directory).");
        var entries = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(eocd + 10, 2));
        var directorySize = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(eocd + 12, 4));
        if (entries == 0xFFFF || directorySize == 0xFFFFFFFF)
            throw new InvalidDataException("This score file uses an archive format (zip64) these files never need.");
        if (entries > MaxZipEntries)
            throw new InvalidDataException($"This score archive holds {entries:N0} files (the limit is {MaxZipEntries:N0}).");

        // Inflate every entry into a counting sink: declared sizes can lie, the caps hold for what really unpacks.
        long total = 0;
        var chunk = new byte[64 * 1024];
        try
        {
            using var zip = new ZipArchive(new MemoryStream(data, writable: false), ZipArchiveMode.Read);
            if (zip.Entries.Count > MaxZipEntries)
                throw new InvalidDataException($"This score archive holds too many files (the limit is {MaxZipEntries:N0}).");
            foreach (var entry in zip.Entries)
            {
                context?.Check();
                if (entry.Length > MaxZipEntryBytes || total + entry.Length > MaxZipTotalBytes) throw TooLarge();
                using var stream = entry.Open();
                long entryBytes = 0;
                int read;
                while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
                {
                    entryBytes += read;
                    total += read;
                    if (entryBytes > MaxZipEntryBytes || total > MaxZipTotalBytes) throw TooLarge();
                }
            }
        }
        catch (InvalidDataException) { throw; }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException)
        {
            throw new InvalidDataException("This score file is a damaged archive.", ex);
        }

        static InvalidDataException TooLarge() => new(
            $"This score archive unpacks to more than the {MaxZipEntryBytes / (1024 * 1024)} MiB per file / {MaxZipTotalBytes / (1024 * 1024)} MiB total limit, so it was not opened.");
    }

    private static void ValidateGpx(byte[] data)
    {
        if (data.Length < 8) throw new InvalidDataException("This .gpx file is truncated.");
        var declared = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4, 4));
        if (declared < 0 || declared > MaxGpxUnpackedBytes)
            throw new InvalidDataException($"This .gpx file declares an unpacked size above the {MaxGpxUnpackedBytes / (1024 * 1024)} MiB limit, so it was not opened.");
    }

    private static void ValidateGp3To5Header(byte[] data)
    {
        // Only a file that carries the Guitar Pro 3-5 signature right after its length byte is checked; anything else is left to the parser.
        if (data.Length < 1 + Gp3To5Signature.Length || !data.AsSpan(1, Gp3To5Signature.Length).SequenceEqual(Gp3To5Signature)) return;
        // Header: a length byte, then a 30-byte field "FICHIER GUITAR PRO vX.YY".
        var length = data[0];
        var versionAt = 1 + Gp3To5Signature.Length;
        if (length < Gp3To5Signature.Length + 4 || length > 30 || data.Length < 31 + 16 ||
            data[versionAt] is < (byte)'3' or > (byte)'5' || data[versionAt + 1] != (byte)'.')
            throw new InvalidDataException("This .gp3-.gp5 file has a damaged header, so it was not opened.");
    }
}
