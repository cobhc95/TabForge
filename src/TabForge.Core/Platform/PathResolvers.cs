using TabForge.Platform.Portable;

namespace TabForge.Platform;

// Owns: choosing the final-path adapter of this OS: Win32 on Windows, the portable one elsewhere.
// Does not own: the adapters or what a caller does with the answer (MediaPathPolicy, PluginTrust).
// Tests: no dedicated test class.
/// <summary>The <see cref="IPathResolver"/> the app composes.</summary>
public static class PathResolvers
{
    /// <summary>A new adapter for this OS.</summary>
    public static IPathResolver Create()
    {
#if WINDOWS
        return new Windows.WindowsPathResolver();
#else
        return new PortablePathResolver();
#endif
    }
}
