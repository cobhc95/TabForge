namespace TabForge.Capture;

// Owns: the rule for the file names a script gives its shots: a relative path with forward slashes that stays under the output folder.
// Does not own: writing the files (the shot steps).
// Tests: TabForge.Avalonia.Tests.CaptureScriptTests.
public static class CaptureNames
{
    /// <summary>The full path of <paramref name="id"/> plus <paramref name="extension"/> under <paramref name="folder"/>; throws when the name is empty, rooted, has a drive, a backslash, "." or ".." or an invalid character.</summary>
    public static string PathUnder(string folder, string id, string extension)
    {
        var bad = id.Length == 0 || Path.IsPathRooted(id) || id.Contains((char)92) || id.Contains(':') || id.IndexOfAny(Path.GetInvalidFileNameChars().Where(c => c != '/').ToArray()) >= 0
            || id.Split('/').Any(part => part.Length == 0 || part == "." || part == "..");
        if (bad) throw new ArgumentException($"The shot name \"{id}\" must be a relative path under the output folder.");
        var root = Path.GetFullPath(folder);
        var full = Path.GetFullPath(Path.Combine(root, id + extension));
        if (!full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException($"The shot name \"{id}\" leaves the output folder.");
        return full;
    }
}
