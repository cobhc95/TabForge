namespace TabForge.Services;

// Owns: the one reading and writing of a folder list stored as one "; "-joined string (Settings rows) or as a list (the plug-in scanner): trim, strip quotes, keep order, drop empty and exact duplicates.
// Does not own: whether a folder exists or is local (VstScannerService) or expanding %ENV% text (kept as typed here).
// Tests: no dedicated test class.
public static class FolderListText
{
    private const string Separator = "; ";

    /// <summary>The folders of <paramref name="value"/> (split at ';').</summary>
    public static List<string> Parse(string? value) => Clean((value ?? "").Split(';'));

    /// <summary>The folders of <paramref name="folders"/> in order: trimmed, outer quotes stripped, empty entries and case-insensitive duplicates dropped; %ENV% text unchanged.</summary>
    public static List<string> Clean(IEnumerable<string?> folders)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return folders.Select(f => (f ?? "").Trim().Trim('"').Trim()).Where(f => f.Length > 0 && seen.Add(f)).ToList();
    }

    /// <summary>
    /// Adds <paramref name="candidates"/> (typed, browsed or dropped) to <paramref name="current"/> through <see cref="Clean"/>: trimmed, no duplicates; UNC paths are kept as given.
    /// A name containing ';' is refused (the stored text would split it in two): <paramref name="refused"/> says so in plain words (empty: none refused). Returns whether the list changed.
    /// </summary>
    public static bool Add(IList<string> current, IEnumerable<string?> candidates, out string refused)
    {
        var all = Clean(candidates);
        var bad = all.Where(f => f.Contains(';')).ToList();
        refused = bad.Count == 0 ? "" : $"A folder name cannot contain ';': {string.Join(", ", bad)}.";
        var changed = false;
        foreach (var folder in Clean(all.Where(f => !f.Contains(';'))))
            if (!current.Contains(folder, StringComparer.OrdinalIgnoreCase)) { current.Add(folder); changed = true; }
        return changed;
    }

    /// <summary>The stored text of <paramref name="folders"/>: <see cref="Clean"/> and joined by "; ".</summary>
    public static string Join(IEnumerable<string?> folders) => string.Join(Separator, Clean(folders));
}
