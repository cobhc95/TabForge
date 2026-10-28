using System.IO;

namespace TabForge.Plugins;

// Owns: the plug-in format names ("VST2", "VST3", "CLAP") and the one rule that names the format of a plug-in file by its extension.
// Does not own: scanning (VstScannerService), hosting (the engine's PluginFactory).
// Tests: PluginScanFolderTests.

/// <summary>The plug-in formats TabForge knows. CLAP is opt-in (Settings > Plug-ins, or the Add plug-in window); VST2 is Windows-only.</summary>
public static class PluginFormats
{
    public const string Vst2 = "VST2", Vst3 = "VST3", Clap = "CLAP";
    /// <summary>TabForge's own effects (their path is a built-in id, see <see cref="TabForge.Audio.Contracts.Builtin.BuiltinEffects"/>).</summary>
    public const string Builtin = TabForge.Audio.Contracts.Builtin.BuiltinEffects.Format;

    /// <summary>The format of a plug-in file by extension: .vst3 is VST3, .clap is CLAP, anything else (a .dll) is VST2 as it always was.</summary>
    public static string Of(string path)
    {
        var trimmed = path.TrimEnd('\\', '/');
        if (TabForge.Audio.Contracts.Builtin.BuiltinEffects.IsBuiltin(trimmed)) return Builtin;
        return trimmed.EndsWith(".vst3", StringComparison.OrdinalIgnoreCase) ? Vst3
            : trimmed.EndsWith(".clap", StringComparison.OrdinalIgnoreCase) ? Clap : Vst2;
    }

    public static bool IsClap(string path) => Of(path) == Clap;

    /// <summary>True for a format name a saved file may carry (an unknown one is reset to "" by the validators).</summary>
    public static bool IsKnown(string? format) => format is Vst2 or Vst3 or Clap or Builtin;
}
