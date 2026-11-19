using TabForge.Services;
using TabForge.Services.Features;

namespace TabForge.Features.Band;

/// <summary>The Band view Preferences group on the Timeline page, with its rows.</summary>
public class BandFeatureSettings : IFeatureSettingsSource
{
    public string Name => "Band";

    public IReadOnlyList<FeatureLayout> Layout { get; } = new FeatureLayout[]
    {
        new(SettingsCatalog.Timeline, "Band view", "band.instrumentsize band.lanecontent band.followscore* band.lanelayout* band.lanezoom* band.noteoutline* band.continuous* band.playheadline* band.rowminheight* band.smoothfollow* band.rowsperscreen* band.fretboardabove* band.colourframe*", "Timeline display"),
    };

    public IEnumerable<SettingDescriptor> SettingRows(AppSettings settings) => SettingsCatalog.BandRows(settings);
}
