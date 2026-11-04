using System.IO;
using TabForge.Platform;

namespace TabForge.Plugins;

// Owns: the passive question "can this Linux build run the bundled Wine bridge?" and the paths it uses. It never starts
// a process; the engine's WineRuntime does, and tests can point TABFORGE_WINE at a scratch executable.
// Does not own: launching the helper (Vst32Bridge/WineRuntime), scanning (VstScannerService) or trust (PluginTrust).
// Tests: Vst32WineSeamTests.

/// <summary>The Windows VST2 bridge's presence on Linux: a bundled wine loader and the x86/x64 helpers beside the app.</summary>
public static class WineBridgeSupport
{
    public const string Variable = "TABFORGE_WINE";
    public const string BundleRelativePath = "wine/bin/wine";
    public const string MissingText =
        "This is a Windows 32-bit VST2 plug-in. TabForge's Windows plug-in bridge (Wine) is not included in this build, so it cannot be loaded here.";
    public const string Missing64Text =
        "This is a Windows 64-bit VST2 plug-in. Its 64-bit helper (tfvst64.exe) is missing from the TabForge folder, so the plug-in was not loaded.";
    public const string MissingVst3Text =
        "This is a Windows 32-bit VST3 plug-in. Its 32-bit helper (tfvst32.exe) or VST3 shim (tfvst3-x86.dll) is missing from the TabForge folder, so the plug-in was not loaded.";
    public const string MissingVst3_64Text =
        "This is a Windows 64-bit VST3 plug-in. Its 64-bit helper (tfvst64.exe) or VST3 shim (tfvst3.dll) is missing from the TabForge folder, so the plug-in was not loaded.";
    public const string NoWineVst3Text =
        "This is a Windows 32-bit VST3 plug-in. TabForge's Windows plug-in bridge (Wine) is not included in this build, so it cannot be loaded here.";
    public const string NoWineVst3_64Text =
        "This is a Windows 64-bit VST3 plug-in. TabForge's Windows plug-in bridge (Wine) is not included in this build, so it cannot be loaded here.";
    public const string NoWine64Text =
        "This is a Windows 64-bit VST2 plug-in. TabForge's Windows plug-in bridge (Wine) is not included in this build, so it cannot be loaded here.";
    public const string MacText = "Windows VST2 plug-ins can only run on Windows, or on Linux through TabForge's Wine bridge, so this file cannot be loaded here.";

    /// <summary>True when the bundled Wine loader and the 32-bit helper exist. File existence only: the prefix and Wine itself are the engine's job.</summary>
    public static bool Available => HostSystem.Current == HostOs.Linux && WineExecutable() is not null && File.Exists(HelperPath);

    /// <summary>True when the bundled Wine loader and the 64-bit helper exist.</summary>
    public static bool Available64 => HostSystem.Current == HostOs.Linux && WineExecutable() is not null && File.Exists(HelperPath64);

    /// <summary>True when either helper is present; the scanner uses it to add the private prefix's VST2 folders.</summary>
    public static bool AnyAvailable => HostSystem.Current == HostOs.Linux && WineExecutable() is not null && (File.Exists(HelperPath) || File.Exists(HelperPath64));

    /// <summary>The 32-bit helper beside the app (the same PE the Windows 32-bit bridge uses).</summary>
    public static string HelperPath => Path.Combine(AppContext.BaseDirectory, "tfvst32.exe");

    /// <summary>The 64-bit helper beside the app (the Wine 64-bit VST2 route).</summary>
    public static string HelperPath64 => Path.Combine(AppContext.BaseDirectory, "tfvst64.exe");

    /// <summary>The wine loader: TABFORGE_WINE when it points at an existing file, else &lt;app&gt;/wine/bin/wine (or wine64).</summary>
    public static string? WineExecutable(string? appFolder = null, Func<string, string?>? environment = null)
    {
        environment ??= Environment.GetEnvironmentVariable;
        var configured = environment(Variable);
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return Path.GetFullPath(configured);
        var root = string.IsNullOrWhiteSpace(appFolder) ? AppContext.BaseDirectory : appFolder;
        foreach (var name in new[] { "wine", "wine64" })
        {
            var candidate = Path.Combine(root, "wine", "bin", name);
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }
        return null;
    }

    /// <summary>The private prefix the engine creates and reuses; never ~/.wine.</summary>
    public static string PrefixPath => Path.Combine(AppFolders.Current.Data, "wine", "prefix");

    /// <summary>Explains why a hand-picked Windows DLL was refused on this OS.</summary>
    public static string RefusalFor(HostOs os, bool sixtyFourBit = false) => os switch
    {
        HostOs.Linux => sixtyFourBit ? NoWine64Text : MissingText,
        HostOs.MacOS => MacText,
        _ => sixtyFourBit ? NoWine64Text : MissingText,
    };
}
