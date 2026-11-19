using System.IO;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace TabForge.Features.TuxGuitar;

/// <summary>Reads the bounded XML payload from a TuxGuitar archive.</summary>
internal static class TuxGuitarContainer
{
    internal const string Invalid = "This score file is invalid, truncated, or unsupported.";
    private const long MaxXmlBytes = 64L * 1024 * 1024;
    private const int MaxZipEntries = 1024;

    internal static XDocument Read(byte[] raw, Services.ImportContext context)
    {
        var xmlBytes = Unpack(raw, context);
        context.Check();
        XDocument doc;
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersFromEntities = 0,
                MaxCharactersInDocument = MaxXmlBytes,
            };
            using var reader = XmlReader.Create(new MemoryStream(xmlBytes, writable: false), settings);
            doc = XDocument.Load(reader);
        }
        catch (Exception ex) when (ex is XmlException or InvalidOperationException or ArgumentException or IOException)
        {
            throw new InvalidDataException(Invalid, ex);
        }
        return doc;
    }

    private static byte[] Unpack(byte[] raw, Services.ImportContext context)
    {
        if (raw.Length < 4 || raw[0] != (byte)'P' || raw[1] != (byte)'K' || raw[2] != 3 || raw[3] != 4)
            throw new InvalidDataException(Invalid);
        try
        {
            var eocd = -1;
            for (var i = raw.Length - 22; i >= Math.Max(0, raw.Length - 22 - 65_535); i--)
            {
                if (System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(i, 4)) == 0x06054b50) { eocd = i; break; }
            }
            if (eocd < 0) throw new InvalidDataException(Invalid);
            var entries = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(eocd + 10, 2));
            if (entries > MaxZipEntries)
                throw new InvalidDataException(Invalid);
            using var zip = new ZipArchive(new MemoryStream(raw, writable: false), ZipArchiveMode.Read);
            if (zip.Entries.Count > MaxZipEntries) throw new InvalidDataException(Invalid);
            ZipArchiveEntry? content = null;
            foreach (var entry in zip.Entries)
            {
                context.Check();
                if (!Services.FilePathPolicy.IsSafeArchiveEntryName(entry.FullName)) continue;
                if (entry.FullName.Equals("content.xml", StringComparison.OrdinalIgnoreCase)) content = entry;
            }
            if (content is null) throw new InvalidDataException(Invalid);
            if (content.Length > MaxXmlBytes) throw new InvalidDataException(Invalid);
            using var stream = content.Open();
            using var output = new MemoryStream();
            var chunk = new byte[64 * 1024];
            int read;
            while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
            {
                if (output.Length + read > MaxXmlBytes) throw new InvalidDataException(Invalid);
                output.Write(chunk, 0, read);
            }
            return output.ToArray();
        }
        catch (InvalidDataException) { throw; }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException or InvalidOperationException)
        {
            throw new InvalidDataException(Invalid, ex);
        }
    }
}
