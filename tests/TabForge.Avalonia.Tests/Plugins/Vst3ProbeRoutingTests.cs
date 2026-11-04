using System.IO;
using TabForge.AudioEngine.Isolation;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the identify probe's decision to hand a Windows VST3 bundle to the Wine helper on Linux. The previous rule only
// recognised VST2 PEs and 32-bit-only VST3 on Windows, so every Windows VST3 on Linux was loaded natively (and refused)
// before the matrix could run its other checks. The explicit overloads take the OS/helper flags so this can be proven
// on Windows without Wine.
// Does not own: running the helper (Vst3WineLinuxTests), the engine route (Vst3NativeTests/Vst32Bridge).
public sealed class Vst3ProbeRoutingTests
{
    [Fact]
    public void A64BitWindowsVst3GoesToThe64BitWineHelper()
    {
        using var folder = new Vst32TestAssets.TempFolder();
        var flat64 = folder.Write("Probe64.vst3", Vst32TestAssets.PeImage(x86: false, export: "GetPluginFactory"));

        Assert.True(PluginInfoProbe.UsesVst64Helper(flat64, linux: true, x64Helper: true));
        Assert.False(PluginInfoProbe.UsesVst32Helper(flat64, windows: false, linux: true, x86Helper: true));
        Assert.True(PluginInfoProbe.BridgeIsSixtyFourBit(flat64));
        // Windows keeps its exact rule: a 64-bit VST3 stays native and never goes to the x86 helper.
        Assert.False(PluginInfoProbe.UsesVst32Helper(flat64, windows: true, linux: false, x86Helper: false));
        Assert.False(PluginInfoProbe.UsesVst64Helper(flat64, linux: false, x64Helper: false));
    }

    [Fact]
    public void A32BitWindowsVst3GoesToThe32BitHelperOnLinuxAndWindows()
    {
        using var folder = new Vst32TestAssets.TempFolder();
        var flat32 = folder.Write("Probe32.vst3", Vst32TestAssets.PeImage(x86: true, export: "GetPluginFactory"));

        Assert.True(PluginInfoProbe.UsesVst32Helper(flat32, windows: false, linux: true, x86Helper: true));
        Assert.True(PluginInfoProbe.UsesVst32Helper(flat32, windows: true, linux: false, x86Helper: false));
        Assert.False(PluginInfoProbe.UsesVst64Helper(flat32, linux: true, x64Helper: true));
        Assert.False(PluginInfoProbe.BridgeIsSixtyFourBit(flat32));
    }

    [Fact]
    public void A64BitBundleWithBothWindowsBinariesPrefersThe64BitHelper()
    {
        using var folder = new Vst32TestAssets.TempFolder();
        var bundle = Path.Combine(folder.Path, "ProbeBundle.vst3");
        var binary64 = Path.Combine(bundle, "Contents", "x86_64-win", "ProbeBundle.vst3");
        var binary32 = Path.Combine(bundle, "Contents", "x86-win32", "ProbeBundle.vst3");
        Directory.CreateDirectory(Path.GetDirectoryName(binary64)!);
        Directory.CreateDirectory(Path.GetDirectoryName(binary32)!);
        File.WriteAllBytes(binary64, Vst32TestAssets.PeImage(x86: false, export: "GetPluginFactory"));
        File.WriteAllBytes(binary32, Vst32TestAssets.PeImage(x86: true, export: "GetPluginFactory"));

        Assert.True(PluginInfoProbe.UsesVst64Helper(bundle, linux: true, x64Helper: true));
        Assert.False(PluginInfoProbe.UsesVst32Helper(bundle, windows: false, linux: true, x86Helper: true));
        Assert.True(PluginInfoProbe.BridgeIsSixtyFourBit(bundle));
    }

    /// <summary>
    /// Regression for Stage 13 D4: a bundle that also ships a Linux binary stays native. The probe must not send it to
    /// the Wine helper when the engine's route loads it in-process.
    /// </summary>
    [Fact]
    public void ABundleWithALinuxBinaryIsNativeForBothTheProbeAndTheEngine()
    {
        using var folder = new Vst32TestAssets.TempFolder();
        var bundle = Path.Combine(folder.Path, "DualBundle.vst3");
        var windows = Path.Combine(bundle, "Contents", "x86_64-win", "DualBundle.vst3");
        var linux = Path.Combine(bundle, "Contents", "x86_64-linux", "DualBundle.so");
        Directory.CreateDirectory(Path.GetDirectoryName(windows)!);
        Directory.CreateDirectory(Path.GetDirectoryName(linux)!);
        File.WriteAllBytes(windows, Vst32TestAssets.PeImage(x86: false, export: "GetPluginFactory"));
        File.WriteAllBytes(linux, new byte[] { 0x7F, 0x45, 0x4C, 0x46 });

        Assert.True(TabForge.Plugins.Vst3Windows.HasLinuxBinary(bundle));
        Assert.False(PluginInfoProbe.UsesVst64Helper(bundle, linux: true, x64Helper: true));
        Assert.False(PluginInfoProbe.UsesVst32Helper(bundle, windows: false, linux: true, x86Helper: true));
        var spec = new TabForge.Audio.Contracts.PluginSpec(bundle, "", false, true, 100, null, "Dual");
        Assert.Equal(Vst32Bridge.PluginRoute.InProcess, Vst32Bridge.Route(spec, separateProcess: false));
    }
}
