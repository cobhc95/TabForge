using TabForge.Services;
using TabForge.Services.Features;

namespace TabForge.Features.LiveInput;

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
