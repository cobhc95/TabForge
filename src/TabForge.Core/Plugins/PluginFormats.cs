using System.IO;

namespace TabForge.Plugins;

// Owns: the plug-in format names ("VST2", "VST3", "CLAP") and the one rule that names the format of a plug-in file by its extension.
// Does not own: scanning (VstScannerService), hosting (the engine's PluginFactory).
// Tests: no dedicated test class.

/// <summary>The plug-in formats TabForge knows. CLAP is opt-in (Settings > Plug-ins, or the Add plug-in window); VST2 is Windows-only.</summary>
public static class PluginFormats
{
    public const string Vst2 = "VST2", Vst3 = "VST3", Clap = "CLAP";
    /// <summary>A 32-bit VST2 DLL or 32-bit-only VST3 hosted through the 32-bit helper process.</summary>
    public const string Bridged32 = "32-bit (bridged)";
    /// <summary>A 64-bit Windows VST2 DLL hosted through the 64-bit helper under Wine on Linux (native on Windows).</summary>
    public const string Bridged64 = "64-bit (bridged)";
    /// <summary>The old label of a 32-bit VST2 DLL; migrated to <see cref="Bridged32"/> when a saved list or project loads.</summary>
    public const string LegacyUnsupported32 = "32-bit (not supported)";
    /// <summary>TabForge's own effects (their path is a built-in id, see <see cref="TabForge.Audio.Contracts.Builtin.BuiltinEffects"/>).</summary>
    public const string Builtin = TabForge.Audio.Contracts.Builtin.BuiltinEffects.Format;

    /// <summary>The format of a plug-in file by extension: .vst3 is VST3, .clap is CLAP, anything else (a .dll) is VST2 as it always was. Never reads the file.</summary>
    public static string Of(string path)
    {
        var trimmed = path.TrimEnd('\\', '/');
        if (TabForge.Audio.Contracts.Builtin.BuiltinEffects.IsBuiltin(trimmed)) return Builtin;
        return trimmed.EndsWith(".vst3", StringComparison.OrdinalIgnoreCase) ? Vst3
            : trimmed.EndsWith(".clap", StringComparison.OrdinalIgnoreCase) ? Clap : Vst2;
    }

    /// <summary>
    /// The format to show for a known plug-in file: <see cref="Of"/> plus the bitness and route, read from the binary.
    /// Windows keeps 64-bit VST2/VST3 native and labels only 32-bit DLLs and 32-bit-only VST3 bridged;
    /// Linux labels Windows VST2 and Windows-layout VST3 bridged only when the matching helper is shipped.
    /// Called at scan, add and identify boundaries (never hot).
    /// </summary>
    public static string OfPlugin(string path)
    {
        var format = Of(path);
        if (format == Vst3)
        {
            if (OperatingSystem.IsWindows()) return Vst3Windows.Is32BitOnly(path) ? Bridged32 : format;
            if (!OperatingSystem.IsLinux() || !Vst3Windows.IsWindowsOnly(path)) return format;
            if (WineBridgeSupport.Available64 && Vst3Windows.Has(path, sixtyFourBit: true)) return Bridged64;
            if (WineBridgeSupport.Available && Vst3Windows.Has(path, sixtyFourBit: false)) return Bridged32;
            return format;
        }
        if (format != Vst2) return format;
        if (OperatingSystem.IsWindows()) return PeExports.IsVst2Plugin32(path) ? Bridged32 : format;
        if (!OperatingSystem.IsLinux()) return format;
        if (WineBridgeSupport.Available && PeExports.IsVst2Plugin32(path)) return Bridged32;
        if (WineBridgeSupport.Available64 && PeExports.IsVst2Plugin64(path)) return Bridged64;
        return format;
    }

    public static bool IsClap(string path) => Of(path) == Clap;

    /// <summary>Maps the old 32-bit label to <see cref="Bridged32"/>; every other value is returned unchanged.</summary>
    public static string Normalize(string? format) =>
        string.Equals(format?.Trim(), LegacyUnsupported32, StringComparison.OrdinalIgnoreCase) ? Bridged32 : format ?? "";

    /// <summary>True for a format name a saved file may carry (an unknown one is reset to "" by the validators).</summary>
    public static bool IsKnown(string? format) => format is Vst2 or Vst3 or Clap or Builtin or Bridged32 or Bridged64;
}
