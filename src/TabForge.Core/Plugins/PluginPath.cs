using System.IO;

namespace TabForge.Plugins;

// Owns: the one spelling of a plug-in path used for trust and approval comparisons (full path, no trailing separator).
// Does not own: the trust decision (PluginTrust), scanning, approval lists.
// Tests: TestSecurityInputBoundaries.
/// <summary>Plug-in path normalisation shared by the settings bounds and plug-in trust.</summary>
public static class PluginPath
{
    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return "";
        if (TabForge.Audio.Contracts.Builtin.BuiltinEffects.IsBuiltin(path.Trim())) return path.Trim();   // a built-in id names TabForge's own effect, not a file
        try { path = Path.GetFullPath(path.Trim()); } catch (Exception) { return ""; } // Not logged: a path that cannot be normalised is never trusted; callers drop ""
        var root = Path.GetPathRoot(path) ?? "";
        while (path.Length > root.Length && (path[^1] is '\\' or '/')) path = path[..^1];
        return path;
    }
}
