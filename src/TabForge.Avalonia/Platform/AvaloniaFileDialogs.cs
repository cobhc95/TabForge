using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using TabForge.Platform;
using TabForge.Services;

namespace TabForge.Avalonia.Platform;

/// <summary>
/// The IFileDialogs port on Avalonia's StorageProvider. The port is synchronous, so each call runs a nested dispatcher frame until the picker answers.
/// Every chosen path passes through FilePathPolicy before it is returned. A dialog with a Key starts in its remembered folder and stores its pick (DialogFolderMemory).
/// Owns: options to picker options, the filter text to file types, picker results to checked paths.
/// Does not own: what callers do with the paths. Tests: AvaloniaDialogTests (filter parsing).
/// </summary>
public sealed class AvaloniaFileDialogs : IFileDialogs
{
    private readonly DialogFolderMemory? _folders;

    /// <param name="folders">The remembered folders of the app's settings; null: every dialog starts where its options say.</param>
    public AvaloniaFileDialogs(DialogFolderMemory? folders = null) => _folders = folders;

    public string? OpenFile(FileDialogOptions options, object? owner) => OpenFiles(options with { Multiselect = false }, owner)?.FirstOrDefault();

    public string[]? OpenFiles(FileDialogOptions options, object? owner)
    {
        var storage = StorageOf(owner);
        var start = _folders?.StartFolder(options.Key, options.InitialDirectory) ?? options.InitialDirectory;
        var files = Wait(async () => await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = options.Title, AllowMultiple = options.Multiselect == true, FileTypeFilter = FileTypes(options.Filter),
            SuggestedStartLocation = await StartFolder(storage, start),
        }));
        var paths = CheckedFiles(files.Select(file => file.TryGetLocalPath()));
        if (paths is { Length: > 0 }) _folders?.Remember(options.Key, paths[0], isFolder: false);
        return paths;
    }

    public string? SaveFile(FileDialogOptions options, object? owner)
    {
        var storage = StorageOf(owner);
        var start = _folders?.StartFolder(options.Key, options.InitialDirectory) ?? options.InitialDirectory;
        var file = Wait(async () => await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = options.Title, SuggestedFileName = options.FileName, DefaultExtension = options.DefaultExt,
            FileTypeChoices = FileTypes(options.Filter), ShowOverwritePrompt = options.OverwritePrompt ?? true,
            SuggestedStartLocation = await StartFolder(storage, start),
        }));
        var saved = file is null ? null : CheckedFiles(new[] { file.TryGetLocalPath() })?[0];
        if (saved is not null) _folders?.Remember(options.Key, saved, isFolder: false);
        return saved;
    }

    public string? PickFolder(FileDialogOptions options, object? owner) => PickFolders(options with { Multiselect = false }, owner)?.FirstOrDefault();

    public string[]? PickFolders(FileDialogOptions options, object? owner)
    {
        var storage = StorageOf(owner);
        var start = _folders?.StartFolder(options.Key, options.InitialDirectory) ?? options.InitialDirectory;
        var folders = Wait(async () => await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = options.Title, AllowMultiple = options.Multiselect == true,
            SuggestedStartLocation = await StartFolder(storage, start),
        }));
        var paths = LocalPaths(folders.Select(folder => folder.TryGetLocalPath()))?.Select(path => FilePathPolicy.OutputDirectory(path, "chosen folder")).ToArray();
        if (paths is { Length: > 0 }) _folders?.Remember(options.Key, paths[0], isFolder: true);
        return paths;
    }

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

    private static async Task<IStorageFolder?> StartFolder(IStorageProvider storage, string? directory) =>
        string.IsNullOrEmpty(directory) ? null : await storage.TryGetFolderFromPathAsync(directory);

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
