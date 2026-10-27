using System.IO;

namespace TabForge.Services;

// Owns: where the downloaded SoundFont lives and which SoundFont file (if any) the sound engine setting asks the audio engine to play.
// Does not own: fetching it (SoundFontDownloader) or playing it (the audio engine's GmBank).
// Tests: SoundEngineTests.
public static class SoundFontStore
{
    public const string FileName = "GeneralUser-GS.sf2";

    /// <summary>The SoundFont file path (it may not exist yet): in the per-user local data folder, which a profile redirects.</summary>
    public static string FilePath => Path.Combine(UserPaths.Local, "SoundFonts", FileName);

    public static bool IsInstalled => File.Exists(FilePath);

    /// <summary>The SoundFont the Linux and macOS packages bundle next to the program (Stage 5 packaging); Windows never has one.</summary>
    public static string BundledPath => Path.Combine(AppContext.BaseDirectory, "SoundFonts", FileName);

    /// <summary>The SoundFont file to play: the downloaded one, else (off Windows only) the bundled one; empty when there is none. <paramref name="windowsHost"/> is the OS flag tests simulate.</summary>
    public static string ResolvedPath(bool windowsHost) =>
        IsInstalled ? FilePath : !windowsHost && File.Exists(BundledPath) ? BundledPath : "";

    /// <summary>The SoundFont is the chosen engine but there is no file to play: the caller says so (Windows then plays Windows GM, other systems have no sound until it is downloaded).</summary>
    public static bool ChosenButMissing(PluginSettings settings) => ChosenButMissing(settings, OperatingSystem.IsWindows());

    public static bool ChosenButMissing(PluginSettings settings, bool windowsHost) =>
        SoundEngines.Resolve(settings.SoundEngine) == SoundEngines.SoundFont && ResolvedPath(windowsHost).Length == 0;

    /// <summary>The SoundFont file the audio engine is to play; empty = Windows GM (Windows: chosen, or the SoundFont is not installed).</summary>
    public static string ActivePath(PluginSettings settings) =>
        SoundEngines.Resolve(settings.SoundEngine) == SoundEngines.SoundFont ? ResolvedPath(OperatingSystem.IsWindows()) : "";
}
