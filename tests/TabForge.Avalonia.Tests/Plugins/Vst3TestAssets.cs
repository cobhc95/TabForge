using System;
using System.IO;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine;
using TabForge.AudioEngine.Isolation;
using TabForge.AudioEngine.Plugins;
using TabForge.Plugins;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: locating the real Windows VST3 fixtures (never committed) and starting the helper with the VST3 format.
// Does not own: what each test asserts (Vst3WineLinuxTests, Vst3NativeTests).
internal static class Vst3TestAssets
{
    private const int Rate = 48000;

    /// <summary>The workflow-downloaded 64-bit VST3 instrument bundle (Dexed 1.1.0).</summary>
    public static string? RealInstrument => FirstFile(
        Environment.GetEnvironmentVariable("TABFORGE_WINE_REAL_VST3"),
        @"C:\tfrel-tmp\vst3\dexed\Dexed.vst3");

    /// <summary>
    /// The real 32-bit VST3 effect (TDR Nova 2.2.3, flat x86 file): the workflow download on CI,
    /// else the test-only copy in C:\tfrel-tmp\vst32, else the maker's extracted folder. Never committed.
    /// </summary>
    public static string? RealEffect32 => FirstFile(
        Environment.GetEnvironmentVariable("TABFORGE_WINE_REAL_VST3_32"),
        @"C:\tfrel-tmp\vst32\TDRNova32.vst3",
        @"C:\tfrel-tmp\dl\tdrnova\ex\TDR Nova (no installer)\VST3\Win32\TDR Nova.vst3");

    /// <summary>The same official release's 64-bit VST3 effect.</summary>
    public static string? RealEffect64 => FirstFile(
        Environment.GetEnvironmentVariable("TABFORGE_WINE_REAL_VST3_64"),
        @"C:\tfrel-tmp\dl\tdrnova\ex\TDR Nova (no installer)\VST3\x64\TDR Nova.vst3");

    public static PluginSpec Spec(string path, bool instrument) => new(
        path, "VST3", instrument, Enabled: true, Wet: 100, State: null, Name: Path.GetFileNameWithoutExtension(path),
        Id: "vst3", LocationTrusted: false, ExpectedSha256: Vst32TestAssets.Sha256(Vst3Windows.Binary(path, sixtyFourBit: true)
            ?? Vst3Windows.Binary(path, sixtyFourBit: false) ?? path));

    /// <summary>Starts the 64-bit helper over the Windows named pipe (local Windows checks; CI uses the Wine route).</summary>
    public static (RemotePlugin Plugin, EngineThreads Threads) StartNative64(string path, bool instrument, int maxBlock = 1024) =>
        StartNative(path, instrument, maxBlock, sixtyFourBit: true);

    /// <summary>Starts the 32-bit helper over the Windows named pipe (local Windows checks).</summary>
    public static (RemotePlugin Plugin, EngineThreads Threads) StartNative32(string path, bool instrument, int maxBlock = 1024) =>
        StartNative(path, instrument, maxBlock, sixtyFourBit: false);

    private static (RemotePlugin Plugin, EngineThreads Threads) StartNative(string path, bool instrument, int maxBlock, bool sixtyFourBit)
    {
        var helper = (sixtyFourBit ? Vst32TestAssets.HelperPath64 : Vst32TestAssets.HelperPath)
            ?? throw new InvalidOperationException(sixtyFourBit ? "tfvst64.exe is not built" : "tfvst32.exe is not built");
        var launch = new PluginHostLaunch(helper, PluginHostLaunch.Vst32HostMode, 2, BridgeBitness: sixtyFourBit ? 64 : 32, Format: "VST3");
        var threads = new EngineThreads(new EngineLogWriter());
        return (new RemotePlugin(Spec(path, instrument), Rate, maxBlock, threads, launch), threads);
    }

    /// <summary>Starts through the real Linux decision (Vst32Bridge.Launch + WineRuntime) over TCP and the file block.</summary>
    public static (RemotePlugin Plugin, EngineThreads Threads) StartWine(string path, bool instrument, int maxBlock = 1024)
    {
        var spec = Spec(path, instrument);
        var folder = Path.GetDirectoryName(Vst32TestAssets.HelperPath64 ?? Vst32TestAssets.HelperPath!)!;
        var launch = Vst32Bridge.Launch(spec, helperFolder: folder);
        var threads = new EngineThreads(new EngineLogWriter());
        return (new RemotePlugin(spec, Rate, maxBlock, threads, launch), threads);
    }

    /// <summary>
    /// Starts through the real launch decision (<see cref="Vst32Bridge.Launch"/>): the named-pipe helper on Windows,
    /// the Wine TCP route on Linux. Stage 5 uses it to prove the Windows 32-bit VST3 route end to end.
    /// </summary>
    public static (RemotePlugin Plugin, EngineThreads Threads) StartRouted(string path, bool instrument, int maxBlock = 1024)
    {
        var folder = Path.GetDirectoryName(Vst32TestAssets.HelperPath
            ?? throw new InvalidOperationException("tfvst32.exe is not built"))!;
        var spec = Spec(path, instrument);
        var launch = Vst32Bridge.Launch(spec, helperFolder: folder);
        var threads = new EngineThreads(new EngineLogWriter());
        return (new RemotePlugin(spec, Rate, maxBlock, threads, launch), threads);
    }

    private static string? FirstFile(params string?[] candidates) =>
        Array.Find(candidates, path => !string.IsNullOrWhiteSpace(path) && (File.Exists(path) || Directory.Exists(path)));
}

/// <summary>A test that needs the real 64-bit VST3 instrument (workflow-downloaded).</summary>
internal sealed class Vst3WineInstrumentFactAttribute : FactAttribute
{
    public Vst3WineInstrumentFactAttribute()
    {
        if (!OperatingSystem.IsLinux()) Skip = "The Wine VST3 test runs on the Linux CI runner";
        else if (Vst32TestAssets.WineLoader is null) Skip = "TABFORGE_WINE does not point at a Wine loader";
        else if (Vst32TestAssets.HelperPath64 is null) Skip = "tfvst64.exe is not built";
        else if (Vst3TestAssets.RealInstrument is null) Skip = "TABFORGE_WINE_REAL_VST3 is not set";
    }
}

/// <summary>A test that needs the real 32-bit VST3 effect (workflow-downloaded).</summary>
internal sealed class Vst3WineEffect32FactAttribute : FactAttribute
{
    public Vst3WineEffect32FactAttribute()
    {
        if (!OperatingSystem.IsLinux()) Skip = "The Wine VST3 test runs on the Linux CI runner";
        else if (Vst32TestAssets.WineLoader is null) Skip = "TABFORGE_WINE does not point at a Wine loader";
        else if (Vst32TestAssets.HelperPath is null) Skip = "tfvst32.exe is not built";
        else if (Vst3TestAssets.RealEffect32 is null) Skip = "TABFORGE_WINE_REAL_VST3_32 is not set";
    }
}
