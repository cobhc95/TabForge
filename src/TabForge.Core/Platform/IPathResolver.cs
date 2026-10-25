namespace TabForge.Platform;

// Owns: the question "where does this file really live once every link and junction is followed".
// Does not own: the Windows call (the adapter is Platform/Windows/WindowsPathResolver.cs), the prefix handling of the answer,
// or what a caller does when the answer is unknown.
// Tests: TestPathResolverAdapter.
/// <summary>
/// The final path of a file as the file system reports it, for the policies that decide whether a file may be trusted or opened
/// (<c>MediaPathPolicy</c>, <c>PluginTrust</c>). The answer is raw: a Windows adapter returns the <c>\\?\</c> form it reports, and each
/// caller strips or refuses that form the way it always did.
/// </summary>
public interface IPathResolver
{
    /// <summary>
    /// The final path of the file, or null when the system reports none or the path does not fit its buffer. A network, removable or
    /// relative path is returned as it is and never opened. Looking up a missing or unopenable file throws the same
    /// exceptions as <c>File.OpenHandle</c> (for example <c>FileNotFoundException</c>, <c>UnauthorizedAccessException</c>); callers catch
    /// them and treat the file as unresolved. A link on the path whose target cannot be read throws <see cref="UnresolvedLinkException"/>:
    /// the file is unknown, and the caller asks before using it.
    /// </summary>
    string? FinalPath(string fullPath);
}

/// <summary>A link whose target the adapter cannot read or will not follow (an unknown kind of link, a device or volume target).</summary>
public sealed class UnresolvedLinkException : Exception
{
    public UnresolvedLinkException(string message, Exception? inner = null) : base("A link could not be resolved: " + message, inner) { }
}
