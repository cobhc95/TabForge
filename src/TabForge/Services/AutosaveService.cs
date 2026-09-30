using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace TabForge.Services;

/// <summary>
/// Autosave (audit F-04): dirty songs are copied to the Recovery folder as "autosave-&lt;pid&gt;-&lt;id&gt;-&lt;name&gt;.tforge" on a timer. The
/// user's own files are never touched. A normal save, tab close or exit deletes the copy; copies whose process is gone are what a
/// crash leaves behind, and are offered at the next start.
/// </summary>
public static class AutosaveService
{
    public const int DefaultMinutes = 2;
    public const int MaxMinutes = 60;

    private static readonly Regex NamePattern = new(@"^autosave-(?<pid>\d+)-(?<id>[0-9a-f]{32})-.*\.tforge$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string DefaultFolder => UserPaths.Recovery;

    /// <summary>0 (off) or 1..60 minutes; anything else falls back to the default.</summary>
    public static int NormalizeMinutes(int minutes) => minutes is >= 0 and <= MaxMinutes ? minutes : DefaultMinutes;

    /// <summary>The file for one open document in this process.</summary>
    public static string FileFor(string folder, int pid, Guid id, string? songName) =>
        Path.Combine(folder, $"autosave-{pid}-{id:N}-{TabForge.Audio.Contracts.SafeFileNames.SafeFileName(songName, "Untitled", 40)}.tforge");

    public static bool TryParse(string path, out int pid)
    {
        pid = 0;
        var m = NamePattern.Match(Path.GetFileName(path));
        return m.Success && int.TryParse(m.Groups["pid"].Value, out pid);
    }

    /// <summary>Deletes an autosave file and its error list; never throws.</summary>
    public static void Delete(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        foreach (var p in new[] { path, Path.ChangeExtension(path, ".errors.txt") })
        {
            try { if (File.Exists(p)) File.Delete(p); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Debug.WriteLine($"Autosave cleanup failed: {ex.Message}"); }
        }
    }

    /// <summary>Deletes every autosave file of process <paramref name="pid"/> (normal exit).</summary>
    public static void DeleteOwn(string folder, int pid)
    {
        foreach (var file in Enumerate(folder))
            if (TryParse(file, out var owner) && owner == pid) Delete(file);
    }

    /// <summary>Autosave files whose process is no longer running: what a crash or kill left behind.</summary>
    public static List<string> FindOrphans(string folder, Func<int, bool>? isRunning = null)
    {
        isRunning ??= IsTabForgeRunning;
        return Enumerate(folder).Where(f => TryParse(f, out var pid) && !isRunning(pid)).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IEnumerable<string> Enumerate(string folder)
    {
        try { return Directory.Exists(folder) ? Directory.GetFiles(folder, "autosave-*.tforge") : Array.Empty<string>(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Array.Empty<string>(); }
    }

    private static bool IsTabForgeRunning(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return !p.HasExited && p.ProcessName.StartsWith("TabForge", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
    }
}

/// <summary>Preferences choices for the autosave interval.</summary>
public static class AutosaveChoices
{
    private static readonly (string Label, int Minutes)[] Items =
    {
        ("Off", 0), ("Every minute", 1), ("Every 2 minutes", 2), ("Every 5 minutes", 5), ("Every 10 minutes", 10), ("Every 30 minutes", 30),
    };

    public static string[] Labels { get; } = Items.Select(i => i.Label).ToArray();

    public static int ToMinutes(string label) => Items.FirstOrDefault(i => i.Label == label) is { Label: not null } hit ? hit.Minutes : AutosaveService.DefaultMinutes;

    /// <summary>The nearest listed choice (a hand-edited interval shows as the closest one).</summary>
    public static string ToLabel(int minutes) => Items.OrderBy(i => Math.Abs(i.Minutes - minutes)).First().Label;
}

/// <summary>Timer logic of the autosave: when a pass is due. Pure, so it can be tested without a window.</summary>
public sealed class AutosavePlanner
{
    private DateTime _lastRun;

    public AutosavePlanner(DateTime now) => _lastRun = now;

    /// <summary>After a failed pass the next attempt comes this many minutes later (or the interval, when that is shorter).</summary>
    public const int RetryMinutes = 1;

    /// <summary>
    /// True when a pass should run now. While playing or recording the interval is doubled (a cap, not a stop: a long session must
    /// keep a recovery copy, and the UI-thread capture is the only cost). After a failed pass it retries after
    /// <see cref="RetryMinutes"/>.
    /// </summary>
    public bool Due(DateTime now, int minutes, bool playing, bool retrying = false)
    {
        if (minutes <= 0) return false;
        var wait = TimeSpan.FromMinutes(retrying ? Math.Min(minutes, RetryMinutes) : minutes);
        if (playing) wait += wait;
        return now - _lastRun >= wait;
    }

    public void Ran(DateTime now) => _lastRun = now;

    /// <summary>Makes the next <see cref="Due"/> check true (the "Retry now" button).</summary>
    public void RunSoon() => _lastRun = DateTime.MinValue;
}
