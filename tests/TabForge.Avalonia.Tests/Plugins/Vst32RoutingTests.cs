using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TabForge.Audio.Contracts;
using TabForge.Audio.Contracts.Builtin;
using TabForge.AudioEngine;
using TabForge.AudioEngine.Isolation;
using TabForge.AudioEngine.Plugins;
using TabForge.Plugins;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the engine side of the 32-bit VST2 bridge before the native helper exists: the routing decision, the immutable launch
/// record and its arguments, the readable missing-helper failure, the identify-probe hand-off and the bounded first-frame wait.
/// Does not own: the catalog/browser labels (Vst32CatalogTests) or the native helper (a later stage).
/// Tests: this class.
/// </summary>
public sealed class Vst32RoutingTests
{
    [Vst32FixtureFact]
    public void EveryReal32BitFixtureRoutesToTheBridgeFromItsPeImage()
    {
        foreach (var name in Vst32TestAssets.RealFixtures)
        {
            var path = Path.Combine(Vst32TestAssets.FixturesFolder, name + ".dll");
            Assert.True(File.Exists(path), $"missing fixture: {path}");
            Assert.True(Vst32Bridge.IsBridged(Spec(path, "")), $"{name} must bridge from a blank saved format");
            Assert.Equal(Vst32Bridge.PluginRoute.Bridged32, Vst32Bridge.Route(Spec(path, PluginFormats.Vst2), separateProcess: false));
            Assert.Equal(Vst32Bridge.PluginRoute.Bridged32, Vst32Bridge.Route(Spec(path, ""), separateProcess: true));
        }
    }

    [Vst32BridgeAvailableFact]
    public void A32BitVst2IsBridgedFromTheLabelOrThePeImage()
    {
        using var folder = new Vst32TestAssets.TempFolder();
        var old32 = folder.Write("Old32.dll", Vst32TestAssets.PeImage(x86: true));
        var new64 = folder.Write("New64.dll", Vst32TestAssets.PeImage(x86: false));

        Assert.True(Vst32Bridge.IsBridged(Spec(old32, PluginFormats.Bridged32)));
        Assert.True(Vst32Bridge.IsBridged(Spec(old32, PluginFormats.LegacyUnsupported32)));
        Assert.True(Vst32Bridge.IsBridged(Spec(old32, "")));          // a saved format is missing: the PE header decides
        Assert.True(Vst32Bridge.IsBridged(Spec(old32, "VST2")));      // a stale saved format: the PE header still decides
        // Windows keeps 64-bit VST2 native; Linux routes it to the 64-bit helper when that helper is shipped.
        var linux64 = OperatingSystem.IsLinux() && TabForge.Plugins.WineBridgeSupport.Available64;
        Assert.Equal(linux64, Vst32Bridge.IsBridged(Spec(new64, "")));
        Assert.Equal(linux64, Vst32Bridge.IsBridged(Spec(new64, "VST2")));
        if (OperatingSystem.IsLinux()) Assert.True(Vst32Bridge.IsWine64Candidate(Spec(new64, "")));
    }

    [Vst32BridgeAvailableFact]
    public void BuiltinsVst3AndClapNeverBridgeAndSeparateProcessKeepsTheBridge()
    {
        using var folder = new Vst32TestAssets.TempFolder();
        var old32 = folder.Write("Old32.dll", Vst32TestAssets.PeImage(x86: true));
        var new64 = folder.Write("New64.dll", Vst32TestAssets.PeImage(x86: false));
        var vst3 = Path.Combine(folder.Path, "Plug.vst3");
        var clap = Path.Combine(folder.Path, "Plug.clap");
        Directory.CreateDirectory(vst3);
        File.WriteAllText(clap, "");

        Assert.False(Vst32Bridge.IsBridged(Spec(BuiltinEffects.EqId, PluginFormats.Builtin)));
        Assert.False(Vst32Bridge.IsBridged(Spec(vst3, "VST3")));
        Assert.False(Vst32Bridge.IsBridged(Spec(clap, PluginFormats.Clap)));

        Assert.Equal(Vst32Bridge.PluginRoute.Bridged32, Vst32Bridge.Route(Spec(old32, PluginFormats.Bridged32), separateProcess: false));
        Assert.Equal(Vst32Bridge.PluginRoute.Bridged32, Vst32Bridge.Route(Spec(old32, PluginFormats.Bridged32), separateProcess: true));
        var spec64 = Spec(new64, "VST2");
        Assert.Equal(OperatingSystem.IsLinux() ? Vst32Bridge.PluginRoute.Bridged64 : Vst32Bridge.PluginRoute.Isolated,
            Vst32Bridge.Route(spec64, separateProcess: true));
        Assert.Equal(OperatingSystem.IsLinux() ? Vst32Bridge.PluginRoute.Bridged64 : Vst32Bridge.PluginRoute.InProcess,
            Vst32Bridge.Route(spec64, separateProcess: false));
        Assert.Equal(Vst32Bridge.PluginRoute.InProcess, Vst32Bridge.Route(Spec(BuiltinEffects.EqId, PluginFormats.Builtin), separateProcess: true));
    }

    /// <summary>Regression for Stage 13 C7: a native 64-bit VST2 marked crash-prone goes to the existing isolation host even with the global setting off.</summary>
    [Vst32BridgeAvailableFact]
    public void ACrashProneNative64Vst2UsesTheIsolationHostWhenSeparateProcessIsOff()
    {
        using var folder = new Vst32TestAssets.TempFolder();
        var path = folder.Write("Crashy64.dll", Vst32TestAssets.PeImage(x86: false));
        var spec = Spec(path, "VST2") with { Isolate = true };
        Assert.Equal(OperatingSystem.IsLinux() ? Vst32Bridge.PluginRoute.Bridged64 : Vst32Bridge.PluginRoute.Isolated,
            Vst32Bridge.Route(spec, separateProcess: false));
    }

    [Vst32WindowsFact]
    public void TheLaunchRecordPassesTheSpecThroughAndAddsTheSlotCountLast()
    {
        using var folder = new Vst32TestAssets.TempFolder();
        var helper = folder.Write(Vst32Bridge.HelperName, [0]);
        var path = Path.Combine(folder.Path, "Plug32.dll");
        var sha = new string('A', 64);
        var spec = Spec(path, PluginFormats.Bridged32) with { ExpectedSha256 = sha, LocationTrusted = false };

        var launch = Vst32Bridge.Launch(spec, folder.Path);
        var arguments = launch.Arguments("session", 4321, 48000, 256, spec, @"C:\logs\engine.log");

        Assert.Equal(helper, launch.ExecutablePath);
        Assert.Equal((PluginHostLaunch.Vst32HostMode, 2), (launch.Mode, launch.BufferSlots));
        Assert.True(launch.IsVst32Bridge);
        Assert.Equal(new[]
        {
            "--vst32-host", "session", "4321", "48000", "256", "0", "VST2", path, sha, @"C:\logs\engine.log", "checked", "2", "none",
        }, arguments);

        var isolated = PluginHostLaunch.ForCurrentProcess().Arguments("session", 4321, 48000, 256, spec, null);
        Assert.Equal("--plugin-host", isolated[0]);
        Assert.Equal(12, isolated.Count);   // the existing 64-bit host keeps its exact argument list plus the dependency hash
        Assert.Equal("none", isolated[9]);
    }

    [WindowsFact]
    public void TheOptionalStage3HelloFrameMustMatchTheSession()
    {
        using var valid = Hello(RemotePlugin.BridgeHelloMagic, RemotePlugin.BridgeProtocolVersion, RemotePlugin.BridgeHeaderSize, 256, 48000, 1);
        using (var reader = new BinaryReader(valid)) RemotePlugin.ReadBridgeHello(reader, 48000, 256, 1);

        using var wrongBlock = Hello(RemotePlugin.BridgeHelloMagic, RemotePlugin.BridgeProtocolVersion, RemotePlugin.BridgeHeaderSize, 512, 48000, 1);
        using var wrongReader = new BinaryReader(wrongBlock);
        var error = Assert.Throws<InvalidOperationException>(() => RemotePlugin.ReadBridgeHello(wrongReader, 48000, 256, 1));
        Assert.Contains("different protocol", error.Message);

        static MemoryStream Hello(uint magic, ushort version, ushort header, int block, int rate, int slots)
        {
            var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(magic); writer.Write(version); writer.Write(header); writer.Write(block); writer.Write(rate); writer.Write(slots);
            }
            stream.Position = 0;
            return stream;
        }
    }

    [Vst32WindowsFact]
    public void AMissingHelperIsAReadableFailureNotBadImageFormat()
    {
        using var folder = new Vst32TestAssets.TempFolder();
        var path = Path.Combine(folder.Path, "Plug32.dll");

        var error = Assert.Throws<InvalidOperationException>(() => Vst32Bridge.Launch(Spec(path, PluginFormats.Bridged32), folder.Path));

        Assert.Contains("32-bit", error.Message);
        Assert.Contains(Vst32Bridge.HelperName, error.Message);
        Assert.DoesNotContain("193", error.Message);
    }

    [Vst32BridgeAvailableFact]
    public void A32BitProbeGoesToTheHelperAndA64BitFileFollowsTheNativeOrWineRule()
    {
        using var folder = new Vst32TestAssets.TempFolder();
        var old32 = folder.Write("Old32.dll", Vst32TestAssets.PeImage(x86: true));
        var new64 = folder.Write("New64.dll", Vst32TestAssets.PeImage(x86: false));
        var helper = folder.Write(Vst32Bridge.HelperName, [0]);

        Assert.True(PluginInfoProbe.UsesVst32Helper(old32));
        Assert.False(PluginInfoProbe.UsesVst32Helper(new64));
        Assert.Equal(OperatingSystem.IsLinux() && TabForge.Plugins.WineBridgeSupport.Available64,
            PluginInfoProbe.UsesVst64Helper(new64));

        var start = PluginInfoProbe.BridgeStartInfo(helper, old32, "ABC");
        Assert.Equal(helper, start.FileName);
        Assert.Equal(new[] { "--probe", old32, "ABC", "none" }, start.ArgumentList);
        Assert.False(start.UseShellExecute);
    }

    [Vst32WindowsFact]
    public void TheProbeHandsTheHelperAFullDllPath()
    {
        using var folder = new Vst32TestAssets.TempFolder();
        var path = folder.Write("Old32.dll", Vst32TestAssets.PeImage(x86: true));
        folder.Write(Vst32Bridge.HelperName, [0]);
        var relative = Path.GetRelativePath(Directory.GetCurrentDirectory(), path);
        string? handed = null;

        var exit = PluginInfoProbe.RunBridged(relative, Vst32TestAssets.Sha256(path), folder.Path, timeoutMs: 5000, startProcess: start =>
        {
            handed = start.ArgumentList[1];
            return Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/c exit 0")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            })!;
        });

        Assert.Equal(0, exit);
        Assert.Equal(Path.GetFullPath(relative), handed);
        Assert.True(Path.IsPathFullyQualified(handed!));
    }

    [Vst32WindowsFact]
    public void AProbeThatExceedsItsTimeoutLeavesNoHelperProcess()
    {
        using var folder = new Vst32TestAssets.TempFolder();
        var path = folder.Write("Old32.dll", Vst32TestAssets.PeImage(x86: true));
        folder.Write(Vst32Bridge.HelperName, [0]);   // only its existence is checked; the fake process below is what runs
        int? helperPid = null;

        var exit = PluginInfoProbe.RunBridged(path, Vst32TestAssets.Sha256(path), folder.Path, timeoutMs: 250, startProcess: _ =>
        {
            var helper = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/c ping -n 30 127.0.0.1 >nul")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            });
            helperPid = helper!.Id;
            return helper;
        });

        Assert.NotEqual(0, exit);
        Assert.NotNull(helperPid);
        Assert.True(Vst32TestAssets.WaitsForExit(helperPid!.Value, 3000), "the timed-out helper must be killed");
    }

    [Vst32WindowsFact]
    public async Task AConnectedHelperThatNeverFinishesLoadingTimesOutAndIsKilled()
    {
        var id = Guid.NewGuid().ToString("N");
        using var client = new NamedPipeClientStream(".", PipeNames.PluginHost(id), PipeDirection.InOut, PipeOptions.Asynchronous);
        var connect = Task.Run(() => client.Connect(5000));
        int? hostPid = null;
        var timer = Stopwatch.StartNew();
        try
        {
            var spec = Spec(@"C:\vst\Bridge32.dll", PluginFormats.Bridged32);
            var error = Assert.Throws<TimeoutException>(() => new RemotePlugin(spec, 48000, 256, new EngineThreads(new EngineLogWriter()),
                new PluginHostLaunch(@"C:\helper\tfvst32.exe", PluginHostLaunch.Vst32HostMode, 1),
                connectTimeoutMs: 5000, loadTimeoutMs: 300, sessionId: id, startProcess: _ =>
                {
                    var host = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/k rem")
                    {
                        UseShellExecute = false, CreateNoWindow = true,
                    });
                    hostPid = host!.Id;
                    return host;
                }));
            Assert.Contains("did not finish loading", error.Message);
            Assert.True(timer.ElapsedMilliseconds < 5000, "the first-frame wait must be bounded");
            await connect.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(connect.IsCompletedSuccessfully, "the fake helper must connect");
            Assert.NotNull(hostPid);
            Assert.True(Vst32TestAssets.WaitsForExit(hostPid!.Value, 3000), "the timed-out helper must be killed");
        }
        finally
        {
            try { client.Dispose(); } catch (IOException) { }
            if (hostPid is { } pid) KillIfAlive(pid);
        }
    }

    private static PluginSpec Spec(string path, string format) =>
        new(path, format, IsInstrument: false, Enabled: true, Wet: 100, State: null, Name: "Plug", Id: "plug", LocationTrusted: true);

    private static void KillIfAlive(int pid)
    {
        try { using var process = Process.GetProcessById(pid); if (!process.HasExited) process.Kill(true); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { /* swallow: already gone. */ }
    }
}
