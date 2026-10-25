using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the check that the Avalonia exe's --audio-engine start returns at once, with no window, when no session is given.
/// Does not own: the engine itself (TabForge.AudioEngine) or the Avalonia app (Program, App).
/// Tests: this class.
/// </summary>
public class AudioEngineEntryTests
{
    /// <summary>A start that opened a window would never exit; this bound fails the test instead of hanging.</summary>
    private static readonly TimeSpan ExitTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task AudioEngineStartWithoutSessionExitsAtOnceWithoutWindow()
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "TabForge.dll"));
        start.ArgumentList.Add("--audio-engine");

        using var process = Process.Start(start) ?? throw new InvalidOperationException("The Avalonia exe did not start.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        _ = process.StandardError.ReadToEndAsync();   // drained so a chatty child cannot block on a full pipe
        using var timeout = new CancellationTokenSource(ExitTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("--audio-engine without a session did not exit: an Avalonia window may have opened.");
        }

        Assert.Equal(Program.AudioEngineRefusedExitCode, process.ExitCode);
        Assert.Equal(string.Empty, await stdout);
    }
}
