using System.IO;
using TabForge.Platform;

namespace TabForge.Services;

// Owns: the live side of the remembered dialog folders: reads the folder a key starts in from the live settings, and stores a pick there with a quiet save.
// Does not own: the rules (DialogFolders), the dialogs (AvaloniaFileDialogs) or the settings file (AppSettingsStore). Thread: the UI thread only.
// Tests: DialogFoldersTests (the settings round trip through a scratch store).
public sealed class DialogFolderMemory
{
    private readonly Func<AppSettings> _settings;
    private readonly Action _save;
    private readonly IAppFolders _folders;
    private readonly string _documents;

    /// <param name="settings">The live settings the folders are kept in.</param>
    /// <param name="save">Saves the settings without telling the other windows (a folder change re-applies nothing).</param>
    /// <param name="folders">The app folders, for the default of each kind.</param>
    /// <param name="documents">The user's Documents folder, the default of the project and export dialogs.</param>
    public DialogFolderMemory(Func<AppSettings> settings, Action save, IAppFolders folders, string documents)
    {
        (_settings, _save, _folders, _documents) = (settings, save, folders, documents);
    }

    /// <summary>The memory of the app's settings: the app folders and the user's Documents folder.</summary>
    public static DialogFolderMemory ForApp(Func<AppSettings> settings, Action save) =>
        new(settings, save, AppFolders.Current, AppFolders.Current.Documents);

    /// <summary>The folder a dialog with <paramref name="key"/> starts in (see <see cref="DialogFolders.StartFolder"/>).</summary>
    public string? StartFolder(string? key, string? fallback) =>
        DialogFolders.StartFolder(_settings().RememberedFolders, key, fallback, Directory.Exists, _folders, _documents);

    /// <summary>Stores the pick under <paramref name="key"/> and saves once when it changed.</summary>
    public void Remember(string? key, string chosenPath, bool isFolder)
    {
        if (DialogFolders.Remember(_settings().RememberedFolders, key, chosenPath, isFolder)) _save();
    }
}
