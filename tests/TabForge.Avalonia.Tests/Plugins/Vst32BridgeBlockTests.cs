using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using TabForge.AudioEngine;
using TabForge.AudioEngine.Isolation;
using TabForge.AudioEngine.Plugins;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the stage-3 block layout, the native selftest, and the hello/Quit handshake with a fake helper.
// Does not own: audio through a real plug-in (Vst32BridgeAudioTests) or real fixtures (Vst32RealPluginTests).
[Collection("Vst32Native")]
public sealed class Vst32BridgeBlockTests
{
    [Fact]
    public void TheTwoSlotLayoutMatchesThePlan()
    {
        Assert.Equal(8, Marshal.SizeOf<BlockMidi>());
        const long channels = 4160;
        var sizeFor = typeof(PluginHostBlock).GetMethod("SizeFor", BindingFlags.NonPublic | BindingFlags.Static, new[] { typeof(int), typeof(int) })!;
        Assert.Equal(8256L, (long)sizeFor.Invoke(null, new object[] { 256, 1 })!);
        Assert.Equal(12352L, (long)sizeFor.Invoke(null, new object[] { 256, 2 })!);
        Assert.Equal(20544L, (long)sizeFor.Invoke(null, new object[] { 512, 2 })!);
        Assert.Equal(12352L, channels + 2L * 4 * 256 * sizeof(float));
    }

    [Fact]
    public void WaitDoneNeverRoundsAPositiveWaitDownToZero()
    {
        using var block = PluginHostBlock.Create(Guid.NewGuid().ToString("N"), 256, portable: true, bufferSlots: 1);
        Assert.False(block.WaitDone(1, 0.05));   // warm-up: the first call may JIT
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < 5; i++) Assert.False(block.WaitDone(1, 0.05));
        watch.Stop();

        // Five sub-millisecond budgets must each wait at least 1 ms, not spin through WaitOne(0).
        Assert.True(watch.Elapsed.TotalMilliseconds >= 4, $"five sub-ms waits took only {watch.Elapsed.TotalMilliseconds:0.0} ms");
        // The final DoneGeneration recheck still accepts an answer that arrived inside the last wait.
        block.DoneGeneration = 7;
        Assert.True(block.WaitDone(7, 0.05));
    }

    [Vst32HelperFact]
    public void TheNativeSelfTestPasses()
    {
        var helper = Vst32TestAssets.HelperPath!;
        using var process = Process.Start(new ProcessStartInfo(helper, "--selftest")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        })!;
        // Read both pipes while the child runs: a sequential ReadToEnd can block when the child fills the other pipe.
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(60000))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("the native selftest did not finish within 60 s");
        }
        var output = outputTask.GetAwaiter().GetResult();
        var error = errorTask.GetAwaiter().GetResult();
        Assert.True(process.ExitCode == 0, $"exit {process.ExitCode}: {output} {error}");
        Assert.Contains("selftest: ok", output);
    }

    [Vst32Fact]
    public void TheNativeVst2HostSelfTestPasses()
    {
        var testExe = Vst32TestAssets.NativeTestExe;
        Assert.False(string.IsNullOrEmpty(testExe), "tfvst32test.exe is not built");
        var plugin = Vst32TestAssets.TestPluginPath!;
        var start = new ProcessStartInfo(testExe!)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        start.ArgumentList.Add(plugin);
        start.ArgumentList.Add(Vst32TestAssets.Sha256(plugin));
        using var process = Process.Start(start)!;
        // Drain both pipes concurrently and give a loaded machine 60 s: the old sequential ReadToEnd could block the
        // test past its own 10 s wait, and a slow antivirus/scan first run is not a race in the helper.
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(60000))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("the native VST2 host selftest did not finish within 60 s");
        }
        var output = outputTask.GetAwaiter().GetResult();
        var error = errorTask.GetAwaiter().GetResult();
        Assert.True(process.ExitCode == 0, $"exit {process.ExitCode}: {output} {error}");
        Assert.Contains("tfvst32test: ok", output);
    }

    [WindowsFact]
    public void AFakeHelperSendsHelloThenAcceptsQuit()
    {
        var id = Guid.NewGuid().ToString("N");
        var script = Path.Combine(Path.GetTempPath(), $"tf-vst32-fake-{id}.ps1");
        var marker = Path.Combine(Path.GetTempPath(), $"tf-vst32-quit-{id}.txt");
        File.WriteAllText(script, FakeHelperScript, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false, CreateNoWindow = true,
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-ExecutionPolicy");
        start.ArgumentList.Add("Bypass");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(script);
        start.ArgumentList.Add(id);
        start.ArgumentList.Add("48000");
        start.ArgumentList.Add("256");
        start.ArgumentList.Add("1");
        start.ArgumentList.Add(marker);
        var process = Process.Start(start) ?? throw new InvalidOperationException("the fake helper did not start");
        var pid = process.Id;
        try
        {
            var spec = Vst32TestAssets.Spec(@"C:\vst\Fake32.dll", instrument: false, sha256: "none");
            var launch = new PluginHostLaunch(@"C:\helper\tfvst32.exe", PluginHostLaunch.Vst32HostMode, 1);
            var plugin = new RemotePlugin(spec, 48000, 256, new EngineThreads(new EngineLogWriter()), launch,
                connectTimeoutMs: 8000, loadTimeoutMs: 8000, sessionId: id, startProcess: _ => process);
            Assert.False(plugin.IsInstrument);
            Assert.False(plugin.HasEditor);
            Assert.Equal(0, plugin.LatencySamples);
            plugin.Dispose();
            Assert.True(Vst32TestAssets.WaitsForExit(pid, 4000), "the fake helper did not exit after CmdQuit");
            Assert.True(File.Exists(marker), "the fake helper did not receive the Quit command");
        }
        finally
        {
            try { using var alive = Process.GetProcessById(pid); if (!alive.HasExited) alive.Kill(true); }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            try { File.Delete(script); } catch (IOException) { }
            try { File.Delete(marker); } catch (IOException) { }
        }
    }

    // The fake is a real connected helper (a named-pipe client): hello, loaded, then wait for the Quit frame.
    private const string FakeHelperScript = """
        param([string]$id, [int]$rate, [int]$block, [int]$slots, [string]$marker)
        $pipe = New-Object System.IO.Pipes.NamedPipeClientStream('.', "TabForge.PluginHost.$id", [System.IO.Pipes.PipeDirection]::InOut)
        $pipe.Connect(5000)
        $writer = New-Object System.IO.BinaryWriter($pipe)
        $writer.Write([int]21)
        $writer.Write([byte]108)
        $writer.Write([uint32]0x32334654)
        $writer.Write([uint16]1)
        $writer.Write([uint16]64)
        $writer.Write([int]$block)
        $writer.Write([int]$rate)
        $writer.Write([int]$slots)
        $writer.Write([int]11)
        $writer.Write([byte]100)
        $writer.Write([bool]$false)
        $writer.Write([bool]$false)
        $writer.Write([int]0)
        $writer.Write([int]0)
        $writer.Flush()
        $reader = New-Object System.IO.BinaryReader($pipe)
        while ($true) {
            try { $length = $reader.ReadInt32() } catch { break }
            if ($length -lt 1 -or $length -gt 1048576) { break }
            $type = $reader.ReadByte()
            $rest = $reader.ReadBytes($length - 1)
            if ($type -eq 9) { break }
        }
        Set-Content -LiteralPath $marker -Value "quit"
        $pipe.Dispose()
        exit 0
        """;
}
