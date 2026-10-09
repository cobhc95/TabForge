using System.Text.Json.Serialization;
using TabForge.Docking;
using TabForge.Documents;

namespace TabForge.Services;

// Owns: the audio output, recording input and plug-in hosting preferences data, the known-plug-in records and the driver names.
// Does not own: loading, saving and validating (AppSettingsStore, SettingsValidator) or the Preferences rows (SettingsCatalog).
// Tests: TestPreferencesCatalog, TestSettingsFileSplitSnapshots.
/// <summary>Audio engine (plug-in playback) and plug-in folders. Windows MIDI playback does not use these.</summary>
public sealed class PluginSettings
{
    /// <summary>"WASAPI (shared)", "WASAPI (exclusive)", "ASIO" or "DirectSound".</summary>
    public string Driver { get; set; } = AudioDrivers.WasapiShared;
    /// <summary>Audio input (recording) device name; empty = the Windows default recording device.</summary>
    public string InputDevice { get; set; } = "";
    /// <summary>Output device name; empty = the Windows default device.</summary>
    public string Device { get; set; } = "";
    public int SampleRate { get; set; } = 48000;
    /// <summary>ASIO ignores the Windows volume; on: the Windows master volume and mute also scale TabForge's audio (off by default).</summary>
    public bool FollowWindowsVolume { get; set; } = true;
    /// <summary>Master safety limiter on live playback (-0.3 dBFS, 1.5 ms lookahead; adds that much delay). Off by default: renders have their own switch (on).</summary>
    public bool LiveLimiter { get; set; }
    /// <summary>Play every track through the audio engine (its General MIDI synth) so the song uses the chosen audio driver. Off: tracks without plug-ins use Windows MIDI. Older files stored null (auto): read as on.</summary>
    [JsonConverter(typeof(NullIsTrueBoolConverter))]
    public bool PlayAllThroughEngine { get; set; } = true;
    /// <summary>Retired (kept so old files load): the engine is on by default and no rule switches it on or off any more.</summary>
    public bool AutoPlayThroughEngine { get; set; } = true;
    /// <summary>True while "play the whole song through the engine" is on because the automatic rule turned it on (not the user): only then does removing the last plug-in (without ASIO) turn it off again.</summary>
    public bool PlayAllSetAutomatically { get; set; }
    /// <summary>App-wide monitoring effects chain (speaker / room calibration), used by every song that keeps "Use for all projects" ticked. Live output only: never rendered or exported.</summary>
    public TabForge.Models.BusChain MonitorFx { get; set; } = new();
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
    /// <summary>ASIO: use the driver's inputs (recording and monitoring).</summary>
    public bool AsioInputsEnabled { get; set; } = true;
    /// <summary>ASIO: first input channel (0-based).</summary>
    public int AsioInputChannel { get; set; }
    /// <summary>ASIO: last input channel (0-based; equal to the first = one mono input, as a guitar on input 2).</summary>
    public int AsioInputLastChannel { get; set; } = 1;
    /// <summary>ASIO: first output channel (0-based).</summary>
    public int AsioOutputChannel { get; set; }
    /// <summary>ASIO: last output channel (0-based; equal to the first = one mono output, the mix summed to it).</summary>
    public int AsioOutputLastChannel { get; set; } = 1;
    /// <summary>Buffer size in samples per channel.</summary>
    public int BufferSize { get; set; } = 256;
    /// <summary>Folders the user added (browsed to) that hold plug-ins.</summary>
    public List<string> Folders { get; set; } = new();
    /// <summary>Also scan the standard VST folders (off: only the folders above, as in Equalizer APO).</summary>
    public bool ScanStandardFolders { get; set; }
    /// <summary>The "common folders" the Add plug-in window's "Scan common folders" (and the standard-folders option) scan; may use %ENV% variables. Empty = the built-in list (<see cref="TabForge.Plugins.VstScannerService.DefaultCommonFolders"/>).</summary>
    public List<string> CommonFolders { get; set; } = new();
    /// <summary>Run each plug-in in its own process (safest, uses more CPU and memory). Off: one shared engine.</summary>
    public bool SeparateProcessPerPlugin { get; set; }
    /// <summary>Plug-ins switched off after they crashed (full paths).</summary>
    public List<string> Quarantined { get; set; } = new();
    /// <summary>Plug-in paths the user explicitly approved although they are not from their scan (full normalised paths; UNC/removable need this).</summary>
    public List<string> ApprovedPluginPaths { get; set; } = new();
    /// <summary>Opt-in: scan the plug-in folders once and remember the list (Rescan updates it). Off: scan each time.</summary>
    public bool RememberScan { get; set; }
    /// <summary>The remembered plug-in list (when <see cref="RememberScan"/> is on).</summary>
    public List<KnownPlugin> ScanCache { get; set; } = new();
    /// <summary>Result of the most recent completed scan in this run, so the arrangement view can offer VST instruments. Not saved.</summary>
    [JsonIgnore]
    public IReadOnlyList<TabForge.Plugins.VstPluginInfo> LastScan { get; set; } = Array.Empty<TabForge.Plugins.VstPluginInfo>();
    /// <summary>Roles / vendors found by probing plug-ins, kept so each is probed once.</summary>
    public List<KnownPlugin> Probed { get; set; } = new();
    /// <summary>Fingerprints of scanned and approved plug-ins in user-writable locations (see <see cref="TabForge.Plugins.PluginTrust"/>): a file that changed since is only trusted again when it has the same Authenticode signer.</summary>
    public List<PluginFingerprint> TrustRecords { get; set; } = new();
    /// <summary>Show the plug-in's window inside the FX chain window (on) or floating on its own.</summary>
    public bool DockPluginWindows { get; set; } = true;
    /// <summary>Floating plug-in windows stay above other windows.</summary>
    public bool PluginWindowsOnTop { get; set; }
    /// <summary>Default FX chains per instrument type, applied to tracks of that type ("Auto-load for this instrument").</summary>
    public List<TabForge.Plugins.AutoChain> AutoChains { get; set; } = new();
    /// <summary>Chains added as a (never armed) track to every song that is opened or created ("Add as a track on startup").</summary>
    public List<TabForge.Plugins.StartupTrack> StartupTracks { get; set; } = new();
    /// <summary>Default for every chain: measure each VST instrument's sounding octave and transpose it to match the notes (per chain override in the FX window).</summary>
    public bool AutoPitchMatch { get; set; } = true;
}

public sealed class KnownPlugin
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string Format { get; set; } = "";
    public string Vendor { get; set; } = "";
    public string Role { get; set; } = "";
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
    /// <summary>Recorded by a scan (trusted while the file is unchanged); false = recorded by an explicit approval only.</summary>
    public bool Scanned { get; set; }
}

public static class AudioDrivers
{
    public const string WasapiShared = Audio.Contracts.AudioDriverNames.WasapiShared;
    public const string WasapiExclusive = Audio.Contracts.AudioDriverNames.WasapiExclusive;
    public const string Asio = Audio.Contracts.AudioDriverNames.Asio;
    public const string DirectSound = Audio.Contracts.AudioDriverNames.DirectSound;
    public static readonly string[] All = Audio.Contracts.AudioDriverNames.All;
    public static readonly int[] SampleRates = { 44100, 48000, 88200, 96000 };
    /// <summary>Common buffer sizes offered in the list; any value from <see cref="MinBuffer"/> to <see cref="MaxBuffer"/> can be typed.</summary>
    public static readonly int[] BufferSizes = { 16, 32, 48, 64, 96, 128, 192, 256, 384, 512, 768, 1024, 2048, 4096, 8192 };
    public const int MinBuffer = 16, MaxBuffer = 8192;
}
