using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TabForge.Diagnostics;
using TabForge.Services;

namespace TabForge;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        ClipboardService.Compose(new Views.WindowsScoreClipboard());   // the one score clipboard of this process; every window and diagnostic run shares it
        // --profile <folder> (already applied in Program.Main) is removed; the rest are the app's own arguments.
        var args = UserPaths.ApplyProfileArgument(e.Args);
        // --approve-night-plugins (Audit 5 H-5) only works together with --profile; alone it is refused before anything is loaded or written.
        var approveNightPlugins = TabForge.Plugins.NightPluginApproval.Requested(args);
        if (approveNightPlugins && (!UserPaths.IsProfile || UserPaths.ProfileIsRealUserFolder))
        {
            Debug.WriteLine($"{TabForge.Plugins.NightPluginApproval.Switch} needs --profile <folder>; refused, nothing was approved.");
            Shutdown(2);
            return;
        }
        // --capture likewise: refused before settings are loaded or a window exists, so a run without a scratch profile (or without
        // its script and folder) can neither show a window nor save the window state and settings into the real user folder.
        var captureSwitch = Array.FindIndex(args, a => a.Equals("--capture", StringComparison.OrdinalIgnoreCase));
        if (captureSwitch >= 0 && (!UserPaths.IsProfile || UserPaths.ProfileIsRealUserFolder || captureSwitch + 2 >= args.Length))
        {
            Debug.WriteLine("--capture needs --profile <scratch folder> and <script.json> <outDir>; refused, nothing was loaded.");
            Shutdown(2);
            return;
        }
        var approveAll = TabForge.Plugins.NightPluginApproval.AllRequested(args);
        args = TabForge.Plugins.NightPluginApproval.Without(args);
        // --software-render (test runs): WPF draws in software. With the monitor switched off the GPU has no display and hardware
        // rendering leaves the main window black; unattended runs still need real pixels for screenshots and recordings.
        if (args.Any(a => string.Equals(a, "--software-render", StringComparison.OrdinalIgnoreCase)))
        {
            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
            args = args.Where(a => !string.Equals(a, "--software-render", StringComparison.OrdinalIgnoreCase)).ToArray();
        }
        // Headless modes (--selftest, --playtest, --dump, ...) never open a window.
        if (DiagnosticCommands.TryRun(args, out var exitCode))
        {
            _diagnosticExitCode = exitCode;   // a test window closing earlier may already have started a Shutdown() with code 0; OnExit restores this one
            Shutdown(exitCode);
            return;
        }

        // R-09: the app's one settings object, loaded once; every window reads and writes through it.
        var settings = AppSettingsStore.InitializeShared();
        if (approveNightPlugins
            && TabForge.Plugins.NightPluginApproval.Apply(approveAll ? new[] { TabForge.Plugins.NightPluginApproval.Switch, "all" } : new[] { TabForge.Plugins.NightPluginApproval.Switch }, settings.Settings.Plugins, out var nightApproved) == TabForge.Plugins.NightPluginApproval.Outcome.Approved)
        {
            settings.MarkChanged();   // saved into the profile's settings.json
            settings.Flush();
            Debug.WriteLine($"Night plug-ins approved in the profile: {nightApproved.Count}");
        }

        // A song double-clicked in Explorer while TabForge runs: hand it to that window as a new tab (the
        // default; Settings > General can open a separate window instead) and exit without a second window.
        if (StartupSongPath(args) is { } song && OpensInNewTab(settings) && !UserPaths.IsProfile && SingleInstanceService.TrySendToRunningInstance(song))
        {
            Shutdown(0);
            return;
        }

        InstallCrashHandlers();
        // Toolbar/palette buttons are enabled and disabled as the cursor moves. A tooltip whose owner is
        // disabled while it is open stopped receiving mouse-leave and stayed stuck on screen; letting WPF
        // manage tooltips on disabled controls too keeps their open/close lifecycle intact everywhere.
        ToolTipService.ShowOnDisabledProperty.OverrideMetadata(typeof(System.Windows.Controls.Control),
            new FrameworkPropertyMetadata(true));
        base.OnStartup(e);
        TabForge.Views.SlowTrace.HookInput();
        Views.AccessibleNames.Install();
        Shell.WindowPolish.Register();
        ThemeService.PrepareMutableBrushes();
        // --capture <script.json> <outDir> (documentation screenshots): the window is created off-screen and never activated.
        var captureAt = Array.FindIndex(args, a => a.Equals("--capture", StringComparison.OrdinalIgnoreCase));
        string? captureScript = null, captureOut = null;
        if (captureAt >= 0 && captureAt + 2 < args.Length)
        {
            captureScript = args[captureAt + 1];
            captureOut = args[captureAt + 2];
            args = args.Take(captureAt + 1).Concat(args.Skip(captureAt + 3)).ToArray();   // only the bare switch stays, so no value is taken for a song to open
        }
        var window = new MainWindow(Audio.AudioEngineClient.Instance, new Shell.AppOptions());
        MainWindow = window;
        if (captureScript is null) ApplyRequestedWindowSize(window, args); else new WindowProbes(window).PrepareOffscreenCapture();
        window.Show();
        if (captureScript is not null) new WindowProbes(window).RunCaptureScript(captureScript, captureOut!);
        OpenStartupFile(window, args);
        if (!args.Any(a => a.StartsWith("--", StringComparison.Ordinal)))
            window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, window.OfferAutosaveRecovery);   // songs a crash left behind
        if (!args.Any(a => a.StartsWith("--", StringComparison.Ordinal)) && !UserPaths.IsProfile)
            SingleInstanceService.StartServer(path => window.Dispatcher.BeginInvoke(() => window.OpenFromAnotherLaunch(path)), _serverStop.Token);
        // Window options that take one value run in registration order; each uses the first occurrence of its option.
        foreach (var (option, run) in WindowCommands)
        {
            var at = Array.FindIndex(args, a => a.Equals(option, StringComparison.OrdinalIgnoreCase));
            if (at >= 0 && at + 1 < args.Length) run(window, args[at + 1]);
        }
    }

    /// <summary>
    /// `--option &lt;value&gt;` → handler on the opened main window (A-05). Enumerated in insertion order (nothing is ever
    /// removed), so `--theme` is applied before any probe or the screenshot tour starts.
    /// </summary>
    private static readonly Dictionary<string, Action<MainWindow, string>> WindowCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        ["--theme"] = (w, v) => w.ApplyThemeOverride(v),
        // `--screenshots <folder>`: photograph menus, panels, settings and dialogs for the README, then exit.
        ["--screenshots"] = (w, v) => new WindowProbes(w).RunScreenshotTour(v),
        // `--perf-follow <report>`: measure CPU/memory of each score follow style during playback, then exit.
        ["--perf-follow"] = (w, v) => new WindowProbes(w).RunFollowPerfProbe(v),
        ["--probe-settings"] = (w, v) => new WindowProbes(w).RunSettingsProbe(v),
        ["--probe-playback-visuals"] = (w, v) => new WindowProbes(w).RunPlaybackVisualsProbe(v),
        ["--probe-record"] = (w, v) => new WindowProbes(w).RunRecordingProbe(v),
        ["--probe-menus"] = (w, v) => new WindowProbes(w).RunMenuProbe(v),
        ["--probe-update"] = (w, v) => new WindowProbes(w).RunUpdateProbe(v),
        ["--probe-instrument-menu"] = (w, v) => new WindowProbes(w).RunInstrumentMenuProbe(v),
        ["--probe-countin"] = (w, v) => new WindowProbes(w).RunCountInProbe(v),
    };

    private readonly CancellationTokenSource _serverStop = new();

    private int? _diagnosticExitCode;

    protected override void OnExit(ExitEventArgs e)
    {
        if (_diagnosticExitCode is int diagnosticExit) e.ApplicationExitCode = diagnosticExit;   // --selftest and the other diagnostics report their result as the exit code
        AutosaveRegistry.WaitForIdle(TimeSpan.FromSeconds(3));   // a write in flight finishes (or is abandoned) before the copies go
        AutosaveService.DeleteOwn(AutosaveService.DefaultFolder, Environment.ProcessId);   // a normal exit leaves nothing to recover
        AppSettingsStore.FlushShared();   // a debounced settings save still pending is written now
        TabForge.Audio.AudioEngineClient.Instance.Stop(); // no-op when the engine never started; ends the R-10 warm period too
        _serverStop.Cancel();
        base.OnExit(e);
    }

    /// <summary>The song path of a plain "open this file" launch (no test / probe options), else null.</summary>
    private static string? StartupSongPath(string[] args)
    {
        if (args.Length == 0 || args.Any(a => a.StartsWith("--", StringComparison.Ordinal))) return null;
        var direct = args.FirstOrDefault(SingleInstanceService.IsOpenableSong);
        if (direct is not null) return direct;
        var joined = string.Join(" ", args);
        return SingleInstanceService.IsOpenableSong(joined) ? joined : null;
    }

    private static bool OpensInNewTab(AppSettingsStore settings) =>
        !string.Equals(settings.Settings.General.OpenFromExplorer, "A new window", StringComparison.OrdinalIgnoreCase);   // defaults after a failed load: new tab

    /// <summary>`--size WxH`: a deterministic restored-size window in DIPs (test harnesses and screenshots).</summary>
    private static void ApplyRequestedWindowSize(Window window, string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--size", StringComparison.OrdinalIgnoreCase)) continue;
            var value = arg.Contains('=') ? arg[(arg.IndexOf('=') + 1)..] : i + 1 < args.Length ? args[i + 1] : "";
            var parts = value.Split('x', 'X');
            if (parts.Length == 2 && double.TryParse(parts[0], out var w) && double.TryParse(parts[1], out var h))
            {
                window.WindowState = WindowState.Normal;
                window.Width = Math.Max(window.MinWidth, w);
                window.Height = Math.Max(window.MinHeight, h);
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Left = 20;
                window.Top = 20;
            }
            return;
        }
    }

    /// <summary>
    /// Opens a file passed on the command line in the first tab. The arguments are also tried joined,
    /// so a path with spaces still opens when the shell did not quote it.
    /// </summary>
    private static void OpenStartupFile(MainWindow window, string[] args)
    {
        // Values of options ("--screenshots <folder>", "--size WxH") are not files to open.
        var fileArgs = args.Where((a, i) => !a.StartsWith("--", StringComparison.Ordinal) &&
            (i == 0 || !args[i - 1].StartsWith("--", StringComparison.Ordinal))).ToArray();
        if (fileArgs.Length == 0) return;
        // Probes and tours (any "--" option) expect the song open when they start: they keep the synchronous open.
        var background = !args.Any(a => a.StartsWith("--", StringComparison.Ordinal));
        var direct = fileArgs.FirstOrDefault(File.Exists);
        if (direct is not null) { window.OpenStartupFile(direct, background); return; }
        var joined = string.Join(" ", fileArgs);
        if (File.Exists(joined)) window.OpenStartupFile(joined, background);
        else window.ReportStartupFileMissing(joined);
    }

    // ---- last-resort error handling -------------------------------------------------------------

    private bool _reportingCrash;

    /// <summary>
    /// An unexpected error must never silently take unsaved work with it: it is logged, every song with
    /// unsaved changes is written to the Recovery folder, and the user is told where.
    /// </summary>
    private void InstallCrashHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            // Background-thread failure: the process is going down, so only log and rescue.
            LogCrash(args.ExceptionObject as Exception);
            try
            {
                var op = Dispatcher.BeginInvoke(new Action(() => SaveRecoveryCopies()));
                op.Wait(TimeSpan.FromSeconds(3));   // the UI thread may be the one that is stuck
            }
            catch (Exception ex) { Debug.WriteLine($"Recovery after a fatal error failed: {ex}"); }
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogCrash(args.Exception);
            args.SetObserved();
        };
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs args)
    {
        args.Handled = true; // keep the window alive so the user can still save
        if (_reportingCrash) return;
        _reportingCrash = true;
        try
        {
            var log = LogCrash(args.Exception);
            var recovered = SaveRecoveryCopies();
            // The model may be inconsistent now: the window says so, and saving over an existing file asks first.
            foreach (var window in Current.Windows.OfType<MainWindow>())
            {
                try { window.MarkDegraded(); }
                catch (Exception ex) { Debug.WriteLine($"Could not mark a window degraded: {ex}"); }
            }
            var message = "TabForge hit an unexpected error and may not work correctly until it is restarted.\n\n" +
                          $"{args.Exception.GetBaseException().Message}\n\n" +
                          (recovered.Count > 0 ? $"Unsaved songs were copied to:\n{RecoveryFolder}\n\n" : "") +
                          "Saving over an existing file will ask first; saving as a new file is recommended.\n\n" +
                          (log is null ? "" : $"Details: {log}");
            MessageBox.Show(message, "TabForge error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { _reportingCrash = false; }
    }

    private static string RecoveryFolder => UserPaths.Recovery;

    /// <summary>Writes every open song with unsaved changes as a .tforge; returns the files written.</summary>
    private static List<string> SaveRecoveryCopies()
    {
        var written = new List<string>();
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        foreach (var window in Current.Windows.OfType<MainWindow>())
        foreach (var document in window.OpenDocuments)
        {
            try
            {
                if (!document.HasUnsavedChanges) continue;
                Directory.CreateDirectory(RecoveryFolder);
                var path = AutosaveService.EmergencyFileFor(RecoveryFolder, Path.GetFileNameWithoutExtension(document.DisplayName), stamp);
                WriteRecoveryCopy(document.Project, path);
                written.Add(path);
            }
            // One damaged song must not stop the others being rescued.
            catch (Exception ex) { Debug.WriteLine($"Recovery copy failed for {document.DisplayName}: {ex}"); }
        }
        return written;
    }

    /// <summary>
    /// Writes the project's raw .tforge JSON to <paramref name="path"/> WITHOUT validation: an invalid model is exactly what recovery
    /// exists for. Validation still runs, and its error is written beside the copy as "&lt;name&gt;.errors.txt" (null: the model is valid).
    /// The project's unsaved-changes flag is left as it was (a recovery copy must not make closing skip the save prompt).
    /// </summary>
    internal static string? WriteRecoveryCopy(Models.SongProject project, string path)
    {
        ArgumentNullException.ThrowIfNull(project);
        var wasDirty = project.IsDirty;
        string? errors = null;
        try
        {
            var toWrite = project;
            try { if (project.Tracks?.Any(t => t?.StartupTemplateId is not null) == true) toWrite = project.WithoutStartupTracks(); }
            catch (Exception ex) { errors = $"Startup tracks could not be left out ({ex.GetType().Name}): {ex.Message}"; }
            try { ProjectValidator.Validate(toWrite); }
            catch (Exception ex) { errors = (errors is null ? "" : errors + Environment.NewLine) + $"{ex.GetType().Name}: {ex.Message}"; }
            project.IsDirty = false;   // the file on disk records a saved project, as a normal save would
            // PersistBytes is the disk JSON (every property, FormatVersion included), gzip-wrapped: the same format a .tforge save
            // writes since M-03, so it is written as is (ProjectService.Load detects the gzip header).
            var persisted = ProjectService.PersistBytes(toWrite, InputLimits.MaxRecoveryProjectBytes);   // the recovery reader accepts up to the same bound
            FilePathPolicy.WriteAtomically(path, stream => stream.Write(persisted), createDirectory: true);
        }
        finally { project.IsDirty = wasDirty; }
        var sidecar = Path.ChangeExtension(path, ".errors.txt");
        if (errors is not null)
        {
            var text = System.Text.Encoding.UTF8.GetBytes(
                $"This recovery copy was written without validation. It failed validation with:{Environment.NewLine}{errors}{Environment.NewLine}");
            try { FilePathPolicy.WriteAtomically(sidecar, s => s.Write(text)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Debug.WriteLine($"Recovery error list not written: {ex}"); }
        }
        return errors;
    }

    private static string? LogCrash(Exception? exception)
    {
        try
        {
            var path = FilePathPolicy.DefaultDiagnosticsPath($"crash-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            DiagnosticFileService.WriteText(path, $"{DateTime.Now:O}{Environment.NewLine}{exception}");
            return path;
        }
        catch (Exception logError)
        {
            Debug.WriteLine($"Crash log could not be written: {logError}");
            return null;
        }
    }
}
