using System.IO;

namespace TabForge.Services.Video;

// Owns: the live video recording preferences and the names of their choices.
// Does not own: the Preferences rows (SettingsCatalog.Video) or the recording itself (LiveVideoRecordController).
// Tests: TestLiveVideoRecord.
/// <summary>What the Record video button captures and where the MP4 goes.</summary>
public sealed class LiveVideoSettings
{
    /// <summary>Frame size: 1080p or 4K.</summary>
    public string Resolution { get; set; } = LiveVideoChoices.P1080;
    /// <summary>Frames per second: 30, 60 or 120.</summary>
    public int Fps { get; set; } = 60;
    /// <summary>What is recorded: the score, the Band view, the whole window, or the score with the instrument.</summary>
    public string Region { get; set; } = LiveVideoChoices.Window;
    /// <summary>Where the MP4 files go; empty = Videos\TabForge.</summary>
    public string Folder { get; set; } = "";
    /// <summary>Experimental: Auto, Software or Hardware H.264 encoder.</summary>
    public string Encoder { get; set; } = VideoEncoderChoices.Auto;
    /// <summary>Experimental: faster offline export with a lower encoder effort.</summary>
    public bool FastExport { get; set; }
    /// <summary>Experimental: low-latency encoder mode for live recording.</summary>
    public bool LowLatency { get; set; }
}

public static class LiveVideoChoices
{
    public const string P1080 = "1080p", P4K = "4K";
    public const string Score = "Score", Band = "Band view", Window = "Whole window", ScoreAndInstrument = "Score and instrument";

    public static readonly string[] Resolutions = { P1080, P4K };
    public static readonly string[] Regions = { Score, Band, Window, ScoreAndInstrument };
    public static readonly string[] FpsLabels = { "30 fps", "60 fps (may drop frames at 4K)", "120 fps (may drop frames at 4K)" };

    public static string NormalizeResolution(string? v) => v == P4K ? P4K : P1080;
    public static string NormalizeRegion(string? v) => Regions.FirstOrDefault(r => r == v) ?? Window;
    public static int NormalizeFps(int v) => v is 30 or 120 ? v : 60;
    public static string FpsLabel(int fps) => FpsLabels.First(l => FpsOf(l) == NormalizeFps(fps));
    public static int FpsOf(string? label) => int.TryParse(label?.Split(' ')[0], out var n) ? NormalizeFps(n) : 60;

    /// <summary>The frame size: landscape, or portrait (1080 x 1920, 2160 x 3840) for a region taller than wide, so it fills the frame.</summary>
    public static (int Width, int Height) SizeOf(string? resolution, ScreenRect? region = null)
    {
        var (w, h) = NormalizeResolution(resolution) == P4K ? (3840, 2160) : (1920, 1080);
        return region is { } r && r.Height > r.Width ? (h, w) : (w, h);
    }

    /// <summary>The folder for new MP4 files: the setting when it is an absolute path, otherwise Videos\TabForge.</summary>
    public static string FolderOf(LiveVideoSettings s) =>
        !string.IsNullOrWhiteSpace(s.Folder) && Path.IsPathFullyQualified(s.Folder) ? s.Folder
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "TabForge");
}
