using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using TabForge.Capture;
using TabForge.Services;

namespace TabForge.Avalonia.Capture;

// Owns: capture mode start-up: the fresh scratch profile, the app start without the normal window, running the script and ending the process with its exit code.
// Does not own: the steps (CaptureSession and the step classes), the normal start (Program, App).
// Tests: TabForge.Avalonia.Tests.CaptureSessionTests.
// Threads: Run on the entry thread; Start and the script on the UI thread.
internal static class CaptureLaunch
{
    /// <summary>Exit code when the script could not run at all (as against 1: it ran and a step failed).</summary>
    public const int NotRunExitCode = 2;

    /// <summary>Runs capture mode: points the user data at a fresh scratch folder (never the real profile), then starts the app, which calls <see cref="Start"/>.</summary>
    public static int Run(string[] args)
    {
        var scratch = Path.Combine(Path.GetTempPath(), "tabforge-capture-" + Guid.NewGuid().ToString("N"));
        UserPaths.ApplyProfileArgument(new[] { "--profile", scratch });
        Program.InstallMediaPathPolicy();
        try { return Program.BuildAvaloniaApp().StartWithClassicDesktopLifetime(args); }
        finally { RemoveScratch(scratch); }
    }

    /// <summary>Deletes the scratch profile this run made (a folder the harness itself created under the temp folder); a file still held open is left for the system temp clean-up.</summary>
    private static void RemoveScratch(string scratch)
    {
        try { if (Directory.Exists(scratch)) Directory.Delete(scratch, true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>The command line of capture mode, for the message of a refused one.</summary>
    public const string Usage = "Usage: TabForge.exe --capture <script.json> --out <folder>";

    /// <summary>Called by the app instead of opening the normal window: runs the script on the UI thread, then shuts the app down.</summary>
    public static void Start(IClassicDesktopStyleApplicationLifetime desktop, CaptureArguments arguments)
    {
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Dispatcher.UIThread.Post(async () =>
        {
            var code = NotRunExitCode;
            try { code = await RunScriptAsync(desktop, arguments); }
            finally { desktop.Shutdown(code); }   // always: a throw while writing the report must not leave the app running
        });
    }

    private static async Task<int> RunScriptAsync(IClassicDesktopStyleApplicationLifetime desktop, CaptureArguments arguments)
    {
        try
        {
            var script = CaptureScript.Parse(File.ReadAllText(arguments.ScriptPath));
            var session = new CaptureSession(arguments.OutputFolder);
            desktop.MainWindow = session.Window;
            await session.StartAsync();
            var failed = await session.RunAsync(script, Path.GetFileNameWithoutExtension(arguments.ScriptPath));
            session.Window.Close();
            return failed == 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            try
            {
                Directory.CreateDirectory(arguments.OutputFolder);
                File.WriteAllText(Path.Combine(arguments.OutputFolder, Path.GetFileNameWithoutExtension(arguments.ScriptPath) + ".error.txt"), ex.ToString());
            }
            catch (Exception write) when (write is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { Console.Error.WriteLine(ex.ToString()); }
            return NotRunExitCode;
        }
    }
}
