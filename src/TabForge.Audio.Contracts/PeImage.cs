namespace TabForge.Audio.Contracts;

// Owns: reading the headers of a Windows PE file that PeImports (import tables) and PeExports (export names) share: the DOS and PE signatures, the machine, the
//   optional header's magic, the image base, where the data directories start and the section table that turns an RVA into a file offset.
// Does not own: what the callers read from the directories (PeImports, PeExports) or any trust decision (PluginTrust).
// Tests: TestPeHeaderReaders.
/// <summary>The headers of one PE file; <see cref="Read"/> leaves the stream where the section table ends.</summary>
public sealed class PeImage
{
    private readonly List<(uint Va, uint Size, uint Raw)> _sections;

    private PeImage(ushort machine, ushort magic, ulong imageBase, long dataDirectories, List<(uint Va, uint Size, uint Raw)> sections)
    {
        Machine = machine;
        Magic = magic;
        ImageBase = imageBase;
        DataDirectories = dataDirectories;
        _sections = sections;
    }

    /// <summary>0x14C (x86) or 0x8664 (x64): the only machines read.</summary>
    public ushort Machine { get; }

    /// <summary>0x10B (PE32) or 0x20B (PE32+) for the image kinds read; the callers decide which they accept.</summary>
    public ushort Magic { get; }

    public bool Pe32Plus => Magic == 0x20B;

    public ulong ImageBase { get; }

    /// <summary>File offset of the first data directory entry (8 bytes each: RVA, size).</summary>
    public long DataDirectories { get; }

    /// <summary>
    /// Reads the headers; null when the file is not a PE of a known machine and optional-header kind. <paramref name="minLength"/> is the smallest file read and
    /// <paramref name="peOffsetMargin"/> the room the PE header must leave at the end of the file. Throws the reader's own exceptions on a truncated file.
    /// </summary>
    public static PeImage? Read(Stream stream, BinaryReader reader, int minLength, int peOffsetMargin)
    {
        if (stream.Length < minLength || reader.ReadUInt16() != 0x5A4D) return null;   // "MZ"
        stream.Seek(0x3C, SeekOrigin.Begin);
        var peOffset = reader.ReadInt32();
        if (peOffset <= 0 || peOffset > stream.Length - peOffsetMargin) return null;
        stream.Seek(peOffset, SeekOrigin.Begin);
        if (reader.ReadUInt32() != 0x00004550) return null;   // "PE\0\0"
        var machine = reader.ReadUInt16();
        if (machine is not (0x14C or 0x8664)) return null;
        var sections = reader.ReadUInt16();
        stream.Position += 12;
        var optionalSize = reader.ReadUInt16();
        stream.Position += 2;
        var optionalStart = stream.Position;
        var magic = reader.ReadUInt16();
        var pe32Plus = magic == 0x20B;
        if (!pe32Plus && magic != 0x10B) return null;

        stream.Seek(optionalStart + (pe32Plus ? 24 : 28), SeekOrigin.Begin);
        var imageBase = pe32Plus ? reader.ReadUInt64() : reader.ReadUInt32();
        var dataDirectories = optionalStart + (pe32Plus ? 112 : 96);
        stream.Seek(optionalStart + optionalSize, SeekOrigin.Begin);
        var table = new List<(uint Va, uint Size, uint Raw)>();
        for (var i = 0; i < Math.Min((int)sections, 96); i++)
        {
            stream.Position += 8;   // name
            var virtualSize = reader.ReadUInt32();
            var virtualAddress = reader.ReadUInt32();
            var rawSize = reader.ReadUInt32();
            var rawPointer = reader.ReadUInt32();
            stream.Position += 16;
            table.Add((virtualAddress, Math.Max(virtualSize, rawSize), rawPointer));
        }
        return new PeImage(machine, magic, imageBase, dataDirectories, table);
    }

    /// <summary>The file offset of <paramref name="rva"/>; throws when no section holds it.</summary>
    public long Offset(uint rva)
    {
        // Sums in long: a section that ends near the top of the 32-bit range must not wrap around to a small offset.
        foreach (var (va, size, raw) in _sections)
            if (rva >= va && rva < (long)va + size) return raw + (long)(rva - va);
        throw new InvalidDataException("RVA outside sections");
    }
}
