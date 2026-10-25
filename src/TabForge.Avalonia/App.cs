using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using TabForge.Avalonia.Platform;

namespace TabForge.Avalonia;

/// <summary>
/// The Avalonia preview application: Fluent theme, the Core palette and look styles, and the main window.
/// Owns: application start-up. Does not own: any TabForge feature (those stay in Core). Tests: tests/TabForge.Avalonia.Tests.
/// </summary>
public sealed class App : Application
{
    /// <summary>The app's one audio engine client (set once the playback host exists); Settings asks it what an ASIO driver offers.</summary>
    internal TabForge.Audio.AudioEngineClient? Engine { get; private set; }

    /// <summary>The audio device lists Settings shows, listed on a worker and remembered.</summary>
    internal TabForge.Avalonia.Views.AudioDeviceNames DeviceNames { get; } = new(new TabForge.Platform.Windows.WindowsAudioDevices());

    public override void Initialize()
    {
        WindowUiScale.RegisterWindowHook();
        Styles.Add(new FluentTheme());
        AvaloniaPalette.Register(Resources);
        // The WPF app's default text size (12) and compact list rows, in place of the Fluent 14 and touch-sized item padding.
        Resources["ControlContentThemeFontSize"] = new TabForge.Services.AppearanceSettings().FontSize;
        // Fluent's text boxes (the editable combo box text too) are 32 DIP high; the WPF ones are as high as their text and padding.
        Resources["TextControlThemeMinHeight"] = 24.0;
        Resources["TextControlThemePadding"] = new Thickness(5, 3, 5, 3);
        // The WPF app rounds every control surface by 3 DIP; Fluent rounds by 4.
        UiDensityResources.Publish(Resources, new TabForge.Services.AppearanceSettings());
        Resources["ControlCornerRadius"] = AvaloniaFormControlStyles.ControlCornerRadius;
        AvaloniaDefaultLooks.Register();
        Styles.Add(new Style(x => x.OfType<Window>()) { Setters = { new Setter(TemplatedControl.FontSizeProperty, new TabForge.Services.AppearanceSettings().FontSize), new Setter(TemplatedControl.FontFamilyProperty, WindowFontFamily) } });
        // The base surfaces come before the looks, so a look's state rules still win over them.
        Styles.Add(AvaloniaFormControlStyles.Build());
        Styles.Add(AvaloniaStateStyles.Build());
        Styles.Add(AvaloniaCheckSliderStyles.Build());
        Styles.Add(AvaloniaScrollBarStyles.Build());
        Styles.Add(AvaloniaArrangementSlider.Build());
        Styles.Add(AvaloniaMenuTemplates.Build());
        Styles.Add(AvaloniaLooks.Build().Styles);
        // Fluent paints a text box's watermark at half opacity, which drops the placeholder token (the muted ink) under AA contrast; the token itself is the dimming.
        TemplatedControl.TemplateAppliedEvent.AddClassHandler<TextBox>((_, e) =>
        {
            if (e.NameScope.Find<TextBlock>("PART_Watermark") is { } watermark) watermark.Opacity = 1;
        });
        // Sub-pixel (ClearType-style) text anti-aliasing for every control, as WPF draws text on Windows; greyscale looks heavier and blurred.
        // The menu bar and its popups (a logical descendant of a MenuItem) are greyscale, as WPF draws menu text: sub-pixel fringes look sharper and coloured there.
        // Set on each control when it loads, then invalidated: text drawn before the mode was set is not redrawn by the setter itself.
        // The mode has no public styleable property, so a style setter cannot inherit it.
        Control.LoadedEvent.AddClassHandler<Control>((control, _) =>
        {
            var inMenu = control.FindLogicalAncestorOfType<MenuItem>(includeSelf: true) is not null;
            RenderOptions.SetTextRenderingMode(control, inMenu ? TextRenderingMode.Antialias : TextRenderingMode.SubpixelAntialias);
            control.InvalidateVisual();
        });
    }

    // App.xaml sets the Window font to Segoe UI.
    private static readonly FontFamily WindowFontFamily = new(new TabForge.Services.AppearanceSettings().FontFamily);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime captureDesktop && TabForge.Capture.CaptureArguments.TryParse(captureDesktop.Args ?? Array.Empty<string>()) is { } capture)
        {
            Capture.CaptureLaunch.Start(captureDesktop, capture);   // a diagnostic run: the script makes its own window
        }
        else if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var crashLog = AvaloniaCrashLog.ForUserFolder();
            crashLog.Install(TabForge.Avalonia.Platform.AvaloniaUiThread.ForApplication());
#if WINDOWS
            var playback = new TabForge.Avalonia.Playback.AvaloniaPlaybackHost(TabForge.Avalonia.Platform.AvaloniaUiThread.ForApplication());
            Engine = playback.Engine;
            desktop.Exit += (_, _) => playback.Dispose();
            desktop.MainWindow = new MainWindow(MainWindowSettings.OpenDefault(), new Shell.TabWindowRegistry(new Shell.AppServices(playback.Trust)), playback: playback.Sessions);   // the app's one trust state, made with the engine host
#else
            desktop.MainWindow = new MainWindow(MainWindowSettings.OpenDefault());
#endif
            // The first window the app starts with; a test's own window is never offered recovery or a start-up file.
            if (desktop.MainWindow is MainWindow first)
            {
                // The song named on the command line (a double-clicked .gp file) is opened once the window is on screen, in
                // place of the empty start-up song, as the WPF app's OpenStartupFile; a path that does not exist is reported.
                // Subscribed before the recovery offer: the requested song is the one the window starts with.
                var startup = Program.StartupFileFrom(desktop.Args ?? Array.Empty<string>());
                first.Opened += (_, _) => OpenStartupFile(first, startup);
                // The start-up recovery offer runs once the first window is on screen; it is made only by the app, never by a window a test opens.
                first.Opened += (_, _) => _ = first.Autosave.OfferRecoveryAsync();
                // The icon sets load after the first frame (one idle post), so startup paints first; paints before that draw nothing.
                first.Opened += (_, _) => IconArtPreloader.PreloadAfterFirstFrame(first);
                // A Low-integrity start (built in, or launched from, a sandbox-labelled folder) cannot write any user folder: say so once, up front.
                first.Opened += (_, _) => WarnIfRestricted(first);
                // After an unexpected error the model may be damaged: saving over an existing file asks first, in every window.
                crashLog.UnhandledRecorded += () => first.Windows.Degraded.MarkDegraded();
                StartHandOverServer(desktop, first);
            }
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Shows <see cref="ProcessIntegrity.Notice"/> when Windows started this process below Medium integrity.</summary>
    private static void WarnIfRestricted(MainWindow window)
    {
        if (ProcessIntegrity.CurrentRid() is not { } rid || !ProcessIntegrity.IsBelowMedium(rid)) return;
        Services.Trace.Error(Services.Trace.Ui, $"startup: integrity level 0x{rid:X4} is below Medium; writes to user folders will be denied");
        _ = AvaloniaMessageBox.Show(new AvaloniaDialogHost(), window, ProcessIntegrity.Notice, ProcessIntegrity.NoticeCaption,
            TabForge.Presenters.MessageBoxButtons.OK, TabForge.Presenters.MessageBoxIcon.Warning, TabForge.Presenters.MessageBoxAnswer.OK);
    }

    /// <summary>Opens the song the command line named in place of the empty start-up song, or reports the path that does not exist (the WPF app's OpenStartupFile).</summary>
    private static void OpenStartupFile(MainWindow window, Program.StartupFile startup)
    {
        if (startup.SongPath is { } path)
        {
            Services.Trace.Write(Services.Trace.Ui, "startup open: " + path);
            _ = OpenStartupFileAndTrace(window, path);
            return;
        }
        if (startup.MissingPath is { } missing)
        {
            Services.Trace.Error(Services.Trace.Ui, "startup open: file not found " + missing);
            window.ReportStartupFileMissing(missing);
        }
    }

    /// <summary>Opens a start-up song and records the outcome: the former fire-and-forget open left a failure with only a status notice.</summary>
    private static async Task OpenStartupFileAndTrace(MainWindow window, string path)
    {
        try
        {
            var opened = await window.OpenStartupFile(path);
            Services.Trace.Write(Services.Trace.Ui, "startup open: " + (opened ? "opened " : "not opened ") + path);
        }
        catch (Exception ex)
        {
            Services.Trace.Error(Services.Trace.Ui, "startup open: " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    /// <summary>
    /// Receives a song from a later double-click launch on the single-instance pipe and opens it in a new tab of the chosen
    /// window, bringing that window forward. Nothing listens when songs from Explorer open in a new window (the WPF app's
    /// rule): the later launch then shows the song in its own window instead.
    /// </summary>
    private static void StartHandOverServer(IClassicDesktopStyleApplicationLifetime desktop, MainWindow first)
    {
        if (!OperatingSystem.IsWindows()) return;
        CancellationTokenSource? running = null;
        desktop.Exit += (_, _) => running?.Cancel();
        var ui = Platform.AvaloniaUiThread.ForApplication();
        void Sync(TabForge.Services.AppSettings? settings)
        {
            var wanted = !string.Equals(settings?.General.OpenFromExplorer, "A new window", StringComparison.OrdinalIgnoreCase);
            if (!wanted)
            {
                if (running is null) Services.Trace.Write(Services.Trace.Ui, "handover server: not started (Explorer songs open in a new window)");
                running?.Cancel();   // the setting changed to a new window: stop listening, so a later launch opens its own window
                running = null;
                return;
            }
            if (running is not null) return;
            var stop = running = new CancellationTokenSource();
            TabForge.Services.SingleInstanceService.StartServer(
                path => ui.Post(() => OpenHandOver(desktop, first, path)),
                stop.Token,
                () => Services.Trace.Write(Services.Trace.Ui, "handover server: listening"));
        }
        Sync(first.Settings?.Settings);
        // Applied settings start or stop the listener; the cancelled pipe is released by its loop, so a restart can bind it again.
        if (first.Settings is { } store) store.SettingsChanged += Sync;
    }

    /// <summary>Opens a path received on the hand-over pipe in the target window and records the outcome (the former fire-and-forget hid a refusal).</summary>
    private static void OpenHandOver(IClassicDesktopStyleApplicationLifetime desktop, MainWindow fallback, string path)
    {
        var target = HandOverTarget(desktop, fallback);
        Services.Trace.Write(Services.Trace.Ui, "handover receive: " + path + (target is null ? " (no window)" : ""));
        _ = OpenHandOverAndTrace(target, path);
    }

    private static async Task OpenHandOverAndTrace(MainWindow? target, string path)
    {
        if (target is null)
        {
            Services.Trace.Error(Services.Trace.Ui, "handover open: no window for " + path);
            return;
        }
        try
        {
            var opened = await target.OpenFromAnotherLaunch(path);
            Services.Trace.Write(Services.Trace.Ui, "handover open: " + (opened ? "opened " : "not opened ") + path);
        }
        catch (Exception ex)
        {
            Services.Trace.Error(Services.Trace.Ui, "handover open: " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    /// <summary>The shown main window that receives a song from a second launch: the active one, else the one shown last, else the window this app started with.</summary>
    private static MainWindow? HandOverTarget(IClassicDesktopStyleApplicationLifetime desktop, MainWindow fallback)
    {
        MainWindow? found = null;
        foreach (var window in desktop.Windows)
        {
            if (window is not MainWindow main || !main.IsVisible) continue;
            found = main;
            if (main.IsActive) return main;
        }
        return found ?? fallback;
    }
}
