using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using TabForge.Avalonia.Platform;

namespace TabForge.Avalonia.Views;

/// <summary>
/// Owns: the tilted "BYPASSED" stamp and its veil, drawn over the FX chain window's plug-in area while the plug-in does not play or the chain is off.
/// Does not own: when it shows (FxChainPresenter.ShowsBypassOverlay decides, the window asks), the area it covers, or the colours (AvaloniaPalette).
/// Tests: FxBypassOverlayTests.
/// </summary>
internal sealed class FxBypassOverlay : Border
{
    private const string StampText = "BYPASSED";
    private const string VeilPaletteName = "FxBypassVeil";
    private const string StampPaletteName = "FxBypassStamp";
    private const double StampFontSize = 96;
    private const double StampAngleDegrees = -12;
    private const double StampMarginDip = 64;   // room for the tilted text to stay inside the area

    private readonly TextBlock _stamp = new()
    {
        Text = StampText,
        FontWeight = FontWeight.Bold,
        FontSize = StampFontSize,
        RenderTransform = new RotateTransform(StampAngleDegrees),
        RenderTransformOrigin = RelativePoint.Center,
    };

    public FxBypassOverlay()
    {
        Name = "BypassOverlay";
        IsHitTestVisible = false;
        IsVisible = false;
        Child = new Viewbox { Stretch = Stretch.Uniform, Margin = new Thickness(StampMarginDip), Child = _stamp, ClipToBounds = false };
        ClipToBounds = false;
        ActualThemeVariantChanged += (_, _) => { if (IsVisible) PaintColours(ActualThemeVariant == ThemeVariant.Light); };
    }

    /// <summary>Shows or hides the stamp; the colours follow the theme variant the window is shown in.</summary>
    public void Update(bool show, bool light)
    {
        IsVisible = show;
        if (!show) return;
        PaintColours(light);
    }

    /// <summary>The palette colours for the stamp and veil in one theme variant; a theme switch while shown calls it again.</summary>
    public void PaintColours(bool light)
    {
        Background = new SolidColorBrush(AvaloniaPalette.ColorOf(AvaloniaPalette.Get(VeilPaletteName, light)));
        _stamp.Foreground = new SolidColorBrush(AvaloniaPalette.ColorOf(AvaloniaPalette.Get(StampPaletteName, light)));
    }
}
