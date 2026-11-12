using System.IO;
using System.Text;
using TabForge.Audio.Contracts;

namespace TabForge.Plugins;

// Owns: DLL export-name reading from the PE file (VST2 detect, bounded, never loads).
// Does not own: the PE headers and section table (PeImage in Contracts), plug-in loading or trust (PluginTrust).
// Tests: TestPeHeaderReaders.

/// <summary>
/// Reads a DLL's export names straight from the file (PE format) without loading it, to tell a VST2 plug-in
/// (exports "VSTPluginMain" or "main") from any other DLL. Bounded: small reads, capped counts; it also names the 32-bit ones.
/// </summary>
public static class PeExports
{
    private const int MaxExports = 4096;

    public static bool IsVst2Plugin(string path) => HasVst2Entry(path, x86: false);

    /// <summary>True for a 64-bit DLL that exports the VST2 entry point: native on Windows, the 64-bit helper under Wine.</summary>
    public static bool IsVst2Plugin64(string path) => HasVst2Entry(path, x86: false);

    /// <summary>True for a 32-bit DLL that exports the VST2 entry point: listed and hosted through the 32-bit helper.</summary>
    public static bool IsVst2Plugin32(string path) => HasVst2Entry(path, x86: true);

    /// <summary>True for a 64-bit Windows VST3 binary (exports GetPluginFactory): a bundle binary or a flat .vst3 file.</summary>
    public static bool IsVst3Plugin64(string path) => HasVst3Entry(path, x86: false);

    /// <summary>True for a 32-bit Windows VST3 binary (exports GetPluginFactory).</summary>
    public static bool IsVst3Plugin32(string path) => HasVst3Entry(path, x86: true);

    private static bool HasVst3Entry(string path, bool x86)
    {
        try { return ReadExportNames(path, x86).Contains("GetPluginFactory"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or EndOfStreamException) { return false; }
    }

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
        using var reader = new BinaryReader(stream);
        if (PeImage.Read(stream, reader, minLength: 512, peOffsetMargin: 256) is not { } image) return result;
        // Only the bitness asked for: 32-bit uses the 32-bit helper, 64-bit is native or the Wine helper (PE32 / PE32+).
        if (image.Machine != (x86 ? 0x14C : 0x8664) || image.Magic != (x86 ? 0x10B : 0x20B)) return result;
        stream.Position = image.DataDirectories;
        var exportRva = reader.ReadUInt32();
        var exportSize = reader.ReadUInt32();
        if (exportRva == 0 || exportSize == 0) return result;

        stream.Position = image.Offset(exportRva) + 24;
        var nameCount = Math.Min(reader.ReadUInt32(), MaxExports);
        stream.Position += 4; // address of functions
        var namesRva = reader.ReadUInt32();
        var nameOffsets = new uint[nameCount];
        stream.Position = image.Offset(namesRva);
        for (var i = 0; i < nameCount; i++) nameOffsets[i] = reader.ReadUInt32();
        var buffer = new byte[64];
        foreach (var rva in nameOffsets)
        {
            stream.Position = image.Offset(rva);
            var read = stream.Read(buffer, 0, buffer.Length);
            var end = Array.IndexOf(buffer, (byte)0, 0, read);
            if (end > 0) result.Add(Encoding.ASCII.GetString(buffer, 0, end));
        }
        return result;
    }
}
