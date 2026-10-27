using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine.Plugins;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the macOS / Linux side of the VST3 shim that can be checked on any machine: the module check before the shim is asked (through IPluginModuleLoader),
//   and that the native sources (Source/native/tfvst3/posix) carry the module entry points of each system, export the path functions as UTF-16 and are wired in CMake.
// Does not own: building or running the native code (the native-ci workflow does, on Linux and macOS).
// Tests: itself.
public sealed class Vst3ShimWiringTests
{
    private sealed class Files : IModuleFileView
    {
        private readonly HashSet<string> _files, _dirs;
        public Files(IEnumerable<string> files, IEnumerable<string> dirs) { _files = files.ToHashSet(StringComparer.Ordinal); _dirs = dirs.ToHashSet(StringComparer.Ordinal); }
        public bool FileExists(string path) => _files.Contains(path);
        public bool DirectoryExists(string path) => _dirs.Contains(path);
        public IEnumerable<string> FilesIn(string directory) => _files.Where(f => f.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.Ordinal));
    }

    private static string Native(string name) => Path.Combine(ArchitectureScan.RepoRoot(), "Source", "native", "tfvst3", "posix", name);

    [Fact]
    public void WindowsAsksNothingBeforeTheShim()
    {
        Vst3Plugin.RequireModule("C:/nowhere/Amp.vst3", new NativePluginModuleLoader(ModuleOs.Windows, new Files(Array.Empty<string>(), Array.Empty<string>())), windows: true);
    }

    [Fact]
    public void ABundleWithoutAModuleForThisSystemIsRefusedPlainly()
    {
        var loader = new NativePluginModuleLoader(ModuleOs.Linux, new Files(Array.Empty<string>(), Array.Empty<string>()));
        var ex = Assert.Throws<FileNotFoundException>(() => Vst3Plugin.RequireModule("/usr/lib/vst3/Amp.vst3", loader, windows: false));
        Assert.Contains("Amp.vst3", ex.Message);
    }

    [Fact]
    public void ALinuxBundleWithItsSoPasses()
    {
        var bundle = Path.Combine("/usr", "lib", "vst3", "Amp.vst3");
        var arch = PluginModulePaths.Vst3ArchFolder(ModuleOs.Linux, System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.Arm64);
        var so = Path.Combine(bundle, "Contents", arch, "Amp.so");
        var loader = new NativePluginModuleLoader(ModuleOs.Linux, new Files(new[] { so }, new[] { bundle, Path.Combine(bundle, "Contents", arch) }));
        Vst3Plugin.RequireModule(bundle, loader, windows: false);
    }

    [Fact]
    public void TheLinuxModuleLoadsWithDlopenAndCallsModuleEntryAndExit()
    {
        var text = File.ReadAllText(Native("module_linux.cpp"));
        foreach (var needle in new[] { "dlopen", "dlclose", "ModuleEntry", "ModuleExit", "GetPluginFactory", "Module::create", "-linux" })
            Assert.Contains(needle, text);
    }

    [Fact]
    public void TheMacModuleLoadsWithCfBundleAndCallsBundleEntryAndExit()
    {
        var text = File.ReadAllText(Native("module_mac.mm"));
        foreach (var needle in new[] { "CFBundleCreate", "CFBundleLoadExecutableAndReturnError", "CFBundleGetFunctionPointerForName", "bundleEntry", "bundleExit", "GetPluginFactory", "Module::create" })
            Assert.Contains(needle, text);
    }

    [Fact]
    public void ThePathFunctionsAreExportedAsUtf16AndTheOtherNativeNamesAreRenamedInCMake()
    {
        var abi = File.ReadAllText(Native("tfvst3_posix.cpp"));
        Assert.Contains("tfv3_list_classes (const char16_t* path", abi);
        Assert.Contains("tfv3_create (const char16_t* path", abi);
        var cmake = File.ReadAllText(Native("CMakeLists.txt"));
        Assert.Contains("tfv3_list_classes=tfv3_list_classes_native", cmake);
        Assert.Contains("tfv3_create=tfv3_create_native", cmake);
        Assert.Contains("module_mac.mm", cmake);
        Assert.Contains("module_linux.cpp", cmake);
        Assert.Contains("compat", cmake);
        Assert.Contains("typedef void* HWND;", File.ReadAllText(Native(Path.Combine("compat", "windows.h"))));
    }

    [Fact]
    public void TheWindowsProjectAndShimSourcesAreNotTheOnesTheNativeProjectEdits()
    {
        // The macOS / Linux build reuses tfvst3.cpp and tfvst3.h as they are: no portability switches were added to the Windows sources.
        var dir = Path.Combine(ArchitectureScan.RepoRoot(), "Source", "native", "tfvst3");
        Assert.DoesNotContain("posix", File.ReadAllText(Path.Combine(dir, "CMakeLists.txt")));
        Assert.DoesNotContain("__APPLE__", File.ReadAllText(Path.Combine(dir, "tfvst3.cpp")));
    }
}
