using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the checks on the Avalonia exe's start arguments: the helper starts (--import-worker, --plugin-host) returning at once with no window when their
/// arguments are missing, and the startup-file argument decision (an existing song opens, options and missing paths do not).
/// Does not own: the helpers themselves (ImportWorker, PluginHostMain), the app's start (App) or opening the file (MainWindowFile).
/// Tests: this class.
/// </summary>
public class HelperModeEntryTests
{
    /// <summary>A start that opened a window would never exit; this bound fails the test instead of hanging.</summary>
    private static readonly TimeSpan ExitTimeout = TimeSpan.FromSeconds(30);

    /// <summary>The exit code each helper gives when it is started with no arguments of its own.</summary>
    private const int MissingArgumentsExitCode = 2;

    [Theory]
    [InlineData("--import-worker")]
    [InlineData("--plugin-host")]
    public async Task HelperStartWithoutItsArgumentsExitsAtOnceWithoutWindow(string mode)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "TabForge.dll"));
        start.ArgumentList.Add(mode);

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
            Assert.Fail($"{mode} without its arguments did not exit: an Avalonia window may have opened.");
        }

        Assert.Equal(MissingArgumentsExitCode, process.ExitCode);
        Assert.Equal(string.Empty, await stdout);
    }

    [Fact]
    public void StartupFileTakesAnExistingPathAndRejectsOptionsAndMissingPaths()
    {
        var existing = Path.Combine(Path.GetTempPath(), $"tf-startup-{Guid.NewGuid():N}.tforge");
        File.WriteAllText(existing, string.Empty);
        try
        {
            var accepted = Program.StartupFileFrom(new[] { existing });
            Assert.Equal(existing, accepted.SongPath);
            Assert.Null(accepted.MissingPath);

            var option = Program.StartupFileFrom(new[] { "--flag" });
            Assert.Null(option.SongPath);
            Assert.Null(option.MissingPath);

            var missing = Path.Combine(Path.GetTempPath(), $"tf-startup-missing-{Guid.NewGuid():N}.tforge");
            var rejected = Program.StartupFileFrom(new[] { missing });
            Assert.Null(rejected.SongPath);
            Assert.Equal(missing, rejected.MissingPath);
        }
        finally
        {
            File.Delete(existing);
        }
    }
}
