using System.IO;
using TabForge.Platform;

namespace TabForge.Services.Reset;

// Owns: executing a reset plan: refusing paths outside the app roots or through reparse points, deleting files/folders and
//   applying the settings.json groups, with a dry run that only counts.
// Does not own: building the plan (ResetAppPlan) or the backup (ResetAppBackup).
// Tests: ResetAppPlanTests, ResetAppStoreCoverageTests.
/// <summary>What a wipe (or its dry run) removed or refused.</summary>
public sealed record ResetAppWipeResult(
    bool DryRun,
    int Files,
    long Bytes,
    int Values,
    bool SettingsFileDeleted,
    IReadOnlyList<string> Refused,
    IReadOnlyList<string> Errors)
{
    public bool Failed => Errors.Count > 0;
}

/// <summary>Deletes exactly the targets of a plan, never a path outside an <see cref="IAppFolders"/> root.</summary>
public static class ResetAppWipe
{
    /// <summary>Removes the plan's targets; with <paramref name="dryRun"/> only counts them.</summary>
    public static ResetAppWipeResult Run(ResetAppPlan plan, bool dryRun = false)
    {
        var refused = new List<string>();
        var errors = new List<string>();
        var files = 0;
        long bytes = 0;
        var values = 0;
        var settingsDeleted = false;

        var settingsTargets = plan.Items.SelectMany(item => item.Targets)
            .Where(target => target.Kind == ResetAppTargetKind.SettingsJson && target.SettingsGroup is not null).ToList();
        if (settingsTargets.Count > 0)
        {
            var path = settingsTargets[0].Path;
            if (!IsInsideAnyRoot(path, plan.Roots)) refused.Add(path);
            else if (ContainsReparsePoint(path, plan.Roots)) refused.Add(path);
            else
            {
                var groups = settingsTargets.Select(target => target.SettingsGroup!.Value).ToHashSet();
                var result = ResetAppSettingsFile.Apply(path, groups, dryRun);
                values += result.Removed;
                settingsDeleted |= result.FileDeleted;
                if (result.Problem is { } problem) errors.Add($"{path}: {problem}");
            }
        }

        var seen = new HashSet<string>(PathComparer.Default);
        foreach (var target in plan.Items.SelectMany(item => item.Targets))
        {
            if (target.Kind == ResetAppTargetKind.SettingsJson) continue;
            var path = FullPath(target.Path);
            if (path is null || !seen.Add(path)) continue;
            if (!IsInsideAnyRoot(path, plan.Roots)) { refused.Add(target.Path); continue; }
            if (ContainsReparsePoint(path, plan.Roots)) { refused.Add(target.Path); continue; }
            if (target.Kind == ResetAppTargetKind.File) DeleteFile(path, dryRun, ref files, ref bytes, errors);
            else DeleteFolder(path, dryRun, ref files, ref bytes, refused, errors);
        }
        return new ResetAppWipeResult(dryRun, files, bytes, values, settingsDeleted, refused, errors);
    }

    /// <summary>True when <paramref name="path"/> is one of the roots or sits under one (no `..` escape survives GetFullPath).</summary>
    public static bool IsInsideAnyRoot(string? path, IReadOnlyList<string> roots)
    {
        var full = FullPath(path);
        if (full is null) return false;
        foreach (var root in roots)
        {
            var fullRoot = FullPath(root);
            if (fullRoot is null) continue;
            if (string.Equals(full, fullRoot, PathComparer.Comparison)) return true;
            if (full.StartsWith(fullRoot + Path.DirectorySeparatorChar, PathComparer.Comparison)) return true;
            if (full.StartsWith(fullRoot + Path.AltDirectorySeparatorChar, PathComparer.Comparison)) return true;
        }
        return false;
    }

    /// <summary>True when the path below a root passes through a reparse point (a symlink or junction), so it could leave the root.</summary>
    public static bool ContainsReparsePoint(string? path, IReadOnlyList<string> roots)
    {
        var full = FullPath(path);
        if (full is null) return false;
        foreach (var root in roots)
        {
            var fullRoot = FullPath(root);
            if (fullRoot is null) continue;
            var relative = Path.GetRelativePath(fullRoot, full);
            if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)) continue;
            var current = fullRoot;
            foreach (var part in relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, part);
                try
                {
                    if (!File.Exists(current) && !Directory.Exists(current)) continue;   // a not-yet-created file is fine
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return true;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return true; }
            }
            return false;
        }
        return false;
    }

    private static void DeleteFile(string path, bool dryRun, ref int files, ref long bytes, List<string> errors)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return;
            files++;
            bytes += info.Length;
            if (!dryRun) info.Delete();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            errors.Add($"{path}: {ex.Message}");
        }
    }

    private static void DeleteFolder(string folder, bool dryRun, ref int files, ref long bytes, List<string> refused, List<string> errors)
    {
        try
        {
            if (!Directory.Exists(folder)) return;
            foreach (var entry in Directory.EnumerateFileSystemEntries(folder))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0) { refused.Add(entry); continue; }
                if ((attributes & FileAttributes.Directory) != 0) DeleteFolder(entry, dryRun, ref files, ref bytes, refused, errors);
                else
                {
                    var info = new FileInfo(entry);
                    files++;
                    bytes += info.Length;
                    if (!dryRun) info.Delete();
                }
            }
            if (!dryRun && !Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            errors.Add($"{folder}: {ex.Message}");
        }
    }

    private static string? FullPath(string? path)
    {
        try { return string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }
}
