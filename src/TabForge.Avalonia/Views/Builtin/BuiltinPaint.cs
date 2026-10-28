using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views.Dialogs;

namespace TabForge.Avalonia.Views.Fx;

// Owns: the brushes and text the drawn controls of the built-in effect windows share: a theme resource by key, a palette colour (Success, Warning, Danger, Accent)
//   of the theme on screen, and a small muted label. Every colour is a theme or palette key, so both themes and any accent follow.
// Does not own: the controls that draw with them (MeterBar, StateLight, EqCurveView, TransferCurveView).
// Tests: BuiltinEditorWindowTests (shots).
internal static class BuiltinPaint
{
    /// <summary>The brush of a theme resource key (transparent when it does not exist).</summary>
    public static IBrush Theme(Control owner, string key) =>
        owner.TryFindResource(key, owner.ActualThemeVariant, out var found) && found is IBrush brush ? brush : AvaloniaPalette.TransparentBrush;

    /// <summary>The brush of a palette key in the theme variant the control is shown in.</summary>
    public static IBrush Palette(Control owner, string key) => AvaloniaPalette.PaletteBrush(key, owner.ActualThemeVariant == ThemeVariant.Light);

    public static FormattedText Label(string text, IBrush brush) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, DialogMetrics.FontSize - 2, brush);
}
