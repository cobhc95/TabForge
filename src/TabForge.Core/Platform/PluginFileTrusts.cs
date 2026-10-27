using TabForge.Platform.Portable;

namespace TabForge.Platform;

// Owns: choosing the plug-in file-trust adapter of this OS: Win32 on Windows, the portable one elsewhere.
// Does not own: the adapters or any trust decision (PluginTrust).
// Tests: no dedicated test class.
/// <summary>The <see cref="IPluginFileTrust"/> the app composes.</summary>
public static class PluginFileTrusts
{
    /// <summary>A new adapter for this OS.</summary>
    public static IPluginFileTrust Create()
    {
#if WINDOWS
        return new Windows.WindowsPluginFileTrust();
#else
        return new PortablePluginFileTrust();
#endif
    }
}
