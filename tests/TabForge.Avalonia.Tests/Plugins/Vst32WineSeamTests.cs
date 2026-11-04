using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TabForge.AudioEngine.Isolation;
using TabForge.AudioEngine.Plugins;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the Linux launch seam that can be proven on Windows without running Wine: the wine helper-first argument list,
// the private-prefix environment, Z: path conversion and the explicit block path. Wine itself is unproven here.
// Does not own: the TCP helper process (Vst32TcpBridgeTests) or the Windows named-pipe launch (Vst32RoutingTests).
public sealed class Vst32WineSeamTests
{
    [Fact]
    public void WineLaunchPutsTheHelperFirstAndUsesTheTcpMode()
    {
        using var folder = new Vst32TestAssets.TempFolder();
        var runtime = Runtime(folder);
        var plugin = Path.Combine(folder.Path, "Plug32.dll");
        File.WriteAllBytes(plugin, Vst32TestAssets.PeImage(x86: true));
        var spec = new TabForge.Audio.Contracts.PluginSpec(plugin, "VST2", false, true, 100, null, "Plug32", LocationTrusted: true);

        var launch = Vst32Bridge.LaunchWine(spec, runtime);
        var block = WineRuntime.ToWindowsPath("/tmp/tf-wine-abc.shm");
        var arguments = launch.Arguments("session", 4321, 48000, 256, spec, "/tmp/engine.log", 45678, block);

        Assert.Equal(runtime.WineExecutable, launch.ExecutablePath);
        Assert.Equal(runtime.HelperPath, launch.HelperPath);
        Assert.Equal((PluginHostLaunch.Vst32TcpHostMode, PluginHostTransport.Tcp, 2), (launch.Mode, launch.Transport, launch.BufferSlots));
        Assert.True(launch.IsVst32Bridge && launch.IsTcp);
        Assert.Equal(new[]
        {
            runtime.HelperPath, "--vst32-host-tcp", "45678", block, "48000", "256", "0", "VST2", plugin, "none", "/tmp/engine.log", "location", "2", "none",
        }, arguments);
        Assert.StartsWith(@"Z:\", block);
        Assert.NotNull(launch.Environment);
    }

    [Fact]
    public void WineLaunchForA64BitVst2UsesThe64BitHelperAndReportsArch64()
    {
        using var folder = new Vst32TestAssets.TempFolder();
        var runtime = Runtime(folder);
        var plugin = Path.Combine(folder.Path, "Plug64.dll");
        File.WriteAllBytes(plugin, Vst32TestAssets.PeImage(x86: false));
        var spec = new TabForge.Audio.Contracts.PluginSpec(plugin, "VST2", false, true, 100, null, "Plug64", LocationTrusted: true);

        var launch = Vst32Bridge.LaunchWine(spec, runtime);
        var block = WineRuntime.ToWindowsPath("/tmp/tf-wine-x64.shm");
        var arguments = launch.Arguments("session", 4321, 48000, 256, spec, "/tmp/engine.log", 45679, block);

        Assert.Equal(runtime.WineExecutable, launch.ExecutablePath);
        Assert.Equal(runtime.HelperPath64, launch.HelperPath);
        Assert.Equal(64, launch.BridgeBitness);
        Assert.Equal((PluginHostLaunch.Vst32TcpHostMode, PluginHostTransport.Tcp, 2), (launch.Mode, launch.Transport, launch.BufferSlots));
        Assert.Equal(new[]
        {
            runtime.HelperPath64, "--vst32-host-tcp", "45679", block, "48000", "256", "0", "VST2", plugin, "none", "/tmp/engine.log", "location", "2", "none",
        }, arguments);
        Assert.StartsWith(@"Z:\", block);
    }

    [Fact]
    public void WineEnvironmentUsesThePrivatePrefixAndClearsHostVariables()
    {
        using var folder = new Vst32TestAssets.TempFolder();
        var runtime = Runtime(folder);
        var env = runtime.EnvironmentFor();

        Assert.Equal(runtime.PrefixPath, env["WINEPREFIX"]);
        Assert.Equal("mscoree,mshtml=", env["WINEDLLOVERRIDES"]);
        Assert.Equal("-all", env["WINEDEBUG"]);
        Assert.Equal(runtime.WineExecutable, env["WINELOADER"]);
        Assert.Equal(string.Empty, env["LD_LIBRARY_PATH"]);
        Assert.Equal(string.Empty, env["LD_PRELOAD"]);
        Assert.StartsWith(Path.Combine(runtime.BundleRoot, "bin"), env["PATH"]);
        Assert.Contains(Path.Combine(runtime.BundleRoot, "lib", "wine", "x86_64-unix"), env["WINEDLLPATH"]);
        var blockPath = WineRuntime.BlockPath("abc");
        Assert.Equal("tf-wine-abc.shm", Path.GetFileName(blockPath));
        Assert.StartsWith("tabforge-shm", Path.GetFileName(Path.GetDirectoryName(blockPath)));
    }

    /// <summary>Regression for Stage 13 A7: the Z: path is normalised first, so ".." never reaches the helper argument.</summary>
    [Fact]
    public void WinePathNormalisesDotsBeforeItBecomesAZPath()
    {
        var path = WineRuntime.ToWindowsPath("/tmp/../plugins/Plug32.dll");
        Assert.StartsWith(@"Z:\", path);
        Assert.DoesNotContain("..", path);
        Assert.EndsWith(@"plugins\Plug32.dll", path);
    }

    /// <summary>Regression for Stage 13 A9: the stale-block sweep does not unlink a block a live process still holds open.</summary>
    [Vst32WindowsFact]
    public void TheBlockSweepLeavesAnOpenBlockAlone()
    {
        using var folder = new Vst32TestAssets.TempFolder();
        var block = Path.Combine(folder.Path, "tf-wine-open.shm");
        File.WriteAllBytes(block, new byte[128]);
        File.SetLastWriteTimeUtc(block, DateTime.UtcNow - TimeSpan.FromHours(3));
        using (new FileStream(block, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
        {
            Assert.Equal(0, WineRuntime.SweepStaleBlocks(TimeSpan.Zero, folder.Path));
            Assert.True(File.Exists(block));
        }
        Assert.Equal(1, WineRuntime.SweepStaleBlocks(TimeSpan.Zero, folder.Path));
        Assert.False(File.Exists(block));
    }

    [Fact]
    public void ProbeStartOnLinuxIsWineWithTheHelperFirst()
    {
        using var folder = new Vst32TestAssets.TempFolder();
        var runtime = Runtime(folder);
        var plugin = Path.Combine(folder.Path, "Plug32.dll");

        var start = Vst32Bridge.ProbeStartInfo(runtime.HelperPath, plugin, "ABC", runtime);

        Assert.Equal(runtime.WineExecutable, start.FileName);
        Assert.Equal(new[] { runtime.HelperPath, "--probe", WineRuntime.ToWindowsPath(plugin), "ABC", "none" }, start.ArgumentList);
        Assert.Equal(runtime.PrefixPath, start.Environment["WINEPREFIX"]);
        Assert.False(start.UseShellExecute);
    }

    [Fact]
    public void TheTcpSessionPassesTheWindowsPluginPathToTheHelper()
    {
        using var folder = new Vst32TestAssets.TempFolder();
        var runtime = Runtime(folder);
        var plugin = Path.Combine(folder.Path, "Plug32.dll");
        File.WriteAllBytes(plugin, Vst32TestAssets.PeImage(x86: true));
        var spec = new TabForge.Audio.Contracts.PluginSpec(plugin, "VST2", false, true, 100, null, "Plug32", LocationTrusted: true);
        var launch = Vst32Bridge.LaunchWine(spec, runtime);
        List<string>? handed = null;
        var threads = new TabForge.AudioEngine.EngineThreads(new TabForge.AudioEngine.EngineLogWriter());
        var fake = OperatingSystem.IsWindows()
            ? new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/c exit 0")
            : new System.Diagnostics.ProcessStartInfo("/bin/true");
        fake.UseShellExecute = false;
        fake.CreateNoWindow = true;

        Assert.Throws<TimeoutException>(() => new RemotePlugin(spec, 48000, 256, threads, launch,
            connectTimeoutMs: 200, loadTimeoutMs: 200, sessionId: "seam-path", startProcess: start =>
            {
                handed = start.ArgumentList.ToList();
                return System.Diagnostics.Process.Start(fake)!;
            }));

        Assert.NotNull(handed);
        Assert.Equal(PluginHostLaunch.Vst32TcpHostMode, handed![1]);
        Assert.Equal(WineRuntime.ToWindowsPath(plugin), handed[8]);
    }

    [Fact]
    public void AMissingWineHelperIsAReadableRefusal()
    {
        using var folder = new Vst32TestAssets.TempFolder();
        var runtime = Runtime(folder);
        File.Delete(runtime.HelperPath);
        var spec = new TabForge.Audio.Contracts.PluginSpec(Path.Combine(folder.Path, "Plug32.dll"), "VST2", false, true, 100, null, "Plug32", LocationTrusted: true);

        var error = Assert.Throws<InvalidOperationException>(() => Vst32Bridge.LaunchWine(spec, runtime));
        Assert.Contains("tfvst32.exe", error.Message);
        Assert.Contains("32-bit", error.Message);
    }

    [Fact]
    public void LinuxPathConversionUsesTheZWDrive()
    {
        Assert.Equal(@"Z:\tmp\a b\tf-wine-1.shm", WineRuntime.ToWindowsPath("/tmp/a b/tf-wine-1.shm"));
        if (OperatingSystem.IsWindows())
            Assert.Equal(@"C:\tmp\x.shm", WineRuntime.ToWindowsPath(@"C:\tmp\x.shm"));
    }

    [Fact]
    public void TheTcpHelloValidatesTheHelperArchitectureAndToken()
    {
        var token = new string('a', 64);
        using var valid64 = TcpHello(64, token);
        string helper, wine;
        using (var reader = new BinaryReader(valid64))
            RemotePlugin.ReadBridgeTcpHello(reader, token, 48000, 256, 2, expectedArch: 64, out helper, out wine);
        Assert.Equal("tfvst64/1", helper);
        Assert.Equal("wine-test", wine);

        using var wrongArch = TcpHello(32, token);
        using var wrongReader = new BinaryReader(wrongArch);
        var error = Assert.Throws<InvalidOperationException>(() =>
            RemotePlugin.ReadBridgeTcpHello(wrongReader, token, 48000, 256, 2, expectedArch: 64, out _, out _));
        Assert.Contains("arch 32 (expected 64)", error.Message);

        using var wrongToken = TcpHello(64, token);
        using var tokenReader = new BinaryReader(wrongToken);
        var tokenError = Assert.Throws<InvalidOperationException>(() =>
            RemotePlugin.ReadBridgeTcpHello(tokenReader, new string('b', 64), 48000, 256, 2, expectedArch: 64, out _, out _));
        Assert.Contains("token mismatch", tokenError.Message);

        static MemoryStream TcpHello(int arch, string tokenHex)
        {
            var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(RemotePlugin.BridgeTcpHelloMagic);
                writer.Write(RemotePlugin.BridgeProtocolVersion);
                writer.Write(RemotePlugin.BridgeHeaderSize);
                writer.Write(256); writer.Write(48000); writer.Write(2); writer.Write(arch); writer.Write(0);
                writer.Write("tfvst64/1"); writer.Write("wine-test");
                writer.Write(Convert.FromHexString(tokenHex));
            }
            stream.Position = 0;
            return stream;
        }
    }

    [Fact]
    public void TheTrustPathHashesTheWindowsDllItself()
    {
        using var folder = new Vst32TestAssets.TempFolder();
        var dll = folder.Write("Trusted32.dll", Vst32TestAssets.PeImage(x86: true));
        var trust = new TabForge.Plugins.PluginTrust();
        var settings = new TabForge.Services.PluginSettings();

        trust.Approve(settings, dll, new Vst32TestAssets.NoFiles());
        var hash = trust.ProbeHash(dll, settings, new Vst32TestAssets.NoFiles());

        Assert.Equal(Vst32TestAssets.Sha256(dll), hash, ignoreCase: true);
        Assert.Equal(dll, TabForge.Audio.Contracts.PluginIdentity.BinaryOf(dll));
        using var held = TabForge.Audio.Contracts.PluginIdentity.Hold(dll, hash, locationTrusted: false);
        Assert.NotNull(held);
    }

    [Fact]
    public void TheBundledWineLocatorFindsTheLoaderBesideTheApp()
    {
        using var folder = new Vst32TestAssets.TempFolder();
        var wine = Path.Combine(folder.Path, "wine", "bin", "wine");
        Directory.CreateDirectory(Path.GetDirectoryName(wine)!);
        File.WriteAllBytes(wine, []);

        var found = TabForge.Plugins.WineBridgeSupport.WineExecutable(folder.Path, _ => null);

        Assert.Equal(Path.GetFullPath(wine), found);
        Assert.Contains("Wine", TabForge.Plugins.WineBridgeSupport.RefusalFor(TabForge.Platform.HostOs.Linux));
        Assert.Contains("Windows", TabForge.Plugins.WineBridgeSupport.RefusalFor(TabForge.Platform.HostOs.MacOS));
    }

    [Fact]
    public void TheScanAddsThePrivateWinePrefixVst2Folders()
    {
        var inputs = new TabForge.Plugins.PluginScanInputs(TabForge.Platform.HostOs.Linux, "/home/u", "", "", "",
            WinePrefix: "/home/u/.local/share/TabForge/wine/prefix");
        var roots = TabForge.Plugins.PluginScanFolders.Vst2(inputs);

        Assert.Contains(roots, r => r.EndsWith(Path.Combine("Program Files (x86)", "Common Files", "VST2")));
        Assert.Contains(roots, r => r.EndsWith(Path.Combine("Program Files", "Steinberg", "VstPlugins")));
        Assert.Empty(TabForge.Plugins.PluginScanFolders.Vst2(inputs with { Os = TabForge.Platform.HostOs.Windows }));
        Assert.Empty(TabForge.Plugins.PluginScanFolders.Vst2(inputs with { WinePrefix = "" }));
    }

    [Fact]
    public void HandPickingAWindowsDllNeverShowsTheWrongBridgedLabelOffWindows()
    {
        using var folder = new Vst32TestAssets.TempFolder();
        var dll32 = folder.Write("Hand32.dll", Vst32TestAssets.PeImage(x86: true));
        var dll64 = folder.Write("Hand64.dll", Vst32TestAssets.PeImage(x86: false));

        var windows = TabForge.Avalonia.Views.Fx.PluginBrowserSession.FromFile(dll32, out var windowsProblem,
            TabForge.Platform.HostOs.Windows, wineBridge: true);
        Assert.Null(windowsProblem);
        Assert.Equal(TabForge.Plugins.PluginFormats.Bridged32, windows!.Format);

        var linux = TabForge.Avalonia.Views.Fx.PluginBrowserSession.FromFile(dll32, out var linuxProblem,
            TabForge.Platform.HostOs.Linux, wineBridge: false);
        Assert.Null(linux);
        Assert.Contains("Wine", linuxProblem);
        Assert.DoesNotContain("bridged", linuxProblem, StringComparison.OrdinalIgnoreCase);

        var linuxWithWine = TabForge.Avalonia.Views.Fx.PluginBrowserSession.FromFile(dll32, out var wineProblem,
            TabForge.Platform.HostOs.Linux, wineBridge: true);
        Assert.Null(wineProblem);
        Assert.Equal(TabForge.Plugins.PluginFormats.Bridged32, linuxWithWine!.Format);

        var mac = TabForge.Avalonia.Views.Fx.PluginBrowserSession.FromFile(dll32, out var macProblem,
            TabForge.Platform.HostOs.MacOS, wineBridge: false);
        Assert.Null(mac);
        Assert.DoesNotContain("bridged", macProblem, StringComparison.OrdinalIgnoreCase);

        var linux64 = TabForge.Avalonia.Views.Fx.PluginBrowserSession.FromFile(dll64, out var linux64Problem,
            TabForge.Platform.HostOs.Linux, wineBridge: true);
        Assert.Null(linux64);
        Assert.Contains("64-bit", linux64Problem);

        var linux64WithHelper = TabForge.Avalonia.Views.Fx.PluginBrowserSession.FromFile(dll64, out var linux64WineProblem,
            TabForge.Platform.HostOs.Linux, wineBridge: true, wineBridge64: true);
        Assert.Null(linux64WineProblem);
        Assert.Equal(TabForge.Plugins.PluginFormats.Bridged64, linux64WithHelper!.Format);

        var linux64NoWine = TabForge.Avalonia.Views.Fx.PluginBrowserSession.FromFile(dll64, out var linux64NoWineProblem,
            TabForge.Platform.HostOs.Linux, wineBridge: false, wineBridge64: false);
        Assert.Null(linux64NoWine);
        Assert.Contains("Wine", linux64NoWineProblem);
        Assert.Contains("64-bit", linux64NoWineProblem);

        var windows64 = TabForge.Avalonia.Views.Fx.PluginBrowserSession.FromFile(dll64, out var windows64Problem,
            TabForge.Platform.HostOs.Windows, wineBridge: true, wineBridge64: false);
        Assert.Null(windows64Problem);
        Assert.Equal(TabForge.Plugins.PluginFormats.Vst2, windows64!.Format);
    }

    [Fact]
    public void AMissing64BitWineHelperIsAReadableRefusal()
    {
        using var folder = new Vst32TestAssets.TempFolder();
        var runtime = Runtime(folder);
        File.Delete(runtime.HelperPath64);
        var spec = new TabForge.Audio.Contracts.PluginSpec(Path.Combine(folder.Path, "Plug64.dll"), "VST2", false, true, 100, null, "Plug64", LocationTrusted: true);
        File.WriteAllBytes(spec.Path, Vst32TestAssets.PeImage(x86: false));

        var error = Assert.Throws<InvalidOperationException>(() => Vst32Bridge.LaunchWine(spec, runtime));
        Assert.Contains("tfvst64.exe", error.Message);
        Assert.Contains("64-bit", error.Message);
    }

    private static WineRuntime Runtime(Vst32TestAssets.TempFolder folder)
    {
        var wine = Path.Combine(folder.Path, "bundle", "wine", "bin", "wine");
        var helper = Path.Combine(folder.Path, "tfvst32.exe");
        var helper64 = Path.Combine(folder.Path, "tfvst64.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(wine)!);
        Directory.CreateDirectory(Path.Combine(folder.Path, "bundle", "wine", "lib", "wine", "x86_64-unix"));
        File.WriteAllBytes(wine, []);
        File.WriteAllBytes(helper, []);
        File.WriteAllBytes(helper64, []);
        return new WineRuntime(wine, helper, Path.Combine(folder.Path, "prefix"), "wine-test", helper64);
    }
}
