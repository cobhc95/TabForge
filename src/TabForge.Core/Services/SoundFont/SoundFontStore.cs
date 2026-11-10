using System.IO;

namespace TabForge.Services;

// Owns: where each downloaded Sound bank lives, which file (if any) the sound engine setting asks the audio engine to play, and
//   deleting a bank's file safely (inside the SoundFonts data folder only, after switching the engine away when it is in use).
// Does not own: fetching it (SoundFontDownloader) or playing it (the audio engine's GmBank).
// Tests: SoundEngineTests, SoundBankCatalogTests.
public static class SoundFontStore
{
    /// <summary>The GeneralUser GS file name (the app's first bank; older settings point at it without a name).</summary>
    public const string FileName = "GeneralUser-GS.sf2";

    /// <summary>The per-user data folder every downloaded bank lives in (a profile redirects it).</summary>
    public static string Folder => Path.Combine(UserPaths.Local, "SoundFonts");

    /// <summary>The GeneralUser GS file path (it may not exist yet).</summary>
    public static string FilePath => Path.Combine(Folder, FileName);

    /// <summary>The file path of a catalog bank (it may not exist yet).</summary>
    public static string PathOf(SoundBank bank) => Path.Combine(Folder, bank.FileName);

    /// <summary>Whether a catalog bank's file has been downloaded.</summary>
    public static bool IsDownloaded(SoundBank bank) => File.Exists(PathOf(bank)) && IsInsideFolder(PathOf(bank));

    /// <summary>Whether GeneralUser GS is installed (the older setting's bank).</summary>
    public static bool IsInstalled => IsDownloaded(SoundBankCatalog.GeneralUserGs);

    /// <summary>The GeneralUser GS bank the Linux and macOS packages bundle next to the program; Windows never has one.</summary>
    public static string BundledPath => Path.Combine(AppContext.BaseDirectory, "SoundFonts", FileName);

    /// <summary>The bundled file of a bank: only GeneralUser GS ships in the package.</summary>
    public static string BundledPathOf(SoundBank bank) =>
        bank.Id == SoundBankCatalog.GeneralUserGs.Id ? BundledPath : "";

    /// <summary>True when a bank comes with the package (off Windows, GeneralUser GS).</summary>
    public static bool IsBuiltIn(SoundBank bank) =>
        BundledPathOf(bank) is { Length: > 0 } bundled && File.Exists(bundled);

    /// <summary>The older GeneralUser GS path resolution: the downloaded one, else (off Windows only) the bundled one; empty when there is none.</summary>
    public static string ResolvedPath(bool windowsHost) => BankPath(SoundBankCatalog.GeneralUserGs, windowsHost);

    /// <summary>A bank's playable file: downloaded first, else the bundled GeneralUser GS off Windows; empty when neither exists.</summary>
    public static string BankPath(SoundBank bank, bool windowsHost)
    {
        if (IsDownloaded(bank)) return PathOf(bank);
        var bundled = BundledPathOf(bank);
        return !windowsHost && bundled.Length > 0 && File.Exists(bundled) ? bundled : "";
    }

    /// <summary>The user's own .sf2 file, when the setting names one that still exists; empty otherwise. Never copied or changed by TabForge.</summary>
    public static string UserPath(PluginSettings settings) =>
        settings.UserSoundFont is { Length: > 0 } path && File.Exists(path) ? path : "";

    /// <summary>The SoundFont is chosen but there is no file to play: the caller says so (Windows then plays Windows GM, other systems have no sound until it is downloaded).</summary>
    public static bool ChosenButMissing(PluginSettings settings) => ChosenButMissing(settings, OperatingSystem.IsWindows());

    public static bool ChosenButMissing(PluginSettings settings, bool windowsHost) =>
        ActivePath(settings, windowsHost).Length == 0 && SoundEngines.IsDownloadedBank(SoundEngines.Resolve(settings.SoundEngine, windowsHost));

    /// <summary>The SoundFont file the audio engine is to play: empty = Windows GM, "None" = silence, else the chosen bank's file.</summary>
    public static string ActivePath(PluginSettings settings) => ActivePath(settings, OperatingSystem.IsWindows());

    public static string ActivePath(PluginSettings settings, bool windowsHost) => SoundEngines.Resolve(settings.SoundEngine, windowsHost) switch
    {
        SoundEngines.None => SoundEngines.None,
        SoundEngines.WindowsGm => "",
        SoundEngines.OwnFile => UserPath(settings),
        var engine when SoundBankCatalog.ForEngine(engine) is { } bank => BankPath(bank, windowsHost),
        _ => "",
    };

    /// <summary>True when <paramref name="path"/> is a file inside the SoundFonts data folder (the only place TabForge deletes from).</summary>
    public static bool IsInsideFolder(string path) => IsInsideFolder(path, Folder);

    /// <summary><see cref="IsInsideFolder(string)"/> for another folder (tests use a temporary one).</summary>
    internal static bool IsInsideFolder(string path, string folder)
    {
        try
        {
            var root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(path);
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    /// <summary>
    /// The engine to use after deleting <paramref name="bank"/> while it is the engine in use: Windows GM on Windows; elsewhere the next
    /// downloaded bank, else None. <paramref name="downloaded"/> answers whether another bank's file is installed.
    /// </summary>
    public static string EngineAfterDelete(SoundBank bank, bool windowsHost, Func<SoundBank, bool> downloaded)
    {
        if (windowsHost) return SoundEngines.WindowsGm;
        foreach (var other in SoundBankCatalog.All)
            if (other.Id != bank.Id && downloaded(other)) return other.Name;
        return SoundEngines.None;
    }

    /// <summary>
    /// Deletes a downloaded bank after switching the engine away when that bank is in use (Windows GM on Windows; elsewhere the next
    /// downloaded bank, else None). Only a file inside <see cref="Folder"/> is ever removed: another path is refused. Returns null on
    /// success, else the reason in words. The user's own .sf2 is never a catalog bank and is never touched.
    /// </summary>
    public static string? Delete(SoundBank bank, PluginSettings settings, bool windowsHost, Func<SoundBank, bool>? downloaded = null) =>
        DeleteIn(bank, Folder, settings, windowsHost, downloaded);

    /// <summary><see cref="Delete"/> for another folder (tests use a temporary one; the user's own file check still applies).</summary>
    internal static string? DeleteIn(SoundBank bank, string folder, PluginSettings settings, bool windowsHost, Func<SoundBank, bool>? downloaded = null)
    {
        var path = Path.Combine(folder, bank.FileName);
        if (!IsInsideFolder(path, folder)) return "That file is outside the TabForge SoundFonts folder and was not deleted.";
        if (settings.UserSoundFont is { Length: > 0 } own && PathsEqual(path, own))
            return "That is your own SoundFont file and was not deleted.";
        if (SoundEngines.Resolve(settings.SoundEngine, windowsHost) == bank.Name)
            settings.SoundEngine = EngineAfterDelete(bank, windowsHost, other => downloaded?.Invoke(other) ?? IsDownloaded(other));
        return DeleteFile(path, folder);
    }

    /// <summary>Deletes one file inside the SoundFonts folder; any path outside it is refused and left alone.</summary>
    internal static string? DeleteFile(string path, string folder)
    {
        if (!IsInsideFolder(path, folder)) return "That file is outside the TabForge SoundFonts folder and was not deleted.";
        try
        {
            if (File.Exists(path)) File.Delete(path);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"The Sound bank could not be deleted: {ex.Message}";
        }
    }

    private static bool PathsEqual(string first, string second)
    {
        try { return string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }
}
