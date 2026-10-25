using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Models;
using TabForge.Platform.Theme;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the badge reader on bundled files, the badge control, the picker tiles and the track properties cover.
/// Does not own: the reader, the catalogue or the windows. Tests: this class.
/// </summary>
public class InstrumentBadgeTests
{
    [AvaloniaFact]
    public void BundledBadgesReadWithBackgroundAndArt()
    {
        var entries = new[] { "Grand Piano", "Distortion Guitar", "Electric Bass (Pick)", "Drum Kit (Standard)", "Strings" }.Select(InstrumentCatalog.Find).ToList();
        Assert.All(entries, e => Assert.NotNull(e?.SvgPath));
        foreach (var entry in entries)
        {
            var badge = InstrumentBadge.Read(entry!.SvgPath!);
            Assert.NotNull(badge?.Background);
            Assert.NotEmpty(badge!.Art);
        }
    }

    [AvaloniaFact]
    public void PickerTilesAndTrackCoverShowTheBadge()
    {
        var picker = new InstrumentPickerWindow(InstrumentPickerWindow.SampleEntries, "Distortion Guitar");
        picker.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.All(picker.GetVisualDescendants().OfType<InstrumentBadgeView>(), b => Assert.True(b.HasArt));
        Assert.Equal(InstrumentPickerWindow.SampleEntries.Count, picker.GetVisualDescendants().OfType<InstrumentBadgeView>().Count());
        picker.Close();

        var window = new TrackPropertiesWindow(DemoSongFactory.Create().Tracks[0]);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var cover = window.GetVisualDescendants().OfType<InstrumentBadgeView>().First(b => b.Name == "InstrumentBadge");
        Assert.True(cover.IsVisible && cover.HasArt);
        Assert.NotNull(window.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Name == "InstrumentChooserButton"));
        window.Close();
    }
}
