using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using TabForge.Platform;

namespace TabForge.Avalonia.Platform;

/// <summary>
/// One palette icon as a square control: the icon art fits the control's smaller side, in <see cref="Colour"/>.
/// Owns: the icon's drawing surface and its redraw when the colour changes. Does not own: the icon art (ToolIconArt), the button around it (ToolsPaneView).
/// </summary>
public sealed class ToolIconView : Control
{
    private readonly AvaloniaDrawContext _draw = new();
    private UiColor _colour;

    public ToolIconView(string icon)
    {
        Icon = icon;
        IsHitTestVisible = false;
    }

    /// <summary>The icon's path under Assets/ToolIcons, such as "Edit/select_pointer".</summary>
    public string Icon { get; }

    /// <summary>The colour the icon's currentColor shapes take; the owner sets it when the tool's state changes.</summary>
    public UiColor Colour
    {
        get => _colour;
        set
        {
            if (_colour == value) return;
            _colour = value;
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        var d = _draw.Use(context);
        ToolIconArt.Draw(d, Icon, 0, 0, Math.Min(Bounds.Width, Bounds.Height), Colour, ActualThemeVariant == ThemeVariant.Light);
    }
}
