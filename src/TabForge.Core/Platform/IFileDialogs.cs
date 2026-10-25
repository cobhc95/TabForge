namespace TabForge.Platform;

/// <summary>
/// The file and folder dialogs as a port. Each call is modal and synchronous, and returns null when the user cancels.
/// The WPF adapter is <c>Platform/Wpf/WpfFileDialogs</c>, reached as <c>Ui.Dialogs</c>.
/// Owns: the dialog contract. Does not own: what the caller does with the chosen paths.
/// Tests: no dedicated test class.
/// </summary>
public interface IFileDialogs
{
    /// <summary>Shows an open dialog for one file; returns its path, or null when cancelled. <paramref name="owner"/> is the host window, or null.</summary>
    string? OpenFile(FileDialogOptions options, object? owner);
    /// <summary>Shows an open dialog for files (the options set multi-select); returns the paths, or null when cancelled.</summary>
    string[]? OpenFiles(FileDialogOptions options, object? owner);
    /// <summary>Shows a save dialog; returns the path, or null when cancelled.</summary>
    string? SaveFile(FileDialogOptions options, object? owner);
    /// <summary>Shows a folder dialog for one folder; returns its path, or null when cancelled.</summary>
    string? PickFolder(FileDialogOptions options, object? owner);
    /// <summary>Shows a folder dialog for folders (the options set multi-select); returns the paths, or null when cancelled.</summary>
    string[]? PickFolders(FileDialogOptions options, object? owner);
}

/// <summary>
/// What a dialog shows. A property left null keeps the dialog's own default, so a call sets only what it names.
/// Folder dialogs read only Title, InitialDirectory and Multiselect.
/// </summary>
public sealed record FileDialogOptions
{
    public string? Title { get; init; }
    public string? Filter { get; init; }
    public int? FilterIndex { get; init; }
    public string? InitialDirectory { get; init; }
    public string? FileName { get; init; }
    public string? DefaultExt { get; init; }
    public bool? AddExtension { get; init; }
    public bool? CheckFileExists { get; init; }
    public bool? OverwritePrompt { get; init; }
    public bool? Multiselect { get; init; }
}
