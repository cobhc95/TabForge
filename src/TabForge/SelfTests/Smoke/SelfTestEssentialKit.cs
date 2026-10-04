using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presets;
using TabForge.Services;
using TabForge.Shell;
using TabForge.Views;

namespace TabForge;

/// <summary>
/// Shared helpers of the essential-action smoke checks (SelfTestEssential*.cs): a hidden main window, real waits on conditions, calls into the
/// window's own handlers, a silent engine output, scratch files and a guard that turns an unhandled UI exception into a failure.
/// </summary>
public static partial class SelfTest
{
    private sealed class SmOutput : IMidiOutput
    {
        public IReadOnlyList<MidiOutputDeviceInfo> Devices { get; } = Array.Empty<MidiOutputDeviceInfo>();
        public int Sent;
        public void Send(int deviceId, int status, int data1, int data2) => Interlocked.Increment(ref Sent);
        public void ResetAll() { }
        public void Close() { }
        public void Dispose() { }
    }

    private static string SmScratch()
    {
        var folder = Path.Combine(Path.GetTempPath(), "tf-smoke-" + Guid.NewGuid().ToString("N")[..10]);
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static void SmClean(string folder) { try { Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    private static object? SmCall(object target, string method, params object?[] args)
    {
        var m = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .First(x => x.Name == method && x.GetParameters().Length == args.Length);
        try { return m.Invoke(target, args); }
        catch (TargetInvocationException ex) when (ex.InnerException is not null) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }

    private static T? SmField<T>(object target, string name) =>
        (T?)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(target);

    /// <summary>Runs a window's click handler by name (the menu or button that calls it passes the same arguments).</summary>
    private static void SmClick(MainWindow window, string handler) { SmCall(window, handler, window, new RoutedEventArgs()); SmSettle(); }

    private static void SmSettle()
    {
        for (var i = 0; i < 3; i++) Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
    }

    /// <summary>Pumps the UI until the condition holds (true) or the time runs out (false).</summary>
    private static bool SmUntil(Func<bool> done, int ms = 15000)
    {
        var end = Stopwatch.GetTimestamp() + ms * Stopwatch.Frequency / 1000;
        while (!done() && Stopwatch.GetTimestamp() < end)
        {
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
            Thread.Sleep(2);
        }
        return done();
    }

    /// <summary>Waits for a task on the UI thread without blocking the dispatcher.</summary>
    private static bool SmAwait(Task task, int ms = 15000)
    {
        var finished = SmUntil(() => task.IsCompleted, ms);
        if (finished && task.IsFaulted) throw task.Exception!.GetBaseException();
        return finished;
    }

    /// <summary>A real main window, shown off screen and not activated; true when it was laid out.</summary>
    private static MainWindow SmNewWindow()
    {
        var window = new MainWindow(TabForge.Audio.AudioEngineClient.Instance, new AppOptions()) { Width = 1280, Height = 800 };
        window.WindowState = WindowState.Normal; window.ShowActivated = false; window.ShowInTaskbar = false; window.Topmost = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual; window.Left = -20000; window.Top = -20000;
        window.Show();
        for (var i = 0; i < 50 && (PresentationSource.FromVisual(window) is null || !window.IsLoaded); i++) PumpUi();
        window.UpdateLayout();
        SmSettle();
        return window;
    }

    /// <summary>Closes a test window without a prompt: its songs are marked clean first, an asked question is answered "discard".</summary>
    private static void SmCloseWindow(MainWindow window)
    {
        var previous = DialogHost.Capture;
        DialogHost.Capture = d => { if (d is ThemedConfirmDialog t) t.AnswerForTest(MessageBoxResult.No); return true; };
        try
        {
            foreach (var doc in window.OpenDocuments.ToList()) doc.MarkClean();
            window.Close();
            SmSettle();
            foreach (var doc in window.OpenDocuments.ToList()) { doc.DisposePlayback(); TabForge.Audio.AudioEngineClient.Instance.ReleaseOwner(doc); }
        }
        finally { DialogHost.Capture = previous; }
    }

    /// <summary>A song as a new tab with a silent engine (the way a tab dropped on the strip opens).</summary>
    private static DocumentSession SmOpenSong(MainWindow window, SongProject song, SmOutput? output = null)
    {
        var session = new DocumentSession(new PlaybackEngine(output ?? new SmOutput())) { Project = song };
        session.MarkClean();
        SmCall(window, "AdoptDroppedDocument", session, window.OpenDocuments.Count);
        SmSettle();
        return session;
    }

    private static SongProject SmDemoSong(int tempo = 240)
    {
        var song = DemoSongFactory.Create();
        song.Tempo = tempo;
        song.IsDirty = false;
        return song;
    }

    private static DocumentSession SmActive(MainWindow window) => (DocumentSession)typeof(MainWindow).GetProperty("Doc", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;

    /// <summary>Runs a step; any exception, or an unhandled UI exception raised meanwhile, is one failed check.</summary>
    private static void SmStep(string name, Action step)
    {
        Exception? raised = null;
        void OnUnhandled(object? s, DispatcherUnhandledExceptionEventArgs e) { raised ??= e.Exception; e.Handled = true; }
        var dispatcher = Dispatcher.CurrentDispatcher;
        dispatcher.UnhandledException += OnUnhandled;
        try { step(); SmSettle(); }
        catch (Exception ex) { raised ??= ex; }
        finally { dispatcher.UnhandledException -= OnUnhandled; }
        Check(name, raised is null, raised is null ? null : $"{raised.GetType().Name}: {raised.Message}");
    }

    /// <summary>Answers every dialog the code under test shows: the answer per dialog, null to capture and cancel it.</summary>
    private static T SmWithDialogs<T>(Func<Window, bool?> answer, Func<T> run)
    {
        var previous = DialogHost.Capture;
        DialogHost.Capture = answer;
        try { return run(); }
        finally { DialogHost.Capture = previous; }
    }

    private static Func<Window, bool?> SmAnswerConfirm(MessageBoxResult result, List<string>? seen = null) => d =>
    {
        seen?.Add(d.Title ?? d.GetType().Name);
        if (d is ThemedConfirmDialog t) { t.AnswerForTest(result); return true; }
        return false;
    };

    private static string SmSample(string name)
    {
        var root = TabForge.Diagnostics.FeatureMapGenerator.FindRoot();
        var beside = Path.Combine(AppContext.BaseDirectory, "Samples", name);
        if (File.Exists(beside)) return beside;
        return root is null ? "" : Path.Combine(root, "samples", name);
    }
}
