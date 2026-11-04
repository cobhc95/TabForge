using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine;
using TabForge.AudioEngine.Isolation;
using TabForge.AudioEngine.Plugins;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: locating the native helper/test plug-in and the real fixtures, the skip rules, and shared audio helpers.
// Does not own: what each test asserts.
internal static class Vst32TestAssets
{
    public const string FixturesFolder = @"C:\tfrel-tmp\vst32";
    public static readonly string[] RealFixtures =
    [
        "Acceleration32", "ADClip932", "Console4Channel32", "Density32", "PurestGain32",
        "TAL-NoiseMaker32", "TAL-BassLine-101-32", "Dexed32",
    ];
    public static readonly string[] InstrumentFixtures = ["TAL-NoiseMaker32", "TAL-BassLine-101-32", "Dexed32"];

    private const int Rate = 48000;

    public static string? RepoRoot { get; } = FindRepoRoot();

    public static string? HelperPath => FirstFile(
        Environment.GetEnvironmentVariable("TABFORGE_TFVST32_EXE"),
        Path.Combine(AppContext.BaseDirectory, "tfvst32.exe"),
        RepoRoot is null ? null : Path.Combine(RepoRoot, "Source", "src", "TabForge.AudioEngine", "native", "tfvst32.exe"));

    /// <summary>The 64-bit sibling, built from the same source (native\tfvst32, x64).</summary>
    public static string? HelperPath64 => FirstFile(
        Environment.GetEnvironmentVariable("TABFORGE_TFVST64_EXE"),
        Path.Combine(AppContext.BaseDirectory, "tfvst64.exe"),
        RepoRoot is null ? null : Path.Combine(RepoRoot, "Source", "src", "TabForge.AudioEngine", "native", "tfvst64.exe"));

    /// <summary>The separate TFVST32_TEST_HOOKS copy (the stall tests only); never the shipped helper.</summary>
    public static string? HookedHelperPath => FirstFile(
        Environment.GetEnvironmentVariable("TABFORGE_TFVST32_HOOKED_EXE"),
        Path.Combine(AppContext.BaseDirectory, "tfvst32-testhooks.exe"),
        TestFolder is null ? null : Path.Combine(TestFolder, "tfvst32-testhooks.exe"));

    public static string? TestPluginPath => FirstFile(
        Environment.GetEnvironmentVariable("TABFORGE_TFVST32_TEST_DLL"),
        Path.Combine(AppContext.BaseDirectory, "passthrough32.dll"),
        TestFolder is null ? null : Path.Combine(TestFolder, "passthrough32.dll"));

    /// <summary>The 64-bit deterministic test plug-in (never shipped; built from the same test source).</summary>
    public static string? TestPluginPath64 => FirstFile(
        Environment.GetEnvironmentVariable("TABFORGE_TFVST64_TEST_DLL"),
        Path.Combine(AppContext.BaseDirectory, "passthrough64.dll"),
        TestFolder is null ? null : Path.Combine(TestFolder, "passthrough64.dll"));

    public static string? NativeTestExe => FirstFile(
        Environment.GetEnvironmentVariable("TABFORGE_TFVST32_TEST_EXE"),
        Path.Combine(AppContext.BaseDirectory, "tfvst32test.exe"),
        TestFolder is null ? null : Path.Combine(TestFolder, "tfvst32test.exe"));

    public static string? NativeTestExe64 => FirstFile(
        Environment.GetEnvironmentVariable("TABFORGE_TFVST64_TEST_EXE"),
        Path.Combine(AppContext.BaseDirectory, "tfvst64test.exe"),
        TestFolder is null ? null : Path.Combine(TestFolder, "tfvst64test.exe"));

    /// <summary>The Wine loader the Linux CI job points at (TABFORGE_WINE); null everywhere else.</summary>
    public static string? WineLoader
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable("TABFORGE_WINE");
            return !string.IsNullOrWhiteSpace(configured) && File.Exists(configured) ? configured : null;
        }
    }

    /// <summary>The one real 32-bit VST2 the Linux CI job downloads and SHA-checks (never committed).</summary>
    public static string? WineRealPluginPath => FirstFile(Environment.GetEnvironmentVariable("TABFORGE_WINE_REAL_VST32"));

    /// <summary>The one real 64-bit VST2 the Linux CI job downloads from the same official release (never committed).</summary>
    public static string? WineRealPluginPath64 => FirstFile(Environment.GetEnvironmentVariable("TABFORGE_WINE_REAL_VST64"));

    private static string? TestFolder
    {
        get
        {
            var explicitFolder = Environment.GetEnvironmentVariable("TABFORGE_TFVST32_TEST_DIR");
            if (!string.IsNullOrEmpty(explicitFolder)) return explicitFolder;
            return RepoRoot is null ? null : Path.Combine(RepoRoot, "Source", "native", "tfvst32", "test-bin");
        }
    }

    /// <summary>The folder holding the shipped helpers (the app output or the repo's native folder).</summary>
    private static string? HelperFolder => HelperPath is { } helper ? Path.GetDirectoryName(helper) : TestFolder;

    public static string FixturePath(string name) => Path.Combine(FixturesFolder, name + ".dll");

    public static RemotePlugin Start(string path, bool instrument, int maxBlock = 1024, int bufferSlots = 1, string? helperPath = null) =>
        StartWithThreads(path, instrument, maxBlock, bufferSlots, helperPath).Plugin;

    /// <summary>Starts the helper over the Wine transport (loopback TCP + explicit file block) even on Windows, for tests.</summary>
    public static (RemotePlugin Plugin, EngineThreads Threads) StartTcp(string path, bool instrument, int maxBlock = 1024,
        int bufferSlots = 2, string? helperPath = null)
    {
        var helper = helperPath ?? HelperPath ?? throw new InvalidOperationException("tfvst32.exe is not built");
        var launch = new PluginHostLaunch(helper, PluginHostLaunch.Vst32TcpHostMode, bufferSlots, PluginHostTransport.Tcp);
        var threads = new EngineThreads(new EngineLogWriter());
        return (new RemotePlugin(Spec(path, instrument), Rate, maxBlock, threads, launch), threads);
    }

    /// <summary>Starts the 64-bit helper over the Windows named pipe (the same helper protocol; Windows tests only).</summary>
    public static (RemotePlugin Plugin, EngineThreads Threads) Start64(string path, bool instrument, int maxBlock = 1024,
        int bufferSlots = 2, string? helperPath = null)
    {
        var helper = helperPath ?? HelperPath64 ?? throw new InvalidOperationException("tfvst64.exe is not built");
        var launch = new PluginHostLaunch(helper, PluginHostLaunch.Vst32HostMode, bufferSlots, BridgeBitness: 64);
        var threads = new EngineThreads(new EngineLogWriter());
        return (new RemotePlugin(Spec(path, instrument), Rate, maxBlock, threads, launch), threads);
    }

    /// <summary>Starts the 64-bit helper over the Wine transport (loopback TCP + explicit file block) even on Windows, for tests.</summary>
    public static (RemotePlugin Plugin, EngineThreads Threads) StartTcp64(string path, bool instrument, int maxBlock = 1024,
        int bufferSlots = 2, string? helperPath = null)
    {
        var helper = helperPath ?? HelperPath64 ?? throw new InvalidOperationException("tfvst64.exe is not built");
        var launch = new PluginHostLaunch(helper, PluginHostLaunch.Vst32TcpHostMode, bufferSlots, PluginHostTransport.Tcp,
            BridgeBitness: 64);
        var threads = new EngineThreads(new EngineLogWriter());
        return (new RemotePlugin(Spec(path, instrument), Rate, maxBlock, threads, launch), threads);
    }

    /// <summary>Starts through the real Linux decision: Vst32Bridge.Launch (WineRuntime, private prefix) over TCP.</summary>
    public static (RemotePlugin Plugin, EngineThreads Threads) StartWine(string path, bool instrument, int maxBlock,
        int bufferSlots = 2)
    {
        var spec = Spec(path, instrument);
        var launch = Vst32Bridge.Launch(spec, helperFolder: HelperFolder);
        var threads = new EngineThreads(new EngineLogWriter());
        return (new RemotePlugin(spec, Rate, maxBlock, threads, launch), threads);
    }

    /// <summary>Like <see cref="Start"/>, plus the main-thread queue so a test can pump Posted editor events.</summary>
    public static (RemotePlugin Plugin, EngineThreads Threads) StartWithThreads(string path, bool instrument, int maxBlock = 1024,
        int bufferSlots = 1, string? helperPath = null)
    {
        var helper = helperPath ?? HelperPath ?? throw new InvalidOperationException("tfvst32.exe is not built");
        var launch = new PluginHostLaunch(helper, PluginHostLaunch.Vst32HostMode, bufferSlots);
        var threads = new EngineThreads(new EngineLogWriter());
        return (new RemotePlugin(Spec(path, instrument), Rate, maxBlock, threads, launch), threads);
    }

    public static PluginSpec Spec(string path, bool instrument, string? sha256 = null) =>
        new(path, "VST2", instrument, Enabled: true, Wet: 100, State: null, Name: Path.GetFileNameWithoutExtension(path),
            Id: "vst32", LocationTrusted: false, ExpectedSha256: sha256 ?? Sha256(path));

    public static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    public static TransportInfo Transport(int block, int maxBlock, bool playing = true) => new()
    {
        Tempo = 120, PpqPosition = block * maxBlock / 2000.0, Playing = playing,
        Meter = new TransportMeter { Numerator = 4, Denominator = 4, BarStartPpq = 0 },
    };

    public static float Rms(float[] samples)
    {
        if (samples.Length == 0) return 0;
        double sum = 0;
        foreach (var sample in samples) sum += sample * (double)sample;
        return (float)Math.Sqrt(sum / samples.Length);
    }

    public static float Peak(float[] samples) => samples.Length == 0 ? 0 : samples.Max(Math.Abs);

    public static double Db(float value) => 20 * Math.Log10(Math.Max(1e-9f, value));

    public static bool AllFinite(params float[][] channels) =>
        channels.All(channel => channel.All(float.IsFinite));

    public static (int ExitCode, string Output, string Error) RunHelperCaptured(params string[] arguments) =>
        RunCaptured(HelperPath ?? throw new InvalidOperationException("tfvst32.exe is not built"), "tfvst32.exe", arguments);

    public static (int ExitCode, string Output, string Error) RunHelper64Captured(params string[] arguments) =>
        RunCaptured(HelperPath64 ?? throw new InvalidOperationException("tfvst64.exe is not built"), "tfvst64.exe", arguments);

    public static (int ExitCode, string Output, string Error) RunNativeTest64Captured() =>
        RunCaptured(NativeTestExe64 ?? throw new InvalidOperationException("tfvst64test.exe is not built"), "tfvst64test.exe",
            [TestPluginPath64 ?? throw new InvalidOperationException("passthrough64.dll is not built")]);

    private static (int ExitCode, string Output, string Error) RunCaptured(string executable, string name, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"{name} did not start.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(15000)) { try { process.Kill(true); } catch (InvalidOperationException) { } }
        return (process.ExitCode, output, error);
    }

    public static bool WaitsForExit(int pid, int timeoutMs)
    {
        try { using var process = Process.GetProcessById(pid); return process.WaitForExit(timeoutMs); }
        catch (ArgumentException) { return true; }
    }

    /// <summary>A temporary folder that deletes itself; shared by the routing and catalog tests.</summary>
    public sealed class TempFolder : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("tf-vst32-test").FullName;
        public string Write(string name, byte[] bytes)
        {
            var file = System.IO.Path.Combine(Path, name);
            File.WriteAllBytes(file, bytes);
            return file;
        }
        public void Dispose() { try { Directory.Delete(Path, recursive: true); } catch (IOException) { } }
    }

    /// <summary>A trust source that reports no final path and no signature.</summary>
    public sealed class NoFiles : TabForge.Platform.IPluginFileTrust
    {
        public string? FinalPath(string fullPath) => null;
        public bool HasValidEmbeddedSignature(string fullPath) => false;
    }

    /// <summary>The smallest PE image that exports the requested entry point: one section holding the export table.</summary>
    public static byte[] PeImage(bool x86, string export = "VSTPluginMain")
    {
        var image = new byte[0x400];
        void U16(int at, int v) => BitConverter.TryWriteBytes(image.AsSpan(at, 2), (ushort)v);
        void U32(int at, int v) => BitConverter.TryWriteBytes(image.AsSpan(at, 4), (uint)v);
        System.Text.Encoding.ASCII.GetBytes("MZ").CopyTo(image, 0);
        U32(0x3C, 0x40);
        System.Text.Encoding.ASCII.GetBytes("PE").CopyTo(image, 0x40);
        var optionalSize = x86 ? 224 : 240;
        U16(0x44, x86 ? 0x14C : 0x8664); U16(0x46, 1); U16(0x54, optionalSize);
        const int optional = 0x58;
        U16(optional, x86 ? 0x10B : 0x20B);
        var directories = optional + (x86 ? 96 : 112);
        U32(directories, 0x1000); U32(directories + 4, 0x100);
        var section = optional + optionalSize;
        U32(section + 8, 0x200); U32(section + 12, 0x1000); U32(section + 16, 0x200); U32(section + 20, 0x200);
        const int table = 0x200;
        U32(table + 24, 1); U32(table + 28, 0x1028); U32(table + 32, 0x1030);
        U32(table + 0x30, 0x1040);
        System.Text.Encoding.ASCII.GetBytes(export).CopyTo(image, table + 0x40);
        return image;
    }

    private static string? FirstFile(params string?[] candidates) =>
        candidates.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path));

    private static string? FindRepoRoot()
    {
        var checkout = typeof(Vst32TestAssets).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "TabForgeCheckout")?.Value;
        var starts = new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory, checkout ?? "" };
        foreach (var start in starts)
        {
            try
            {
                for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
                    if (File.Exists(Path.Combine(directory.FullName, "Source", "TabForge.sln"))) return directory.FullName;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return null;
    }
}

/// <summary>A test that needs the built native helper (no test plug-in).</summary>
internal sealed class Vst32HelperFactAttribute : FactAttribute
{
    public Vst32HelperFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "The 32-bit VST2 bridge is Windows-only";
        else if (Vst32TestAssets.HelperPath is null) Skip = "tfvst32.exe is not built (run Source/native/build-tfvst32.ps1)";
    }
}

/// <summary>A test that needs a loadable 32-bit VST2 path: Windows, or Linux with the Wine bridge present.</summary>
internal sealed class Vst32BridgeAvailableFactAttribute : FactAttribute
{
    public Vst32BridgeAvailableFactAttribute()
    {
        if (!OperatingSystem.IsWindows() && !TabForge.Plugins.WineBridgeSupport.Available)
            Skip = "The 32-bit VST2 bridge needs Windows or the Linux Wine bundle";
    }
}

/// <summary>A test that needs the Windows named-pipe launch (the Linux path uses Wine and is covered by the TCP tests).</summary>
internal sealed class Vst32WindowsFactAttribute : FactAttribute
{
    public Vst32WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "The Windows 32-bit named-pipe bridge is Windows-only";
    }
}

/// <summary>A test that needs a real 32-bit fixture folder (the helper itself is not required).</summary>
internal sealed class Vst32FixtureFactAttribute : FactAttribute
{
    public Vst32FixtureFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "The 32-bit VST2 bridge is Windows-only";
        else if (!Directory.Exists(Vst32TestAssets.FixturesFolder)) Skip = $@"No real 32-bit VST2 plug-ins in {Vst32TestAssets.FixturesFolder} (the fixture folder is not in the repo)";
    }
}

/// <summary>A test that needs the helper and the deterministic 32-bit test plug-in.</summary>
internal sealed class Vst32FactAttribute : FactAttribute
{
    public Vst32FactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "The 32-bit VST2 bridge is Windows-only";
        else if (Vst32TestAssets.HelperPath is null) Skip = "tfvst32.exe is not built (run Source/native/build-tfvst32.ps1)";
        else if (Vst32TestAssets.TestPluginPath is null) Skip = "passthrough32.dll is not built (run Source/native/build-tfvst32.ps1)";
    }
}

/// <summary>A test that needs the helper and a real fixture folder (test-only, never in the repo).</summary>
internal sealed class Vst32RealFactAttribute : FactAttribute
{
    public Vst32RealFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "The 32-bit VST2 bridge is Windows-only";
        else if (Vst32TestAssets.HelperPath is null) Skip = "tfvst32.exe is not built (run Source/native/build-tfvst32.ps1)";
        else if (!Directory.Exists(Vst32TestAssets.FixturesFolder)) Skip = $@"No real 32-bit VST2 plug-ins in {Vst32TestAssets.FixturesFolder}";
    }
}

/// <summary>A stall test that needs the TFVST32_TEST_HOOKS copy (never the shipped helper) and the test plug-in.</summary>
internal sealed class Vst32HookedFactAttribute : FactAttribute
{
    public Vst32HookedFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "The 32-bit VST2 bridge is Windows-only";
        else if (Vst32TestAssets.HookedHelperPath is null) Skip = "tfvst32-testhooks.exe is not built (run Source/native/build-tfvst32.ps1)";
        else if (Vst32TestAssets.TestPluginPath is null) Skip = "passthrough32.dll is not built (run Source/native/build-tfvst32.ps1)";
    }
}

/// <summary>A stall test with a real fixture, needing the TFVST32_TEST_HOOKS copy (never the shipped helper).</summary>
internal sealed class Vst32HookedRealTheoryAttribute : TheoryAttribute
{
    public Vst32HookedRealTheoryAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "The 32-bit VST2 bridge is Windows-only";
        else if (Vst32TestAssets.HookedHelperPath is null) Skip = "tfvst32-testhooks.exe is not built (run Source/native/build-tfvst32.ps1)";
        else if (!Directory.Exists(Vst32TestAssets.FixturesFolder)) Skip = $@"No real 32-bit VST2 plug-ins in {Vst32TestAssets.FixturesFolder}";
    }
}

internal sealed class Vst32RealTheoryAttribute : TheoryAttribute
{
    public Vst32RealTheoryAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "The 32-bit VST2 bridge is Windows-only";
        else if (Vst32TestAssets.HelperPath is null) Skip = "tfvst32.exe is not built (run Source/native/build-tfvst32.ps1)";
        else if (!Directory.Exists(Vst32TestAssets.FixturesFolder)) Skip = $@"No real 32-bit VST2 plug-ins in {Vst32TestAssets.FixturesFolder}";
    }
}

/// <summary>A test that runs the real engine + shipped helper under Wine on the Linux CI runner.</summary>
internal sealed class Vst32WineFactAttribute : FactAttribute
{
    public Vst32WineFactAttribute()
    {
        if (!OperatingSystem.IsLinux()) Skip = "The Wine engine test runs on the Linux CI runner";
        else if (WineEnvironmentProblem() is { } problem) Skip = problem;
    }

    internal static string? WineEnvironmentProblem()
    {
        if (Vst32TestAssets.WineLoader is null) return "TABFORGE_WINE does not point at a Wine loader";
        if (Vst32TestAssets.HelperPath is null) return "tfvst32.exe is not built";
        if (Vst32TestAssets.TestPluginPath is null) return "passthrough32.dll is not built";
        return null;
    }
}

internal sealed class Vst32WineTheoryAttribute : TheoryAttribute
{
    public Vst32WineTheoryAttribute()
    {
        if (!OperatingSystem.IsLinux()) Skip = "The Wine engine test runs on the Linux CI runner";
        else if (Vst32WineFactAttribute.WineEnvironmentProblem() is { } problem) Skip = problem;
    }
}

/// <summary>A Wine test that also needs the workflow-downloaded real 32-bit VST2 (never committed).</summary>
internal sealed class Vst32WineRealFactAttribute : FactAttribute
{
    public Vst32WineRealFactAttribute()
    {
        if (!OperatingSystem.IsLinux()) Skip = "The Wine engine test runs on the Linux CI runner";
        else if (Vst32WineFactAttribute.WineEnvironmentProblem() is { } problem) Skip = problem;
        else if (Vst32TestAssets.WineRealPluginPath is null) Skip = "TABFORGE_WINE_REAL_VST32 is not set";
    }
}

/// <summary>A Windows test that needs the built 64-bit helper and the deterministic 64-bit test DLL.</summary>
internal sealed class Vst64FactAttribute : FactAttribute
{
    public Vst64FactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "The 64-bit helper process test runs on Windows";
        else if (Vst32TestAssets.HelperPath64 is null) Skip = "tfvst64.exe is not built (run Source/native/build-tfvst32.ps1)";
        else if (Vst32TestAssets.TestPluginPath64 is null) Skip = "passthrough64.dll is not built (run Source/native/build-tfvst32.ps1)";
    }
}

/// <summary>A native-only 64-bit fact (the helper self-test does not need the test DLL).</summary>
internal sealed class Vst64HelperFactAttribute : FactAttribute
{
    public Vst64HelperFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "The 64-bit helper self-test runs on Windows";
        else if (Vst32TestAssets.HelperPath64 is null) Skip = "tfvst64.exe is not built (run Source/native/build-tfvst32.ps1)";
    }
}

/// <summary>A Wine test that needs the 64-bit helper and the deterministic 64-bit test DLL.</summary>
internal sealed class Vst64WineFactAttribute : FactAttribute
{
    public Vst64WineFactAttribute()
    {
        if (!OperatingSystem.IsLinux()) Skip = "The Wine 64-bit helper test runs on the Linux CI runner";
        else if (Vst32TestAssets.WineLoader is null) Skip = "TABFORGE_WINE does not point at a Wine loader";
        else if (Vst32TestAssets.HelperPath64 is null) Skip = "tfvst64.exe is not built";
        else if (Vst32TestAssets.TestPluginPath64 is null) Skip = "passthrough64.dll is not built";
    }
}

internal sealed class Vst64WineTheoryAttribute : TheoryAttribute
{
    public Vst64WineTheoryAttribute()
    {
        if (!OperatingSystem.IsLinux()) Skip = "The Wine 64-bit helper test runs on the Linux CI runner";
        else if (Vst32TestAssets.WineLoader is null) Skip = "TABFORGE_WINE does not point at a Wine loader";
        else if (Vst32TestAssets.HelperPath64 is null) Skip = "tfvst64.exe is not built";
        else if (Vst32TestAssets.TestPluginPath64 is null) Skip = "passthrough64.dll is not built";
    }
}

/// <summary>A Wine 64-bit test that also needs the workflow-downloaded real 64-bit VST2 (never committed).</summary>
internal sealed class Vst64WineRealFactAttribute : FactAttribute
{
    public Vst64WineRealFactAttribute()
    {
        if (!OperatingSystem.IsLinux()) Skip = "The Wine 64-bit helper test runs on the Linux CI runner";
        else if (Vst32TestAssets.WineLoader is null) Skip = "TABFORGE_WINE does not point at a Wine loader";
        else if (Vst32TestAssets.HelperPath64 is null) Skip = "tfvst64.exe is not built";
        else if (Vst32TestAssets.WineRealPluginPath64 is null) Skip = "TABFORGE_WINE_REAL_VST64 is not set";
    }
}

[CollectionDefinition("Vst32Native", DisableParallelization = true)]
public sealed class Vst32NativeCollection { }
