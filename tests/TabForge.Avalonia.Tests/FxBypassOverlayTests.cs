using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Views;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the check that the FX bypass stamp is hidden by default and follows the show flag it is updated with.
/// Does not own: when the stamp shows (FxChainPresenter.ShowsBypassOverlay), the window that places it, or the palette colours.
/// Tests: this class.
/// </summary>
public class FxBypassOverlayTests
{
    [AvaloniaFact]
    public void StampShowsOnlyWhileUpdateAsksForIt()
    {
        var overlay = new FxBypassOverlay();
        Assert.False(overlay.IsVisible);
        overlay.Update(show: true, light: false);
        Assert.True(overlay.IsVisible);
        overlay.Update(show: false, light: true);
        Assert.False(overlay.IsVisible);
    }

    [AvaloniaFact]
    public void ThemeSwitchRepicksTheVeilColour()
    {
        var overlay = new FxBypassOverlay();
        overlay.Update(show: true, light: false);
        overlay.PaintColours(light: true);
        var expected = global::TabForge.Avalonia.Platform.AvaloniaPalette.ColorOf(global::TabForge.Avalonia.Platform.AvaloniaPalette.Get("FxBypassVeil", true));
        Assert.Equal(expected, ((global::Avalonia.Media.SolidColorBrush)overlay.Background!).Color);
    }
}
