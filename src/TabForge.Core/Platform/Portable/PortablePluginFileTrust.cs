namespace TabForge.Platform.Portable;

// Owns: the plug-in file facts on macOS and Linux: the final path after links, and "no embedded signature" (neither OS has Authenticode).
// Does not own: any trust decision (PluginTrust) or the Windows calls (Platform/Windows/WindowsPluginFileTrust.cs).
// Tests: no dedicated test class.
/// <summary><see cref="IPluginFileTrust"/> for non-Windows hosts. Fail closed: no binary counts as signed, so every plug-in needs the user's approval.</summary>
public sealed class PortablePluginFileTrust : IPluginFileTrust
{
    public string? FinalPath(string fullPath)
    {
        try { return new FileInfo(fullPath).ResolveLinkTarget(true)?.FullName ?? Path.GetFullPath(fullPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return null; } // Not logged: link probe: null reads as unknown, which denies
    }

    public bool HasValidEmbeddedSignature(string fullPath) => false;
}
