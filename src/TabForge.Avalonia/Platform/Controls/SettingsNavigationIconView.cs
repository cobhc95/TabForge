using System;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using TabForge.Platform;

namespace TabForge.Avalonia.Platform.Controls;

/// <summary>
/// The glyph of one settings category as a square control on the settings rail, in the rail colour, or the selected colour while its page is open.
/// Owns: the colour choice, the redraw when the selection changes, and the scale from the glyph grid to the control's side.
/// Does not own: which glyph a category has (<see cref="SettingsNavigationGlyphs"/>) or the rail row (PreferencesWindow).
/// Tests: TabForge.Avalonia.Tests.PreferencesWindowTests.
/// </summary>
public sealed class SettingsNavigationIconView : Control
{
    private const string RailInkKey = "SettingsNavIcon";
    private const string SelectedInkKey = "SettingsNavIconSelected";

    private readonly AvaloniaDrawContext _draw = new();
    private readonly string _category;
    private bool _isSelected;

    public SettingsNavigationIconView(string category)
    {
        _category = category;
        IsHitTestVisible = false;
    }

    /// <summary>True while the page of this glyph's category is open; the glyph then takes the selected colour.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        var d = _draw.Use(context);
        var light = ActualThemeVariant == ThemeVariant.Light;
        var ink = AvaloniaPalette.Get(IsSelected ? SelectedInkKey : RailInkKey, light);
        SettingsNavigationGlyphs.Draw(d, _category, Math.Min(Bounds.Width, Bounds.Height), ink);
    }
}
