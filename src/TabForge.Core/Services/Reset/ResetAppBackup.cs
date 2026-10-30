using System.IO;
using System.IO.Compression;
using TabForge.Platform;

namespace TabForge.Services.Reset;

// Owns: the optional "Back up first" zip of exactly the ticked targets, written under the user's Documents/TabForge-backups.
// Does not own: what the plan contains (ResetAppPlan) or the wipe (ResetAppWipe).
// Tests: ResetAppPlanTests.
/// <summary>What the backup wrote, and any files it could not read (a locked log is skipped, never fatal).</summary>
public sealed record ResetAppBackupResult(string? Path, int Files, IReadOnlyList<string> Skipped)
{
    public bool Ok => Path is not null;
}

/// <summary>Writes a zip of the plan's existing targets; a file that cannot be read is listed as skipped.</summary>
public static class ResetAppBackup
{
    public const string FolderName = "TabForge-backups";

    public static string DefaultFolder(IAppFolders folders) => System.IO.Path.Combine(folders.Documents, FolderName);

    /// <summary>Writes the zip and returns its path; throws when the Documents folder is unknown or the folder cannot be created.</summary>
    public static ResetAppBackupResult Write(ResetAppPlan plan, string documentsFolder, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(documentsFolder)) throw new InvalidOperationException("The Documents folder is not available.");
        var folder = System.IO.Path.Combine(documentsFolder, FolderName);
        Directory.CreateDirectory(folder);
        var path = UniquePath(folder, now);
        var skipped = new List<string>();
        var files = 0;
        var added = new HashSet<string>(PathComparer.Default);

        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var item in plan.Items)
        {
            var prefix = $"{(int)item.Kind + 1:00}-{item.Kind}/";
            foreach (var target in item.Targets)
            {
                if (target.Kind == ResetAppTargetKind.SettingsJson)
                {
                    if (added.Add(Full(target.Path))) AddFile(archive, target.Path, prefix + System.IO.Path.GetFileName(target.Path), ref files, skipped);
                }
                else if (target.Kind == ResetAppTargetKind.File)
                {
                    if (added.Add(Full(target.Path))) AddFile(archive, target.Path, prefix + System.IO.Path.GetFileName(target.Path), ref files, skipped);
                }
                else AddFolder(archive, target.Path, prefix + System.IO.Path.GetFileName(target.Path), added, ref files, skipped);
            }
        }
        return new ResetAppBackupResult(path, files, skipped);
    }

    private static string UniquePath(string folder, DateTime now)
    {
        var stem = "TabForge-reset-backup-" + now.ToString("yyyyMMdd-HHmmss");
        var path = System.IO.Path.Combine(folder, stem + ".zip");
        for (var n = 2; File.Exists(path); n++) path = System.IO.Path.Combine(folder, $"{stem}-{n}.zip");
        return path;
    }

    private static void AddFolder(ZipArchive archive, string folder, string prefix, HashSet<string> added, ref int files, List<string> skipped)
    {
        try
        {
            if (!Directory.Exists(folder)) return;
            foreach (var entry in Directory.EnumerateFileSystemEntries(folder))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0) { skipped.Add(entry); continue; }
                var name = prefix + "/" + System.IO.Path.GetFileName(entry);
                if ((attributes & FileAttributes.Directory) != 0) AddFolder(archive, entry, name, added, ref files, skipped);
                else if (added.Add(Full(entry))) AddFile(archive, entry, name, ref files, skipped);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            skipped.Add($"{folder}: {ex.Message}");
        }
    }

    private static void AddFile(ZipArchive archive, string path, string entryName, ref int files, List<string> skipped)
    {
        try
        {
            if (!File.Exists(path)) return;
            var entry = archive.CreateEntry(entryName.Replace('\\', '/'), CompressionLevel.Optimal);
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var output = entry.Open();
            input.CopyTo(output);
            files++;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            skipped.Add($"{path}: {ex.Message}");
        }
    }

    private static string Full(string path)
    {
        try { return System.IO.Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return path; }
    }
}
