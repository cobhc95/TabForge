namespace TabForge.Services;

// Owns: the "Sound engine" choice (Windows GM, one of the download-and-play Sound banks, the user's own .sf2, or silence) and its
//   default for the operating system, plus how an older saved value maps onto today's choices.
// Does not own: fetching a bank (SoundFontDownloader), where it lives (SoundFontStore) or playing it (the audio engine's GmBank).
// Tests: SoundEngineTests.
public static class SoundEngines
{
    /// <summary>The Microsoft GS Wavetable bank of Windows (never a file TabForge owns).</summary>
    public const string WindowsGm = "Windows GM";
    /// <summary>The name of the GeneralUser GS bank; its old saved value was <see cref="LegacySoundFont"/>.</summary>
    public const string SoundFont = "GeneralUser GS";
    /// <summary>The value older settings files stored for GeneralUser GS.</summary>
    public const string LegacySoundFont = "TabForge SoundFont";
    /// <summary>The bank the Linux package bundles next to the program.</summary>
    public const string FluidR3 = "FluidR3 GM";
    public const string MuseScoreGeneral = "MuseScore General";
    /// <summary>The user's own .sf2 file: TabForge never copies, alters or deletes it.</summary>
    public const string OwnFile = "Open your own .sf2…";
    /// <summary>Silence: the built-in synth plays nothing.</summary>
    public const string None = "None";

    /// <summary>The engines this system offers. Windows GM is listed on Windows only (there is no gm.dls elsewhere).</summary>
    public static IReadOnlyList<string> All { get; } = OperatingSystem.IsWindows()
        ? new[] { None, WindowsGm, SoundFont, FluidR3, MuseScoreGeneral, OwnFile }
        : new[] { None, SoundFont, FluidR3, MuseScoreGeneral, OwnFile };

    /// <summary>Windows GM on Windows (what the app has always played), GeneralUser GS on every other system.</summary>
    public static string Default => OperatingSystem.IsWindows() ? WindowsGm : SoundFont;

    /// <summary>The engine a stored value means: an empty or unknown value is the default of this operating system.</summary>
    public static string Resolve(string? stored) => Resolve(stored, OperatingSystem.IsWindows());

    /// <summary>
    /// <see cref="Resolve(string?)"/> for a Windows host or not (tests simulate the other OS): off Windows a stored "Windows GM" means
    /// GeneralUser GS. Old values keep working ("TabForge SoundFont" = GeneralUser GS) and a bank id ("bank:generaluser-gs") maps too.
    /// </summary>
    public static string Resolve(string? stored, bool windowsHost) => Canonical(stored) switch
    {
        None => None,
        WindowsGm => windowsHost ? WindowsGm : SoundFont,
        SoundFont => SoundFont,
        FluidR3 => FluidR3,
        MuseScoreGeneral => MuseScoreGeneral,
        OwnFile => OwnFile,
        _ => windowsHost ? WindowsGm : SoundFont,
    };

    /// <summary>The engine the stored value names, without the operating-system fallback (empty when unknown).</summary>
    private static string Canonical(string? stored) => stored switch
    {
        null or "" => "",
        None or "none" => None,
        WindowsGm or "windows-gm" => WindowsGm,
        LegacySoundFont or SoundFont or "generaluser-gs" or "bank:generaluser-gs" => SoundFont,
        FluidR3 or "fluidr3-gm" or "bank:fluidr3-gm" => FluidR3,
        MuseScoreGeneral or "musescore-general" or "bank:musescore-general" => MuseScoreGeneral,
        OwnFile or "own" or "own-file" => OwnFile,
        _ => "",
    };

    /// <summary>Whether a stored engine value plays one of TabForge's Sound bank files (GeneralUser GS, FluidR3 GM or MuseScore General).</summary>
    public static bool IsDownloadedBank(string? engine) => Canonical(engine) is SoundFont or FluidR3 or MuseScoreGeneral;

    /// <summary>The bank id of a stored engine value, or null for Windows GM, None and the user's own file.</summary>
    public static string? BankIdOf(string? engine) => Canonical(engine) switch
    {
        SoundFont => "generaluser-gs",
        FluidR3 => "fluidr3-gm",
        MuseScoreGeneral => "musescore-general",
        _ => null,
    };
}
