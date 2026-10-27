namespace TabForge.Platform.Portable;

// Owns: the final path of a file on macOS and Linux: the target after every symbolic link.
// Does not own: what a caller does with an unknown answer, or the Windows call (Platform/Windows/WindowsPathResolver.cs).
// Tests: no dedicated test class.
/// <summary><see cref="IPathResolver"/> for non-Windows hosts, over the runtime's link resolution.</summary>
public sealed class PortablePathResolver : IPathResolver
{
    public string? FinalPath(string fullPath)
    {
        try { return new FileInfo(fullPath).ResolveLinkTarget(true)?.FullName ?? Path.GetFullPath(fullPath); }
        catch (IOException ex) { throw new UnresolvedLinkException(fullPath, ex); }
    }
}
