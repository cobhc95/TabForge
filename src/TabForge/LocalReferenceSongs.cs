using System.IO;
using System.Text.Json;

namespace TabForge;

/// <summary>
/// Resolves local-only reference songs for the self-tests by key ("reference-a", "reference-a-gp",
/// "reference-b"). Keys map to file names inside Tabs/ through the git-ignored Tabs/reference-songs.json
/// (a flat {"key": "file name"} object). Missing map or file resolves to null, so tests skip exactly
/// as they do on a clean checkout. TABFORGE_NO_LOCAL_SONGS=1 disables all lookups.
/// </summary>
internal static class LocalReferenceSongs
{
    private const string MapFileName = "reference-songs.json";

    /// <summary>Full path of the song for a key, or null when the map, key or file is missing.</summary>
    public static string? Resolve(string key)
    {
        foreach (var entry in Entries())
            if (entry.Key.Equals(key, StringComparison.OrdinalIgnoreCase)) return entry.Path;
        return null;
    }

    /// <summary>True when the file name or path is the song of the key, or of a variant key ("reference-a" also matches "reference-a-gp").</summary>
    public static bool Matches(string fileNameOrPath, string key)
    {
        var name = Path.GetFileName(fileNameOrPath);
        foreach (var entry in Entries())
        {
            var sameKey = entry.Key.Equals(key, StringComparison.OrdinalIgnoreCase) ||
                          entry.Key.StartsWith(key + "-", StringComparison.OrdinalIgnoreCase);
            if (sameKey && Path.GetFileName(entry.Path).Equals(name, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static IEnumerable<(string Key, string Path)> Entries()
    {
        if (SelfTest.LocalSongsDisabled) yield break;
        var starts = new[] { Environment.CurrentDirectory, AppContext.BaseDirectory };
        foreach (var start in starts)
        {
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            {
                var tabs = Path.Combine(dir.FullName, "Tabs");
                var map = Path.Combine(tabs, MapFileName);
                if (!File.Exists(map)) continue;
                Dictionary<string, string>? parsed = null;
                try { parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(map)); }
                catch { /* unreadable map: treat as missing */ }
                if (parsed is null) continue;
                var found = false;
                foreach (var pair in parsed)
                {
                    var file = Path.Combine(tabs, pair.Value);
                    if (!File.Exists(file)) continue;
                    found = true;
                    yield return (pair.Key, file);
                }
                if (found) yield break;
            }
        }
    }
}
