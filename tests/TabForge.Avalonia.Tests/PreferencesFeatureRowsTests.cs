using System;
using System.Linq;
using TabForge.Avalonia.Views;
using TabForge.Services;
using TabForge.Services.Features;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// The Avalonia Preferences catalogue carries the feature rows (Band, Video, Keyboard mode, Live input) by name and count, as the WPF catalogue does.
/// Owns: the check that the feature sources reach the preview's catalogue.
/// Does not own: the rows themselves (FeatureSettingsRows.cs in Core) or the controls (PreferencesWindow).
/// Tests: this class.
/// </summary>
public class PreferencesFeatureRowsTests
{
    /// <summary>The catalogue holds exactly the rows of the four feature sources, by name and count.</summary>
    [Fact]
    public void CatalogueCarriesTheFeatureRowsOfEveryFeatureSource()
    {
        var settings = new AppSettings();
        var featureKeys = new IFeatureSettingsSource[] { new BandFeatureSettings(), new VideoFeatureSettings(), new KeyboardModeFeatureSettings(), new LiveInputFeatureSettings() }
            .SelectMany(source => source.SettingRows(settings).Select(row => row.Key))
            .ToList();
        var catalogueKeys = PreferencesCatalog.Build(settings).Select(row => row.Key).Where(featureKeys.Contains).ToList();

        Assert.NotEmpty(featureKeys);
        Assert.Equal(featureKeys.OrderBy(key => key, StringComparer.Ordinal), catalogueKeys.OrderBy(key => key, StringComparer.Ordinal));
    }
}
