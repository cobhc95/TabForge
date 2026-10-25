using System.IO;
using System.IO.Compression;
using System.Xml;

namespace TabForge.Services;

// Owns: turning a MusicXML file's bytes (.musicxml / .xml / compressed .mxl) into the score XML alphaTab reads, and refusing anything else.
// Does not own: parsing the score (alphaTab) or the conversion (GuitarProImporter, the same one Guitar Pro uses).
// Tests: MusicXmlImportTests.
internal static class MusicXmlInput
{
    private const string Invalid = "This score file is invalid, truncated, or unsupported.";
    /// <summary>One unpacked MusicXML document (the score or container.xml); well above any real score.</summary>
    internal const long MaxXmlBytes = 64L * 1024 * 1024;
    /// <summary>Unpacked bytes allowed per byte of the real container: a bomb inflates far past this, real XML (about 10-50x) does not.</summary>
    internal const int MaxRatio = 200;

    /// <summary>The score XML of a MusicXML file; throws <see cref="InvalidDataException"/> when it is not a partwise/timewise score. A .mxl is held to the Guitar Pro zip limits here (entry count, zip64, running unpacked caps) plus a compression-ratio cap.</summary>
    public static byte[] Prepare(byte[] raw)
    {
        var xml = GuitarProPreParse.HasZipSignature(raw) ? FromContainer(raw) : raw;
        if (xml.LongLength > MaxXmlBytes) throw new InvalidDataException(Invalid);
        RequireScoreRoot(xml);
        return xml;
    }

    private static byte[] FromContainer(byte[] raw)
    {
        try
        {
            GuitarProPreParse.ValidateZipDirectory(raw);
            using var zip = new ZipArchive(new MemoryStream(raw, writable: false), ZipArchiveMode.Read);
            if (zip.Entries.Count > GuitarProPreParse.MaxZipEntries) throw new InvalidDataException(Invalid);
            var budget = new Budget(Math.Min(GuitarProPreParse.MaxZipTotalBytes, raw.LongLength * MaxRatio + 1024 * 1024));
            var name = RootFile(zip, budget) ?? zip.Entries.Where(e => !e.FullName.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase) && e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                .Select(e => e.FullName).FirstOrDefault() ?? throw new InvalidDataException(Invalid);
            if (!FilePathPolicy.IsSafeArchiveEntryName(name)) throw new InvalidDataException(Invalid);
            return ReadEntry(zip.GetEntry(name) ?? throw new InvalidDataException(Invalid), budget);
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException or XmlException or InvalidDataException)
        {
            // Whatever the cause, the user sees the one standard message; the specific error stays attached for the log.
            throw ex is InvalidDataException { Message: Invalid } ? ex : new InvalidDataException(Invalid, ex);
        }
    }

    /// <summary>The rootfile that META-INF/container.xml names, or null.</summary>
    private static string? RootFile(ZipArchive zip, Budget budget)
    {
        if (zip.GetEntry("META-INF/container.xml") is not { } container) return null;
        using var reader = XmlReader.Create(new MemoryStream(ReadEntry(container, budget)), Settings);
        while (reader.Read())
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "rootfile" && reader.GetAttribute("full-path") is { Length: > 0 } path)
                return path;
        return null;
    }

    /// <summary>Unpacked bytes still allowed across the whole container.</summary>
    private sealed class Budget(long remaining) { public long Remaining = remaining; }

    /// <summary>Inflates one entry in chunks under a running cap; the declared entry length is never trusted.</summary>
    private static byte[] ReadEntry(ZipArchiveEntry entry, Budget budget)
    {
        using var stream = entry.Open();
        using var output = new MemoryStream();
        var chunk = new byte[64 * 1024];
        int read;
        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (output.Length + read > MaxXmlBytes || read > budget.Remaining) throw new InvalidDataException(Invalid);
            budget.Remaining -= read;
            output.Write(chunk, 0, read);
        }
        return output.ToArray();
    }

    private static void RequireScoreRoot(byte[] xml)
    {
        try
        {
            using var reader = XmlReader.Create(new MemoryStream(xml, writable: false), Settings);
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element) continue;
                if (reader.LocalName is "score-partwise" or "score-timewise") return;
                break;
            }
        }
        catch (XmlException ex)
        {
            // Not XML: the caller reports the refusal; the parse error stays attached for the log.
            throw new InvalidDataException(Invalid, ex);
        }
        throw new InvalidDataException(Invalid);
    }

    // MusicXML files carry a DOCTYPE naming an external DTD: ignored, never fetched.
    private static readonly XmlReaderSettings Settings = new() { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, MaxCharactersFromEntities = 0, MaxCharactersInDocument = MaxXmlBytes };
}
