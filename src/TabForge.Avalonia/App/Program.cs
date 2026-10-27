using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using TabForge.Capture;
using TabForge.Platform;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: process-mode routing and app building (entry point).
// Does not own: the app itself (App), the audio engine or the import worker.

/// <summary>
/// Entry point of TabForge. Owns: routing the process modes and building the app.
/// Does not own: the app itself (App), the audio engine (TabForge.AudioEngine, reached only through EngineHost.Run) or the
/// import worker (TabForge.Services.ImportWorker).
/// Tests: TabForge.Avalonia.Tests.AudioEngineEntryTests, TabForge.Avalonia.Tests.HelperModeEntryTests.
/// </summary>
public static class Program
{
    /// <summary>Exit code of a helper-process start that is refused (no session given, or not Windows).</summary>
    public const int AudioEngineRefusedExitCode = 2;

    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (Exception ex)
        {
            // Last resort when anything before the crash handler fails (a pre-logging death leaves no trace/errors lines):
            // record it beside settings.json and in errors.log, then rethrow so the Event Log still gets the 1026.
            try { Platform.AvaloniaCrashLog.ForUserFolder().Record(ex); }
            catch (Exception) { } // swallow: the last-resort crash record must not throw
            try { Services.Trace.Error(Services.Trace.Ui, "startup: " + ex.Message + Environment.NewLine + (ex.InnerException ?? ex).StackTrace); }   // the stack too: the message alone (e.g. an index out of range) does not say where
            catch (Exception) { } // swallow: the trace write must not throw in the last-resort handler
            // Shootout-M H1: surface the fatal startup error on the launching console (WinExe has none of its own).
            try { ReportStartupErrorToConsole(ex); }
            catch (Exception) { } // swallow: the console report must not throw in the last-resort handler
            throw;
        }
    }

    private static int Run(string[] args)
    {
        // The self-test mode opens no window and holds no single-instance mutex, so it runs beside the owner's copy; it leaves the first-chance log alone.
        if (args.Length > 0 && args[0] == SelfTest.Argument) return RunSelfTest(args);
        // Each helper mode returns before Avalonia starts, and before anything that can throw (first-chance log,
        // single-instance mutex, media policy), so a helper started without its arguments exits 2 at once with no window.
        if (args.Length > 0 && args[0] == "--audio-engine") return RunAudioEngine(args);
        if (args.Length > 0 && args[0] == "--plugin-host") return RunPluginHost(args);
        if (args.Length > 0 && args[0] == "--plugin-info") return RunPluginInfo(args);
        if (args.Length > 0 && args[0] == ImportWorker.Argument)
        {
            // No pipe name: refuse before touching ImportWorker statics.
            if (args.Length < 2) return AudioEngineRefusedExitCode;
            return ImportWorker.Run(args);
        }
        // Capture mode (--capture <script> --out <folder>) is a diagnostic: it uses a scratch profile and no single-instance mutex, so it runs beside the user's own copy.
        bool capture;
        try { capture = CaptureArguments.TryParse(args) is not null; }
        catch (Exception ex) when (ex is FormatException or ArgumentException or NotSupportedException or IOException) { Console.Error.WriteLine(ex.Message + " " + Capture.CaptureLaunch.Usage); return Capture.CaptureLaunch.NotRunExitCode; }
        if (capture) return Capture.CaptureLaunch.Run(args);
        // Shootout-M shared diagnostic (all contenders): log every first-chance managed exception so a death that
        // bypasses Main's catch still leaves a trace. Best-effort only; never throws.
        InstallFirstChanceLog();
        // The song the shell named, resolved before the single-instance check so a second launch can hand it to the
        // running window instead of discarding it with the process.
        var startup = StartupFileFrom(args);
        // Single-instance: a second UI launch of the same exe activates the existing window and exits 0 before
        // Avalonia starts. Helpers bypass inside; an abandoned mutex counts as owned; any failure proceeds.
        try
        {
            if (Platform.AvaloniaSingleInstance.IsSecondInstance(args))
            {
                // A plain second launch exits here. A launch naming an existing song hands it to the running window and
                // exits; when nothing receives it (no receiver listening, or songs from Explorer open in a new window)
                // this launch opens it itself below, so a double-clicked file is never dropped.
                if (startup.SongPath is not { } secondSong || TryHandOver(secondSong)) return 0;
            }
        }
        catch (Exception) { } // swallow: any single-instance failure proceeds with a new window
        InstallMediaPathPolicy();
        // Tag any lifetime throw so the crash log names dispatcher-startup. Never touch Dispatcher.UIThread before this:
        // resolving it before the Win32 platform registers caches a dispatcher MainLoop cannot run (PlatformNotSupportedException).
        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("dispatcher-startup: " + ex.Message, ex);
        }
    }

    /// <summary>Hands the song a second launch named to the window already running; false when nothing receives it (not Windows, or no receiver listening).</summary>
    private static bool TryHandOver(string song)
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            var sent = SingleInstanceService.TrySendToRunningInstance(song);
            // A double-click that opens nothing is traced through this line: "no receiver" means the running window's
            // hand-over server is absent (an older build, or OpenFromExplorer = A new window), so this launch opens its own window.
            Services.Trace.Write(Services.Trace.Ui, "handover: " + (sent ? "sent " : "no receiver for ") + song);
            return sent;
        }
        catch (Exception ex)
        {
            Services.Trace.Error(Services.Trace.Ui, "handover: " + ex.GetType().Name + ": " + ex.Message);
            return false;
        }
    }

    /// <summary>The media path policy reads final paths through the Windows adapter; set once, before any process mode runs.</summary>
    internal static void InstallMediaPathPolicy() => MediaPathPolicy.FileSystem = new RealMediaFileSystem(PathResolvers.Create());

    /// <summary>Runs the headless self-tests (<c>--selftest &lt;log&gt;</c>); a <c>--profile</c> folder (default: a temp folder) keeps the run off the user's own data.</summary>
    private static int RunSelfTest(string[] args)
    {
        var profile = UserPaths.ApplySelfTestProfile(args);
        try
        {
            InstallMediaPathPolicy();
            return SelfTest.Run(profile.Args);
        }
        finally { profile.Dispose(); }
    }

    /// <summary>The first-chance log rotates past this size: the full file moves to firstchance.1.log and a new one starts.</summary>
    internal const long FirstChanceRotationBytes = 512L * 1024;

    private static readonly object FirstChanceGate = new();

    /// <summary>Shootout-M shared diagnostic: appends every first-chance managed exception to
    /// %LOCALAPPDATA%\TabForge\Diagnostics\firstchance.log. Best-effort only; never throws.</summary>
    private static void InstallFirstChanceLog()
    {
        try
        {
            var dir = TabForge.Platform.AppFolders.Current.Logs;
            Directory.CreateDirectory(dir);
            var log = Path.Combine(dir, "firstchance.log");
            AppDomain.CurrentDomain.FirstChanceException += (_, e) =>
            {
                try { WriteFirstChanceEntry(log, e.Exception); }
                catch (Exception) { } // swallow: the first-chance handler never throws
            };
        }
        catch (Exception) { } // swallow: the first-chance log is best-effort only
    }

    /// <summary>Appends one first-chance entry: the full <see cref="Exception.ToString"/> (type, message and stack),
    /// rotating the file past <see cref="FirstChanceRotationBytes"/>. Never throws.</summary>
    internal static void WriteFirstChanceEntry(string logPath, Exception ex)
    {
        // A failed write (the log held by another TabForge process, a full disk) raises its own first-chance exception on this
        // thread; logging that one would recurse until the stack overflows, so a write in progress ignores the ones it causes.
        if (_writingFirstChance) return;
        _writingFirstChance = true;
        try
        {
            lock (FirstChanceGate)
            {
                try
                {
                    if (File.Exists(logPath) && new FileInfo(logPath).Length > FirstChanceRotationBytes)
                    {
                        try
                        {
                            var rolled = Path.Combine(Path.GetDirectoryName(logPath) ?? ".", "firstchance.1.log");
                            File.Move(logPath, rolled, overwrite: true);
                        }
                        catch (Exception) { } // swallow: best-effort rotation of the first-chance log
                    }
                }
                catch (Exception) { } // swallow: best-effort rotation of the first-chance log
                string text;
                try { text = ex.ToString(); }
                catch (Exception)
                {
                    try { text = ex.GetType().FullName + ": " + ex.Message; }
                    catch (Exception) { return; }
                }
                // Shared so the helper processes (audio engine, plug-in hosts), which install the same log, do not lock each other out.
                using var stream = new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                using var writer = new StreamWriter(stream);
                writer.Write(DateTimeOffset.Now.ToString("o") + " " + text + Environment.NewLine);
            }
        }
        catch (Exception) { } // swallow: the first-chance entry write never throws
        finally { _writingFirstChance = false; }
    }

    [ThreadStatic] private static bool _writingFirstChance;

    /// <summary>Shootout-M H1: the framework-dependent apphost can die before managed code runs (missing/mismatched
    /// WindowsDesktop runtime, no rollForward) leaving zero log lines. Managed startup deaths must at least be visible
    /// when launched from a console, so attach to the parent console and print the fatal error there.</summary>
    private static void ReportStartupErrorToConsole(Exception ex)
    {
        try
        {
            // WinExe double-clicked from Explorer has no console to attach to: AttachConsole fails and the write below is dropped.
            if (OperatingSystem.IsWindows())
            {
                try { TabForge.Avalonia.Platform.WindowsConsole.AttachParent(); }
                catch (Exception) { } // swallow: a double-clicked app has no parent console to attach to
            }
            Console.Error.WriteLine("TabForge failed to start: " + ex.GetType().FullName + ": " + ex.Message);
            Console.Error.WriteLine("Details: %APPDATA%\\TabForge\\avalonia-crash.log and %LOCALAPPDATA%\\TabForge\\Diagnostics (firstchance.log, errors.log).");
        }
        catch (Exception) { } // swallow: the console report is best-effort only
    }


    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect();

    /// <summary>A song file named on a start: the path to open, or the one to report as missing; both null when the start names no song.</summary>
    public readonly record struct StartupFile(string? SongPath, string? MissingPath);

    /// <summary>
    /// The song file a start names: the first argument that is not an option and exists, else the arguments joined (a path
    /// the shell did not quote). Arguments beginning with "--" belong to the helper modes routed above and are ignored, as is the value of such an
    /// option ("--profile &lt;folder&gt;"); a named path that does not exist is returned as <see cref="StartupFile.MissingPath"/> so it can be reported
    /// instead of opened.
    /// </summary>
    public static StartupFile StartupFileFrom(string[] args)
    {
        var candidates = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--", StringComparison.Ordinal)) continue;
            if (i > 0 && args[i - 1].StartsWith("--", StringComparison.Ordinal)) continue;   // the value of an option, not a file to open
            candidates.Add(args[i]);
        }
        if (candidates.Count == 0) return default;
        foreach (var path in candidates)
            if (File.Exists(path)) return new StartupFile(path, null);
        var joined = string.Join(" ", candidates);
        return File.Exists(joined) ? new StartupFile(joined, null) : new StartupFile(null, joined);
    }

    private static int RunAudioEngine(string[] args)
    {
        // No session: refuse before touching EngineHost statics (its type initializer builds a session).
        if (args.Length < 3) return AudioEngineRefusedExitCode;
        return TabForge.AudioEngine.EngineHost.Run(args);
    }

    private static int RunPluginHost(string[] args)
    {
        // Missing plug-in-host arguments: refuse before touching PluginHostMain.
        if (args.Length < 8) return AudioEngineRefusedExitCode;
        return TabForge.AudioEngine.Isolation.PluginHostMain.Run(args);
    }

    private static int RunPluginInfo(string[] args)
    {
        // Missing probe path: refuse before touching PluginInfoProbe.
        if (args.Length < 2) return AudioEngineRefusedExitCode;
        return TabForge.AudioEngine.Isolation.PluginInfoProbe.Run(args);
    }
}
