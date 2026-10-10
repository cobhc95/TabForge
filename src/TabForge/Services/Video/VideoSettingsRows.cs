using TabForge.Services.Video;

namespace TabForge.Services;

// Owns: the Video recording rows of the settings catalogue (General page); VideoFeatureModule hands them to the catalogue.
// Does not own: page layout (SettingsCatalog.Layout) or the stored values (AppSettings.Video).
// Tests: TestLiveVideoRecord, TestPreferencesCatalog.
public static partial class SettingsCatalog
{
    internal static List<SettingDescriptor> LiveVideoRows(AppSettings s)
    {
        var video = s.LiveVideo ??= new LiveVideoSettings();
        return new List<SettingDescriptor>
        {
            Choice(General, "Video recording", "video.resolution", "Resolution", v => video.Resolution = LiveVideoChoices.NormalizeResolution(v), () => LiveVideoChoices.NormalizeResolution(video.Resolution),
                LiveVideoChoices.Resolutions, "Frame size of the MP4 the Record video button makes: 1080p (1920 x 1080) or 4K (3840 x 2160). The picture is scaled to fit with black bars.",
                "video record mp4 resolution 1080p 4k size"),
            Choice(General, "Video recording", "video.fps", "Frame rate", v => video.Fps = LiveVideoChoices.FpsOf(v), () => LiveVideoChoices.FpsLabel(video.Fps),
                LiveVideoChoices.FpsLabels, "Frames per second of the recording. Higher is smoother and makes bigger files.", "video record mp4 fps frame rate 30 60 120"),
            Choice(General, "Video recording", "video.region", "What to record", v => video.Region = LiveVideoChoices.NormalizeRegion(v), () => LiveVideoChoices.NormalizeRegion(video.Region),
                LiveVideoChoices.Regions, "The part of the screen the recording shows: the score, the Band view, the whole window, or the score with the instrument below it. What is on screen is what is recorded.",
                "video record region score band window instrument capture"),
            Text(General, "Video recording", "video.folder", "Save folder", v => video.Folder = v.Trim(), () => video.Folder,
                "Where the recordings are saved. Empty: the TabForge folder inside your Videos folder.", "video record folder save location path videos"),
            Choice(General, "Video encoder (experimental)", "video.encoder", "Encoder (experimental)", v => video.Encoder = VideoEncoderChoices.Normalize(v), () => VideoEncoderChoices.Normalize(video.Encoder),
                VideoEncoderChoices.All, "Which H.264 encoder writes the MP4. Auto uses a hardware encoder when the PC has one (the default behaviour); Software and Hardware force the choice. A hardware encoder that fails to start falls back to software when exporting.",
                "video encoder hardware software h264 gpu experimental"),
            Bool(General, "Video encoder (experimental)", "video.fastexport", "Fast export (experimental)", v => video.FastExport = v, () => video.FastExport,
                "Export video faster with a lower encoder effort. The picture can look slightly different and the file slightly bigger. Off by default: on the test PC it gave no measurable gain (see docs/VIDEO_ENCODER_OPTIONS.md).", "video export fast speed quality experimental"),
            Bool(General, "Video encoder (experimental)", "video.lowlatency", "Low latency recording (experimental)", v => video.LowLatency = v, () => video.LowLatency,
                "Ask the encoder for its low-latency mode while recording live. Off by default: on the test PC it made the hardware encoder slower (see docs/VIDEO_ENCODER_OPTIONS.md).", "video record low latency live encoder experimental"),
        };
    }
}
