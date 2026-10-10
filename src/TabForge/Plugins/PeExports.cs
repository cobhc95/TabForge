using System.IO;
using System.Text;

namespace TabForge.Plugins;

/// <summary>
/// Reads a DLL's export names straight from the file (PE format) without loading it, to tell a VST2 plug-in
/// (exports "VSTPluginMain" or "main") from any other DLL. Bounded: small reads, capped counts, 64-bit only.
/// </summary>
public static class PeExports
{
    private const int MaxExports = 4096;

    public static bool IsVst2Plugin(string path)
    {
        try
        {
            var names = ExportNames(path);
            return names.Contains("VSTPluginMain") || names.Contains("main");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or EndOfStreamException) // Not logged: not a PE image with a VST entry point: expected for most DLLs
        {
            return false;
        }
    }

    /// <summary>Exported function names of a 64-bit DLL (empty for 32-bit or non-PE files).</summary>
    public static HashSet<string> ExportNames(string path)
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
        if (machine != 0x8664) return result; // x64 only: TabForge is 64-bit and cannot host 32-bit plug-ins
        var sections = reader.ReadUInt16();
        stream.Position += 12;
        var optionalSize = reader.ReadUInt16();
        stream.Position += 2;
        var optionalStart = stream.Position;
        if (reader.ReadUInt16() != 0x20B) return result; // PE32+
        stream.Position = optionalStart + 112; // data directories in PE32+
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
            foreach (var (va, size, raw) in table)
                if (rva >= va && rva < va + size) return raw + (rva - va);
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
