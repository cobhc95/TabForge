using System.IO;
using TabForge.Audio.Contracts;

namespace TabForge.Plugins;

// Owns: the "is this .vst3 a Windows plug-in?" question used by scanning, labels and the bridge routes.
// Does not own: loading it (the helper), trust (PluginIdentity) or the launches (the engine).
// Tests: Vst32CatalogTests, Vst3WineLinuxTests, Vst3NativeTests.

/// <summary>Windows VST3 layouts (Contents/x86_64-win, Contents/x86-win32, or a flat .vst3 PE), inspected without loading.</summary>
public static class Vst3Windows
{
    /// <summary>The Windows binary of <paramref name="path"/> for one bitness, or null.</summary>
    public static string? Binary(string path, bool sixtyFourBit) =>
        PluginModulePaths.Vst3WindowsBinary(path, sixtyFourBit, DiskModuleFileView.Instance);

    /// <summary>True when that binary exists and is a Windows PE of the requested bitness that exports GetPluginFactory.</summary>
    public static bool Has(string path, bool sixtyFourBit) =>
        Binary(path, sixtyFourBit) is { } binary && (sixtyFourBit ? PeExports.IsVst3Plugin64(binary) : PeExports.IsVst3Plugin32(binary));

    /// <summary>True when the bundle has a native Linux binary (then Linux keeps its native in-process route).</summary>
    public static bool HasLinuxBinary(string path)
    {
        if (File.Exists(path)) return false;
        if (!Directory.Exists(path)) return false;
        return PluginModulePaths.Vst3Binary(path, ModuleOs.Linux, DiskModuleFileView.Instance) is { } binary && binary != path;
    }

    /// <summary>True for a flat Windows .vst3 file, or a bundle with a Windows binary and no Linux binary.</summary>
    public static bool IsWindowsOnly(string path)
    {
        if (File.Exists(path)) return PeExports.IsVst3Plugin64(path) || PeExports.IsVst3Plugin32(path);
        if (!Directory.Exists(path) || HasLinuxBinary(path)) return false;
        return Has(path, sixtyFourBit: true) || Has(path, sixtyFourBit: false);
    }

    /// <summary>
    /// True for a 32-bit-only Windows VST3: a 32-bit Windows binary is present and no 64-bit one is.
    /// A bundle with both bitnesses (or a 64-bit-only file) stays on the native 64-bit route.
    /// File inspection only; the plug-in is never loaded.
    /// </summary>
    public static bool Is32BitOnly(string path) =>
        Has(path, sixtyFourBit: false) && !Has(path, sixtyFourBit: true);
}
