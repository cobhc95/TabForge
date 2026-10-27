namespace TabForge.Audio.Contracts;

// Owns: the engine-side clip path rules for macOS and Linux (absolute plain audio-file paths, no device trees); string-only, so the rules run and are tested on any OS.
// Does not own: the Windows rules (ClipPathGuard: drive letters, UNC, device forms, reserved names), which stay exactly as they were, or the UI's media policy (UnixPathRules in Core).
// Tests: UnixPathRulesTests.
/// <summary>The Unix half of <see cref="ClipPathGuard"/>.</summary>
public static class UnixClipPathRules
{
    public static bool IsAllowed(string? path, out string reason)
    {
        reason = "";
        if (string.IsNullOrWhiteSpace(path)) { reason = "empty path"; return false; }
        if (path.Any(c => c < 32)) { reason = "control characters"; return false; }
        var p = path.Trim();
        if (p[0] != '/') { reason = "not an absolute path"; return false; }
        var normal = Normalize(p);
        if (IsDeviceTree(normal)) { reason = "device path"; return false; }
        var dot = normal.LastIndexOf('.');
        if (dot < normal.LastIndexOf('/') || !IsAudioExtension(normal[dot..].ToLowerInvariant())) { reason = "not an audio file type"; return false; }
        return true;
    }

    /// <summary>Collapses "//", "." and ".." in an absolute path; ".." at the root stays at the root. No trailing slash except the root itself.</summary>
    public static string Normalize(string absolute)
    {
        var parts = new List<string>();
        foreach (var segment in absolute.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".") continue;
            if (segment == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); continue; }
            parts.Add(segment);
        }
        return "/" + string.Join('/', parts);
    }

    private static bool IsAudioExtension(string ext) => ext is ".wav" or ".mp3" or ".aif" or ".aiff" or ".flac" or ".ogg" or ".m4a" or ".wma";

    /// <summary>True when a normalised absolute path is /dev, /proc or /sys or below one: folders of devices, process state and kernel files, never user media.</summary>
    public static bool IsDeviceTree(string normalised) => InTree(normalised, "/dev") || InTree(normalised, "/proc") || InTree(normalised, "/sys");

    private static bool InTree(string path, string root) => path == root || path.StartsWith(root + "/", StringComparison.Ordinal);
}
