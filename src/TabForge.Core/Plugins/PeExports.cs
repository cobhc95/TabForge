using System.IO;
using System.Text;

namespace TabForge.Plugins;

// Owns: DLL export-name reading from the PE file (VST2 detect, bounded, never loads).
// Does not own: plug-in loading or trust (PluginTrust).
// Tests: plug-in scan tests.

/// <summary>
/// Reads a DLL's export names straight from the file (PE format) without loading it, to tell a VST2 plug-in
/// (exports "VSTPluginMain" or "main") from any other DLL. Bounded: small reads, capped counts; hosts 64-bit only, and names the 32-bit ones.
/// </summary>
public static class PeExports
{
    private const int MaxExports = 4096;

    public static bool IsVst2Plugin(string path) => HasVst2Entry(path, x86: false);

    /// <summary>True for a 32-bit DLL that exports the VST2 entry point: a plug-in TabForge cannot host (it is 64-bit).</summary>
    public static bool IsVst2Plugin32(string path) => HasVst2Entry(path, x86: true);

    private static bool HasVst2Entry(string path, bool x86)
    {
        try
        {
            var names = ReadExportNames(path, x86);
            return names.Contains("VSTPluginMain") || names.Contains("main");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or EndOfStreamException) // Not logged: not a PE image with a VST entry point: expected for most DLLs
        {
            return false;
        }
    }

    private static HashSet<string> ReadExportNames(string path, bool x86)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096);
        if (stream.Length < 512) return result;
        using var reader = new BinaryReader(stream);
        if (reader.ReadUInt16() != 0x5A4D) return result; // "MZ"
        stream.Position = 0x3C;
        var peOffset = reader.ReadInt32();
        if (peOffset <= 0 || peOffset > stream.Length - 256) return result;
        stream.Position = peOffset;
        if (reader.ReadUInt32() != 0x00004550) return result; // "PE\0\0"
        var machine = reader.ReadUInt16();
        if (machine != (x86 ? 0x14C : 0x8664)) return result; // only the bitness asked for: TabForge is 64-bit and cannot host 32-bit plug-ins
        var sections = reader.ReadUInt16();
        stream.Position += 12;
        var optionalSize = reader.ReadUInt16();
        stream.Position += 2;
        var optionalStart = stream.Position;
        if (reader.ReadUInt16() != (x86 ? 0x10B : 0x20B)) return result; // PE32 / PE32+
        stream.Position = optionalStart + (x86 ? 96 : 112); // data directories
        var exportRva = reader.ReadUInt32();
        var exportSize = reader.ReadUInt32();
        if (exportRva == 0 || exportSize == 0) return result;

        stream.Position = optionalStart + optionalSize;
        var table = new List<(uint va, uint size, uint raw)>();
        for (var i = 0; i < Math.Min((int)sections, 96); i++)
        {
            stream.Position += 8; // name
            var virtualSize = reader.ReadUInt32();
            var virtualAddress = reader.ReadUInt32();
            var rawSize = reader.ReadUInt32();
            var rawPointer = reader.ReadUInt32();
            stream.Position += 16;
            table.Add((virtualAddress, Math.Max(virtualSize, rawSize), rawPointer));
        }
        long Offset(uint rva)
        {
            // Sums in long: a section that ends near the top of the 32-bit range must not wrap around to a small offset.
            foreach (var (va, size, raw) in table)
                if (rva >= va && rva < (long)va + size) return raw + (long)(rva - va);
            throw new InvalidDataException("RVA outside sections");
        }

        stream.Position = Offset(exportRva) + 24;
        var nameCount = Math.Min(reader.ReadUInt32(), MaxExports);
        stream.Position += 4; // address of functions
        var namesRva = reader.ReadUInt32();
        var nameOffsets = new uint[nameCount];
        stream.Position = Offset(namesRva);
        for (var i = 0; i < nameCount; i++) nameOffsets[i] = reader.ReadUInt32();
        var buffer = new byte[64];
        foreach (var rva in nameOffsets)
        {
            stream.Position = Offset(rva);
            var read = stream.Read(buffer, 0, buffer.Length);
            var end = Array.IndexOf(buffer, (byte)0, 0, read);
            if (end > 0) result.Add(Encoding.ASCII.GetString(buffer, 0, end));
        }
        return result;
    }
}
