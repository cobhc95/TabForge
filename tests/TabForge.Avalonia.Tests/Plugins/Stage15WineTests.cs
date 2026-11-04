using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine;
using TabForge.AudioEngine.Isolation;
using TabForge.AudioEngine.Plugins;
using Xunit;
using Xunit.Abstractions;

namespace TabForge.Avalonia.Tests;

// Owns: Stage 15 Linux/Wine fault injection: killing the Wine server mid-playback, the helper's block cleanup when its
// engine disappears, a real Wine prefix that is corrupted or read-only, and audio with an unwritable log.
// Does not own: the fake-loader prefix state machine (WineRuntimeLinuxTests), Windows TCP (Stage15FaultTests) or the
// existing kill-helper containment (Vst32WineLinuxTests).
[Collection("Vst32Native")]
public sealed class Stage15WineTests
{
    private const int Rate = 48000, Frames = 256;
    private readonly ITestOutputHelper _output;

    public Stage15WineTests(ITestOutputHelper output) => _output = output;

    [Vst32WineFact]
    public void KillingTheWineServerMidPlaybackIsContainedAndTheNextSessionRecovers()
    {
        var (plugin, threads) = Vst32TestAssets.StartWine(Vst32TestAssets.TestPluginPath!, instrument: false, Frames);
        var blockPath = plugin.BlockFilePath;
        var pid = plugin.HostProcessId;
        var crashed = 0;
        var signal = new ManualResetEventSlim();
        plugin.Crashed += _ => { Interlocked.Increment(ref crashed); signal.Set(); };
        var input = Enumerable.Repeat(0.2f, Frames).ToArray();
        var output = new[] { new float[Frames], new float[Frames] };
        try
        {
            plugin.Process(new[] { input, input }, output, Frames, ReadOnlySpan<BlockMidi>.Empty, Vst32TestAssets.Transport(0, Frames));
            var runtime = Locate();
            var wineServer = Path.Combine(Path.GetDirectoryName(runtime.WineExecutable)!, "wineserver");
            Assert.True(File.Exists(wineServer), $"missing wineserver beside {runtime.WineExecutable}");
            KillWineServer(wineServer, runtime.PrefixPath);

            var clock = Stopwatch.StartNew();
            while (clock.Elapsed.TotalSeconds < 15 && !signal.IsSet)
            {
                threads.RunPending();
                Thread.Sleep(10);
            }
            threads.RunPending();
            Assert.True(signal.IsSet, "a dead Wine server must be reported as a lost plug-in");
            Assert.Equal(1, crashed);
            Assert.True(Vst32TestAssets.WaitsForExit(pid, 10000), "the helper must not survive its Wine server");
            Assert.False(File.Exists(blockPath), "the engine must delete the block file on the Wine-server crash");
            for (var i = 0; i < 8; i++)
            {
                plugin.Process(new[] { input, input }, output, Frames, ReadOnlySpan<BlockMidi>.Empty, Vst32TestAssets.Transport(i, Frames));
                Assert.Equal(input, output[0]);
            }
            _output.WriteLine("wine server kill contained: crashed=1 helper_gone=true block_deleted=true");
        }
        finally { plugin.Dispose(); }

        // Wine restarts its server for the next process: a fresh session must load and process again.
        using var recovered = Vst32TestAssets.StartWine(Vst32TestAssets.TestPluginPath!, instrument: false, Frames).Plugin;
        for (var i = 0; i < 6; i++)
            recovered.Process(new[] { input, input }, output, Frames, ReadOnlySpan<BlockMidi>.Empty, Vst32TestAssets.Transport(i, Frames));
        Assert.True(Vst32TestAssets.AllFinite(output[0], output[1]));
        Assert.False(recovered.LinkFailed, "a new Wine session after a server kill must work");
        _output.WriteLine("wine server recovery: new session processed 6 blocks");
    }

    /// <summary>The engine process dies: the helper must exit and remove the block file even though no engine code runs.</summary>
    [Vst32WineFact]
    public void AnEngineDeathUnderWineLeavesNoHelperOrBlockFile()
    {
        var runtime = Locate();
        runtime.EnsureReady();
        var folder = Directory.CreateTempSubdirectory("tf-s15-wine-death").FullName;
        var blockPath = Path.Combine(folder, "tf-wine-death-" + Guid.NewGuid().ToString("N") + ".shm");
        using (var file = File.Create(blockPath)) file.SetLength(BlockBytes(Frames));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var start = new ProcessStartInfo(runtime.WineExecutable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var (key, value) in runtime.EnvironmentFor()) start.Environment[key] = value;
        start.Environment[RemotePlugin.TokenVariable] = token;
        start.ArgumentList.Add(runtime.HelperPath);
        foreach (var argument in new[]
        {
            PluginHostLaunch.Vst32TcpHostMode, port.ToString(), WineRuntime.ToWindowsPath(blockPath), Rate.ToString(),
            Frames.ToString(), "0", "VST2", WineRuntime.ToWindowsPath(Vst32TestAssets.TestPluginPath!),
            Vst32TestAssets.Sha256(Vst32TestAssets.TestPluginPath!), "none", "checked", "2",
        }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        try
        {
            using var client = listener.AcceptTcpClient();
            client.NoDelay = true;
            var stream = client.GetStream();
            stream.ReadTimeout = 30000;
            using (var hello = TabForge.Audio.Contracts.Frames.Read(stream, IsolatedFrameLimits.ForReply) ?? throw new InvalidOperationException("no hello"))
                Assert.Equal(RemotePlugin.MsgHello, hello.Type);
            using (var loaded = TabForge.Audio.Contracts.Frames.Read(stream, IsolatedFrameLimits.ForReply) ?? throw new InvalidOperationException("no loaded frame"))
                Assert.Equal(RemotePlugin.MsgLoaded, loaded.Type);
            client.Client.LingerState = new LingerOption(true, 0);
            client.Dispose();
            Assert.True(process.WaitForExit(30000), "the Wine helper must notice the vanished engine and exit");
            Assert.False(File.Exists(blockPath), "the helper must delete the block file when the engine cannot");
            _output.WriteLine($"wine engine death: helper exit={process.ExitCode} block_deleted={!File.Exists(blockPath)}");
        }
        finally
        {
            try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { }
            Stage15FullDiskKit.Delete(blockPath);
            Stage15FullDiskKit.DeleteFolder(folder);
        }
    }

    [Vst32WineFact]
    public void AudioContinuesWhenItsLogCannotBeCreated()
    {
        using var folder = new Vst32TestAssets.TempFolder();
        var logPath = Path.Combine(folder.Path, "engine.log");
        Directory.CreateDirectory(logPath);
        var writer = new EngineLogWriter();
        Assert.False(writer.CreateForRun(logPath));
        var threads = new EngineThreads(writer);
        var spec = Vst32TestAssets.Spec(Vst32TestAssets.TestPluginPath!, instrument: false);
        var launch = Vst32Bridge.Launch(spec, helperFolder: Path.GetDirectoryName(Vst32TestAssets.HelperPath!)!);
        using var plugin = new RemotePlugin(spec, Rate, Frames, threads, launch);
        var input = Enumerable.Repeat(0.125f, Frames).ToArray();
        var output = new[] { new float[Frames], new float[Frames] };
        for (var block = 0; block < 6; block++)
            plugin.Process(new[] { input, input }, output, Frames, ReadOnlySpan<BlockMidi>.Empty, Vst32TestAssets.Transport(block, Frames));
        Assert.True(Vst32TestAssets.AllFinite(output[0], output[1]));
        Assert.False(plugin.LinkFailed);
        writer.Write("engine still alive");
        writer.Flush(2000);
        _output.WriteLine($"wine log-less session missed={plugin.MissedBlocks}");
    }

    /// <summary>A real prefix: a corrupted marker is repaired, and a read-only prefix is refused before Wine runs.</summary>
    [Vst32WineFact]
    public void RealWineRepairsACorruptedMarkerAndRefusesAReadOnlyPrefix()
    {
        if (!OperatingSystem.IsLinux()) return;
        if (Environment.UserName == "root") return;   // root can write a 0555 folder; the CI runner is not root
        var located = Locate();
        var folder = Directory.CreateTempSubdirectory("tf-s15-real-prefix").FullName;
        var prefix = Path.Combine(folder, "prefix");
        var runtime = new WineRuntime(located.WineExecutable, located.HelperPath, prefix, helperPath64: located.HelperPath64);
        try
        {
            runtime.EnsureReady(5000, 120000);
            var marker = Path.Combine(prefix, ".tabforge-wine");
            Assert.True(File.Exists(marker));
            File.WriteAllText(marker, "corrupted\n");
            runtime.EnsureReady(5000, 120000);
            Assert.Contains("crashDialog=off", File.ReadAllLines(marker));

            var mode = File.GetUnixFileMode(prefix);
            try
            {
                File.SetUnixFileMode(prefix, UnixFileMode.UserRead | UnixFileMode.UserExecute);
                var error = Assert.Throws<InvalidOperationException>(() => runtime.EnsureReady(5000, 120000));
                Assert.Contains("not writable", error.Message);
                Assert.False(Path.Exists(Path.Combine(prefix, ".tabforge-setup.lock")));
            }
            finally { File.SetUnixFileMode(prefix, mode); }
            _output.WriteLine("real wine prefix: corrupted marker repaired, read-only prefix refused readably");
        }
        finally
        {
            KillWineServer(Path.Combine(Path.GetDirectoryName(runtime.WineExecutable)!, "wineserver"), prefix);
            Stage15FullDiskKit.DeleteFolder(folder);
        }
    }

    private static WineRuntime Locate() =>
        WineRuntime.Locate(helperFolder: Path.GetDirectoryName(Vst32TestAssets.HelperPath!)!)
        ?? throw new InvalidOperationException("WineRuntime.Locate returned null on the Wine runner");

    private static void KillWineServer(string wineServer, string prefix)
    {
        try
        {
            var start = new ProcessStartInfo(wineServer)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            start.ArgumentList.Add("-k");
            start.Environment["WINEPREFIX"] = prefix;
            using var process = Process.Start(start);
            process?.WaitForExit(15000);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }

    private static long BlockBytes(int frames) => 64 + PluginHostBlock.MaxEvents * 8L + 2L * 4 * frames * sizeof(float);
}
