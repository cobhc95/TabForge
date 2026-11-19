using TabForge.Services;
using TabForge.Services.Features;

namespace TabForge.Features.KeyboardMode;

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
