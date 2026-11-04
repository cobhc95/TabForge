using System.IO;
using TabForge.Platform;
using TabForge.Plugins;
using static System.StringComparison;

namespace TabForge.Services;

// Owns: the rules of the remembered dialog folders: where a dialog key starts (its last folder while it still exists, else the caller's fallback,
// else the default of its kind) and what a successful pick stores (the folder of the pick, for that key only). The start folder is checked off the
// caller's thread under a short timeout, so a stalled shell (an offline OneDrive, a dead network share) never blocks the UI.
// Does not own: the dialogs (AvaloniaFileDialogs), the live settings and their save (DialogFolderMemory) or the settings file (AppSettingsStore).
// Tests: DialogFoldersTests (two keys keep separate folders; the default of each kind; the settings round trip), FileDialogHangTests (the bounded check).
public static class DialogFolders
{
    /// <summary>The most keys kept; the rest are dropped when a new key is added.</summary>
    public const int MaxKeys = 64;

    /// <summary>The longest a start folder may take to answer before it is treated as unreachable and the dialog keeps the OS location.</summary>
    public static readonly TimeSpan FolderCheckTimeout = TimeSpan.FromMilliseconds(300);

    /// <summary>The folder a dialog with <paramref name="key"/> starts in: its remembered folder when <paramref name="exists"/> says it is still there,
    /// else <paramref name="fallback"/> (the caller's own start, such as the folder of the loaded impulse response), else <see cref="DefaultFor"/>.</summary>
    public static string? StartFolder(IReadOnlyDictionary<string, string> remembered, string? key, string? fallback, Func<string, bool> exists,
        IAppFolders folders, string documents)
    {
        if (key is not null && remembered.TryGetValue(key, out var last) && last.Length > 0 && exists(last)) return last;
        return fallback is { Length: > 0 } ? fallback : DefaultFor(key, folders, documents);
    }

    /// <summary>The folders a dialog may start in, most preferred first: its remembered folder (when it has one), then the caller's fallback, else the
    /// default of its kind. No file system work, so this can run on the calling thread and the candidates can then be checked off it.</summary>
    public static IReadOnlyList<string> StartCandidates(IReadOnlyDictionary<string, string> remembered, string? key, string? fallback,
        IAppFolders folders, string documents)
    {
        var candidates = new List<string>(2);
        if (key is not null && remembered.TryGetValue(key, out var last) && last.Length > 0) candidates.Add(last);
        var otherwise = fallback is { Length: > 0 } ? fallback : DefaultFor(key, folders, documents);
        if (otherwise is { Length: > 0 } && !candidates.Contains(otherwise)) candidates.Add(otherwise);
        return candidates;
    }

    /// <summary>The first candidate that answers as a folder within <paramref name="timeout"/>; null when every candidate is gone, slow or unreachable,
    /// so the dialog keeps the OS default location. Each check runs on a worker, never on the caller's thread.</summary>
    public static async Task<string?> StartFolderBoundedAsync(IReadOnlyDictionary<string, string> remembered, string? key, string? fallback,
        Func<string, bool> exists, IAppFolders folders, string documents, TimeSpan timeout)
    {
        foreach (var candidate in StartCandidates(remembered, key, fallback, folders, documents))
            if (await IsFastFolderAsync(candidate, exists, timeout)) return candidate;
        return null;
    }

    /// <summary>Whether <paramref name="path"/> answers as a folder within <paramref name="timeout"/>. The check runs on a worker and a slow one is left to
    /// finish on its own, so a stalled shell cannot hold the caller. False when the folder is missing, slow or unreachable.</summary>
    public static async Task<bool> IsFastFolderAsync(string path, Func<string, bool> exists, TimeSpan timeout)
    {
        var check = Task.Run(() =>
        {
            try { return exists(path); }
            catch (Exception) { return false; }
        });
        if (await Task.WhenAny(check, Task.Delay(timeout)) != check)
        {
            _ = check.ContinueWith(static task => _ = task.Exception, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return false;
        }
        return check.Result;
    }

    /// <summary><paramref name="path"/> when it is a folder that answers within <paramref name="timeout"/>; else null.</summary>
    public static async Task<string?> FastFolderAsync(string? path, TimeSpan timeout) =>
        path is { Length: > 0 } && await IsFastFolderAsync(path, Directory.Exists, timeout) ? path : null;

    /// <summary>The start folder of a kind when nothing is remembered: projects and exports the documents folder, audio the Music folder, video the Videos
    /// folder, settings the settings folder, FX chains the chains folder, presets, articulations and plug-ins the plug-in library. Null for the
    /// impulse-response browser, which starts at the folder of the loaded file, and for unknown keys (the OS decides).</summary>
    public static string? DefaultFor(string? key, IAppFolders folders, string documents)
    {
        if (key is null) return null;
        if (key == DialogKeys.ExportVideo) return folders.Videos;
        if (key.StartsWith("project.", Ordinal) || key.StartsWith("export.", Ordinal)) return documents;
        if (key.StartsWith("audio.", Ordinal) || key.StartsWith("midi.", Ordinal) || key.StartsWith("media.", Ordinal) || key == DialogKeys.RenderFolder) return folders.Music;
        if (key.StartsWith("settings.", Ordinal)) return folders.Settings;
        if (key.StartsWith("fxchain.", Ordinal)) return PluginLibrary.ChainsFolder;
        if (key.StartsWith("presets.", Ordinal)) return Path.Combine(PluginLibrary.Root, PresetFolderStore.PluginFolderName);
        if (key.StartsWith("articulations.", Ordinal)) return Path.Combine(PluginLibrary.Root, "Articulations");
        if (key.StartsWith("plugin.", Ordinal)) return PluginLibrary.Root;
        return null;
    }

    /// <summary>Stores the folder of <paramref name="chosenPath"/> under <paramref name="key"/> (the path itself when <paramref name="isFolder"/>, else its folder).
    /// Other keys are not touched. True when the stored folder changed, so the caller saves once.</summary>
    public static bool Remember(IDictionary<string, string> remembered, string? key, string chosenPath, bool isFolder)
    {
        if (key is null || chosenPath.Length == 0) return false;
        var folder = isFolder ? chosenPath : Path.GetDirectoryName(chosenPath);
        if (string.IsNullOrEmpty(folder)) return false;
        if (remembered.TryGetValue(key, out var previous) && string.Equals(previous, folder, Ordinal)) return false;
        remembered.Remove(key);   // re-added at the end, so the newest keys are the ones kept
        remembered[key] = folder;
        while (remembered.Count > MaxKeys) remembered.Remove(remembered.Keys.First());
        return true;
    }
}
