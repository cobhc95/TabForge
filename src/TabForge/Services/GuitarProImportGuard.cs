using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;

namespace TabForge.Services;

/// <summary>
/// Cooperative limits for one Guitar Pro import (audit A5-07): cancellation, a wall-clock time budget and a managed-memory
/// watermark, checked between import stages and per converted bar. The guard is ambient on the importing thread
/// (<see cref="Enter"/>), so the synchronous importer needs no extra parameters and headless tools run without one.
/// </summary>
/// <remarks>
/// Residual limit: alphaTab's parse is a single call that cannot be interrupted. A file that makes it run long is only
/// noticed when it returns; the UI side (ScoreImportQueue) stops waiting at the time budget or on
/// Cancel and abandons that worker. A separate import process with OS-enforced time and memory limits is the future option.
/// </remarks>
public sealed class ImportGuard
{
    public static readonly TimeSpan DefaultTimeBudget = TimeSpan.FromSeconds(60);
    /// <summary>Growth of the managed heap during one import that aborts it (ordinary songs use a few tens of MiB).</summary>
    public const long DefaultMemoryBudgetBytes = 2L * 1024 * 1024 * 1024;

    [ThreadStatic] private static ImportGuard? _current;
    /// <summary>The guard of the import running on this thread, or null (synchronous headless use: no budget checks).</summary>
    public static ImportGuard? Current => _current;

    private readonly CancellationToken _token;
    private readonly long _startTimestamp = Stopwatch.GetTimestamp();
    private readonly long _deadlineTimestamp;
    private readonly long _heapLimit;

    public TimeSpan TimeBudget { get; }
    public long MemoryBudgetBytes { get; }

    public ImportGuard(CancellationToken token, TimeSpan? timeBudget = null, long? memoryBudgetBytes = null)
    {
        _token = token;
        TimeBudget = timeBudget ?? DefaultTimeBudget;
        MemoryBudgetBytes = memoryBudgetBytes ?? DefaultMemoryBudgetBytes;
        _deadlineTimestamp = _startTimestamp + (long)(TimeBudget.TotalSeconds * Stopwatch.Frequency);
        _heapLimit = GC.GetTotalMemory(false) + MemoryBudgetBytes;
    }

    /// <summary>Makes this guard ambient on the current thread until the returned scope is disposed.</summary>
    public IDisposable Enter()
    {
        var previous = _current;
        _current = this;
        return new Scope(previous);
    }

    /// <summary>Throws when the import was cancelled (<see cref="OperationCanceledException"/>) or ran over its time or memory budget.</summary>
    public void Check()
    {
        _token.ThrowIfCancellationRequested();
        if (Stopwatch.GetTimestamp() > _deadlineTimestamp)
            throw new InvalidDataException($"Importing this Guitar Pro file took longer than {TimeBudget.TotalSeconds:0} seconds, so it was stopped.");
        if (GC.GetTotalMemory(false) > _heapLimit)
            throw new InvalidDataException("Importing this Guitar Pro file needed too much memory, so it was stopped.");
    }

    /// <summary>Checks the ambient guard, if any (cheap: a token read, a timestamp and a heap counter).</summary>
    public static void CheckCurrent() => _current?.Check();

    private sealed class Scope : IDisposable
    {
        private readonly ImportGuard? _previous;
        private bool _disposed;
        public Scope(ImportGuard? previous) => _previous = previous;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _current = _previous;
        }
    }
}

/// <summary>
/// Checks run on the raw bytes before alphaTab parses them (audit A5-07): the container of a GP7/8 .gp (zip) is inflated with
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
    public static void Validate(byte[] data)
    {
        if (data.Length >= 4 && data[0] == (byte)'P' && data[1] == (byte)'K' && data[2] == 3 && data[3] == 4) { ValidateZip(data); return; }
        if (data.Length >= 4 && data[0] == (byte)'B' && data[1] == (byte)'C' && data[2] == (byte)'F' && data[3] == (byte)'Z') { ValidateGpx(data); return; }
        ValidateGp3To5Header(data);
    }

    private static void ValidateZip(byte[] data)
    {
        // The end-of-central-directory record gives the entry count without building any entry objects.
        var eocd = -1;
        for (var i = data.Length - 22; i >= Math.Max(0, data.Length - 22 - 65_535); i--)
            if (BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(i, 4)) == 0x06054b50) { eocd = i; break; }
        if (eocd < 0) throw new InvalidDataException("This Guitar Pro file is a damaged archive (no directory).");
        var entries = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(eocd + 10, 2));
        var directorySize = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(eocd + 12, 4));
        if (entries == 0xFFFF || directorySize == 0xFFFFFFFF)
            throw new InvalidDataException("This Guitar Pro file uses an archive format (zip64) Guitar Pro files never need.");
        if (entries > MaxZipEntries)
            throw new InvalidDataException($"This Guitar Pro archive holds {entries:N0} files (the limit is {MaxZipEntries:N0}).");

        // Inflate every entry into a counting sink: declared sizes can lie, the caps hold for what really unpacks.
        long total = 0;
        var chunk = new byte[64 * 1024];
        try
        {
            using var zip = new ZipArchive(new MemoryStream(data, writable: false), ZipArchiveMode.Read);
            if (zip.Entries.Count > MaxZipEntries)
                throw new InvalidDataException($"This Guitar Pro archive holds too many files (the limit is {MaxZipEntries:N0}).");
            foreach (var entry in zip.Entries)
            {
                ImportGuard.CheckCurrent();
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
            throw new InvalidDataException("This Guitar Pro file is a damaged archive.", ex);
        }

        static InvalidDataException TooLarge() => new(
            $"This Guitar Pro archive unpacks to more than the {MaxZipEntryBytes / (1024 * 1024)} MiB per file / {MaxZipTotalBytes / (1024 * 1024)} MiB total limit, so it was not opened.");
    }

    private static void ValidateGpx(byte[] data)
    {
        if (data.Length < 8) throw new InvalidDataException("This Guitar Pro 6 (.gpx) file is truncated.");
        var declared = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4, 4));
        if (declared < 0 || declared > MaxGpxUnpackedBytes)
            throw new InvalidDataException($"This Guitar Pro 6 (.gpx) file declares an unpacked size above the {MaxGpxUnpackedBytes / (1024 * 1024)} MiB limit, so it was not opened.");
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
            throw new InvalidDataException("This Guitar Pro 3-5 file has a damaged header, so it was not opened.");
    }
}
