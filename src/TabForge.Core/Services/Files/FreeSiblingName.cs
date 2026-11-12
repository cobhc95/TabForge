using System.IO;

namespace TabForge.Services;

// Owns: finding the first name in a folder that no file or folder holds yet, from a first try and a rule for the next tries (the "name (2)" and "name (full copy 2)" finders).
// Does not own: which names to try or how many tries are allowed (DocumentSaveFlow, GpExportPreflight) or writing the file.
// Tests: TestSiblingNameFinder, TestNumberedSiblingCeiling.
internal static class FreeSiblingName
{
    /// <summary>
    /// <paramref name="firstPath"/> when nothing holds it, else the path in <paramref name="folder"/> named <paramref name="nameFor"/>(2), (3) and so on: the first one nothing holds
    /// (case-insensitive; a folder counts). Null when try number <paramref name="maxTry"/> is passed. One directory listing answers every probe; per-path stats
    /// are the fallback when the folder cannot be listed.
    /// </summary>
    public static string? FirstFree(string folder, string firstPath, Func<int, string> nameFor, int maxTry = int.MaxValue)
    {
        HashSet<string>? existing = null;
        try
        {
            if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                existing = new HashSet<string>(
                    Directory.EnumerateFileSystemEntries(folder).Select(e => Path.GetFileName(e)!),
                    StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { existing = null; }   // Not logged: listing failed: per-path stats answer instead
        bool Exists(string candidate) => existing is not null
            ? existing.Contains(Path.GetFileName(candidate))
            : File.Exists(candidate) || Directory.Exists(candidate);
        var candidate = firstPath;
        for (var i = 2; Exists(candidate); i++)
        {
            if (i > maxTry) return null;
            candidate = Path.Combine(folder, nameFor(i));
        }
        return candidate;
    }
}
