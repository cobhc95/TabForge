using System.Security.Cryptography;
using System.Text;

namespace TabForge.Audio.Contracts;

// Owns: the local DLL dependency closure of a Windows PE plug-in, read from its import/delay-import tables without loading it,
//   and one stable hash over the local dependency files. Does not own: the trust decision (PluginTrust) or the load (PluginIdentity).
// Tests: PluginDependencyTests.
/// <summary>
/// Reads a PE file's import and delay-import tables and returns the DLLs that would be resolved from the plug-in's own folder
/// (the folder is what <c>LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR</c> searches first). The closure is recursive, capped and bounded.
/// A dependency that is not in the folder is a system DLL and is not part of the approval; a file planted under a system name
/// in the folder is, because Windows would load the planted file first.
/// </summary>
public static class PeImports
{
    /// <summary>Most local dependency files one approval may cover; a folder beyond this is refused as unreadable.</summary>
    public const int MaxDependencies = 256;
    private const int MaxDepth = 8;
    private const int MaxNameBytes = 256;

    /// <summary>Absolute paths of the DLLs imported from the plug-in's own folder, recursively, sorted by path.</summary>
    public static IReadOnlyList<string> LocalDependencies(string binaryPath)
    {
        try
        {
            var full = Path.GetFullPath(binaryPath);
            var root = Path.GetDirectoryName(full);
            if (string.IsNullOrEmpty(root) || !File.Exists(full)) return Array.Empty<string>();
            var inFolder = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.EnumerateFiles(root))
                inFolder[Path.GetFileName(file)] = file;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<string>();
            void Visit(string binary, int depth)
            {
                if (depth > MaxDepth || result.Count >= MaxDependencies) return;
                foreach (var name in ImportedDllNames(binary))
                {
                    if (result.Count >= MaxDependencies) return;
                    if (!inFolder.TryGetValue(Path.GetFileName(name), out var dependency)) continue;
                    if (!seen.Add(dependency)) continue;
                    result.Add(dependency);
                    Visit(dependency, depth + 1);
                }
            }
            Visit(full, 0);
            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidDataException or EndOfStreamException)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// One SHA-256 over the local dependency closure: each relative path (lowercase, forward slashes) and the file's SHA-256,
    /// sorted. It is non-empty even when the closure is empty, so an empty record always means "not recorded" (legacy approval).
    /// </summary>
    public static string DependencyHash(string binaryPath)
    {
        var full = Path.GetFullPath(binaryPath);
        var root = Path.GetDirectoryName(full) ?? "";
        var entries = new List<string>();
        foreach (var dependency in LocalDependencies(full))
        {
            using var stream = new FileStream(dependency, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                1 << 16, FileOptions.SequentialScan);
            var sha = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            var relative = Path.GetRelativePath(root, dependency).Replace('\\', '/').ToLowerInvariant();
            entries.Add(relative + "\n" + sha + "\n");
        }
        entries.Sort(StringComparer.Ordinal);
        var text = "tf-deps-v1\n" + entries.Count + "\n" + string.Concat(entries);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    /// <summary>Import and delay-import DLL names from one PE file; empty for a non-PE or unreadable file.</summary>
    private static List<string> ImportedDllNames(string path)
    {
        var names = new List<string>();
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                4096, FileOptions.RandomAccess);
            using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
            if (stream.Length < 64 || reader.ReadUInt16() != 0x5A4D) return names;   // "MZ"
            stream.Seek(0x3C, SeekOrigin.Begin);
            var peOffset = reader.ReadInt32();
            if (peOffset <= 0 || peOffset > stream.Length - 24) return names;
            stream.Seek(peOffset, SeekOrigin.Begin);
            if (reader.ReadUInt32() != 0x00004550) return names;   // "PE\0\0"
            var machine = reader.ReadUInt16();
            if (machine is not (0x14C or 0x8664)) return names;
            var sections = reader.ReadUInt16();
            stream.Position += 12;
            var optionalSize = reader.ReadUInt16();
            stream.Position += 2;
            var optionalStart = stream.Position;
            var magic = reader.ReadUInt16();
            var pe32Plus = magic == 0x20B;
            if (!pe32Plus && magic != 0x10B) return names;

            stream.Seek(optionalStart + (pe32Plus ? 24 : 28), SeekOrigin.Begin);
            var imageBase = pe32Plus ? reader.ReadUInt64() : reader.ReadUInt32();
            var dataDirectories = optionalStart + (pe32Plus ? 112 : 96);
            stream.Seek(optionalStart + optionalSize, SeekOrigin.Begin);
            var table = new List<(uint Va, uint Size, uint Raw)>();
            for (var i = 0; i < Math.Min((int)sections, 96); i++)
            {
                stream.Position += 8;
                var virtualSize = reader.ReadUInt32();
                var virtualAddress = reader.ReadUInt32();
                var rawSize = reader.ReadUInt32();
                var rawPointer = reader.ReadUInt32();
                stream.Position += 16;
                table.Add((virtualAddress, Math.Max(virtualSize, rawSize), rawPointer));
            }

            long Offset(uint rva)
            {
                foreach (var (va, size, raw) in table)
                    if (rva >= va && rva < (long)va + size) return raw + (long)(rva - va);
                throw new InvalidDataException("RVA outside sections");
            }

            string? ReadName(uint rva)
            {
                if (rva == 0) return null;
                stream.Seek(Offset(rva), SeekOrigin.Begin);
                var buffer = new byte[MaxNameBytes];
                var read = stream.Read(buffer, 0, buffer.Length);
                var end = Array.IndexOf(buffer, (byte)0, 0, read);
                if (end <= 0) return null;
                return Encoding.ASCII.GetString(buffer, 0, end);
            }

            void ReadDirectory(int index, int descriptorSize, bool delay)
            {
                stream.Seek(dataDirectories + index * 8, SeekOrigin.Begin);
                var directoryRva = reader.ReadUInt32();
                var directorySize = reader.ReadUInt32();
                if (directoryRva == 0 || directorySize == 0) return;
                var directoryOffset = Offset(directoryRva);
                var count = Math.Min(MaxDependencies, (int)Math.Min(int.MaxValue, directorySize / (uint)descriptorSize));
                for (var i = 0; i < count; i++)
                {
                    var start = directoryOffset + (long)i * descriptorSize;
                    if (start < 0 || start + descriptorSize > stream.Length) break;
                    stream.Seek(start, SeekOrigin.Begin);
                    uint nameField;
                    if (delay)
                    {
                        var attributes = reader.ReadUInt32();
                        nameField = reader.ReadUInt32();
                        if (attributes == 0 && nameField == 0) break;
                        if ((attributes & 1) == 0)   // old-style delay descriptor: Name is a VA, not an RVA
                        {
                            if (nameField < imageBase) continue;
                            nameField = (uint)(nameField - imageBase);
                        }
                    }
                    else
                    {
                        var originalFirstThunk = reader.ReadUInt32();
                        reader.ReadUInt32();   // TimeDateStamp
                        reader.ReadUInt32();   // ForwarderChain
                        nameField = reader.ReadUInt32();
                        var firstThunk = reader.ReadUInt32();
                        if (originalFirstThunk == 0 && nameField == 0 && firstThunk == 0) break;
                    }
                    var name = ReadName(nameField);
                    if (!string.IsNullOrEmpty(name)) names.Add(name);
                }
            }

            ReadDirectory(1, 20, delay: false);    // import table
            ReadDirectory(13, 32, delay: true);    // delay-import table
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidDataException or EndOfStreamException)
        {
            // Not a readable PE: no local dependency closure. The main binary's hash still gates the load.
        }
        return names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
