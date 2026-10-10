using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml;
using TabForge.Models;

namespace TabForge.Services;

// Owns: the binding that ties the project embedded in a .gp file to the score bytes it was saved with, and its staleness
//     check.
// Does not own: the rest of the project file (ProjectService).
// Tests: TestProjectRoundtrip.
/// <summary>
/// Metadata safety. A score file written by TabForge carries its whole project in one extra zip entry. The score format drops entries it does not know,
/// but another program may keep them while editing the score: the embedded project would then be older than the score and, because it wins on
/// open, would silently replace the newer music. The binding is a small record next to the project: the SHA-256 of the score part
/// (Content/score.gpif) and of the project bytes it was saved with.
/// <para>Conflict rule (never guess which copy is newer): the Guitar Pro score is the content the file shows to every other program, so when the
/// score no longer matches the record the embedded project is NOT used and the file opens as plain Guitar Pro with a notice. Nothing is
/// merged. A file without a record (saved by an earlier build) cannot be proven fresh; it is accepted as before unless its bar count
/// differs from the score's, which never happens in a file TabForge wrote.</para>
/// </summary>
internal static class EmbeddedBinding
{
    internal const int Version = 1;
    internal static readonly string StaleReason = "the score was changed in another program after TabForge saved this file";
    private const long MaxScoreBytes = 256L * 1024 * 1024;

    private sealed class Record
    {
        public int Version { get; set; }
        public string ScoreSha256 { get; set; } = "";
        public string ProjectSha256 { get; set; } = "";
    }

    internal static byte[] Serialize(byte[] scoreBytes, byte[] projectBytes) =>
        JsonSerializer.SerializeToUtf8Bytes(new Record { Version = Version, ScoreSha256 = Convert.ToHexString(SHA256.HashData(scoreBytes)), ProjectSha256 = Convert.ToHexString(SHA256.HashData(projectBytes)) });

    /// <summary>Null when the project may be used (record matches, or there is no record); otherwise the reason it must not be.</summary>
    internal static string? Check(ZipArchive zip, byte[] projectBytes)
    {
        ZipArchiveEntry? entry;
        try { entry = zip.GetEntry(GuitarProExporter.EmbeddedBindingEntry); }
        catch (InvalidDataException ex) { Services.Trace.Error(Services.Trace.Import, "embedded binding: read: " + ex.Message); return "the data is damaged or invalid"; }
        if (entry is null) return null;
        Record? record;
        try
        {
            if (entry.Length > 4096) return "the data is damaged or invalid";
            using var stream = entry.Open();
            record = JsonSerializer.Deserialize<Record>(stream, new JsonSerializerOptions { MaxDepth = 4 });
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException) { return "the data is damaged or invalid"; } // Not logged: damaged binding: the reason is returned to the caller
        if (record is null || record.Version != Version) return "it was saved by a newer or unknown version of TabForge";
        if (!string.Equals(record.ProjectSha256, Convert.ToHexString(SHA256.HashData(projectBytes)), StringComparison.OrdinalIgnoreCase)) return "the data is damaged or invalid";
        var score = FindScore(zip);
        if (score is null) return StaleReason;
        try
        {
            if (score.Length > MaxScoreBytes) return StaleReason;
            using var stream = score.Open();
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var chunk = new byte[64 * 1024]; long total = 0; int read;
            while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
            {
                total += read;
                if (total > MaxScoreBytes) return StaleReason;
                hash.AppendData(chunk, 0, read);
            }
            return string.Equals(Convert.ToHexString(hash.GetHashAndReset()), record.ScoreSha256, StringComparison.OrdinalIgnoreCase) ? null : StaleReason;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException) { Services.Trace.Error(Services.Trace.Import, "embedded binding: verify: " + ex.Message); return StaleReason; }
    }

    private static ZipArchiveEntry? FindScore(ZipArchive zip)
    {
        try { return zip.Entries.FirstOrDefault(e => e.FullName.Equals(GuitarProExporter.ScoreEntry, StringComparison.OrdinalIgnoreCase)); }
        catch (InvalidDataException) { return null; } // Not logged: entry lookup: null means not present
    }

    /// <summary>
    /// For a file with no record: true when the score has a different number of bars than the embedded project. TabForge writes one master bar per
    /// bar of its longest track, so a difference means the score was edited after the project was embedded.
    /// </summary>
    internal static bool LegacyBarCountMismatch(ZipArchive zip, SongProject project)
    {
        try
        {
            if (zip.GetEntry(GuitarProExporter.EmbeddedBindingEntry) is not null) return false;
            var score = FindScore(zip);
            if (score is null || score.Length > MaxScoreBytes) return false;   // not a TabForge-shaped file: nothing to compare, accepted as before
            var bars = 0;
            using var stream = score.Open();
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreWhitespace = true, IgnoreComments = true });
            while (reader.Read())
                if (reader.NodeType == XmlNodeType.Element && reader.Name == "MasterBar" && reader.Depth == 2) bars++;
            var expected = Math.Max(1, project.Tracks.Count == 0 ? 1 : project.Tracks.Max(t => t.Measures.Count));
            return bars > 0 && bars != expected;
        }
        catch (Exception ex) when (ex is XmlException or InvalidDataException or IOException) { Services.Trace.Error(Services.Trace.Import, "embedded binding: bar count: " + ex.Message); return false; }
    }
}
