using TabForge.Audio.Contracts;

namespace TabForge.Services;

// Owns: classifying a media path on macOS and Linux (absolute or project-relative; device trees refused; mounted volumes need approval). String-only apart from the link callback, so the rules run and are tested on any OS.
// Does not own: the Windows rules (MediaPathPolicy: drive letters, UNC, the extended-length forms, reserved names, 8.3 names), which stay exactly as they were, or the approval state (MediaAccess).
// Tests: UnixPathRulesTests.
/// <summary>The Unix half of <see cref="MediaPathPolicy.Classify(string?, string?, bool)"/>.</summary>
public static class UnixPathRules
{
    // Places where the system mounts other volumes: external, optical and network drives. Treated like Windows' removable drives: the user approves first.
    /// <summary>Local, or Removable for a path on a mounted volume.</summary>
    public static MediaLocation LocationOf(string normalised) =>
        InMount(normalised, "/Volumes") || InMount(normalised, "/media") || InMount(normalised, "/mnt") || InMount(normalised, "/run/media") || InMount(normalised, "/net") || InMount(normalised, "/Network")
            ? MediaLocation.Removable : MediaLocation.Local;

    private static bool InMount(string path, string root) => path.StartsWith(root + "/", StringComparison.Ordinal);

    /// <param name="finalPath">The place a local file really leads to once links are followed (null: it is where it says); only called for local paths.</param>
    public static MediaVerdict Classify(string? path, string? projectFolder, bool requireAudio, Func<string, string?> finalPath)
    {
        static MediaVerdict Bad(MediaLocation l, string why) => new(l, "", false, why);
        if (string.IsNullOrWhiteSpace(path)) return Bad(MediaLocation.Invalid, "no file path");
        if (path.Length > InputLimits.MaxPathLength) return Bad(MediaLocation.Invalid, "the path is too long");
        if (path.Any(c => c < 32)) return Bad(MediaLocation.Invalid, "the path has control characters");
        var p = path.Trim();
        var relative = p[0] != '/';
        if (relative)
        {
            if (string.IsNullOrWhiteSpace(projectFolder) || projectFolder.TrimStart()[0] != '/') return Bad(MediaLocation.Invalid, "a relative path needs a saved project folder");
            p = projectFolder.Trim() + "/" + p;
        }
        var full = UnixClipPathRules.Normalize(p);
        if (UnixClipPathRules.IsDeviceTree(full)) return Bad(MediaLocation.Device, "device paths are never opened");
        if (requireAudio && !MediaPathPolicy.IsAudioExtension(full)) return Bad(MediaLocation.Invalid, "not an audio file type");

        var folder = string.IsNullOrWhiteSpace(projectFolder) ? null : UnixClipPathRules.Normalize(projectFolder.Trim());
        var location = LocationOf(full);
        if (location == MediaLocation.Local && finalPath(full) is { Length: > 0 } final && final[0] == '/')
        {
            var resolved = UnixClipPathRules.Normalize(final);
            if (UnixClipPathRules.IsDeviceTree(resolved)) return Bad(MediaLocation.Device, "the file leads to a device path");
            if (!resolved.Equals(full, StringComparison.Ordinal)) { full = resolved; location = LocationOf(full); }
        }
        var inProject = folder is not null && (full == folder || full.StartsWith(folder.TrimEnd('/') + "/", StringComparison.Ordinal));
        return new MediaVerdict(location, full, inProject, null) { EscapesProject = relative && !inProject };
    }
}
