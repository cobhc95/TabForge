using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TabForge.Services.Reset;

// Owns: the settings.json side of the wipe: which top-level key paths belong to which tick, counting them, and removing only
//   the selected groups (for Settings, every leaf not owned by an unticked group).
// Does not own: the other stores (ResetAppPlan), path safety or file deletion (ResetAppWipe).
// Tests: ResetAppPlanTests.
/// <summary>Removes selected settings groups from settings.json without touching the groups that stay ticked off.</summary>
public static class ResetAppSettingsFile
{
    // The key paths each tick owns. The Settings tick owns every other leaf.
    /// <summary>The key paths an explicit group owns; empty for the wildcard Settings group.</summary>
    public static IReadOnlyList<string> OwnedPaths(ResetAppSettingsGroup group) => group switch
    {
        ResetAppSettingsGroup.Layouts => new[] { "Workspace", "WorkspaceBeforeSideHide", "SavedLayouts", "LastLayout" },
        ResetAppSettingsGroup.Presets => new[] { "EffectPresets" },
        ResetAppSettingsGroup.PluginLibrary => new[]
        {
            "Plugins.ApprovedPluginPaths", "Plugins.TrustRecords", "Plugins.ScanCache", "Plugins.Probed", "Plugins.Quarantined",
        },
        // Render is handled separately: it mixes last-used options with the SafetyLimiter Settings row.
        ResetAppSettingsGroup.Recents => new[] { "RememberedFolders", "MidiExport", "Video", "Plugins.RecentImpulseFiles" },
        _ => Array.Empty<string>(),
    };

    /// <summary>How many saved leaves the Settings tick would remove: everything the four explicit groups do not own.</summary>
    public static int CountSettings(JsonObject root)
    {
        var explicitCount = Enum.GetValues<ResetAppSettingsGroup>()
            .Where(group => group != ResetAppSettingsGroup.Settings)
            .Sum(group => CountGroup(root, group));
        return Math.Max(0, CountLeaves(root) - explicitCount);
    }

    /// <summary>How many leaves of an explicit group are present; an owned object contributes its leaves, not one key.</summary>
    public static int CountGroup(JsonObject root, ResetAppSettingsGroup group) => CurrentOwnedPaths(root, group).Count();

    /// <summary>The leaves an explicit group owns in this document: exact paths expanded, plus Render without its SafetyLimiter setting.</summary>
    private static IEnumerable<string> CurrentOwnedPaths(JsonObject root, ResetAppSettingsGroup group)
    {
        foreach (var path in OwnedPaths(group))
            foreach (var leaf in Expand(root, path)) yield return leaf;
        if (group != ResetAppSettingsGroup.Recents) yield break;
        if (!root.TryGetPropertyValue("Render", out var render)) yield break;
        if (render is JsonObject renderObject)
            foreach (var leaf in Leaves(renderObject, "Render"))
                if (!string.Equals(leaf, "Render.SafetyLimiter", StringComparison.Ordinal)) yield return leaf;
        else yield return "Render";
    }

    private static IEnumerable<string> Expand(JsonObject root, string path)
    {
        var node = NodeAt(root, path.Split('.'), 0);
        if (node is null) yield break;
        if (node is JsonObject child && child.Count > 0)
            foreach (var leaf in Leaves(child, path)) yield return leaf;
        else yield return path;
    }

    private static JsonNode? NodeAt(JsonObject root, string[] parts, int index)
    {
        if (!root.TryGetPropertyValue(parts[index], out var child)) return null;
        return index == parts.Length - 1 ? child : child is JsonObject next ? NodeAt(next, parts, index + 1) : null;
    }

    /// <summary>The number of leaf values (an array or a scalar) in the document.</summary>
    public static int CountLeaves(JsonObject root)
    {
        var count = 0;
        foreach (var (_, value) in root)
        {
            if (value is JsonObject child && child.Count > 0) count += CountLeaves(child);
            else count++;
        }
        return count;
    }

    /// <summary>Reads settings.json as an object; null when it is missing or not a JSON object.</summary>
    public static JsonObject? TryRead(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var text = File.ReadAllText(path);
            return text.Length == 0 ? null : JsonNode.Parse(text) as JsonObject;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Removes the selected groups. Returns the number of leaves removed, whether the file became empty, and a problem for an unreadable file.</summary>
    public static ResetAppSettingsResult Apply(string path, IReadOnlySet<ResetAppSettingsGroup> selected, bool dryRun)
    {
        if (!File.Exists(path)) return new(0, false, null);
        var root = TryRead(path);
        if (root is null)
        {
            // Settings is a full reset and may remove the malformed file; any other group must not destroy values it does not own.
            if (!selected.Contains(ResetAppSettingsGroup.Settings)) return new(0, false, "settings.json could not be read");
            if (!dryRun) TryDelete(path);
            return new(0, true, null);
        }

        var preserved = Enum.GetValues<ResetAppSettingsGroup>()
            .Where(group => group != ResetAppSettingsGroup.Settings && !selected.Contains(group))
            .SelectMany(group => CurrentOwnedPaths(root, group)).ToHashSet(StringComparer.Ordinal);
        var remove = selected.Where(group => group != ResetAppSettingsGroup.Settings)
            .SelectMany(group => CurrentOwnedPaths(root, group)).ToHashSet(StringComparer.Ordinal);
        if (selected.Contains(ResetAppSettingsGroup.Settings))
            remove.UnionWith(Leaves(root).Where(leaf => !preserved.Contains(leaf)));

        var removed = 0;
        // Deepest first so an empty parent is pruned after its children; a leaf path removes exactly one value.
        foreach (var leaf in remove.OrderByDescending(p => p.Count(c => c == '.')).ThenBy(p => p, StringComparer.Ordinal))
            if (RemovePath(root, leaf.Split('.'), 0)) removed++;

        var empty = root.Count == 0;
        if (!dryRun && removed > 0)
        {
            if (empty) TryDelete(path);
            else FilePathPolicy.WriteAtomically(path, stream => stream.Write(Encoding.UTF8.GetBytes(root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }))));
        }
        return new(removed, empty, null);
    }

    private static IEnumerable<string> Leaves(JsonObject root, string prefix = "")
    {
        foreach (var (key, value) in root)
        {
            var path = prefix.Length == 0 ? key : prefix + "." + key;
            if (value is JsonObject child && child.Count > 0)
                foreach (var leaf in Leaves(child, path)) yield return leaf;
            else yield return path;
        }
    }

    private static bool RemovePath(JsonObject root, string[] parts, int index)
    {
        if (!root.TryGetPropertyValue(parts[index], out var child)) return false;
        if (index == parts.Length - 1) return root.Remove(parts[index]);
        if (child is not JsonObject next || !RemovePath(next, parts, index + 1)) return false;
        if (next.Count == 0) root.Remove(parts[index]);
        return true;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { TabForge.Services.Trace.Error(TabForge.Services.Trace.Ui, "reset app: delete settings: " + ex.Message); }
    }
}

/// <summary>What removing groups from settings.json did.</summary>
public sealed record ResetAppSettingsResult(int Removed, bool FileDeleted, string? Problem);
