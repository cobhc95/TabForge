using System.IO;

// Owns: the naming rules every preset kind shares: the check of a typed name, and the copy name ("name copy", "name copy 2").
// Does not own: the files of the presets (PresetFolderStore, DrumMapStore) or the list that shows them (PresetManagePanel).
// Tests: PresetManagerWindowTests, TestPresetManagePanel.
namespace TabForge.Presenters;

/// <summary>The naming rules every preset kind shares.</summary>
public static class PresetRules
{
    public const int MaxNameLength = 64;

    /// <summary>Why the (trimmed) text cannot be a preset name; null when it can.</summary>
    public static string? Error(string text)
    {
        var name = text.Trim();
        if (name.Length == 0) return "Type a name.";
        if (name.Length > MaxNameLength) return $"Use at most {MaxNameLength} characters.";
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return "A name cannot contain \\ / : * ? \" < > |";
        if (name.EndsWith('.')) return "A name cannot end with a dot.";
        return null;
    }

    /// <summary>"name copy", then "name copy 2"…: the first one no preset has yet.</summary>
    public static string CopyName(string name, IReadOnlyList<string> existing)
    {
        for (var n = 1; ; n++)
        {
            var suffix = n == 1 ? " copy" : $" copy {n}";
            var candidate = name[..Math.Min(name.Length, MaxNameLength - suffix.Length)].TrimEnd() + suffix;
            if (!existing.Contains(candidate, StringComparer.OrdinalIgnoreCase)) return candidate;
        }
    }
}
