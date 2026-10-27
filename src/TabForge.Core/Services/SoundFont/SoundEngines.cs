namespace TabForge.Services;

// Owns: the "Sound engine" choice (Windows GM or the TabForge SoundFont) and its default for the operating system.
// Does not own: fetching the SoundFont (SoundFontDownloader), where it lives (SoundFontStore) or playing it (the audio engine's GmBank).
// Tests: SoundEngineTests.
public static class SoundEngines
{
    public const string WindowsGm = "Windows GM", SoundFont = "TabForge SoundFont";

    /// <summary>The engines this system offers: both on Windows; only the SoundFont elsewhere (there is no gm.dls to play).</summary>
    public static IReadOnlyList<string> All { get; } = OperatingSystem.IsWindows() ? new[] { WindowsGm, SoundFont } : new[] { SoundFont };

    /// <summary>Windows GM on Windows (what the app has always played), the SoundFont on every other system.</summary>
    public static string Default => OperatingSystem.IsWindows() ? WindowsGm : SoundFont;

    /// <summary>The engine a stored value means: an empty or unknown value is the default of this operating system.</summary>
    public static string Resolve(string? stored) => Resolve(stored, OperatingSystem.IsWindows());

    /// <summary><see cref="Resolve(string?)"/> for a Windows host or not (tests simulate the other OS): off Windows a stored "Windows GM" means the SoundFont.</summary>
    public static string Resolve(string? stored, bool windowsHost) =>
        string.Equals(stored, SoundFont, StringComparison.Ordinal) || (windowsHost && string.Equals(stored, WindowsGm, StringComparison.Ordinal)) ? stored! : windowsHost ? WindowsGm : SoundFont;
}
