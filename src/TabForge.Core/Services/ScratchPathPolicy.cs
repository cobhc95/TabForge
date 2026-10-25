using System.IO;

namespace TabForge.Services;

// Owns: which folders a headless diagnostic may write new files to (the system temp folder, the diagnostics folder and the --profile folder)
//     and the refusal of a target that already exists.
// Does not own: path validation (FilePathPolicy supplies the full path and the extension check) or the writes themselves.
// Tests: none dedicated(full suite).
/// <summary>Scratch-folder rule for the headless data commands: a new file under a scratch root, never a replacement.</summary>
public static class ScratchPathPolicy
{
    /// <summary>A new file under a scratch root (see the class header). Throws when it exists, or lies outside the roots.</summary>
    public static string NewFile(string path, string description, params string[] extensions)
    {
        var fullPath = FilePathPolicy.OutputFile(path, description, extensions);
        RequireScratch(fullPath, description);
        if (File.Exists(fullPath) || Directory.Exists(fullPath)) throw new IOException($"The {description} already exists: {fullPath}. Choose a new name.");
        return fullPath;
    }

    /// <summary>A folder under a scratch root, created when missing. Throws when it lies outside the roots (nothing is created then).</summary>
    public static string Folder(string path, string description)
    {
        var fullPath = FilePathPolicy.OutputDirectory(path, description);
        RequireScratch(fullPath, description);
        Directory.CreateDirectory(fullPath);
        return fullPath;
    }

    private static void RequireScratch(string fullPath, string description)
    {
        var roots = new List<string> { Path.GetTempPath(), UserPaths.Diagnostics };
        if (UserPaths.ProfileRoot is { } profile) roots.Add(profile);
        if (roots.Any(root => IsUnder(fullPath, root))) return;
        throw new InvalidDataException($"The {description} must be under the temp folder, the diagnostics folder or the --profile folder: {fullPath}");
    }

    private static bool IsUnder(string fullPath, string root)
    {
        var folder = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(folder, StringComparison.OrdinalIgnoreCase);
    }
}
