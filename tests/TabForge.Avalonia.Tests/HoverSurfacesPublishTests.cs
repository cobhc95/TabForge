using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the hover and press shades following a published palette: a Custom panel colour changes them at once, and the preset brings them back.
// Does not own: the contrast minimums (HoverSurfaceContrastTests).
// Tests: this file.
public class HoverSurfacesPublishTests
{
    [AvaloniaFact]
    public void ACustomPanelColourChangesTheHoverShadeOnPublish()
    {
        try
        {
            AvaloniaPalette.Publish(new AppearanceSettings());
            var before = AvaloniaPalette.Get("HoverSurface", light: false);

            AvaloniaPalette.Publish(new AppearanceSettings { ThemeMode = "Custom", Panel = "#6A5ACD" });
            var custom = AvaloniaPalette.Get("HoverSurface", light: false);
            Assert.NotEqual(before, custom);

            AvaloniaPalette.Publish(new AppearanceSettings());
            Assert.Equal(before, AvaloniaPalette.Get("HoverSurface", light: false));
        }
        finally { AvaloniaPalette.Publish(new AppearanceSettings()); }
    }
}
