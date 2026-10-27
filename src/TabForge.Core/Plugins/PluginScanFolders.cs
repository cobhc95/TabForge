using System.IO;
using TabForge.Platform;

namespace TabForge.Plugins;

// Owns: the standard plug-in folders per operating system (VST3, and CLAP when the user opted in), from explicit inputs so every system can be checked on any machine.
// Does not own: scanning (VstScannerService), the user's own folders (PluginSettings), the app folders (IAppFolders).
// Tests: PluginScanFolderTests.

/// <summary>The inputs the folder rules read; <see cref="Current"/> takes them from this machine.</summary>
public sealed record PluginScanInputs(HostOs Os, string Home, string CommonProgramFiles, string ProgramFiles, string UserPluginRoot, string LocalAppData = "")
{
    public static PluginScanInputs Current() => new(HostSystem.Current, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles), ProgramFolders.Current.ProgramFiles, AppFolders.Current.UserPluginRoot);
}

public static class PluginScanFolders
{
    /// <summary>
    /// The standard VST3 folders. Windows is the list the scanner always used (Common Files\VST3, Program Files\Common Files\VST3,
    /// VstPlugins, Steinberg\VSTPlugins, Common Files\VST2, the per-user Programs\Common\VST3); macOS and Linux use the VST3 specification's folders.
    /// </summary>
    public static IReadOnlyList<string> Vst3(PluginScanInputs i)
    {
        var roots = new List<string>();
        switch (i.Os)
        {
            case HostOs.Windows:
                Add(roots, i.CommonProgramFiles, "VST3");
                Add(roots, i.ProgramFiles, "Common Files", "VST3");
                Add(roots, i.ProgramFiles, "VstPlugins");
                Add(roots, i.ProgramFiles, "Steinberg", "VSTPlugins");
                Add(roots, i.CommonProgramFiles, "VST2");
                Add(roots, i.UserPluginRoot, "VST3");
                break;
            case HostOs.MacOS:
                roots.Add("/Library/Audio/Plug-Ins/VST3");
                Add(roots, i.Home, "Library", "Audio", "Plug-Ins", "VST3");
                break;
            default:
                Add(roots, i.Home, ".vst3");
                roots.Add("/usr/lib/vst3");
                roots.Add("/usr/local/lib/vst3");
                break;
        }
        return roots.Distinct(PathComparer.Default).ToList();
    }

    /// <summary>The standard CLAP folders (scanned only when the user turned CLAP on).</summary>
    public static IReadOnlyList<string> Clap(PluginScanInputs i)
    {
        var roots = new List<string>();
        switch (i.Os)
        {
            case HostOs.Windows:
                Add(roots, i.CommonProgramFiles, "CLAP");
                Add(roots, i.UserPluginRoot, "CLAP");
                break;
            case HostOs.MacOS:
                roots.Add("/Library/Audio/Plug-Ins/CLAP");
                Add(roots, i.Home, "Library", "Audio", "Plug-Ins", "CLAP");
                break;
            default:
                Add(roots, i.Home, ".clap");
                roots.Add("/usr/lib/clap");
                break;
        }
        return roots.Distinct(PathComparer.Default).ToList();
    }

    /// <summary>The standard folders as shown to the user, with environment or home names, never an expanded user path.</summary>
    public static IReadOnlyList<string> Labels(HostOs os) => os switch
    {
        HostOs.Windows => new[]
        {
            @"%CommonProgramFiles%\VST3", @"%ProgramFiles%\Common Files\VST3", @"%ProgramFiles%\VstPlugins", @"%ProgramFiles%\Steinberg\VSTPlugins",
            @"%CommonProgramFiles%\VST2", @"%LocalAppData%\Programs\Common\VST3",
        },
        HostOs.MacOS => new[] { "/Library/Audio/Plug-Ins/VST3", "~/Library/Audio/Plug-Ins/VST3" },
        _ => new[] { "~/.vst3", "/usr/lib/vst3", "/usr/local/lib/vst3" },
    };

    /// <summary>The CLAP folders as shown to the user.</summary>
    public static IReadOnlyList<string> ClapLabels(HostOs os) => os switch
    {
        HostOs.Windows => new[] { @"%CommonProgramFiles%\CLAP", @"%LocalAppData%\Programs\Common\CLAP" },
        HostOs.MacOS => new[] { "/Library/Audio/Plug-Ins/CLAP", "~/Library/Audio/Plug-Ins/CLAP" },
        _ => new[] { "~/.clap", "/usr/lib/clap" },
    };

    /// <summary>VST2 hosting exists on Windows only (decision 6): the scanner skips VST2 libraries elsewhere.</summary>
    public static bool Vst2Supported(HostOs os) => os == HostOs.Windows;

    private static void Add(List<string> roots, string root, params string[] parts)
    {
        if (string.IsNullOrWhiteSpace(root)) return;
        roots.Add(parts.Aggregate(root, Path.Combine));
    }
}
