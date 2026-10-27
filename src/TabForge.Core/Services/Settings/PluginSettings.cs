using System.Text.Json.Serialization;

namespace TabForge.Services;

// Owns: the audio output, recording input and plug-in hosting preferences data, the known-plug-in records and the driver names.
// Does not own: loading, saving and validating (AppSettingsStore, SettingsValidator) or the Preferences rows (SettingsCatalog).
// Tests: TestSettingsFileSplitSnapshots.
/// <summary>Audio engine (plug-in playback) and plug-in folders. Windows MIDI playback does not use these.</summary>
public sealed class PluginSettings
{
    /// <summary>"WASAPI (shared)", "WASAPI (exclusive)", "ASIO" or "DirectSound".</summary>
    public string Driver { get; set; } = AudioDrivers.Default;
    /// <summary>Audio input (recording) device name; empty = the Windows default recording device.</summary>
    public string InputDevice { get; set; } = "";
    /// <summary>Output device name; empty = the Windows default device.</summary>
    public string Device { get; set; } = "";
    /// <summary>Output sample rate of the current output (driver + device) in Hz; 0 = the device default (see <see cref="OutputRates"/>).</summary>
    public int SampleRate { get; set; } = 48000;
    /// <summary>ASIO ignores the Windows volume; on: the Windows master volume and mute also scale TabForge's audio (off by default).</summary>
    public bool FollowWindowsVolume { get; set; } = true;
    /// <summary>Master safety limiter on live playback (-0.3 dBFS, 1.5 ms lookahead; adds that much delay). Off by default: renders have their own switch (on).</summary>
    public bool LiveLimiter { get; set; }
    /// <summary>Play every track through the audio engine (its General MIDI synth) so the song uses the chosen audio driver. Off: tracks without plug-ins use Windows MIDI. Older files stored null (auto): read as on.</summary>
    [JsonConverter(typeof(NullIsTrueBoolConverter))]
    public bool PlayAllThroughEngine { get; set; } = true;
    /// <summary>The General MIDI sound engine: "Windows GM", "TabForge SoundFont", or empty for the default of this operating system (see <see cref="SoundEngines"/>).</summary>
    public string SoundEngine { get; set; } = "";
    /// <summary>Retired (kept so old files load): the engine is on by default and no rule switches it on or off any more.</summary>
    public bool AutoPlayThroughEngine { get; set; } = true;
    /// <summary>True while "play the whole song through the engine" is on because the automatic rule turned it on (not the user): only then does removing the last plug-in (without ASIO) turn it off again.</summary>
    public bool PlayAllSetAutomatically { get; set; }
    /// <summary>App-wide monitoring effects chain (speaker / room calibration), used by every song that keeps "Use for all projects" ticked. Live output only: never rendered or exported.</summary>
    public TabForge.Models.BusChain MonitorFx { get; set; } = new();
    /// <summary>Live input (hear an input through the app-wide live FX chain): its chain, input and options. See LiveInput/README.md.</summary>
    public TabForge.LiveInput.LiveInputSettings LiveInput { get; set; } = new();
    /// <summary>Tick a track's GM sound automatically when no VST instrument plays it (chain off, instrument bypassed / removed), untick when one does; manual unticks are respected.</summary>
    public bool AutoGmSound { get; set; } = true;
    /// <summary>Output latency of the Windows MIDI synth (ms); plug-in tracks are delayed by the difference so both stay in time.</summary>
    public int WindowsMidiLatencyMs { get; set; } = 200;
    /// <summary>1 once the old guessed default of 60 ms has been moved to the measured 200 ms (a stored 60 in an older file is that guess; any other value is the user's own).</summary>
    public int WindowsMidiLatencyVersion { get; set; } = 1;
    /// <summary>
    /// Recording offset in ms (a manual input offset), on top of the input latency the device reports: positive moves takes
    /// earlier (use it when recordings sound late), negative later. -1000..1000; 0 by default.
    /// </summary>
    public int RecordingOffsetMs { get; set; }
    /// <summary>Recording over a MIDI clip: false = the take gets a new lane (default); true = its notes merge into the clip underneath.</summary>
    public bool MergeMidiTakes { get; set; }
    /// <summary>Snap recorded MIDI note starts to the current snap grid when the take lands (off keeps the raw timing).</summary>
    public bool QuantizeMidiInput { get; set; }
    /// <summary>Which tracks play the MIDI keyboard through their own instrument: one of <see cref="TabForge.LiveInput.MidiMonitorModes"/> (armed tracks + the selected track by default).</summary>
    public string MonitorMidiInput { get; set; } = TabForge.LiveInput.MidiMonitorModes.ArmedAndSelected;
    /// <summary>ASIO: use the driver's inputs (recording and monitoring).</summary>
    public bool AsioInputsEnabled { get; set; } = true;
    /// <summary>ASIO: first input channel (0-based).</summary>
    public int AsioInputChannel { get; set; }
    /// <summary>ASIO: last input channel (0-based; equal to the first = one mono input, as a guitar on input 2).</summary>
    /// <summary>Equal to (or below) <see cref="AsioInputChannel"/>: the ASIO input is that one channel, heard on inputs 1 and 2. Above it: every channel from the first on is open.</summary>
    public int AsioInputLastChannel { get; set; } = 1;
    /// <summary>On: every input of the audio device is opened. Off: two input channels, as a device with two inputs.</summary>
    public bool UseAllInputs { get; set; } = true;
    /// <summary>Fade the sound out before the audio device or its settings change, and back in afterwards.</summary>
    public bool ProtectFromPops { get; set; } = true;
    /// <summary>Per audio device name: the inputs its track pickers and the live input menu leave out. No entry: every input shows.</summary>
    public Dictionary<string, TabForge.Models.HiddenAudioInputs> HiddenInputs { get; set; } = new();
    /// <summary>ASIO: first output channel (0-based).</summary>
    public int AsioOutputChannel { get; set; }
    /// <summary>ASIO: last output channel (0-based; equal to the first = one mono output, the mix summed to it).</summary>
    public int AsioOutputLastChannel { get; set; } = 1;
    /// <summary>Buffer size in samples per channel, of the current output (driver + device).</summary>
    public int BufferSize { get; set; } = AudioDrivers.DefaultBuffer;
    /// <summary>The buffer size and sample rate each other output (driver + device, see <see cref="OutputKey"/>) was last used with; the current output's are <see cref="BufferSize"/> and <see cref="SampleRate"/>.</summary>
    public Dictionary<string, OutputProfile> OutputProfiles { get; set; } = new();
    /// <summary>1 once the current output's own entry in <see cref="OutputProfiles"/> exists: a file without it is seeded once from the single <see cref="BufferSize"/> and <see cref="SampleRate"/> (see SettingsMigration).</summary>
    public int OutputProfilesVersion { get; set; } = 1;

    /// <summary>The dictionary key of an output: its driver and device name (empty device = the Windows default).</summary>
    public static string OutputKey(string driver, string device) => driver + "|" + device;

    /// <summary>
    /// Switches to another output: the current buffer size and sample rate are kept for it, and the new output gets the ones it was last used
    /// with, or (never used) <paramref name="newBufferSize"/> and the device default rate. Nothing changes when the output is the same.
    /// </summary>
    public void SelectOutput(string driver, string device, Func<int> newBufferSize)
    {
        if (driver == Driver && device == Device) return;
        OutputProfiles[OutputKey(Driver, Device)] = new OutputProfile { BufferSize = BufferSize, SampleRate = SampleRate };
        Driver = driver; Device = device;
        if (OutputProfiles.TryGetValue(OutputKey(driver, device), out var known)) { BufferSize = known.BufferSize; SampleRate = known.SampleRate; }
        else { BufferSize = newBufferSize(); SampleRate = 0; }
    }
    /// <summary>Folders the user added (browsed to) that hold plug-ins.</summary>
    public List<string> Folders { get; set; } = new();
    /// <summary>Plug-ins installed under Program Files (where only an administrator can write) load without asking; off: every plug-in needs a scan or an approval first.</summary>
    public bool AllowInstalledPlugins { get; set; } = true;
    /// <summary>Also scan the standard VST folders (off: only the folders above, as in Equalizer APO).</summary>
    public bool ScanStandardFolders { get; set; }
    /// <summary>Also list CLAP plug-ins (.clap) in the scan and offer them in the plug-in browser. Off by default: CLAP is a newer format and loading it is opt-in.</summary>
    public bool ScanClap { get; set; }
    /// <summary>The "common folders" the Add plug-in window's "Scan common folders" (and the standard-folders option) scan; may use %ENV% variables. Empty = the built-in list (<see cref="TabForge.Plugins.VstScannerService.DefaultCommonFolders"/>).</summary>
    public List<string> CommonFolders { get; set; } = new();
    /// <summary>Run each plug-in in its own process (safest, uses more CPU and memory). Off: one shared engine.</summary>
    public bool SeparateProcessPerPlugin { get; set; }
    /// <summary>Plug-ins switched off after they crashed (full paths).</summary>
    public List<string> Quarantined { get; set; } = new();
    /// <summary>Plug-in paths the user explicitly approved (full normalised paths; uncapped). Each needs its record in <see cref="TrustRecords"/> to be trusted.</summary>
    public List<string> ApprovedPluginPaths { get; set; } = new();
    /// <summary>Scan the plug-in folders once and remember the list (Rescan updates it); the default. Off: scan each time.</summary>
    public bool RememberScan { get; set; } = true;
    /// <summary>The wording of the <see cref="RememberScan"/> option in Preferences and in the Add plug-in window.</summary>
    public const string ScanClapLabel = "Also list CLAP plug-ins (.clap)";
    public const string RememberScanLabel = "Remember plug-ins (load from the saved list, no scan each time)";
    /// <summary>1 once <see cref="RememberScan"/> defaults on: a file without it stored the old off default, which is moved once; a choice made afterwards is saved with the version and kept.</summary>
    public int RememberScanVersion { get; set; } = 1;
    /// <summary>The remembered plug-in list (when <see cref="RememberScan"/> is on).</summary>
    public List<KnownPlugin> ScanCache { get; set; } = new();
    /// <summary>Result of the most recent completed scan in this run, so the arrangement view can offer VST instruments. Not saved.</summary>
    [JsonIgnore]
    public IReadOnlyList<TabForge.Plugins.VstPluginInfo> LastScan { get; set; } = Array.Empty<TabForge.Plugins.VstPluginInfo>();
    /// <summary>Roles / vendors found by probing plug-ins, kept so each is probed once.</summary>
    public List<KnownPlugin> Probed { get; set; } = new();
    /// <summary>
    /// Fingerprints of scanned and approved plug-ins in user-writable locations (see <see cref="TabForge.Plugins.PluginTrust"/>). Capped at 1024 for scan-only
    /// records; an approved path's record is always kept. A file that changed since is only trusted again when it has the same Authenticode signer.
    /// </summary>
    public List<PluginFingerprint> TrustRecords { get; set; } = new();
    /// <summary>Show the plug-in's window inside the FX chain window (on) or floating on its own.</summary>
    public bool DockPluginWindows { get; set; } = true;
    /// <summary>Width of the plug-in list in the FX chain window (the divider beside it is dragged).</summary>
    public double FxListWidth { get; set; } = 290;
    /// <summary>The tracks whose FX window Tuner switch the user turned off (on by default); a window preference, not part of the song.</summary>
    public List<Guid> TunerOffTracks { get; set; } = new();
    /// <summary>Floating plug-in windows stay above other windows.</summary>
    public bool PluginWindowsOnTop { get; set; }
    /// <summary>Default FX chains per instrument type, applied to tracks of that type ("Auto-load for this instrument").</summary>
    public List<TabForge.Plugins.AutoChain> AutoChains { get; set; } = new();
    /// <summary>Chains added as a (never armed) track to every song that is opened or created ("Add as a track on startup").</summary>
    public List<TabForge.Plugins.StartupTrack> StartupTracks { get; set; } = new();
    /// <summary>Default for every chain: measure each VST instrument's sounding octave and transpose it to match the notes (per chain override in the FX window).</summary>
    public bool AutoPitchMatch { get; set; } = true;
}

/// <summary>What one output (driver + device) runs at: buffer size in samples and sample rate in Hz (0 = device default).</summary>
public sealed class OutputProfile
{
    public int BufferSize { get; set; } = AudioDrivers.DefaultBuffer;
    public int SampleRate { get; set; }
}

public sealed class KnownPlugin
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string Format { get; set; } = "";
    public string Vendor { get; set; } = "";
    public string Role { get; set; } = "";
    public string ClassId { get; set; } = "";
}

/// <summary>What a plug-in file looked like when the user scanned or approved it (full normalised plug-in path; for a VST3 bundle the size, time and hash are of its x64 binary).</summary>
public sealed class PluginFingerprint
{
    public string Path { get; set; } = "";
    public long Size { get; set; }
    public long LastWriteUtcTicks { get; set; }
    /// <summary>Hex SHA-256 of the binary; empty = the file did not exist when approved (any file appearing there counts as changed).</summary>
    public string Sha256 { get; set; } = "";
    /// <summary>Subject of the valid Authenticode signer (WinVerifyTrust), empty when unsigned or the signature does not verify.</summary>
    public string Signer { get; set; } = "";
    /// <summary>Recorded by a scan (a baseline only: the plug-in still needs approval); false = recorded by an explicit approval.</summary>
    public bool Scanned { get; set; }
}

public static class AudioDrivers
{
    public const string WasapiShared = Audio.Contracts.AudioDriverNames.WasapiShared;
    public const string WasapiExclusive = Audio.Contracts.AudioDriverNames.WasapiExclusive;
    public const string Asio = Audio.Contracts.AudioDriverNames.Asio;
    public const string DirectSound = Audio.Contracts.AudioDriverNames.DirectSound;
    public const string Miniaudio = Audio.Contracts.AudioDriverNames.Miniaudio;
    /// <summary>The driver a new setup starts with: WASAPI shared on Windows, miniaudio elsewhere.</summary>
    public static string Default => Audio.Contracts.AudioDriverNames.Default;
    public static readonly string[] All = Audio.Contracts.AudioDriverNames.All;
    /// <summary>The rates a driver is asked about (see <see cref="OutputRates"/>); only the ones it supports are offered.</summary>
    public static readonly int[] SampleRates = Audio.Contracts.AudioDriverNames.SampleRates.ToArray();
    /// <summary>The buffer size a new output starts with (an ASIO driver starts with its own preferred size).</summary>
    public const int DefaultBuffer = 256;
    /// <summary>Common buffer sizes offered in the list; any value from <see cref="MinBuffer"/> to <see cref="MaxBuffer"/> can be typed.</summary>
    public static readonly int[] BufferSizes = { 16, 32, 48, 64, 96, 128, 192, 256, 384, 512, 768, 1024, 2048, 4096, 8192 };
    public const int MinBuffer = 16, MaxBuffer = 8192;
}
