using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine;
using TabForge.AudioEngine.Isolation;
using TabForge.AudioEngine.Plugins;
using Xunit;
using Xunit.Abstractions;

namespace TabForge.Avalonia.Tests;

// Owns: the Wine transport proven natively on Windows: loopback TCP control, the explicit file block with polled
// flags, audio/parameters/state through the deterministic and real 32-bit fixtures, and crash containment.
// Does not own: Windows named-pipe behaviour (the existing Vst32* tests) or Wine itself (no Wine on this machine).
[Collection("Vst32Native")]
public sealed class Vst32TcpBridgeTests
{
    private const int Rate = 48000;
    private readonly ITestOutputHelper _output;

    public Vst32TcpBridgeTests(ITestOutputHelper output) => _output = output;

    [Vst32Fact]
    public void TcpSessionUsesAnExplicitFileBlockAndProcessesAudio()
    {
        var (plugin, _) = Vst32TestAssets.StartTcp(Vst32TestAssets.TestPluginPath!, instrument: false, maxBlock: 256);
        var blockPath = plugin.BlockFilePath;
        var pid = plugin.HostProcessId;
        try
        {
            Assert.NotNull(blockPath);
            Assert.True(File.Exists(blockPath), "the file block must exist while the helper is running");
            Assert.Equal(12352, new FileInfo(blockPath!).Length);   // 64 header + 512*8 events + 2 slots * 4 * 256 floats
            using (var view = PluginHostBlock.OpenFile(blockPath!, 256, 2))
            {
                Assert.Equal((256, 2), (view.MaxBlock, view.BufferSlots));
                Assert.Equal(12352, (int)new FileInfo(blockPath!).Length);
            }

            var input = new[] { new float[256], new float[256] };
            var output = new[] { new float[256], new float[256] };
            double inputSum = 0, outputSum = 0;
            var phase = 0.0;
            for (var block = 0; block < 8; block++)
            {
                for (var i = 0; i < 256; i++)
                {
                    var sample = (float)(0.25 * Math.Sin(phase));
                    phase += 2 * Math.PI * 1000 / Rate;
                    input[0][i] = sample;
                    input[1][i] = sample;
                }
                plugin.Process(input, output, 256, ReadOnlySpan<BlockMidi>.Empty, Vst32TestAssets.Transport(block, 256));
                Assert.True(Vst32TestAssets.AllFinite(output[0], output[1]), "output must be finite");
                if (block == 0) continue;   // the first block may pass through dry while the helper warms up
                foreach (var sample in input[0]) inputSum += sample * sample;
                foreach (var sample in output[0]) outputSum += sample * sample;
            }
            Assert.True(Vst32TestAssets.Rms(output[0]) > 0.01f, "output must be non-silent");
            Assert.InRange(Math.Sqrt(outputSum / Math.Max(1e-12, inputSum)), 0.95, 1.05);
        }
        finally
        {
            plugin.Dispose();
        }
        Assert.True(Vst32TestAssets.WaitsForExit(pid, 4000), "the TCP helper must exit after CmdQuit");
        Assert.False(File.Exists(blockPath), "the engine must delete the explicit block file on dispose");
    }

    [Vst32Fact]
    public void TcpParametersAndStateRoundTripThroughTheFileBlock()
    {
        using var plugin = Vst32TestAssets.StartTcp(Vst32TestAssets.TestPluginPath!, instrument: false, maxBlock: 1024).Plugin;
        var parameters = plugin.GetParameters();
        var parameter = Assert.Single(parameters);
        Assert.Contains("gain", parameter.Name, StringComparison.OrdinalIgnoreCase);
        Assert.True(plugin.SetParameter(0, 0.25));
        Assert.InRange(plugin.GetParameters().Single().Value, 0.20, 0.30);
        Assert.True(plugin.SetParameter(0, 0.75));
        Assert.InRange(plugin.GetParameters().Single().Value, 0.70, 0.80);
        var state = plugin.GetState();
        Assert.NotNull(state);
        Assert.NotEmpty(state);
        plugin.SetState(state!);
        Assert.InRange(plugin.GetParameters().Single().Value, 0.70, 0.80);   // the saved state was captured at 0.75
        Assert.Equal(LoadedPluginInfo.Vst2, plugin.Describe().Format);
        _output.WriteLine($"TCP test plug-in: state {state!.Length} bytes, parameter and state round trip ok");
    }

    [Vst32Fact]
    public void TcpParameterChangesAreAppliedToAudio()
    {
        using var plugin = Vst32TestAssets.StartTcp(Vst32TestAssets.TestPluginPath!, instrument: false, maxBlock: 1024).Plugin;
        Assert.True(plugin.SetParameter(0, 0.25));
        var quiet = RenderProcessed(plugin, 1024, blocks: 4, skip: 1);
        Assert.True(plugin.SetParameter(0, 0.75));
        var loud = RenderProcessed(plugin, 1024, blocks: 4, skip: 1);
        Assert.True(loud > quiet * 2.5f, $"gain 0.5 must be quieter than gain 1.5 (quiet {quiet:0.0000}, loud {loud:0.0000})");
    }

    [Vst32RealFact]
    public void TcpRealFixtureAudioParametersAndState()
    {
        var path = Vst32TestAssets.FixturePath("PurestGain32");
        Assert.True(File.Exists(path), $"missing fixture: {path}");
        using var plugin = Vst32TestAssets.StartTcp(path, instrument: false, maxBlock: 1024).Plugin;
        var parameters = plugin.GetParameters();
        Assert.NotEmpty(parameters);
        var id = parameters[0].Id;
        _output.WriteLine("PurestGain TCP params: " + string.Join(", ", parameters.Select(p => $"{p.Id}:{p.Name}={p.Value:0.000} ({p.Text})")));
        var dry = Render(plugin, 1024, blocks: 8, skip: 2);
        Assert.True(dry > 1e-4f, $"the real fixture must produce audio over TCP (RMS {dry:0.0000})");
        Assert.True(plugin.SetParameter(id, 0.0));
        Assert.InRange(plugin.GetParameters().First(p => p.Id == id).Value, 0.0, 0.01);   // the parameter write reaches the plug-in
        var state = plugin.GetState();
        Assert.NotNull(state);
        Assert.NotEmpty(state);
        Assert.True(plugin.SetParameter(id, 1.0));
        Assert.InRange(plugin.GetParameters().First(p => p.Id == id).Value, 0.99, 1.0);
        plugin.SetState(state!);
        Assert.InRange(plugin.GetParameters().First(p => p.Id == id).Value, 0.0, 0.01);   // the state captured at 0 was restored
        var after = Render(plugin, 1024, blocks: 4, skip: 1);
        Assert.True(after > 1e-5f, "audio must still flow after the state round trip");
        _output.WriteLine($"PurestGain TCP: dry {Vst32TestAssets.Db(dry):0.0} dB, state {state!.Length} bytes, parameter and state round trip ok");
    }

    [Vst32RealFact]
    public void TcpCrashContainmentKeepsTheEngineAndBypassesDry()
    {
        var path = Vst32TestAssets.FixturePath("PurestGain32");
        var (plugin, threads) = Vst32TestAssets.StartTcp(path, instrument: false, maxBlock: 256);
        var blockPath = plugin.BlockFilePath;
        var crashed = 0;
        var crashSignal = new ManualResetEventSlim();
        plugin.Crashed += _ => { Interlocked.Increment(ref crashed); crashSignal.Set(); };
        var input = Enumerable.Repeat(0.25f, 256).ToArray();
        var output = new[] { new float[256], new float[256] };
        try
        {
            plugin.Process(new[] { input, input }, output, 256, ReadOnlySpan<BlockMidi>.Empty, Vst32TestAssets.Transport(0, 256));
            var pid = plugin.HostProcessId;
            using (var process = Process.GetProcessById(pid)) process.Kill(true);
            var killedAt = Stopwatch.GetTimestamp();
            while (Stopwatch.GetElapsedTime(killedAt).TotalMilliseconds < 3000 && !crashSignal.IsSet)
            {
                threads.RunPending();
                Thread.Sleep(2);
            }
            threads.RunPending();
            Assert.True(crashSignal.IsSet, "the TCP helper's death must be reported");
            Assert.Equal(1, crashed);
            Assert.False(File.Exists(blockPath), "the crash path must delete the block file, not leave it for the 2 h sweep");
            for (var i = 0; i < 20; i++)
            {
                plugin.Process(new[] { input, input }, output, 256, ReadOnlySpan<BlockMidi>.Empty, Vst32TestAssets.Transport(i, 256));
                Assert.Equal(input, output[0]);   // effect dry: the failed block is bypassed, not an exception
            }
            var disposeAt = Stopwatch.GetTimestamp();
            plugin.Dispose();
            Assert.True(Stopwatch.GetElapsedTime(disposeAt).TotalMilliseconds < 2000, "Dispose must stay bounded");
        }
        finally { plugin.Dispose(); }
        Assert.False(File.Exists(blockPath), "the block file must be cleaned up after a crash too");
    }

    /// <summary>
    /// Regression for Stage 13 C2/C3: a control-stream EOF is a crash, not an orderly end. The helper must be killed (it would
    /// otherwise loop TakeRequest forever) and the explicit block file must be deleted now, not left for the 2 h sweep.
    /// </summary>
    [Vst32Fact]
    public void ClosingTheControlStreamKillsTheHelperAndDeletesTheBlock()
    {
        var (plugin, threads) = Vst32TestAssets.StartTcp(Vst32TestAssets.TestPluginPath!, instrument: false, maxBlock: 256);
        var blockPath = plugin.BlockFilePath;
        var pid = plugin.HostProcessId;
        var crashed = new ManualResetEventSlim();
        plugin.Crashed += _ => crashed.Set();
        try
        {
            var pipe = (Stream)typeof(RemotePlugin)
                .GetField("_pipe", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(plugin)!;
            pipe.Dispose();   // the reader sees EOF/Disposed and reports a connection loss
            var clock = Stopwatch.StartNew();
            while (clock.Elapsed.TotalSeconds < 5 && !crashed.IsSet)
            {
                threads.RunPending();
                Thread.Sleep(5);
            }
            threads.RunPending();
            Assert.True(crashed.IsSet, "the connection loss must be reported");
            Assert.True(Vst32TestAssets.WaitsForExit(pid, 4000), "the helper must be killed on control EOF");
            Assert.False(File.Exists(blockPath), "the block file must be deleted on the crash path");
        }
        finally { plugin.Dispose(); }
    }

    /// <summary>
    /// Regression for Stage 13 C9: a helper whose control channel faulted (its main thread stopped answering) is killed by
    /// the link watchdog even though audio may still flow, instead of being left orphaned with the block file.
    /// </summary>
    [Vst32Fact]
    public void AFaultedControlChannelMakesTheWatchdogKillTheHelper()
    {
        var (plugin, threads) = Vst32TestAssets.StartTcp(Vst32TestAssets.TestPluginPath!, instrument: false, maxBlock: 256);
        var blockPath = plugin.BlockFilePath;
        var pid = plugin.HostProcessId;
        var crashed = new ManualResetEventSlim();
        plugin.Crashed += _ => crashed.Set();
        try
        {
            var control = typeof(RemotePlugin).GetField("_control", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(plugin)!;
            control.GetType().GetField("_faulted", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(control, 1);
            var clock = Stopwatch.StartNew();
            while (clock.Elapsed.TotalSeconds < 5 && !crashed.IsSet)
            {
                threads.RunPending();
                Thread.Sleep(5);
            }
            threads.RunPending();
            Assert.True(crashed.IsSet, "the faulted control channel must be reported");
            Assert.True(Vst32TestAssets.WaitsForExit(pid, 4000), "the watchdog must kill the faulted helper");
            Assert.False(File.Exists(blockPath), "the block file must be deleted with the faulted helper");
        }
        finally { plugin.Dispose(); }
    }

    [Fact]
    public void TcpHelloRequiresTheSessionToken()
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        using var valid = Hello(token);
        RemotePlugin.ReadBridgeTcpHello(new BinaryReader(valid), token, Rate, 256, 2, expectedArch: 32, out var helper, out var wine);
        Assert.StartsWith("tfvst32/", helper);
        Assert.Equal("wine-test", wine);

        using var wrong = Hello(new string('0', 64));
        Assert.Throws<InvalidOperationException>(() =>
            RemotePlugin.ReadBridgeTcpHello(new BinaryReader(wrong), token, Rate, 256, 2, expectedArch: 32, out _, out _));
        using var wrongBlock = Hello(token, block: 512);
        Assert.Throws<InvalidOperationException>(() =>
            RemotePlugin.ReadBridgeTcpHello(new BinaryReader(wrongBlock), token, Rate, 256, 2, expectedArch: 32, out _, out _));
    }

    /// <summary>
    /// Regression for Stage 13 A1: a local process that finds the ephemeral control port first must not be able to keep the
    /// real helper out. The squatter presents a well-formed TFH1 hello with the wrong token; the engine must drop it and
    /// keep accepting until the helper's valid hello arrives.
    /// </summary>
    [Vst32Fact]
    public void TcpAcceptDropsAPortSquatterAndStillStartsTheRealHelper()
    {
        var launch = new PluginHostLaunch(Vst32TestAssets.HelperPath!, PluginHostLaunch.Vst32TcpHostMode, 2, PluginHostTransport.Tcp);
        var threads = new EngineThreads(new EngineLogWriter());
        TcpClient? squatter = null;
        try
        {
            using var plugin = new RemotePlugin(Vst32TestAssets.Spec(Vst32TestAssets.TestPluginPath!, instrument: false),
                Rate, 256, threads, launch, connectTimeoutMs: 15000, loadTimeoutMs: 90000, sessionId: null,
                startProcess: start =>
                {
                    var port = int.Parse(start.ArgumentList[1]);   // mode, port, block path, ...
                    squatter = new TcpClient();
                    squatter.Connect(IPAddress.Loopback, port);
                    Frames.Write(squatter.GetStream(), RemotePlugin.MsgHello, w =>
                    {
                        w.Write(RemotePlugin.BridgeTcpHelloMagic);
                        w.Write(RemotePlugin.BridgeProtocolVersion);
                        w.Write(RemotePlugin.BridgeHeaderSize);
                        w.Write(256);
                        w.Write(Rate);
                        w.Write(2);
                        w.Write(32);
                        w.Write(0);
                        w.WriteString("tfvst32/1");
                        w.WriteString("squatter");
                        w.Write(new byte[32]);   // the wrong token
                    });
                    return Process.Start(start)!;
                });

            var input = Enumerable.Repeat(0.25f, 256).ToArray();
            var output = new[] { new float[256], new float[256] };
            plugin.Process(new[] { input, input }, output, 256, ReadOnlySpan<BlockMidi>.Empty, Vst32TestAssets.Transport(0, 256));
            Assert.True(Vst32TestAssets.AllFinite(output[0], output[1]), "the real helper must still be driving the session");
        }
        finally { squatter?.Dispose(); }
    }

    /// <summary>
    /// Regression for Stage 13 A2: the native reader used to allocate the frame's declared length (up to 32 MiB) before
    /// checking its type. A complete 2 MiB control frame is now refused at the type byte: the helper closes without
    /// answering the CmdSetParam. The old reader would have processed it and sent MsgParams.
    /// </summary>
    [Vst32Fact]
    public void TcpHelperRefusesAnOversizedControlFrameBeforeAllocatingIt()
    {
        var blockPath = Path.Combine(Path.GetTempPath(), "tf-cap-" + Guid.NewGuid().ToString("N") + ".shm");
        using (var block = File.Create(blockPath)) block.SetLength(12352);   // 256 frames, two slots
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
            PluginHostLaunch.Vst32TcpHostMode, port.ToString(), blockPath, "48000", "256", "0", "VST2",
            Vst32TestAssets.TestPluginPath!, Vst32TestAssets.Sha256(Vst32TestAssets.TestPluginPath!), "none", "checked", "2",
        }) start.ArgumentList.Add(argument);

        using var process = Process.Start(start)!;
        try
        {
            using var client = listener.AcceptTcpClient();
            client.NoDelay = true;
            var stream = client.GetStream();
            stream.ReadTimeout = 10000;
            using (var hello = Frames.Read(stream, IsolatedFrameLimits.ForReply) ?? throw new InvalidOperationException("no hello"))
                Assert.Equal(RemotePlugin.MsgHello, hello.Type);
            using (var loaded = Frames.Read(stream, IsolatedFrameLimits.ForReply) ?? throw new InvalidOperationException("no loaded frame"))
                Assert.Equal(RemotePlugin.MsgLoaded, loaded.Type);

            var frame = new byte[5 + 2 * 1024 * 1024];
            BinaryPrimitives.WriteInt32LittleEndian(frame, frame.Length - 4);
            frame[4] = RemotePlugin.CmdSetParam;
            stream.Write(frame);
            stream.Flush();

            ReceivedFrame? reply = null;
            try { reply = Frames.Read(stream, IsolatedFrameLimits.ForReply); }
            catch (IOException) { /* the helper closed the connection: the refused frame is the expected outcome */ }
            reply?.Dispose();
            Assert.Null(reply);
            Assert.True(process.WaitForExit(5000), "the helper must refuse the oversized control frame and exit");
        }
        finally
        {
            try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { }
            try { File.Delete(blockPath); } catch (IOException) { }
        }
    }

    private static MemoryStream Hello(string tokenHex, int block = 256)
    {
        var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(RemotePlugin.BridgeTcpHelloMagic);
            writer.Write(RemotePlugin.BridgeProtocolVersion);
            writer.Write(RemotePlugin.BridgeHeaderSize);
            writer.Write(block);
            writer.Write(Rate);
            writer.Write(2);
            writer.Write(32);
            writer.Write(0);
            writer.WriteString("tfvst32/1");
            writer.WriteString("wine-test");
            writer.Write(Convert.FromHexString(tokenHex));
        }
        stream.Position = 0;
        return stream;
    }

    private static float Render(RemotePlugin plugin, int frames, int blocks, int skip)
    {
        var input = new[] { new float[frames], new float[frames] };
        var output = new[] { new float[frames], new float[frames] };
        var phase = 0.0;
        double sum = 0;
        var count = 0;
        for (var block = 0; block < blocks; block++)
        {
            for (var i = 0; i < frames; i++)
            {
                var sample = (float)(0.25 * Math.Sin(phase));
                phase += 2 * Math.PI * 1000 / Rate;
                input[0][i] = sample;
                input[1][i] = sample;
            }
            plugin.Process(input, output, frames, ReadOnlySpan<BlockMidi>.Empty, Vst32TestAssets.Transport(block, frames));
            if (block < skip) continue;
            foreach (var sample in output[0]) { sum += sample * (double)sample; count++; }
            foreach (var sample in output[1]) { sum += sample * (double)sample; count++; }
        }
        return count == 0 ? 0 : (float)Math.Sqrt(sum / count);
    }

    /// <summary>
    /// The same tone, but blocks the link bypassed (a missed deadline or a spent callback budget) are not measured: on a loaded
    /// machine a dry bypass would otherwise mix the input into the gain comparison. The assertion is unchanged; only the
    /// measurement ignores blocks the plug-in did not process, and extra blocks are rendered until enough clean ones arrived.
    /// </summary>
    private static float RenderProcessed(RemotePlugin plugin, int frames, int blocks, int skip)
    {
        var input = new[] { new float[frames], new float[frames] };
        var output = new[] { new float[frames], new float[frames] };
        var phase = 0.0;
        double sum = 0;
        var count = 0;
        var cleanBlocks = 0;
        for (var block = 0; block < blocks * 4 && cleanBlocks < blocks; block++)
        {
            for (var i = 0; i < frames; i++)
            {
                var sample = (float)(0.25 * Math.Sin(phase));
                phase += 2 * Math.PI * 1000 / Rate;
                input[0][i] = sample;
                input[1][i] = sample;
            }
            var missed = plugin.MissedBlocks;
            var skipped = plugin.BudgetSkippedBlocks;
            plugin.Process(input, output, frames, ReadOnlySpan<BlockMidi>.Empty, Vst32TestAssets.Transport(block, frames));
            if (block < skip) continue;
            if (plugin.MissedBlocks != missed || plugin.BudgetSkippedBlocks != skipped) continue;
            foreach (var sample in output[0]) { sum += sample * (double)sample; count++; }
            foreach (var sample in output[1]) { sum += sample * (double)sample; count++; }
            cleanBlocks++;
        }
        return count == 0 ? 0 : (float)Math.Sqrt(sum / count);
    }
}
