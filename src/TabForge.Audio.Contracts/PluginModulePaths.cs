namespace TabForge.Audio.Contracts;

// Owns: where the loadable binary of a plug-in lies per operating system (VST3 bundle layout, CLAP file or bundle), from an injected system and file view.
// Does not own: loading it (IPluginModuleLoader in the engine), trust (PluginIdentity hashes whatever this returns).
// Tests: PluginModulePathsTests.

/// <summary>The operating-system families a plug-in binary layout depends on.</summary>
public enum ModuleOs
{
    Windows,
    MacOS,
    Linux,
}

/// <summary>The file questions the layout rules ask, so every family can be checked on any machine.</summary>
public interface IModuleFileView
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    /// <summary>Files directly in <paramref name="directory"/> (full paths), or none when it is missing.</summary>
    IEnumerable<string> FilesIn(string directory);
}

/// <summary>The real file system.</summary>
public sealed class DiskModuleFileView : IModuleFileView
{
    public static readonly DiskModuleFileView Instance = new();
    public bool FileExists(string path) => File.Exists(path);
    public bool DirectoryExists(string path) => Directory.Exists(path);
    public IEnumerable<string> FilesIn(string directory) => Directory.Exists(directory) ? Directory.EnumerateFiles(directory) : Array.Empty<string>();
}

/// <summary>Plug-in binary layouts: Windows (.vst3 bundle with x86_64-win, .clap DLL), macOS (bundles with Contents/MacOS), Linux (x86_64-linux / aarch64-linux .so).</summary>
public static class PluginModulePaths
{
    public static ModuleOs Current { get; } = OperatingSystem.IsWindows() ? ModuleOs.Windows : OperatingSystem.IsMacOS() ? ModuleOs.MacOS : ModuleOs.Linux;

    /// <summary>The folder inside a VST3 bundle's Contents that holds the binary ("MacOS" on macOS).</summary>
    public static string Vst3ArchFolder(ModuleOs os, bool arm64) => os switch
    {
        ModuleOs.Windows => "x86_64-win",
        ModuleOs.MacOS => "MacOS",
        _ => arm64 ? "aarch64-linux" : "x86_64-linux",
    };

    /// <summary>The binary of a VST3 bundle, or null when the layout of <paramref name="os"/> is not found. A plain file is returned as it is.</summary>
    public static string? Vst3Binary(string path, ModuleOs os, IModuleFileView files, bool arm64 = false)
    {
        if (files.FileExists(path)) return path;
        if (!files.DirectoryExists(path)) return null;
        var arch = Path.Combine(path, "Contents", Vst3ArchFolder(os, arm64));
        var stem = Path.GetFileName(path.TrimEnd('\\', '/'));
        var baseName = Path.GetFileNameWithoutExtension(stem);
        switch (os)
        {
            case ModuleOs.Windows:
            {
                var named = Path.Combine(arch, stem);
                if (files.FileExists(named)) return named;
                return files.FilesIn(arch).Where(f => f.EndsWith(".vst3", StringComparison.OrdinalIgnoreCase)).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            }
            case ModuleOs.MacOS:
                return FirstOf(files, arch, Path.Combine(arch, baseName), Path.Combine(arch, stem), null);
            default:
                return FirstOf(files, arch, Path.Combine(arch, baseName + ".so"), Path.Combine(arch, stem + ".so"), ".so");
        }
    }

    /// <summary>The binary of a CLAP plug-in: the .clap file itself (Windows, Linux), or Contents/MacOS/&lt;name&gt; of the bundle (macOS). Null when missing.</summary>
    public static string? ClapBinary(string path, ModuleOs os, IModuleFileView files)
    {
        if (files.FileExists(path)) return path;
        if (os != ModuleOs.MacOS || !files.DirectoryExists(path)) return null;
        var dir = Path.Combine(path, "Contents", "MacOS");
        return FirstOf(files, dir, Path.Combine(dir, Path.GetFileNameWithoutExtension(path.TrimEnd('\\', '/'))), Path.Combine(dir, Path.GetFileName(path.TrimEnd('\\', '/'))), null);
    }

    /// <summary>The binary of a plug-in path by its extension: .clap as CLAP, anything else as a VST3 file or bundle.</summary>
    public static string? BinaryOf(string path, ModuleOs os, IModuleFileView files) =>
        path.TrimEnd('\\', '/').EndsWith(".clap", StringComparison.OrdinalIgnoreCase) ? ClapBinary(path, os, files) : Vst3Binary(path, os, files, System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.Arm64);

    private static string? FirstOf(IModuleFileView files, string dir, string first, string second, string? extension)
    {
        if (files.FileExists(first)) return first;
        if (files.FileExists(second)) return second;
        return files.FilesIn(dir).Where(f => extension is null || f.EndsWith(extension, StringComparison.OrdinalIgnoreCase)).OrderBy(f => f, StringComparer.Ordinal).FirstOrDefault();
    }
}
