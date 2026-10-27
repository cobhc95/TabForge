namespace TabForge.Platform;

// Owns: the per-user file association questions and writes the app asks the operating system (is TabForge registered for this executable, register it, remove it).
// Does not own: when to register (MainWindow.View.cs ApplyFileAssociations), the extension and ProgID names (Services/Files/FileAssociations.cs) or the Windows calls (adapter: Platform/Windows/WindowsFileAssociations.cs).
// Tests: none dedicated(the adapter's answer equals a direct registry read).
/// <summary>
/// The "Open with TabForge" registration for Guitar Pro and TabForge files. Writes touch only TabForge's own ProgIDs and the "Open with" entries that name them.
/// </summary>
public interface IFileAssociations
{
    /// <summary>True when every extension lists TabForge (by ProgID) with a command pointing at <paramref name="exePath"/>.</summary>
    bool IsRegistered(string exePath);

    /// <summary>Writes TabForge's ProgIDs for <paramref name="exePath"/> and tells the shell the associations changed. Throws when Windows refuses the write.</summary>
    void Register(string exePath);

    /// <summary>Removes TabForge's ProgIDs and "Open with" entries and tells the shell the associations changed. Throws when Windows refuses the write.</summary>
    void Unregister();
}

/// <summary>The non-Windows stub: registration happens at install time there (Info.plist, .desktop), so nothing is written at run time.</summary>
public sealed class NullFileAssociations : IFileAssociations
{
    public bool IsRegistered(string exePath) => true;
    public void Register(string exePath) { }
    public void Unregister() { }
}
