using TabForge.Services.Features;

namespace TabForge.Services.Video;

/// <summary>What the main window offers the Video commands.</summary>
public interface IVideoCommandHost : IFeatureHost
{
    void ExportVideo();
    void ToggleVideoRecording();
}

// Owns: everything Video adds to the central tables: the "Video recording" and "Video encoder (experimental)" Preferences rows and their groups, the two hotkey rows
//   (File.ExportVideo, Transport.RecordVideo), their commands, the File and Sound menu entries and the settings bounds.
// Does not own: the recording and export code, the handlers (MainWindow.VideoRecord.cs) or the saved class (LiveVideoSettings).
// Tests: TestFeatureModuleContributions, TestLiveVideoRecord, TestMainMenuTreeGolden.
public sealed class VideoFeatureModule : IFeatureModule
{
    public string Name => "Video";

    public IReadOnlyList<FeatureHotkey> Hotkeys { get; } = new FeatureHotkey[]
    {
        new(new("File.ExportVideo", HotkeyCatalog.CategoryFile, "Export video (MP4)", "", "Export the song as an MP4 video of the score, its instrument and the Band view, with the full audio mix, in 1080p or 4K at 30, 60 or 120 frames per second."), "File.Render"),
        new(new("Transport.RecordVideo", HotkeyCatalog.CategoryTransport, "Record video", "Ctrl+Alt+V", "Play the song and record the screen and the sound to an MP4 (press again, or Stop, to finish)."), "Transport.Record"),
    };

    public IReadOnlyList<FeatureMenuRow> MenuRows { get; } = new FeatureMenuRow[]
    {
        new("_File", "File.ExportMidi", "Export _video (MP4)…", "File.ExportVideo"),
        new("_Sound", "Transport.PlayFromStart", "Record _video", "Transport.RecordVideo"),
    };

    public IReadOnlyList<FeatureLayout> Layout { get; } = new FeatureLayout[]
    {
        new(SettingsCatalog.General, "Video recording", "video.resolution video.fps video.region video.folder", "Window"),
        new(SettingsCatalog.General, "Video encoder (experimental)", "video.encoder video.fastexport video.lowlatency", "Video recording"),
    };

    public IEnumerable<SettingDescriptor> SettingRows(AppSettings settings) => SettingsCatalog.LiveVideoRows(settings);

    public IEnumerable<FeatureCommand> Commands(IFeatureHost host)
    {
        var video = host as IVideoCommandHost ?? throw new InvalidOperationException("The host does not implement IVideoCommandHost.");
        yield return new("File.ExportVideo", video.ExportVideo);
        yield return new("Transport.RecordVideo", video.ToggleVideoRecording);
    }

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
