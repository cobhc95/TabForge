using TabForge.KeyboardMode;
using TabForge.LiveInput;
using TabForge.Services.Video;

namespace TabForge.Services.Features;

// Owns: the Preferences groups, rows and settings bounds of the four features that add settings rows (Band, Video, Keyboard mode, Live input), as plain data for the catalogues.
//   The WPF feature modules and TabForge both derive from these classes, so both read the same groups, rows and bounds.
// Does not own: the commands and window code (the feature modules), the saved values (the AppSettings sections) or the row builders (SettingsCatalog.*Rows).
// Tests: PreferencesFeatureRowsTests (Avalonia).
/// <summary>The Band view Preferences group on the Timeline page, with its rows.</summary>
public class BandFeatureSettings : IFeatureSettingsSource
{
    public string Name => "Band";

    public IReadOnlyList<FeatureLayout> Layout { get; } = new FeatureLayout[]
    {
        new(SettingsCatalog.Timeline, "Band view", "band.instrumentsize band.lanecontent band.followscore* band.lanelayout* band.lanezoom* band.noteoutline* band.continuous* band.playheadline* band.smoothfollow* band.rowsperscreen* band.fretboardabove* band.colourframe*", "Timeline display"),
    };

    public IEnumerable<SettingDescriptor> SettingRows(AppSettings settings) => SettingsCatalog.BandRows(settings);
}

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

/// <summary>The Keyboard mode group on the Fretboard page, with its rows and settings bounds.</summary>
public class KeyboardModeFeatureSettings : IFeatureSettingsSource
{
    public string Name => "Keyboard mode";

    public IReadOnlyList<FeatureLayout> Layout { get; } = new FeatureLayout[]
    {
        new(SettingsCatalog.Fretboard, "Keyboard mode (experimental)", "learn.lookahead learn.sizekeys* learn.playalong learn.wait* learn.hands learn.timing* learn.notenames* learn.fingers*", "Practice aids"),
    };

    public IEnumerable<SettingDescriptor> SettingRows(AppSettings settings) => SettingsCatalog.KeyboardModeRows(settings);

    public void Normalize(AppSettings settings)
    {
        var value = settings.Learn ??= new KeyboardModeSettings();
        value.LookAheadSeconds = KeyboardModeSettings.NormalizeLookAhead(value.LookAheadSeconds);
        value.KeyboardSize = KeyboardModeSettings.NormalizeSize(value.KeyboardSize, KeyboardModeSettings.SizeFull);
        value.MidiInput = KeyboardModeSettings.NormalizeMidiInput(value.MidiInput);
        value.Hands = KeyboardModeSettings.NormalizeHands(value.Hands);
        value.TimingTolerance = KeyboardModeSettings.NormalizeTiming(value.TimingTolerance);
    }
}

/// <summary>The Live input group on the Audio and plug-ins page, with its row and settings bounds.</summary>
public class LiveInputFeatureSettings : IFeatureSettingsSource
{
    public string Name => "Live input";

    public IReadOnlyList<FeatureLayout> Layout { get; } = new FeatureLayout[]
    {
        new(SettingsCatalog.AudioVst, SettingsCatalog.LiveInputGroup, "live.restore live.tuner.a4 live.tuner.window", "Plug-ins"),
    };

    public IEnumerable<SettingDescriptor> SettingRows(AppSettings settings) => SettingsCatalog.LiveInputRows(settings);

    public void Normalize(AppSettings settings) => (settings.Plugins.LiveInput ??= new LiveInputSettings()).Normalize();
}
