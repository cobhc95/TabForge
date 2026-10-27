using System.Runtime.InteropServices;
using TabForge.Audio.Contracts;

namespace TabForge.AudioEngine.Plugins;

// Owns: turning a plug-in path into a loaded native module per operating system (binary lookup by the bundle layout, then NativeLibrary).
// Does not own: trust (PluginIdentity.Hold runs before this), the VST3 module the tfvst3 shim loads itself, VST2 (Vst2Plugin keeps LoadLibraryEx; Windows only).
// Tests: PluginModuleLoaderTests (path resolution per injected OS).

/// <summary>Loads the native module of a plug-in file or bundle. VST2 is not behind it: VST2 hosting is Windows-only by decision.</summary>
public interface IPluginModuleLoader
{
    /// <summary>The binary that would be loaded for <paramref name="pluginPath"/> (.clap or .vst3 file / bundle), or null when it is not found in this system's layout.</summary>
    string? ResolveBinary(string pluginPath);

    /// <summary>Loads the module of <paramref name="pluginPath"/> and returns its handle; throws <see cref="FileNotFoundException"/> or <see cref="BadImageFormatException"/> with a readable message.</summary>
    IntPtr Load(string pluginPath);

    /// <summary>Releases a handle from <see cref="Load"/>.</summary>
    void Free(IntPtr module);
}

/// <summary>The real loader: Windows LoadLibrary, macOS / Linux dlopen (both through <see cref="NativeLibrary"/>), with the bundle layout of <paramref name="os"/>.</summary>
public sealed class NativePluginModuleLoader : IPluginModuleLoader
{
    private readonly ModuleOs _os;
    private readonly IModuleFileView _files;

    public NativePluginModuleLoader() : this(PluginModulePaths.Current, DiskModuleFileView.Instance) { }

    /// <param name="os">Injected so the path rules of every system can be tested on any machine.</param>
    public NativePluginModuleLoader(ModuleOs os, IModuleFileView files) { _os = os; _files = files; }

    public string? ResolveBinary(string pluginPath) => PluginModulePaths.BinaryOf(pluginPath, _os, _files);

    public IntPtr Load(string pluginPath)
    {
        var binary = ResolveBinary(pluginPath) ?? throw new FileNotFoundException($"The plug-in file was not found for this system: {pluginPath}");
        try { return NativeLibrary.Load(binary); }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException)
        {
            throw new BadImageFormatException($"'{Path.GetFileName(binary)}' could not be loaded ({ex.Message.Trim()}). It may be built for another system or CPU.");
        }
    }

    public void Free(IntPtr module) { if (module != IntPtr.Zero) NativeLibrary.Free(module); }
}
