namespace TabForge.Audio.Contracts;

/// <summary>
/// Last line of defence in the engine process: the UI only sends clip paths its media policy allowed, and the engine still refuses
/// anything that is not an absolute, plain audio-file path (device forms, streams, reserved names, other extensions).
/// Network and removable approval is the UI's decision; the engine cannot ask the user.
/// </summary>
public static class ClipPathGuard
{
    private static readonly string[] Extensions = { ".wav", ".mp3", ".aif", ".aiff", ".flac", ".ogg", ".m4a", ".wma" };
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public static bool IsAllowed(string? path, out string reason)
    {
        reason = "";
        if (string.IsNullOrWhiteSpace(path)) { reason = "empty path"; return false; }
        if (path.Any(c => c < 32)) { reason = "control characters"; return false; }
        var p = path.Trim().Replace('/', '\\');
        if (p.StartsWith(@"\\.\", StringComparison.Ordinal) || p.StartsWith(@"\\?\", StringComparison.Ordinal) || p.StartsWith(@"\??\", StringComparison.Ordinal)
            || p.Contains("GLOBALROOT", StringComparison.OrdinalIgnoreCase)) { reason = "device path"; return false; }
        if (!Path.IsPathFullyQualified(p)) { reason = "not an absolute path"; return false; }
        var afterDrive = p.Length >= 2 && char.IsAsciiLetter(p[0]) && p[1] == ':' ? p[2..] : p;
        if (afterDrive.Contains(':')) { reason = "stream or drive-relative form"; return false; }
        foreach (var segment in p.Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            var stem = segment.TrimEnd('.', ' ');
            var dot = stem.IndexOf('.');
            if (Reserved.Contains(dot >= 0 ? stem[..dot] : stem)) { reason = "reserved device name"; return false; }
        }
        if (!Extensions.Contains(Path.GetExtension(p).ToLowerInvariant())) { reason = "not an audio file type"; return false; }
        return true;
    }
}
