using MeltySynth;

namespace TabForge.AudioEngine.Synth;

// Owns: which sound bank the General MIDI synths play: the Windows gm.dls (converted) or a SoundFont 2 file (GeneralUser GS), the live switch, and the rule that off Windows
//   the gm.dls is never read (the bundled SoundFont plays, else a clear "download the SoundFont" failure instead of silence).
// Does not own: the synth (GmSynth), the DLS conversion (DlsToSoundFont) or fetching the SoundFont (the app).
// Tests: AudioSmokeTests (bank switch with the local SF2), PortableBackendTests (the non-Windows fallback, with the OS flag injected).
/// <summary>
/// The bank choice of one engine session (the session owns one and hands it to every synth it creates). <see cref="Select"/> loads the file on the calling (main) thread and publishes one immutable
/// <see cref="Choice"/>; each running <see cref="GmSynth"/> notices the new <see cref="Choice.Version"/> at its next block and rebuilds its synthesizer.
/// </summary>
public sealed class GmBank
{
    /// <summary>Master volume for a plain SoundFont (MeltySynth default is 0.5; GeneralUser GS is mastered a little quieter than the calibrated Windows bank).</summary>
    public const float SoundFontMasterVolume = 0.6f;

    /// <summary>One published choice: the font (null = Windows GM, loaded on demand), the master volume, the version that changes with every switch.</summary>
    public sealed record Choice(SoundFont? Font, string Path, float MasterVolume, int Version, bool WindowsHost = true)
    {
        /// <summary>The font to play. Windows without a SoundFont: the converted gm.dls. Elsewhere without a SoundFont: a failure that says to download it (the synth then reports itself failed).</summary>
        public SoundFont Bank => Font ?? (WindowsHost ? DlsToSoundFont.LoadWindowsBank() : throw new FileNotFoundException(NoBankMessage));
    }

    /// <summary>Said when a system without gm.dls has no SoundFont to play.</summary>
    public const string NoBankMessage = "There is no General MIDI sound bank: download the TabForge SoundFont (Settings > Audio & Plug-ins > Sound engine > Download SoundFont).";

    /// <summary>Where the packages for Linux and macOS bundle the SoundFont (next to the program).</summary>
    public static string DefaultBundledPath => System.IO.Path.Combine(AppContext.BaseDirectory, "SoundFonts", "GeneralUser-GS.sf2");

    private readonly bool _windowsHost;
    private readonly string _bundledPath;
    private volatile Choice _current;

    /// <summary>The bank of this OS: Windows GM until a SoundFont is selected; elsewhere the bundled SoundFont when it is there.</summary>
    public GmBank() : this(OperatingSystem.IsWindows(), DefaultBundledPath) { }

    /// <param name="windowsHost">False: this system has no gm.dls, so the bundled SoundFont (or a clear failure) takes its place. Tests inject it.</param>
    /// <param name="bundledPath">The SoundFont a non-Windows system plays when none is selected.</param>
    public GmBank(bool windowsHost, string bundledPath)
    {
        _windowsHost = windowsHost;
        _bundledPath = bundledPath;
        _current = Fallback(0);
    }

    /// <summary>The choice for "no SoundFont selected": Windows GM on Windows; the bundled SoundFont when present elsewhere (else a choice that fails with <see cref="NoBankMessage"/>).</summary>
    private Choice Fallback(int version)
    {
        if (_windowsHost) return new Choice(null, "", GmSynthTuning.EffectiveMasterVolume, version);
        var font = File.Exists(_bundledPath) ? TryLoad(_bundledPath, out _) : null;
        return font is null ? new Choice(null, "", SoundFontMasterVolume, version, WindowsHost: false) : new Choice(font, _bundledPath, SoundFontMasterVolume, version, WindowsHost: false);
    }

    private static SoundFont? TryLoad(string path, out string error)
    {
        error = "";
        try
        {
            if (!path.EndsWith(".sf2", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("not a .sf2 file");
            return new SoundFont(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or NotSupportedException)
        {
            error = ex.Message;
            return null;
        }
    }

    public Choice Current => _current;

    /// <summary>Selects the bank: a .sf2 path, or empty for Windows GM. False (the bank stays as it was) when the file cannot be read.</summary>
    public bool Select(string path, out string error)
    {
        error = "";
        var old = _current;
        if (string.Equals(old.Path, path, StringComparison.OrdinalIgnoreCase)) return true;
        if (path.Length == 0) { _current = Fallback(old.Version + 1); return true; }
        var font = TryLoad(path, out error);
        if (font is null) return false;
        _current = new Choice(font, path, SoundFontMasterVolume, old.Version + 1, _windowsHost);
        return true;
    }
}
