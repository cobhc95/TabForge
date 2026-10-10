namespace TabForge.Services.Video;

// Owns: the experimental encoder choices (encoder kind, fast export, low latency) and their mapping to codec property values.
// Does not own: writing the file (VideoEncoder), the stored values (LiveVideoSettings) or the Preferences rows (VideoSettingsRows).
// Tests: TestVideoEncoderOptions, TestVideoEncoderGolden.
/// <summary>Opt-in encoder settings. The default (Auto, both off) leaves the encoder exactly as it is without options.</summary>
public sealed record VideoEncoderOptions(string Encoder = VideoEncoderChoices.Auto, bool FastExport = false, bool LowLatency = false)
{
    public static VideoEncoderOptions From(LiveVideoSettings s) => new(VideoEncoderChoices.Normalize(s.Encoder), s.FastExport, s.LowLatency);

    /// <summary>The codec properties (CODECAPI guid, value) to ask the encoder for; fast export applies offline only, low latency live only.</summary>
    internal IReadOnlyList<(string Name, Guid Key, int Value)> Properties(bool live) => Extra.Count == 0 ? Chosen(live) : Chosen(live).Concat(Extra).ToArray();

    /// <summary>A test hook: the first hardware start throws, as a failing driver would.</summary>
    internal bool SimulateHardwareFailure { get; init; }

    /// <summary>Extra properties asked for in every mode (a test hook for a property an encoder rejects).</summary>
    internal IReadOnlyList<(string Name, Guid Key, int Value)> Extra { get; init; } = Array.Empty<(string, Guid, int)>();

    private IReadOnlyList<(string Name, Guid Key, int Value)> Chosen(bool live)
    {
        if (live && LowLatency) return LowLatencySet;
        if (!live && FastExport) return FastExportSet;
        return Array.Empty<(string, Guid, int)>();
    }

    internal static readonly Guid LowLatencyMode = new("9c27891a-ed7a-40e1-88e8-b22727a024ee");   // also MF_LOW_LATENCY
    private static readonly Guid QualityVsSpeed = new("98332df8-03cd-476b-89fa-3f9e442dec9f");
    private static readonly Guid BPictureCount = new("8d390aac-dc5c-4200-b57f-814d04babab2");
    private static readonly Guid RateControlMode = new("1c0608e9-370c-4710-8a58-cb6181c42423");   // 4 = low-delay VBR
    internal static readonly (string Name, Guid Key, int Value)[] LowLatencySet = { ("LowLatencyMode", LowLatencyMode, 1), ("BPictureCount", BPictureCount, 0), ("RateControlMode", RateControlMode, 4) };
    internal static readonly (string Name, Guid Key, int Value)[] FastExportSet = { ("QualityVsSpeed", QualityVsSpeed, 0), ("BPictureCount", BPictureCount, 0) };
}

// Owns: the names of the encoder choices and their normalising.
// Does not own: the stored setting (LiveVideoSettings) or the encoder (VideoEncoder).
// Tests: TestVideoEncoderOptions.
public static class VideoEncoderChoices
{
    public const string Auto = "Auto", Software = "Software", Hardware = "Hardware";
    public static readonly string[] All = { Auto, Software, Hardware };
    public static string Normalize(string? v) => All.FirstOrDefault(c => c == v) ?? Auto;
}
