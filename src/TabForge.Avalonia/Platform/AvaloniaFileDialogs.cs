using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using TabForge.Platform;
using TabForge.Services;

namespace TabForge.Avalonia.Platform;

/// <summary>
/// The IFileDialogs port on Avalonia's StorageProvider. The port is synchronous, so each call runs a nested dispatcher frame until the picker answers.
/// The start folder is resolved off the UI thread under a short timeout (DialogFolders.FolderCheckTimeout), and TryGetFolderFromPathAsync is bounded the
/// same way: a stalled shell (an offline OneDrive, a dead network share) falls back to the OS location instead of hanging the window.
/// Every chosen path passes through FilePathPolicy before it is returned. A dialog with a Key starts in its remembered folder and stores its pick (DialogFolderMemory).
/// Owns: options to picker options, the filter text to file types, picker results to checked paths.
/// Does not own: what callers do with the paths. Tests: AvaloniaDialogTests (filter parsing), FileDialogHangTests (bounded start, options).
/// </summary>
public sealed class AvaloniaFileDialogs : IFileDialogs
{
    private readonly DialogFolderMemory? _folders;

    /// <param name="folders">The remembered folders of the app's settings; null: every dialog starts where its options say.</param>
    public AvaloniaFileDialogs(DialogFolderMemory? folders = null) => _folders = folders;

    public string? OpenFile(FileDialogOptions options, object? owner) => OpenFiles(options with { Multiselect = false }, owner)?.FirstOrDefault();

    /// <summary>The one-file open dialog awaited, never through a nested dispatcher frame. Must be called on the UI thread.</summary>
    public async Task<string?> OpenFileAsync(FileDialogOptions options, object? owner) => (await OpenFilesAsync(options with { Multiselect = false }, owner))?.FirstOrDefault();

    public string[]? OpenFiles(FileDialogOptions options, object? owner) => Wait(() => OpenFilesAsync(options, owner));

    /// <summary>The open dialog awaited, never through a nested dispatcher frame: the File > Open command (a menu or key handler on the UI thread) returns to the
    /// message loop while the picker is up. Must be called on the UI thread.</summary>
    public async Task<string[]?> OpenFilesAsync(FileDialogOptions options, object? owner)
    {
        var storage = StorageOf(owner);
        var start = await ResolveStartFolder(_folders, options.Key, options.InitialDirectory);
        var files = await storage.OpenFilePickerAsync(OpenOptions(options, await StorageFolder(storage, start)));
        var paths = CheckedFiles(files.Select(file => file.TryGetLocalPath()));
        if (paths is { Length: > 0 }) _folders?.Remember(options.Key, paths[0], isFolder: false);
        return paths;
    }

    public string? SaveFile(FileDialogOptions options, object? owner) => Wait(() => SaveFileAsync(options, owner));

    /// <summary>The save dialog awaited, never through a nested dispatcher frame (see <see cref="OpenFilesAsync"/>). Must be called on the UI thread.</summary>
    public async Task<string?> SaveFileAsync(FileDialogOptions options, object? owner)
    {
        var storage = StorageOf(owner);
        var start = await ResolveStartFolder(_folders, options.Key, options.InitialDirectory);
        var file = await storage.SaveFilePickerAsync(SaveOptions(options, await StorageFolder(storage, start)));
        var saved = file is null ? null : CheckedFiles(new[] { file.TryGetLocalPath() })?[0];
        if (saved is not null) _folders?.Remember(options.Key, saved, isFolder: false);
        return saved;
    }

    public string? PickFolder(FileDialogOptions options, object? owner) => PickFolders(options with { Multiselect = false }, owner)?.FirstOrDefault();

    /// <summary>The one-folder dialog awaited, never through a nested dispatcher frame. Must be called on the UI thread.</summary>
    public async Task<string?> PickFolderAsync(FileDialogOptions options, object? owner) => (await PickFoldersAsync(options with { Multiselect = false }, owner))?.FirstOrDefault();

    public string[]? PickFolders(FileDialogOptions options, object? owner) => Wait(() => PickFoldersAsync(options, owner));

    /// <summary>The folder dialog awaited, never through a nested dispatcher frame (see <see cref="OpenFilesAsync"/>). Must be called on the UI thread.</summary>
    public async Task<string[]?> PickFoldersAsync(FileDialogOptions options, object? owner)
    {
        var storage = StorageOf(owner);
        var start = await ResolveStartFolder(_folders, options.Key, options.InitialDirectory);
        var folders = await storage.OpenFolderPickerAsync(FolderOptions(options, await StorageFolder(storage, start)));
        var paths = LocalPaths(folders.Select(folder => folder.TryGetLocalPath()))?.Select(path => FilePathPolicy.OutputDirectory(path, "chosen folder")).ToArray();
        if (paths is { Length: > 0 }) _folders?.Remember(options.Key, paths[0], isFolder: true);
        return paths;
    }

    /// <summary>The open picker's options, starting at <paramref name="start"/> only when one proved fast-local (null keeps the OS location).</summary>
    internal static FilePickerOpenOptions OpenOptions(FileDialogOptions options, IStorageFolder? start) => new()
    {
        Title = options.Title,
        AllowMultiple = options.Multiselect == true,
        FileTypeFilter = FileTypes(options.Filter),
        SuggestedStartLocation = start,
    };

    /// <summary>The save picker's options, starting at <paramref name="start"/> only when one proved fast-local (null keeps the OS location).</summary>
    internal static FilePickerSaveOptions SaveOptions(FileDialogOptions options, IStorageFolder? start) => new()
    {
        Title = options.Title,
        SuggestedFileName = options.FileName,
        DefaultExtension = options.DefaultExt,
        FileTypeChoices = FileTypes(options.Filter),
        ShowOverwritePrompt = options.OverwritePrompt ?? true,
        SuggestedStartLocation = start,
    };

    /// <summary>The folder picker's options, starting at <paramref name="start"/> only when one proved fast-local (null keeps the OS location).</summary>
    internal static FolderPickerOpenOptions FolderOptions(FileDialogOptions options, IStorageFolder? start) => new()
    {
        Title = options.Title,
        AllowMultiple = options.Multiselect == true,
        SuggestedStartLocation = start,
    };

    /// <summary>The filter text ("Name|*.a;*.b|...") as picker file types; null for no filter.</summary>
    public static List<FilePickerFileType>? FileTypes(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return null;
        var parts = filter.Split('|');
        if (parts.Length % 2 != 0) throw new ArgumentException($"A file filter needs name|pattern pairs: '{filter}'.", nameof(filter));
        var types = new List<FilePickerFileType>();
        for (var i = 0; i < parts.Length; i += 2)
            types.Add(new FilePickerFileType(parts[i]) { Patterns = parts[i + 1].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) });
        return types;
    }

    private static IStorageProvider StorageOf(object? owner) =>
        (owner as TopLevel)?.StorageProvider ?? throw new ArgumentException("A file dialog on Avalonia needs its owner window.", nameof(owner));

    /// <summary>The start folder of a dialog: the remembered/fallback/default candidate that answered fast, or null (the OS default location).
    /// Without a memory, the caller's own initial folder is checked under the same bound.</summary>
    private static async Task<string?> ResolveStartFolder(DialogFolderMemory? folders, string? key, string? initial) =>
        folders is null
            ? await DialogFolders.FastFolderAsync(initial, DialogFolders.FolderCheckTimeout)
            : await folders.StartFolderAsync(key, initial);

    /// <summary>The storage folder of <paramref name="directory"/>, awaited for at most <see cref="DialogFolders.FolderCheckTimeout"/>; null when it is
    /// missing, slow or fails, so the picker opens at the OS location instead of holding the window on a stalled shell.</summary>
    private static async Task<IStorageFolder?> StorageFolder(IStorageProvider storage, string? directory)
    {
        if (string.IsNullOrEmpty(directory)) return null;
        var lookup = storage.TryGetFolderFromPathAsync(directory);
        if (await Task.WhenAny(lookup, Task.Delay(DialogFolders.FolderCheckTimeout)) != lookup)
        {
            _ = lookup.ContinueWith(static task => _ = task.Exception, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return null;
        }
        try { return lookup.Result; }
        catch (Exception) { return null; }
    }

    // A cancel, or a pick that is not on the local file system, has no path the app can use: both read as null.
    private static string[]? LocalPaths(IEnumerable<string?> paths)
    {
        var list = paths.ToArray();
        return list.Length == 0 || list.Any(path => path is null) ? null : list.Select(path => path!).ToArray();
    }

    /// <summary>The chosen local paths, or null when none was chosen or one is refused (a device path or a link) before any open or save.</summary>
    internal static string[]? CheckedFiles(IEnumerable<string?> paths)
    {
        var local = LocalPaths(paths);
        return local is null || local.Any(path => FilePathBoundary.RefusalReason(path) is not null)
            ? null
            : local.Select(path => FilePathPolicy.OutputFile(path, "chosen file")).ToArray();
    }

    private static T Wait<T>(Func<Task<T>> pick)
    {
        var task = pick();
        if (!task.IsCompleted)
        {
            var frame = new DispatcherFrame();
            task.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
            Dispatcher.UIThread.PushFrame(frame);
        }
        return task.GetAwaiter().GetResult();
    }
}
