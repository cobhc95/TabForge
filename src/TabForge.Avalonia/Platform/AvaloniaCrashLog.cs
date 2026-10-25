using System;
using System.IO;
using System.Threading.Tasks;
using TabForge.Services;

namespace TabForge.Avalonia.Platform;

// Owns: the Avalonia app's crash log: one text file beside settings.json that each unhandled exception is appended to (local time, app
//   version, full exception), kept under a size ceiling by dropping the oldest lines.
// Does not own: what happens after the exception (the process ends or the failed call ends, as before), the dispatcher (the caller passes
//   its UI thread adapter), the WPF build's per-crash files (App.xaml.cs).
// Tests: TabForge.Avalonia.Tests.AvaloniaCrashLogTests. Any thread: one lock per log instance.
/// <summary>Appends unhandled exceptions of the Avalonia app to its crash log.</summary>
public sealed class AvaloniaCrashLog
{
    /// <summary>The file name, in the per-user roaming folder that holds settings.json.</summary>
    public const string FileName = "avalonia-crash.log";

    /// <summary>The ceiling in characters: the newest entries are kept and the oldest lines are dropped to stay under it.</summary>
    public const int MaxCharacters = 256 * 1024;

    private readonly string _path;
    private readonly string _version;
    private readonly Func<DateTime> _now;
    private readonly object _gate = new();

    /// <param name="path">The log file; its folder is created when needed.</param>
    /// <param name="version">The app version written into each entry.</param>
    /// <param name="now">The clock for each entry's time.</param>
    public AvaloniaCrashLog(string path, string version, Func<DateTime> now)
    {
        _path = path;
        _version = version;
        _now = now;
    }

    /// <summary>The log in the user's TabForge folder, stamped with the app version and local time.</summary>
    public static AvaloniaCrashLog ForUserFolder() =>
        new(Path.Combine(UserPaths.Roaming, FileName), AppInfo.Version, () => DateTime.Now);

    /// <summary>Raised after each unhandled exception is recorded (on the thread that raised it): the app marks its windows degraded.</summary>
    public event Action? UnhandledRecorded;

    /// <summary>
    /// Records the exceptions of the process's unhandled-exception event, of the UI thread's dispatcher and of tasks nobody awaited, and marks when the
    /// process starts and when it ends in order: a log that stops without an exit marker was ended from outside (killed, power lost), not by an exception.
    /// </summary>
    public void Install(AvaloniaUiThread uiThread)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) => RecordUnhandled(args.ExceptionObject as Exception);
        uiThread.UnhandledException += RecordUnhandled;
        TaskScheduler.UnobservedTaskException += (_, args) => Record(args.Exception);   // the task's fault does not end the process: recorded, not marked degraded
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Mark("exit: the process ended in order");
        Mark($"start: process {Environment.ProcessId}");
    }

    /// <summary>Appends one line marking an event of the process's life (start, orderly exit). Never throws.</summary>
    public void Mark(string text) => Append($"[{_now():yyyy-MM-dd HH:mm:ss}] TabForge {_version} {text}{Environment.NewLine}");

    private void RecordUnhandled(Exception? exception)
    {
        Record(exception);
        try { UnhandledRecorded?.Invoke(); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Trace.Error(Trace.Ui, "crash log: a subscriber failed: " + ex.Message); }   // a handler bug must not replace the crash being reported
    }

    /// <summary>Appends one entry for <paramref name="exception"/>. Never throws: a crash handler must not fail while it reports.</summary>
    public void Record(Exception? exception)
    {
        try
        {
            var details = RedactProfile(exception?.ToString() ?? "No exception object was given.", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            Append($"[{_now():yyyy-MM-dd HH:mm:ss}] TabForge {_version}{Environment.NewLine}" +
                   $"{details}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Trace.Error(Trace.Ui, "crash log: record failed: " + ex.Message); }
    }

    private void Append(string entry)
    {
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                var text = (File.Exists(_path) ? File.ReadAllText(_path) : "") + entry;
                File.WriteAllText(_path, Keep(text));
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // A locked or read-only folder (or any other fault): the failure goes to the trace, and the original exception still ends the call as before.
                Trace.Error(Trace.Ui, "crash log: write failed: " + ex.Message);
            }
        }
    }

    /// <summary>
    /// The text with the user's profile folder replaced by <c>%USERPROFILE%</c>, so the log does not name the account. An empty profile
    /// folder changes nothing.
    /// </summary>
    internal static string RedactProfile(string text, string profileFolder) =>
        string.IsNullOrEmpty(profileFolder) ? text : text.Replace(profileFolder, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);

    /// <summary>The text to keep: at most <see cref="MaxCharacters"/> from the end, starting at a whole line.</summary>
    private static string Keep(string text)
    {
        if (text.Length <= MaxCharacters) return text;
        var tail = text[^MaxCharacters..];
        var lineEnd = tail.IndexOf('\n');
        return lineEnd < 0 ? tail : tail[(lineEnd + 1)..];
    }
}
