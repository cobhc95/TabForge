using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine.Editors;
using TabForge.AudioEngine.Plugins;
using TabForge.Platform;
using TabForge.Plugins;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: cross-platform Stage 4 outside the CLAP host: plug-in binary lookup per OS (injected OS and file view), the standard plug-in folders per OS,
//   CLAP opt-in scanning, VST2 being Windows-only, the format names, and the editor host that reports "not supported yet" cleanly.
// Does not own: the CLAP host itself (ClapHostTests).
// Tests: itself.
public sealed class PluginPortabilityTests
{
    /// <summary>A file view over a fixed list of paths (files) and folders, so a layout can be checked for any OS.</summary>
    private sealed class FakeFiles : IModuleFileView
    {
        private readonly HashSet<string> _files, _dirs;
        public FakeFiles(IEnumerable<string> files, IEnumerable<string> dirs) { _files = files.ToHashSet(StringComparer.Ordinal); _dirs = dirs.ToHashSet(StringComparer.Ordinal); }
        public bool FileExists(string path) => _files.Contains(path);
        public bool DirectoryExists(string path) => _dirs.Contains(path);
        public IEnumerable<string> FilesIn(string directory) => _files.Where(f => f.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.Ordinal) && f.IndexOf(Path.DirectorySeparatorChar, directory.Length + 1) < 0);
    }

    private static string J(params string[] parts) => Path.Combine(parts);

    [Fact]
    public void AWindowsVst3BundleResolvesToItsX64WinBinary()
    {
        var bundle = J("C:", "VST3", "Amp.vst3");
        var binary = J(bundle, "Contents", "x86_64-win", "Amp.vst3");
        var files = new FakeFiles(new[] { binary }, new[] { bundle, J(bundle, "Contents", "x86_64-win") });
        Assert.Equal(binary, PluginModulePaths.Vst3Binary(bundle, ModuleOs.Windows, files));
        Assert.Equal(binary, new NativePluginModuleLoader(ModuleOs.Windows, files).ResolveBinary(bundle));
        Assert.Null(PluginModulePaths.Vst3Binary(bundle, ModuleOs.Linux, files));   // the same bundle has no Linux binary
    }

    [Fact]
    public void AWindowsBundleWithAnotherBinaryNameStillFindsTheVst3File()
    {
        var bundle = J("C:", "VST3", "Amp.vst3");
        var arch = J(bundle, "Contents", "x86_64-win");
        var odd = J(arch, "AmpPlugin.vst3");
        var files = new FakeFiles(new[] { odd }, new[] { bundle, arch });
        Assert.Equal(odd, PluginModulePaths.Vst3Binary(bundle, ModuleOs.Windows, files));
    }

    [Fact]
    public void AMacBundleResolvesToContentsMacOs()
    {
        var bundle = J("/Library", "Audio", "Plug-Ins", "VST3", "Amp.vst3");
        var macos = J(bundle, "Contents", "MacOS");
        var named = J(macos, "Amp");
        var files = new FakeFiles(new[] { named }, new[] { bundle, macos });
        Assert.Equal(named, PluginModulePaths.Vst3Binary(bundle, ModuleOs.MacOS, files));
        // A bundle whose executable carries another name: the one file in Contents/MacOS.
        var other = new FakeFiles(new[] { J(macos, "AmpEngine") }, new[] { bundle, macos });
        Assert.Equal(J(macos, "AmpEngine"), PluginModulePaths.Vst3Binary(bundle, ModuleOs.MacOS, other));
    }

    [Fact]
    public void ALinuxBundleResolvesToItsSoForTheCpu()
    {
        var bundle = J("/usr", "lib", "vst3", "Amp.vst3");
        var x64 = J(bundle, "Contents", "x86_64-linux");
        var arm = J(bundle, "Contents", "aarch64-linux");
        var files = new FakeFiles(new[] { J(x64, "Amp.so"), J(arm, "Amp.so") }, new[] { bundle, x64, arm });
        Assert.Equal(J(x64, "Amp.so"), PluginModulePaths.Vst3Binary(bundle, ModuleOs.Linux, files, arm64: false));
        Assert.Equal(J(arm, "Amp.so"), PluginModulePaths.Vst3Binary(bundle, ModuleOs.Linux, files, arm64: true));
    }

    [Fact]
    public void AClapIsAFileOnWindowsAndLinuxAndABundleOnMac()
    {
        var file = J("C:", "CLAP", "Synth.clap");
        Assert.Equal(file, PluginModulePaths.ClapBinary(file, ModuleOs.Windows, new FakeFiles(new[] { file }, Array.Empty<string>())));
        Assert.Equal(file, PluginModulePaths.ClapBinary(file, ModuleOs.Linux, new FakeFiles(new[] { file }, Array.Empty<string>())));
        var bundle = J("/Library", "Audio", "Plug-Ins", "CLAP", "Synth.clap");
        var binary = J(bundle, "Contents", "MacOS", "Synth");
        var mac = new FakeFiles(new[] { binary }, new[] { bundle, J(bundle, "Contents", "MacOS") });
        Assert.Equal(binary, PluginModulePaths.ClapBinary(bundle, ModuleOs.MacOS, mac));
        Assert.Equal(binary, PluginModulePaths.BinaryOf(bundle, ModuleOs.MacOS, mac));   // chosen by the .clap extension
        Assert.Null(PluginModulePaths.ClapBinary(bundle, ModuleOs.Windows, mac));   // a folder named .clap is no Windows plug-in
    }

    [Fact]
    public void ALoaderRefusesAMissingFileWithAReadableMessage()
    {
        var loader = new NativePluginModuleLoader(ModuleOs.Linux, new FakeFiles(Array.Empty<string>(), Array.Empty<string>()));
        var ex = Assert.Throws<FileNotFoundException>(() => loader.Load("/nowhere/Missing.clap"));
        Assert.Contains("Missing.clap", ex.Message);
    }

    [Fact]
    public void TheShippedHashRuleStillFindsTheWindowsBundleBinary()
    {
        // PluginIdentity.BinaryOf goes through the same layout rules now: the answer for a real Windows-layout bundle is unchanged.
        if (!OperatingSystem.IsWindows()) return;   // BinaryOf answers for the layout of the machine it runs on
        var dir = Directory.CreateTempSubdirectory("bundle");
        try
        {
            var bundle = Path.Combine(dir.FullName, "Amp.vst3");
            var arch = Path.Combine(bundle, "Contents", "x86_64-win");
            Directory.CreateDirectory(arch);
            File.WriteAllBytes(Path.Combine(arch, "Amp.vst3"), new byte[] { 1 });
            Assert.Equal(Path.Combine(arch, "Amp.vst3"), PluginIdentity.BinaryOf(bundle));
            File.Move(Path.Combine(arch, "Amp.vst3"), Path.Combine(arch, "Other.vst3"));
            Assert.Equal(Path.Combine(arch, "Other.vst3"), PluginIdentity.BinaryOf(bundle));
            var flat = Path.Combine(dir.FullName, "Flat.dll");
            File.WriteAllBytes(flat, new byte[] { 1 });
            Assert.Equal(flat, PluginIdentity.BinaryOf(flat));
            Assert.Null(PluginIdentity.BinaryOf(Path.Combine(dir.FullName, "missing.vst3")));
        }
        finally { dir.Delete(true); }
    }

    private static PluginScanInputs Inputs(HostOs os) => os switch
    {
        HostOs.Windows => new(os, @"C:\Users\u", @"C:\Program Files\Common Files", @"C:\Program Files", @"C:\Users\u\AppData\Local\Programs\Common"),
        HostOs.MacOS => new(os, "/Users/u", "", "", ""),
        _ => new(os, "/home/u", "", "", ""),
    };

    [Fact]
    public void WindowsKeepsTodaysVst3FolderList()
    {
        var roots = PluginScanFolders.Vst3(Inputs(HostOs.Windows));
        Assert.Equal(new[]
        {
            Path.Combine(@"C:\Program Files\Common Files", "VST3"),
            Path.Combine(@"C:\Program Files", "Common Files", "VST3"),
            Path.Combine(@"C:\Program Files", "VstPlugins"),
            Path.Combine(@"C:\Program Files", "Steinberg", "VSTPlugins"),
            Path.Combine(@"C:\Program Files\Common Files", "VST2"),
            Path.Combine(@"C:\Users\u\AppData\Local\Programs\Common", "VST3"),
        }.Distinct().ToArray(), roots.ToArray());
    }

    [Fact]
    public void MacAndLinuxUseTheVst3SpecificationFolders()
    {
        Assert.Equal(new[] { "/Library/Audio/Plug-Ins/VST3", Path.Combine("/Users/u", "Library", "Audio", "Plug-Ins", "VST3") }, PluginScanFolders.Vst3(Inputs(HostOs.MacOS)).ToArray());
        Assert.Equal(new[] { Path.Combine("/home/u", ".vst3"), "/usr/lib/vst3", "/usr/local/lib/vst3" }, PluginScanFolders.Vst3(Inputs(HostOs.Linux)).ToArray());
    }

    [Fact]
    public void ClapFoldersFollowTheClapSpecificationPerOs()
    {
        Assert.Equal(new[] { Path.Combine(@"C:\Program Files\Common Files", "CLAP"), Path.Combine(@"C:\Users\u\AppData\Local\Programs\Common", "CLAP") }, PluginScanFolders.Clap(Inputs(HostOs.Windows)).ToArray());
        Assert.Equal(new[] { "/Library/Audio/Plug-Ins/CLAP", Path.Combine("/Users/u", "Library", "Audio", "Plug-Ins", "CLAP") }, PluginScanFolders.Clap(Inputs(HostOs.MacOS)).ToArray());
        Assert.Equal(new[] { Path.Combine("/home/u", ".clap"), "/usr/lib/clap" }, PluginScanFolders.Clap(Inputs(HostOs.Linux)).ToArray());
    }

    [Fact]
    public void FolderLabelsNameTheFoldersWithoutAUserPath()
    {
        Assert.Contains(@"%CommonProgramFiles%\VST3", PluginScanFolders.Labels(HostOs.Windows));
        Assert.Contains("~/.vst3", PluginScanFolders.Labels(HostOs.Linux));
        Assert.Contains("~/Library/Audio/Plug-Ins/VST3", PluginScanFolders.Labels(HostOs.MacOS));
        Assert.Contains(@"%CommonProgramFiles%\CLAP", PluginScanFolders.ClapLabels(HostOs.Windows));
        Assert.Equal(PluginScanFolders.Labels(HostSystem.Current), VstScannerService.StandardFolderLabels);
    }

    [Fact]
    public void Vst2IsWindowsOnly()
    {
        Assert.True(PluginScanFolders.Vst2Supported(HostOs.Windows));
        Assert.False(PluginScanFolders.Vst2Supported(HostOs.MacOS));
        Assert.False(PluginScanFolders.Vst2Supported(HostOs.Linux));
    }

    [Fact]
    public void FormatsAreNamedByExtension()
    {
        Assert.Equal("VST3", PluginFormats.Of(@"C:\a\Amp.vst3"));
        Assert.Equal("CLAP", PluginFormats.Of(@"C:\a\Synth.clap"));
        Assert.Equal("CLAP", PluginFormats.Of("/a/Synth.clap/"));
        Assert.Equal("VST2", PluginFormats.Of(@"C:\a\Old.dll"));
        Assert.True(PluginFormats.IsKnown("CLAP") && PluginFormats.IsKnown("VST2") && PluginFormats.IsKnown("VST3"));
        Assert.False(PluginFormats.IsKnown("AU") || PluginFormats.IsKnown(null));
    }

    private sealed class NoFiles : IPluginFileTrust
    {
        public string? FinalPath(string fullPath) => null;
        public bool HasValidEmbeddedSignature(string fullPath) => false;
    }

    [Fact]
    public void AScanListsClapOnlyWhenTheUserTurnedItOn()
    {
        var dir = Directory.CreateTempSubdirectory("clapscan");
        try
        {
            File.WriteAllBytes(Path.Combine(dir.FullName, "Synth.clap"), new byte[] { 1 });
            Directory.CreateDirectory(Path.Combine(dir.FullName, "Amp.vst3"));
            var off = new PluginSettings();
            var found = VstScannerService.Scan(new[] { dir.FullName }, CancellationToken.None, null, new PluginTrust(), new NoFiles(), off, forceRefresh: true);
            Assert.Equal(new[] { "VST3" }, found.Select(p => p.Format).ToArray());   // CLAP is not listed by default: Windows behaviour is unchanged
            var on = new PluginSettings { ScanClap = true };
            found = VstScannerService.Scan(new[] { dir.FullName }, CancellationToken.None, null, new PluginTrust(), new NoFiles(), on, forceRefresh: true);
            Assert.Equal(new[] { "CLAP", "VST3" }, found.Select(p => p.Format).OrderBy(f => f, StringComparer.Ordinal).ToArray());
            Assert.Equal("Synth", found.Single(p => p.Format == "CLAP").Name);
            // Turning it off again drops them even though a scan was cached a moment ago.
            found = VstScannerService.Scan(new[] { dir.FullName }, CancellationToken.None, null, new PluginTrust(), new NoFiles(), off, forceRefresh: false);
            Assert.DoesNotContain(found, p => p.Format == "CLAP");
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public void TheClapFoldersJoinTheScanRootsOnlyWhenOn()
    {
        var off = new PluginSettings();
        var on = new PluginSettings { ScanClap = true };
        var clap = PluginScanFolders.Clap(PluginScanInputs.Current()).Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(VstScannerService.RootsFor(off), clap.Contains);
        foreach (var added in VstScannerService.RootsFor(on).Where(clap.Contains)) Assert.True(Directory.Exists(added));   // only folders that exist are added
        Assert.False(new PluginSettings().ScanClap);   // off by default
    }

    [Fact]
    public void AnUnsupportedEditorHostSaysSoWithoutThrowing()
    {
        var host = new UnsupportedEditorHost();
        Assert.False(host.IsSupported);
        Assert.Contains("not supported yet", host.UnsupportedReason);
        var plugin = new Fake();
        Assert.False(host.Show(plugin, "x", IntPtr.Zero, dark: true));
        Assert.False(host.Show(plugin, "x", IntPtr.Zero, dark: true, docked: true));
        host.CloseFor(plugin);
        host.Idle();
        host.UserClosed = () => { };
        host.CloseAll();
        Assert.Null(host.UserClosed);
        long last = 0;
        Assert.True(host.PumpOnce(ref last, () => true));
        last = 0;
        Assert.False(host.PumpOnce(ref last, () => false));   // the loop ends when the idle callback says so
        Assert.Equal(0, host.OpenCount);
    }

    private sealed class Fake : IPluginInstance
    {
        public string Path => "x";
        public bool IsInstrument => false;
        public bool HasEditor => true;
        public int LatencySamples => 0;
        public void Process(float[][] input, float[][] output, int frames, ReadOnlySpan<BlockMidi> midi, in TransportInfo transport) { }
        public byte[]? GetState() => null;
        public void SetState(byte[] state) { }
        public (int Width, int Height)? OpenEditor(IntPtr parent) => null;
        public void CloseEditor() { }
        public void EditorIdle() { }
        public void Dispose() { }
    }

    [Fact]
    public void TheEditorHostIsTheWin32OneOnWindowsAndTheCleanStubElsewhere()
    {
        var host = PluginEditorHosts.Create(new TabForge.AudioEngine.EngineLogWriter());
        if (OperatingSystem.IsWindows()) { Assert.IsType<EditorWindows>(host); Assert.True(host.IsSupported); }
        else { Assert.IsType<UnsupportedEditorHost>(host); Assert.False(host.IsSupported); }
        host.CloseAll();
    }
}
