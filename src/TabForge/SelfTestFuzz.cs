using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using TabForge.Models;
using TabForge.Services;

namespace TabForge;

/// <summary>
/// Deterministic malformed-input fuzzing (area "fuzz"). For every file or clipboard format TabForge reads from outside, N inputs
/// (TABFORGE_FUZZ_COUNT, default 300) are mutated from valid seeds with a fixed seed (TABFORGE_FUZZ_SEED): byte flips, truncation,
/// length-field corruption, zip size lies and entry corruption, deep JSON nesting and huge counts. Each input must end in success or
/// in the format's controlled error (InvalidDataException), within 2 s and without allocating more than 256 MiB. Failing inputs are
/// written to TABFORGE_FUZZ_SAVE_DIR when it is set (the weekly CI run uploads that folder). The same seed gives the same inputs.
/// </summary>
public static partial class SelfTest
{
    private const string RequireFuzz = "fuzz";
    private static bool _fuzzRan;

    private const int FuzzDefaultCount = 300;
    private const int FuzzDefaultSeed = 20260930;
    private const double FuzzMaxSeconds = 2.0;
    private const long FuzzMaxAllocatedBytes = 256L * 1024 * 1024;
    private static readonly TimeSpan FuzzHangLimit = TimeSpan.FromSeconds(20);

    private static int FuzzEnvInt(string name, int fallback)
    {
        var text = Environment.GetEnvironmentVariable(name);
        return int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : fallback;
    }

    private static void TestMalformedInputFuzz()
    {
        var count = Math.Clamp(FuzzEnvInt("TABFORGE_FUZZ_COUNT", FuzzDefaultCount), 1, 1_000_000);
        var seed = FuzzEnvInt("TABFORGE_FUZZ_SEED", FuzzDefaultSeed);
        Log.Add($"  info  fuzz: {count} inputs per format, seed {seed} (TABFORGE_FUZZ_COUNT / TABFORGE_FUZZ_SEED; the same seed gives the same inputs)");
        var root = Path.Combine(Path.GetTempPath(), "tf-fuzz-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var project = BuildSyntheticGpSong();
            project.Tracks[0].AudioClips.Add(new AudioClip { File = @"C:\fuzz\take.wav", Name = "take", StartSec = 1, SourceLengthSec = 2, FileLengthSec = 4, GainDb = -3 });
            project.Tracks[0].Lanes.Add(new ClipLane());
            var tforgePath = Path.Combine(root, "seed.tforge");
            ProjectService.Save(tforgePath, project);
            var gz = File.ReadAllBytes(tforgePath);
            var plainJson = Gunzip(gz);

            // ---- .tforge: plain JSON and gzip ----
            var loadPath = Path.Combine(root, "case.tforge");
            void LoadTforge(byte[] bytes) { File.WriteAllBytes(loadPath, bytes); ProjectService.Load(loadPath); }
            FuzzGroup("tforge-plain", 1, count, seed, root, plainJson, LoadTforge,
                (r, s) => FuzzMutateJson(r, s), Controlled);
            FuzzGroup("tforge-gz", 2, count, seed, root, gz, LoadTforge,
                (r, s) => r.Next(2) == 0 ? FuzzMutateBinary(r, s, hotBytes: 32) : Gzip(FuzzMutateJson(r, plainJson)), Controlled);

            // ---- .gp: zip + the embedded TabForge project ----
            var gpEmbedded = GuitarProExporter.ToBytes(project, embedProject: true);
            var gpClean = GuitarProExporter.ToBytes(project, embedProject: false);
            var embeddedEntry = ReadZipEntry(gpEmbedded, GuitarProExporter.EmbeddedProjectEntry);
            FuzzGroup("gp-zip", 3, count, seed, root, gpEmbedded, bytes => GuitarProImporter.ImportBytes(bytes, "fuzz.gp"),
                (r, s) => r.Next(4) switch
                {
                    0 => FuzzMutateZip(r, s),
                    1 => FuzzMutateZip(r, gpClean),
                    2 when embeddedEntry is not null => ReplaceZipEntry(gpEmbedded, GuitarProExporter.EmbeddedProjectEntry,
                        r.Next(2) == 0 ? Gzip(FuzzMutateJson(r, Gunzip(embeddedEntry))) : FuzzMutateBinary(r, embeddedEntry, hotBytes: 32)),
                    _ => FuzzMutateBinary(r, s, hotBytes: 64),
                }, Controlled);

            // ---- .gpx (BCFZ): generated from the same song, since no writer exists ----
            var gpx = BuildGpxSeed(gpClean);
            if (Environment.GetEnvironmentVariable("TABFORGE_FUZZ_SAVE_DIR") is { Length: > 0 } seedDir) { Directory.CreateDirectory(seedDir); File.WriteAllBytes(Path.Combine(seedDir, "seed.gpx"), gpx); }
            FuzzGroup("gpx", 4, count, seed, root, gpx, bytes => GuitarProImporter.ImportBytes(bytes, "fuzz.gpx"),
                (r, s) => FuzzMutateBinary(r, s, hotBytes: 64), Controlled);

            // ---- GP3-5 binary (the shipped demo song is the seed) ----
            var gp5 = FuzzFindSample("TabForge Demo - Ashen Meridian.gp5");
            Check("fuzz: the GP3-5 seed (the shipped demo song) is present", gp5 is not null);
            if (gp5 is not null)
                FuzzGroup("gp3-5", 5, count, seed, root, gp5, bytes => GuitarProImporter.ImportBytes(bytes, "fuzz.gp5"),
                    (r, s) => FuzzMutateBinary(r, s, hotBytes: 1200), Controlled);

            // ---- .tfaudio (the sidecar applies best effort: it must never throw) ----
            var tfaudio = AudioDataFile.Serialize(project);
            var sidecarGp = Path.Combine(root, "sidecar.gp");
            File.WriteAllBytes(sidecarGp, new byte[] { 1 });
            var sidecarPath = AudioDataFile.PathFor(sidecarGp);
            FuzzGroup("tfaudio", 6, count, seed, root, tfaudio, bytes =>
            {
                File.WriteAllBytes(sidecarPath, bytes);
                var target = BuildSyntheticGpSong();
                AudioDataFile.TryApply(target, sidecarGp, new List<string>());
            }, (r, s) => FuzzMutateJson(r, s), _ => false);

            // ---- the ScoreClip clipboard JSON ----
            var clipSong = ClipSong();
            var barClip = ClipboardService.CaptureSelection(clipSong, 0, 0, 0, 0, 1, -1, "song-a").ToUtf8Json();
            var beatClip = ClipboardService.CaptureSelection(clipSong, 0, 0, 0, 8, 1, 3).ToUtf8Json();
            var legacyClip = Encoding.UTF8.GetBytes(ProjectService.Snapshot(project));
            FuzzGroup("scoreclip", 7, count, seed, root, barClip, bytes => ScoreClip.Parse(bytes),
                (r, s) => FuzzMutateJson(r, r.Next(3) switch { 0 => s, 1 => beatClip, _ => legacyClip }), Controlled);

            // MusicXML is written by TabForge, never read: there is no MusicXML importer to fuzz.
            _fuzzRan = true;
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static bool Controlled(Exception ex) => ex is InvalidDataException;

    private sealed class FuzzOutcome
    {
        public Exception? Error;
        public TimeSpan Elapsed;
        public long Allocated;
        public bool Hung;
    }

    private static FuzzOutcome FuzzRunCase(Action<byte[]> target, byte[] input)
    {
        var outcome = new FuzzOutcome();
        var task = Task.Factory.StartNew(() =>
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            var watch = Stopwatch.StartNew();
            try { target(input); }
            catch (Exception ex) { outcome.Error = ex; }
            outcome.Elapsed = watch.Elapsed;
            outcome.Allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        if (!task.Wait(FuzzHangLimit)) outcome.Hung = true;
        return outcome;
    }

    private static void FuzzGroup(string name, int groupIndex, int count, int seed, string root, byte[] validSeed, Action<byte[]> target,
        Func<Random, byte[], byte[]> mutate, Func<Exception, bool> isControlled)
    {
        // The valid seed must be accepted: otherwise the mutations would only ever test the first error path.
        var clean = FuzzRunCase(target, validSeed);
        Check($"fuzz [{name}]: the valid seed is accepted", clean.Error is null && !clean.Hung, clean.Error is null ? null : clean.Error.Message + " / " + clean.Error.GetBaseException().GetType().Name + ": " + Truncate(clean.Error.GetBaseException().Message, 200));

        int accepted = 0, rejected = 0, bad = 0, slow = 0, heavy = 0;
        double worstSeconds = 0; long worstAllocated = 0;
        var details = new List<string>();
        var saveDir = Environment.GetEnvironmentVariable("TABFORGE_FUZZ_SAVE_DIR");
        for (var i = 0; i < count; i++)
        {
            var rng = new Random(unchecked(seed * 1_000_003 + groupIndex * 10_007 + i));
            byte[] input;
            try { input = mutate(rng, validSeed); }
            catch (Exception ex)
            {
                bad++; if (details.Count < 4) details.Add($"#{i}: the mutator failed ({ex.GetType().Name})");
                continue;
            }
            var outcome = FuzzRunCase(target, input);
            worstSeconds = Math.Max(worstSeconds, outcome.Elapsed.TotalSeconds);
            worstAllocated = Math.Max(worstAllocated, outcome.Allocated);
            string? problem = null;
            if (outcome.Hung) problem = $"no result after {FuzzHangLimit.TotalSeconds:0} s (hang)";
            else if (outcome.Error is { } error && !isControlled(error)) problem = $"uncontrolled {error.GetType().Name}: {Truncate(error.Message, 90)}";
            else if (outcome.Elapsed.TotalSeconds > FuzzMaxSeconds) { slow++; problem = $"{outcome.Elapsed.TotalSeconds:0.0} s (limit {FuzzMaxSeconds:0.#} s)"; }
            else if (outcome.Allocated > FuzzMaxAllocatedBytes) { heavy++; problem = $"allocated {outcome.Allocated / (1024 * 1024)} MiB (limit {FuzzMaxAllocatedBytes / (1024 * 1024)} MiB)"; }
            if (problem is null)
            {
                if (outcome.Error is null) accepted++; else rejected++;
                continue;
            }
            bad++;
            if (details.Count < 4) details.Add($"#{i}: {problem}");
            if (!string.IsNullOrWhiteSpace(saveDir))
            {
                try
                {
                    Directory.CreateDirectory(saveDir);
                    File.WriteAllBytes(Path.Combine(saveDir, $"{name}-seed{seed}-case{i}.bin"), input);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        Log.Add($"  info  fuzz [{name}]: {count} inputs, {rejected} rejected with the controlled error, {accepted} still valid, worst {worstSeconds:0.00} s / {worstAllocated / (1024 * 1024)} MiB allocated");
        Check($"fuzz [{name}]: {count} mutated inputs (seed {seed}) end in success or the controlled error, each within {FuzzMaxSeconds:0.#} s and {FuzzMaxAllocatedBytes / (1024 * 1024)} MiB",
            bad == 0, $"{bad} failed (slow {slow}, heavy {heavy}); " + string.Join("; ", details));
    }

    private static string Truncate(string text, int length) => text.Length <= length ? text : text[..length];

    // ---------------------------------------------------------------- mutators

    private static readonly long[] FuzzInteresting =
    {
        0, 1, 2, 3, 0x7F, 0x80, 0xFF, 0x100, 0x7FFF, 0x8000, 0xFFFF, 0x10000, 0x7FFFFFFF, 0x80000000L, 0xFFFFFFFFL,
        0x08000000, 0x10000000, 0x20000000, 0x7FFFFFF0,
    };

    /// <summary>Flips, truncation, length-field corruption, chunk copies, zero fills and trailing junk. <paramref name="hotBytes"/>: the header region where length fields live.</summary>
    private static byte[] FuzzMutateBinary(Random r, byte[] source, int hotBytes)
    {
        var data = (byte[])source.Clone();
        if (data.Length == 0) return data;
        switch (r.Next(7))
        {
            case 0:
                for (var n = r.Next(1, 9); n > 0; n--) data[r.Next(data.Length)] ^= (byte)r.Next(1, 256);
                return data;
            case 1:
                return data.AsSpan(0, r.Next(0, data.Length)).ToArray();
            case 2:
            case 3:
                return FuzzCorruptLengthField(r, data, hotBytes);
            case 4:
            {
                var length = r.Next(1, Math.Min(64, data.Length) + 1);
                var from = r.Next(0, data.Length - length + 1);
                var to = r.Next(0, data.Length - length + 1);
                Array.Copy(data, from, data, to, length);
                return data;
            }
            case 5:
            {
                var length = r.Next(1, Math.Min(256, data.Length) + 1);
                Array.Clear(data, r.Next(0, data.Length - length + 1), length);
                return data;
            }
            default:
            {
                var junk = new byte[r.Next(1, 4096)];
                r.NextBytes(junk);
                return data.Concat(junk).ToArray();
            }
        }
    }

    private static byte[] FuzzCorruptLengthField(Random r, byte[] data, int hotBytes)
    {
        var width = r.Next(3) switch { 0 => 1, 1 => 2, _ => 4 };
        if (data.Length < width) return data;
        var region = r.Next(3) switch { 0 => Math.Min(data.Length, Math.Max(hotBytes, width)), 1 => data.Length, _ => Math.Min(data.Length, 64) };
        var offset = r.Next(0, region - width + 1);
        if (r.Next(6) == 0) offset = data.Length - width - r.Next(0, Math.Min(64, data.Length - width) + 1);   // the tail (zip directory, gzip size)
        var value = r.Next(4) == 0 ? r.NextInt64(0, 0x1_0000_0000L) : FuzzInteresting[r.Next(FuzzInteresting.Length)];
        if (r.Next(5) == 0) value = Math.Max(0, (long)data.Length + r.Next(-2, 3));
        var bigEndian = r.Next(4) == 0;
        for (var k = 0; k < width; k++)
        {
            var shift = bigEndian ? 8 * (width - 1 - k) : 8 * k;
            data[offset + k] = (byte)(value >> shift);
        }
        return data;
    }

    /// <summary>Zip-aware: lies in the local and central headers' sizes, entry counts and offsets, data corruption inside one entry, or a plain binary mutation.</summary>
    private static byte[] FuzzMutateZip(Random r, byte[] source)
    {
        var data = (byte[])source.Clone();
        var locals = FuzzSignatures(data, 0x04034b50);
        var centrals = FuzzSignatures(data, 0x02014b50);
        var eocd = FuzzSignatures(data, 0x06054b50);
        switch (r.Next(6))
        {
            case 0 when locals.Count > 0:
            {
                var at = locals[r.Next(locals.Count)];
                var field = r.Next(2) == 0 ? 18 : 22;   // compressed / uncompressed size
                if (at + field + 4 <= data.Length) BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at + field, 4), FuzzSize(r));
                return data;
            }
            case 1 when centrals.Count > 0:
            {
                var at = centrals[r.Next(centrals.Count)];
                var field = r.Next(3) switch { 0 => 20, 1 => 24, _ => 42 };   // compressed / uncompressed size / local header offset
                if (at + field + 4 <= data.Length) BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at + field, 4), FuzzSize(r));
                return data;
            }
            case 2 when eocd.Count > 0:
            {
                var at = eocd[^1];
                switch (r.Next(3))
                {
                    case 0: if (at + 12 <= data.Length) BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(at + 10, 2), (ushort)FuzzSize(r)); break;
                    case 1: if (at + 16 <= data.Length) BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at + 12, 4), FuzzSize(r)); break;
                    default: if (at + 20 <= data.Length) BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at + 16, 4), FuzzSize(r)); break;
                }
                return data;
            }
            case 3 when locals.Count > 0:
            {
                // Corrupt the packed bytes of one entry (between its local header and the next header).
                var index = r.Next(locals.Count);
                var start = locals[index] + 30;
                var end = index + 1 < locals.Count ? locals[index + 1] : (centrals.Count > 0 ? centrals[0] : data.Length);
                if (end - start > 4)
                    for (var n = r.Next(1, 6); n > 0; n--) data[r.Next(start, end)] ^= (byte)r.Next(1, 256);
                return data;
            }
            default:
                return FuzzMutateBinary(r, source, hotBytes: 64);
        }
    }

    private static uint FuzzSize(Random r) => r.Next(3) switch
    {
        0 => (uint)FuzzInteresting[r.Next(FuzzInteresting.Length)],
        1 => (uint)r.Next(0, 1 << 20),
        _ => (uint)r.NextInt64(0, 0x1_0000_0000L),
    };

    private static List<int> FuzzSignatures(byte[] data, uint signature)
    {
        var found = new List<int>();
        for (var i = 0; i + 4 <= data.Length; i++)
            if (BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(i, 4)) == signature) found.Add(i);
        return found;
    }

    private static readonly string[] FuzzNumbers = { "-1", "0", "2147483647", "2147483648", "4294967296", "99999999999999999999", "1e400", "-0", "0.5", "1E-400", "-2147483649" };

    /// <summary>JSON-aware: deep nesting, huge counts, extreme numbers, oversized strings, structural swaps, truncation and flips.</summary>
    private static byte[] FuzzMutateJson(Random r, byte[] source)
    {
        var text = source.ToList();
        if (text.Count == 0) return source;
        List<byte> Insert(int at, IEnumerable<byte> bytes) { text.InsertRange(Math.Clamp(at, 0, text.Count), bytes); return text; }
        int Find(Func<byte, bool> predicate)
        {
            var hits = new List<int>();
            for (var i = 0; i < text.Count; i++) if (predicate(text[i])) hits.Add(i);
            return hits.Count == 0 ? -1 : hits[r.Next(hits.Count)];
        }
        switch (r.Next(9))
        {
            case 0:
                for (var n = r.Next(1, 9); n > 0; n--) text[r.Next(text.Count)] ^= (byte)r.Next(1, 256);
                break;
            case 1:
                return source.AsSpan(0, r.Next(0, source.Length)).ToArray();
            case 2:
            {
                // Deep nesting around the depth limit and far beyond it, balanced or not.
                var depth = new[] { 60, 64, 65, 66, 100, 1000, 20_000 }[r.Next(7)];
                var at = Find(b => b == (byte)':');
                var open = Enumerable.Repeat((byte)'[', depth).ToArray();
                Insert(at + 1, open);
                if (r.Next(2) == 0) Insert(at + 1 + open.Length, Enumerable.Repeat((byte)']', depth));
                break;
            }
            case 3:
            {
                // Huge counts: a long run of empty objects / numbers where an array starts.
                var count = new[] { 100, 1_000, 10_000, 100_000 }[r.Next(4)];
                var at = Find(b => b == (byte)'[');
                var element = r.Next(2) == 0 ? "{}," : "0,";
                Insert(at + 1, Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat(element, count))));
                break;
            }
            case 4:
            {
                var at = Find(b => b is >= (byte)'0' and <= (byte)'9');
                if (at < 0) break;
                var end = at;
                while (end < text.Count && text[end] is >= (byte)'0' and <= (byte)'9') end++;
                text.RemoveRange(at, end - at);
                Insert(at, Encoding.ASCII.GetBytes(FuzzNumbers[r.Next(FuzzNumbers.Length)]));
                break;
            }
            case 5:
            {
                var at = Find(b => b == (byte)'"');
                Insert(at + 1, Enumerable.Repeat((byte)'A', new[] { 1_000, 100_000, 1_000_000 }[r.Next(3)]));
                break;
            }
            case 6:
            {
                var at = Find(b => b is (byte)'[' or (byte)'{' or (byte)']' or (byte)'}' or (byte)'"' or (byte)',' or (byte)':');
                if (at >= 0) text[at] = (byte)"[{]}\",:0n"[r.Next(9)];
                break;
            }
            case 7:
            {
                var from = r.Next(0, text.Count);
                text.RemoveRange(from, Math.Min(text.Count - from, r.Next(1, 200)));
                break;
            }
            default:
            {
                var from = r.Next(0, text.Count);
                var chunk = text.GetRange(from, Math.Min(text.Count - from, r.Next(1, 300)));
                Insert(r.Next(0, text.Count), chunk);
                break;
            }
        }
        return text.ToArray();
    }

    // ---------------------------------------------------------------- seeds and helpers

    private static byte[] Gzip(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true)) gzip.Write(data);
        return output.ToArray();
    }

    private static byte[] Gunzip(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }

    private static byte[]? ReadZipEntry(byte[] zipBytes, string name)
    {
        using var zip = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
        var entry = zip.Entries.FirstOrDefault(e => e.FullName == name);
        if (entry is null) return null;
        using var stream = entry.Open();
        using var output = new MemoryStream();
        stream.CopyTo(output);
        return output.ToArray();
    }

    private static byte[] ReplaceZipEntry(byte[] zipBytes, string name, byte[] content)
    {
        using var output = new MemoryStream();
        using (var source = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read))
        using (var target = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var entry in source.Entries)
            {
                var copy = target.CreateEntry(entry.FullName, CompressionLevel.Fastest);
                if (entry.FullName.EndsWith('/')) continue;
                using var to = copy.Open();
                if (entry.FullName == name) { to.Write(content); continue; }
                using var from = entry.Open();
                from.CopyTo(to);
            }
        return output.ToArray();
    }

    private static byte[]? FuzzFindSample(string fileName)
    {
        var beside = Path.Combine(AppContext.BaseDirectory, "Samples", fileName);
        if (File.Exists(beside)) return File.ReadAllBytes(beside);
        var root = FindRepositoryRoot();
        var inRepo = root is null ? null : Path.Combine(root, "samples", fileName);
        return inRepo is not null && File.Exists(inRepo) ? File.ReadAllBytes(inRepo) : null;
    }

    /// <summary>
    /// A valid Guitar Pro 6 file built from a .gp: its score XML inside a BCFS sector file system, packed as BCFZ with literal-only
    /// runs (the bit stream the reader expects: flag 0, a 2-bit count written low bit first, then the bytes).
    /// </summary>
    private static byte[] BuildGpxSeed(byte[] gp)
    {
        using var zip = new ZipArchive(new MemoryStream(gp), ZipArchiveMode.Read);
        var entry = zip.Entries.First(e => e.FullName.EndsWith("score.gpif", StringComparison.OrdinalIgnoreCase));
        byte[] gpif;
        using (var stream = entry.Open())
        using (var copy = new MemoryStream()) { stream.CopyTo(copy); gpif = copy.ToArray(); }

        const int sector = 0x1000;
        var dataSectors = (gpif.Length + sector - 1) / sector;
        var fs = new byte[4 + sector * (2 + dataSectors)];   // the reader counts sectors from after the 4-byte header
        Encoding.ASCII.GetBytes("BCFS").CopyTo(fs, 0);
        var at = 4 + sector;   // the file entry
        BinaryPrimitives.WriteInt32LittleEndian(fs.AsSpan(at, 4), 2);
        Encoding.ASCII.GetBytes("score.gpif").CopyTo(fs, at + 4);
        BinaryPrimitives.WriteInt32LittleEndian(fs.AsSpan(at + 0x8C, 4), gpif.Length);
        for (var i = 0; i < dataSectors; i++) BinaryPrimitives.WriteInt32LittleEndian(fs.AsSpan(at + 0x94 + 4 * i, 4), 2 + i);
        gpif.CopyTo(fs, 4 + sector * 2);

        var bits = new List<byte>();
        var current = 0; var used = 0;
        void Bit(int bit)
        {
            current = (current << 1) | bit; used++;
            if (used == 8) { bits.Add((byte)current); current = 0; used = 0; }
        }
        for (var i = 0; i < fs.Length; i += 3)
        {
            var run = Math.Min(3, fs.Length - i);
            Bit(0);
            Bit(run & 1); Bit((run >> 1) & 1);
            for (var k = 0; k < run; k++)
                for (var b = 7; b >= 0; b--) Bit((fs[i + k] >> b) & 1);
        }
        while (used != 0) Bit(0);

        var result = new byte[8 + bits.Count];
        Encoding.ASCII.GetBytes("BCFZ").CopyTo(result, 0);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(4, 4), fs.Length);
        bits.CopyTo(result, 8);
        return result;
    }
}
