using TabForge.Services;
using TabForge.Services.Features;

namespace TabForge.Features.Video;

/// <summary>The Video recording and Video encoder groups on the General page, with their rows and settings bounds.</summary>
public class VideoFeatureSettings : IFeatureSettingsSource
{
    public string Name => "Video";

    public IReadOnlyList<FeatureLayout> Layout { get; } = new FeatureLayout[]
    {
        new(SettingsCatalog.General, "Video recording", "video.resolution video.fps video.region video.folder", "Window"),
        new(SettingsCatalog.General, "Video encoder (experimental)", "video.encoder video.fastexport video.lowlatency", "Video recording"),
    };

    public IEnumerable<SettingDescriptor> SettingRows(AppSettings settings) => SettingsCatalog.LiveVideoRows(settings);

    public void Normalize(AppSettings settings)
    {
        var value = settings.LiveVideo ??= new LiveVideoSettings();
        value.Resolution = LiveVideoChoices.NormalizeResolution(value.Resolution);
        value.Region = LiveVideoChoices.NormalizeRegion(value.Region);
        value.Fps = LiveVideoChoices.NormalizeFps(value.Fps);
        value.Folder = InputLimits.IsSafeText(value.Folder, 260, allowLineBreaks: false) ? value.Folder.Trim() : "";
        value.Encoder = VideoEncoderChoices.Normalize(value.Encoder);
    }
}
