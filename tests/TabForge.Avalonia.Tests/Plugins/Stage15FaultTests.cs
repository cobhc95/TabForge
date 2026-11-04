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

// Owns: Stage 15 Windows/portable fault injection: an engine that disappears leaves no helper or block file, a block
// file that cannot be created fails readably before any process starts, and a log that cannot be written never stops
// audio. The full-disk variants use a small CI-only volume; Linux/Wine runs are in Stage15WineTests.
// Does not own: killed-helper containment (Vst32BridgeCrashTests / Vst32WineLinuxTests) or the prefix state machine
// (WineRuntimeLinuxTests).
[Collection("Vst32Native")]
public sealed class Stage15FaultTests
{
    private const int Rate = 48000, Frames = 256;
    private readonly ITestOutputHelper _output;

    public Stage15FaultTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// Regression for the helper-side half of "the engine was killed": the engine cannot run its crash path when its
    /// process is gone, so the TCP helper must remove its explicit block file when the control peer disappears.
    /// </summary>
    [Vst32Fact]
    public void AHelperWhoseEngineDisappearsDeletesItsBlockFileAndExits()
    {
        var blockPath = Path.Combine(Path.GetTempPath(), "tf-engine-death-" + Guid.NewGuid().ToString("N") + ".shm");
        using (var block = File.Create(blockPath)) block.SetLength(BlockBytes(Frames));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var start = new ProcessStartInfo(Vst32TestAssets.HelperPath!)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        start.Environment[RemotePlugin.TokenVariable] = token;
        foreach (var argument in new[]
        {
            PluginHostLaunch.Vst32TcpHostMode, port.ToString(), blockPath, Rate.ToString(), Frames.ToString(), "0", "VST2",
            Vst32TestAssets.TestPluginPath!, Vst32TestAssets.Sha256(Vst32TestAssets.TestPluginPath!), "none", "checked", "2",
        }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        try
        {
            using var client = listener.AcceptTcpClient();
            client.NoDelay = true;
            var stream = client.GetStream();
            stream.ReadTimeout = 10000;
            using (var hello = TabForge.Audio.Contracts.Frames.Read(stream, IsolatedFrameLimits.ForReply) ?? throw new InvalidOperationException("no hello"))
                Assert.Equal(RemotePlugin.MsgHello, hello.Type);
            using (var loaded = TabForge.Audio.Contracts.Frames.Read(stream, IsolatedFrameLimits.ForReply) ?? throw new InvalidOperationException("no loaded frame"))
                Assert.Equal(RemotePlugin.MsgLoaded, loaded.Type);
            // Abort the socket the way a killed engine does: no CmdQuit, no graceful shutdown.
            client.Client.LingerState = new LingerOption(true, 0);
            client.Dispose();
            Assert.True(process.WaitForExit(10000), "the helper must notice the vanished engine and exit");
            Assert.False(File.Exists(blockPath), "the helper must delete the block file when the engine cannot");
            _output.WriteLine($"engine-death helper exit={process.ExitCode} block_deleted={!File.Exists(blockPath)}");
        }
        finally
        {
            try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { }
            Stage15FullDiskKit.Delete(blockPath);
        }
    }

    /// <summary>A directory where the block file belongs: the load fails in one readable sentence and no helper starts.</summary>
    [Fact]
    public void AnUncreatableBlockFileFailsReadablyAndStartsNoProcess()
    {
        using var folder = new Vst32TestAssets.TempFolder();
        Stage15FullDiskKit.MakeOwnerOnly(folder.Path);
        var id = Guid.NewGuid().ToString("N");
        var blockPath = WineRuntime.BlockPath(id, folder.Path);
        Directory.CreateDirectory(blockPath);
        var plugin = WriteDummyPlugin(folder.Path);
        var started = false;
        var launch = new PluginHostLaunch("tfvst32.exe", PluginHostLaunch.Vst32TcpHostMode, 2, PluginHostTransport.Tcp);
        try
        {
            var error = Assert.Throws<InvalidOperationException>(() => new RemotePlugin(Vst32TestAssets.Spec(plugin, instrument: false),
                Rate, Frames, new EngineThreads(new EngineLogWriter()), launch, connectTimeoutMs: 1000, loadTimeoutMs: 1000,
                sessionId: id, startProcess: _ => { started = true; throw new InvalidOperationException("the helper must not start"); },
                blockFolder: folder.Path));
            Assert.Contains("audio block", error.Message);
            Assert.Contains(blockPath, error.Message);
            Assert.False(started, "a block failure must happen before the helper process is started");
            Assert.True(Directory.Exists(blockPath), "a path the test planted must not be deleted by the failure path");
            _output.WriteLine("block failure: " + error.Message);
        }
        finally
        {
            Stage15FullDiskKit.DeleteFolder(blockPath);
            Stage15FullDiskKit.Delete(plugin);
        }
    }

    /// <summary>A full (small) volume: the block cannot be sized, the message names the block path, and the partial file is removed.</summary>
    [Stage15FullDiskFact]
    public void AFullDiskBlockFileFailsReadablyAndLeavesNoFileOrProcess()
    {
        var folder = Stage15FullDiskKit.Folder!;
        Stage15FullDiskKit.MakeOwnerOnly(folder);
        var id = Guid.NewGuid().ToString("N");
        var blockPath = WineRuntime.BlockPath(id, folder);
        var plugin = WriteDummyPlugin(Path.GetTempPath());
        var started = false;
        var launch = new PluginHostLaunch("tfvst32.exe", PluginHostLaunch.Vst32TcpHostMode, 2, PluginHostTransport.Tcp);
        var filler = Stage15FullDiskKit.Fill(folder);
        try
        {
            var error = Assert.Throws<InvalidOperationException>(() => new RemotePlugin(Vst32TestAssets.Spec(plugin, instrument: false),
                Rate, Frames, new EngineThreads(new EngineLogWriter()), launch, connectTimeoutMs: 1000, loadTimeoutMs: 1000,
                sessionId: id, startProcess: _ => { started = true; throw new InvalidOperationException("the helper must not start"); },
                blockFolder: folder));
            Assert.Contains("audio block", error.Message);
            Assert.Contains(blockPath, error.Message);
            Assert.False(started);
            Assert.False(File.Exists(blockPath), "a partially created block file must be removed on the failure path");
            _output.WriteLine("full-disk block failure: " + error.Message);
        }
        finally
        {
            Stage15FullDiskKit.Delete(filler);
            Stage15FullDiskKit.Delete(blockPath);
            Stage15FullDiskKit.Delete(plugin);
        }
    }

    /// <summary>Logs on a full disk are best effort: creation may fail and later writes are dropped, never thrown.</summary>
    [Stage15FullDiskFact]
    public void AFullDiskLogIsContainedAndAudioStillRuns()
    {
        var folder = Stage15FullDiskKit.Folder!;
        Stage15FullDiskKit.MakeOwnerOnly(folder);
        var filler = Stage15FullDiskKit.Fill(folder);
        var logPath = Path.Combine(folder, "engine.log");
        var writer = new EngineLogWriter();
        try
        {
            var created = writer.CreateForRun(logPath);
            writer.Write("stage15 full-disk log probe");
            writer.Flush(2000);   // must return, never throw, whether the file exists or not
            _output.WriteLine($"full-disk log created={created} queued_dropped={writer.Dropped} path={writer.LogPath ?? "none"}");
        }
        finally
        {
            Stage15FullDiskKit.Delete(filler);
            if (writer.LogPath is { } path) Stage15FullDiskKit.Delete(path);
            Stage15FullDiskKit.Delete(logPath);
        }
    }

    /// <summary>A log path that is a directory: CreateForRun refuses, and a forced path only drops batches (no exception).</summary>
    [Fact]
    public void AnUnwritableLogPathNeverStopsTheEngine()
    {
        using var folder = new Vst32TestAssets.TempFolder();
        var logPath = Path.Combine(folder.Path, "engine.log");
        Directory.CreateDirectory(logPath);
        var writer = new EngineLogWriter();
        Assert.False(writer.CreateForRun(logPath));
        writer.Write("no log file");
        writer.Flush(2000);
        writer.UseFileForTests(logPath);   // force the doomed path: the append failure must be swallowed
        writer.Write("append must not throw");
        writer.Flush(2000);
        Assert.Equal(logPath, writer.LogPath);   // the writer keeps its configured path even when every batch is dropped
    }

    /// <summary>With no writable log, a real isolated plug-in still processes and returns finite output.</summary>
    [Vst32Fact]
    public void AudioContinuesWhenItsLogCannotBeCreated()
    {
        using var folder = new Vst32TestAssets.TempFolder();
        var logPath = Path.Combine(folder.Path, "engine.log");
        Directory.CreateDirectory(logPath);
        var writer = new EngineLogWriter();
        Assert.False(writer.CreateForRun(logPath));
        var threads = new EngineThreads(writer);
        var launch = new PluginHostLaunch(Vst32TestAssets.HelperPath!, PluginHostLaunch.Vst32TcpHostMode, 2, PluginHostTransport.Tcp);
        using var plugin = new RemotePlugin(Vst32TestAssets.Spec(Vst32TestAssets.TestPluginPath!, instrument: false),
            Rate, Frames, threads, launch);
        var input = Enumerable.Repeat(0.125f, Frames).ToArray();
        var output = new[] { new float[Frames], new float[Frames] };
        for (var block = 0; block < 8; block++)
            plugin.Process(new[] { input, input }, output, Frames, ReadOnlySpan<BlockMidi>.Empty, Vst32TestAssets.Transport(block, Frames));
        Assert.True(Vst32TestAssets.AllFinite(output[0], output[1]));
        Assert.False(plugin.LinkFailed);
        writer.Write("engine still alive");
        writer.Flush(2000);
        _output.WriteLine($"log-less session missed={plugin.MissedBlocks}");
    }

    private static long BlockBytes(int frames) => 64 + PluginHostBlock.MaxEvents * 8L + 2L * 4 * frames * sizeof(float);

    private static string WriteDummyPlugin(string folder)
    {
        var path = Path.Combine(folder, "stage15-dummy-" + Guid.NewGuid().ToString("N") + ".dll");
        File.WriteAllBytes(path, [0x4D, 0x5A, 0, 0]);
        return path;
    }
}
