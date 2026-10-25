using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.Threading;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Transport;
using TabForge.Capture;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Presenters;
using TabForge.Presets;
using TabForge.Services;

namespace TabForge.Avalonia.Capture;

// Owns: one capture run: the main window in its fixed state (size, theme, the Ashen Meridian demo song, a playback engine that holds a chosen moment),
//   the step loop with its per-step log, and the report.
// Does not own: what each verb does (ViewSteps, ToolSteps, ShotSteps, FrameSteps), the files they write (CaptureImage), the command line (CaptureLaunch).
// Tests: TabForge.Avalonia.Tests.CaptureSessionTests.
// Threads: UI thread only.
internal sealed class CaptureSession
{
    /// <summary>How long a step waits for layout and render to settle unless the script says otherwise (ms).</summary>
    public const int SettleMs = 250;

    /// <summary>The wait after the window first shows: icon sets and the track list build after the first frame (ms).</summary>
    public const int StartSettleMs = 3000;

    private readonly Dictionary<string, Func<CaptureStep, Task>> _verbs = new(StringComparer.Ordinal);
    private readonly List<string> _log = new();

    public CaptureSession(string outputFolder)
    {
        OutputFolder = outputFolder;
        Engine = new CaptureMomentEngine();
        Sessions = new AvaloniaPlaybackSessions(new AvaloniaUiThread(Dispatcher.UIThread), () => Engine);
        Document = new SongDocument(() => null) { Project = FullDemoSongFactory.Create() };
        Document.MarkClean();
        Window = new MainWindow(null, null, Document, Sessions)
        {
            Width = CaptureScript.WindowDipsAcross,
            Height = CaptureScript.WindowDipsDown,
        };
        CaptureMetrics.PlaceOffScreen(Window);
        new ViewSteps(this).Register(_verbs);
        new ToolSteps(this).Register(_verbs);
        new ShotSteps(this).Register(_verbs);
        new FrameSteps(this).Register(_verbs);
        new FixtureSteps(this).Register(_verbs);
        new PointerSteps(this).Register(_verbs);
        new FxWindowSteps(this).Register(_verbs);
    }

    public string OutputFolder { get; }
    public MainWindow Window { get; }
    public SongDocument Document { get; }
    public CaptureMomentEngine Engine { get; }
    public AvaloniaPlaybackSessions Sessions { get; }

    /// <summary>The theme in force (dark or light), recorded in the report of each shot.</summary>
    public string Theme { get; set; } = CaptureScript.DefaultTheme;

    /// <summary>The shots written so far, for the report.</summary>
    public List<CaptureShot> Shots { get; } = new();

    /// <summary>Tool windows and dialogs this run opened, by the name a script gives them.</summary>
    public Dictionary<string, Window> Tools { get; } = new(StringComparer.Ordinal);

    /// <summary>The content of the last menu a step opened (the target "menu").</summary>
    public Control? OpenMenu { get; set; }

    public T Find<T>() where T : Control =>
        Window.GetLogicalDescendants().OfType<T>().FirstOrDefault() ?? throw new InvalidOperationException($"The main window has no {typeof(T).Name}.");

    public IEnumerable<Window> AllWindows() =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows ?? Enumerable.Empty<Window>();

    /// <summary>Privacy: the status bar shows a generic audio device text instead of this machine's device, driver and rate.</summary>
    public void ScrubStatusBar() => Window.GetLogicalDescendants().OfType<StatusBarView>().FirstOrDefault()?.ShowAudioDevice(new AudioDeviceStatus(true, "Audio device", null));

    public void Log(string line) => _log.Add(line);

    /// <summary>Shows the main window and lets it settle; every run starts here.</summary>
    public async Task StartAsync()
    {
        Window.Opened += (_, _) => IconArtPreloader.PreloadAfterFirstFrame(Window);   // as the app does after its first frame
        Window.Show();
        await SettleAsync(StartSettleMs);
        // The track list and the pane heights lay out fully only after a size change, as when the user resizes the window.
        Window.Width += 1;
        await SettleAsync();
        Window.Width -= 1;
        await SettleAsync();
    }

    /// <summary>Runs the idle jobs and waits <paramref name="ms"/>, twice, so layout, the background build slices and the render have finished.</summary>
    public async Task SettleAsync(int ms = SettleMs)
    {
        for (var pass = 0; pass < 2; pass++)
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            await Task.Delay(ms / 2);
        }
    }

    /// <summary>Runs every step in order; a failed step is logged and the run goes on. Returns the number of failed steps.</summary>
    public async Task<int> RunAsync(CaptureScript script, string scriptName)
    {
        var failed = 0;
        var index = 0;
        foreach (var step in script.Steps)
        {
            index++;
            try
            {
                await RunStepAsync(step);
                _log.Add($"{index} ok {step}");
            }
            catch (Exception ex)
            {
                failed++;
                _log.Add($"{index} FAILED {step}: {ex.GetType().Name}: {ex.Message}");
            }
        }
        Directory.CreateDirectory(OutputFolder);
        File.WriteAllLines(Path.Combine(OutputFolder, scriptName + ".log"), _log);
        File.WriteAllText(Path.Combine(OutputFolder, scriptName + ".report.json"),
            JsonSerializer.Serialize(new { script = scriptName, failedSteps = failed, shots = Shots }, new JsonSerializerOptions { WriteIndented = true }));
        return failed;
    }

    /// <summary>Runs one step; throws when the verb is unknown or the step fails.</summary>
    public Task RunStepAsync(CaptureStep step) =>
        _verbs.TryGetValue(step.Verb, out var handler) ? handler(step) : throw new InvalidOperationException($"Unknown verb \"{step.Verb}\".");

    /// <summary>Draws the playing views at the <paramref name="performed"/>-th performed bar (0-based, repeats counted) and <paramref name="fraction"/> of it; starts the held playback on first use.</summary>
    public void HoldMoment(int performed, double fraction)
    {
        var playback = Sessions.For(Document) ?? throw new InvalidOperationException("The capture session has no playback.");
        if (!playback.IsPlaying) playback.PlayOrPause();
        Engine.MoveTo(performed, fraction);
        playback.PumpFrame();
    }

    /// <summary>Applies the theme to the application and the main window so every tool window follows it.</summary>
    public void ApplyTheme(bool light)
    {
        // The app's own path (palette, variant, application variant), so a capture matches the theme the app shows; the scale root is a throwaway so the window keeps its size.
        var appearance = JsonSerializer.Deserialize<AppearanceSettings>(JsonSerializer.Serialize(WindowUiScale.CurrentAppearance))!;
        appearance.ThemeMode = light ? "Light" : "Dark";
        ThemeSettingsBinding.ApplyWindow(Window, new LayoutTransformControl(), appearance);
        Theme = light ? "light" : "dark";
    }
}

/// <summary>One shot written to disk, as the report lists it.</summary>
internal sealed record CaptureShot(string Id, string File, int Width, int Height, string Theme, double Ink);
