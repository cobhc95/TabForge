using MeltySynth;

namespace TabForge.AudioEngine.Synth;

// Owns: which sound bank the General MIDI synths play: the Windows gm.dls (converted) or a SoundFont 2 file (GeneralUser GS), whether the experimental TabForge Synth
//   plays over it (its fallback is the bank "no SoundFont" would play), the live switch, and the rule that off Windows
//   the gm.dls is never read (the bundled SoundFont plays, else a clear "download the SoundFont" failure instead of silence).
// Does not own: the synth (GmSynth), the DLS conversion (DlsToSoundFont) or fetching the SoundFont (the app).
// Tests: AudioSmokeTests (bank switch with the local SF2), PortableBackendTests (the non-Windows fallback, with the OS flag injected).
/// <summary>
/// The bank choice of one engine session (the session owns one and hands it to every synth it creates). <see cref="Select"/> loads the file on the calling (main) thread and publishes one immutable
/// <see cref="Choice"/>; running <see cref="GmSynth"/> instances prepare replacements on that same control thread and swap them at a block boundary.
/// </summary>
public sealed class GmBank
{
    /// <summary>Master volume for a plain SoundFont (MeltySynth default is 0.5; GeneralUser GS is mastered a little quieter than the calibrated Windows bank).</summary>
    public const float SoundFontMasterVolume = 0.6f;

    /// <summary>One published choice: the font (null = Windows GM, loaded on demand), the master volume, the version that changes with every switch.</summary>
    public sealed record Choice(SoundFont? Font, string Path, float MasterVolume, int Version, bool WindowsHost = true, bool Silent = false, TabForge.Sf2Synth.Sf2.SoundFont? Sf2 = null, bool Experimental = false)
    {
        // Experimental: the TabForge Synth's guitar layer plays; Sf2 (when readable) replaces MeltySynth for everything else.
        // Amp/Cab: the user's .nam and cabinet .wav for that layer ("" = built-in); RigOnly: only they changed, so running synths swap them in place.
        public string Amp { get; init; } = "";
        public string Cab { get; init; } = "";
        public bool RigOnly { get; init; }
        public bool Humanize { get; init; } = true;
        /// <summary>The font to play. Windows without a SoundFont: the converted gm.dls. Elsewhere without a SoundFont: a failure that says to download it (the synth then reports itself failed). Never read when <see cref="Silent"/>.</summary>
        public SoundFont Bank => Silent ? throw new InvalidOperationException("The silent sound bank has no font to play.") : Font ?? (WindowsHost ? DlsToSoundFont.LoadWindowsBank() : throw new FileNotFoundException(NoBankMessage));
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

    // "tabforge:synth|<sf2>|<amp .nam>|<cab .wav>|<off: no humanizer>" (empty sf2: the bundled SoundFont). An unreadable SoundFont plays this OS's fallback bank,
    // so the song always sounds; a new amp or cabinet with the same SoundFont keeps the loaded font and only swaps the rig.
    private Choice Experimental(string path, Choice old, out string error)
    {
        error = "";
        var parts = (path + "||||").Split('|');
        var human = parts[4] != "off";
        var (file, version) = (parts[1].Length > 0 ? parts[1] : _bundledPath, old.Version + 1);
        if (old.Experimental && (old.Path + "|||").Split('|')[1] == parts[1])
            return old with { Path = path, Version = version, Amp = parts[2], Cab = parts[3], RigOnly = true, Humanize = human };
        try { return new Choice(null, path, SoundFontMasterVolume, version, _windowsHost, Sf2: new TabForge.Sf2Synth.Sf2.SoundFont(file), Experimental: true) { Amp = parts[2], Cab = parts[3], Humanize = human }; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            error = ex.Message;
            return Fallback(version) with { Path = path, Experimental = true, Amp = parts[2], Cab = parts[3] };
        }
    }

    public Choice Current => _current;

    // Control-thread notification only. Synths prepare replacements here; the render callback only swaps a prepared reference.
    internal event Action<Choice>? Changed;

    private void Publish(Choice choice)
    {
        _current = choice;
        Changed?.Invoke(choice);
    }

    /// <summary>Selects the bank: a .sf2 path, empty for Windows GM, "None" for silence. False (the bank stays as it was) when the file cannot be read.</summary>
    public bool Select(string path, out string error)
    {
        error = "";
        var old = _current;
        if (string.Equals(old.Path, path, StringComparison.OrdinalIgnoreCase)) return true;
        if (path.Length == 0) { Publish(Fallback(old.Version + 1)); return true; }
        if (string.Equals(path, TabForge.Services.SoundEngines.None, StringComparison.Ordinal)) { Publish(new Choice(null, path, 0, old.Version + 1, _windowsHost, Silent: true)); return true; }
        if (path.StartsWith(TabForge.Services.SoundEngines.TabForgeSynthPath, StringComparison.Ordinal)) { Publish(Experimental(path, old, out error)); return true; }
        var font = TryLoad(path, out error);
        if (font is null) return false;
        Publish(new Choice(font, path, SoundFontMasterVolume, old.Version + 1, _windowsHost));
        return true;
    }
}
