using TabForge.Audio.Metering;

namespace TabForge.Services.Features;

// Owns: the Master meter group of the Preferences layout (Audio & Plug-ins page) and its settings bounds, as plain data for the catalogues.
// Does not own: the rows (SettingsCatalog.MasterMeterRows) or the stored values (AppSettings.MasterMeter).
// Tests: TabForge.Avalonia.Tests.MasterMeterTests.
/// <summary>The Master meter group on the Audio and plug-ins page, with its rows and settings bounds.</summary>
public class MasterMeterFeatureSettings : IFeatureSettingsSource
{
    public string Name => "Master meter";

    public IReadOnlyList<FeatureLayout> Layout { get; } = new FeatureLayout[]
    {
        new(SettingsCatalog.AudioVst, SettingsCatalog.MasterMeterGroup, "meter.mode meter.number* meter.hold*", "Playback engine"),
    };

    public IEnumerable<SettingDescriptor> SettingRows(AppSettings settings) => SettingsCatalog.MasterMeterRows(settings);

    public void Normalize(AppSettings settings) => (settings.MasterMeter ??= new MasterMeterSettings()).Normalize();
}
