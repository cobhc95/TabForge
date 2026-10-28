using System;
using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views.Dialogs;

// Owns: the small icons of a user preset row: the pencil (Rename) and bin (Delete) buttons and the grip of the drag handle. They are 14 DIP ink
//   shapes with no box behind them: full-contrast theme ink at rest, the accent (pencil) or the danger colour (bin) under the pointer, a softer ink
//   while pressed; the tooltip and the automation name name the action and the preset.
// Does not own: when the buttons show or what they do (PresetRows), the row's text and padding, or the shortcut keys (PresetRows.OnKey).
// Tests: PresetRowsTests.

/// <summary>The Rename and Delete icon buttons and the drag grip of one user preset row.</summary>
internal static class PresetRowIcons
{
    /// <summary>The side of one icon button (DIPs): the icon with a little room to hit.</summary>
    public const double PresetButtonSize = TabForge.Avalonia.UiMetrics.PresetRowButtonSize;

    /// <summary>The gap between the two buttons (DIPs).</summary>
    public const double ButtonGap = 2;

    // The palette keys the states take their ink from, so both themes repaint from the one palette (never a colour literal).
    private const string RestInk = "TextPrimary", MutedInk = "TextMuted", AccentInk = "Accent", DangerInk = "Danger";

    // The pencil as a filled path: the body and a separate sharpened tip, so the tip reads with a notch.
    private const string PencilGeometry =
        "M3.23,10.89 L13.86,0.26 L17.74,4.14 L7.11,14.77 Z M2.94,11.74 L6.26,15.06 L2.2,15.8 Z";

    // The trash can as stroked paths: the handle, the lid bar, the tapering body with rounded bottom corners, and the two slots.
    private const string TrashGeometry =
        "M6.6,3.2 C6.6,1.9 7.4,1.0 8.5,1.0 L9.5,1.0 C10.6,1.0 11.4,1.9 11.4,3.2 " +
        "M3.3,3.2 L14.7,3.2 " +
        "M4.5,4.8 L5.4,15.2 C5.5,16.1 6.1,16.8 7.0,16.8 L11.0,16.8 C11.9,16.8 12.5,16.1 12.6,15.2 L13.5,4.8 " +
        "M7.2,8.0 L7.2,13.6 M10.8,8.0 L10.8,13.6";

    /// <summary>The Rename button of <paramref name="preset"/>'s row, named "Rename (F2)" to the pointer and "Rename &lt;preset&gt;" to automation.</summary>
    public static Button Rename(string preset, Action run) => Create(PencilGeometry, filled: true, AccentInk, "Rename (F2)", $"Rename {preset}", run);

    /// <summary>The Delete button of <paramref name="preset"/>'s row, named "Delete (Delete)" to the pointer and "Delete &lt;preset&gt;" to automation.</summary>
    public static Button Delete(string preset, Action run) => Create(TrashGeometry, filled: false, DangerInk, "Delete (Delete)", $"Delete {preset}", run);

    /// <summary>The drag handle of <paramref name="preset"/>'s row: two columns of three dots. It is not a button; the row's drag code listens to its pointer.</summary>
    public static Border Grip(string preset)
    {
        var ink = new Path { Data = Geometry.Parse(DotsGeometry()), Stretch = Stretch.None, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        var grip = new Border { Width = DialogMetrics.PresetGripWidth, Background = AvaloniaPalette.TransparentBrush, Child = ink, VerticalAlignment = VerticalAlignment.Stretch };
        ToolTip.SetTip(grip, "Drag to reorder");
        AutomationProperties.SetName(grip, $"Reorder {preset}");
        var over = false;
        void Paint() => ink.Fill = BandBrush.Of(ink, over ? RestInk : MutedInk);
        grip.PointerEntered += (_, _) => { over = true; Paint(); };
        grip.PointerExited += (_, _) => { over = false; Paint(); };
        grip.FollowTheme(Paint);
        grip.AttachedToVisualTree += (_, _) => Paint();
        Paint();
        return grip;
    }

    // Six dots (two columns, three rows) as circle subpaths, drawn from the grip's dot radius.
    private static string DotsGeometry()
    {
        var r = DialogMetrics.PresetGripDot;
        var text = new StringBuilder();
        foreach (var x in new[] { r + 1, r * 5 })
            foreach (var y in new[] { r + 1, r * 4.5 + 1, r * 8 + 1 })
                text.Append(CultureInfo.InvariantCulture, $"M{x - r},{y} a{r},{r} 0 1,0 {2 * r},0 a{r},{r} 0 1,0 {-2 * r},0 ");
        return text.ToString();
    }

    // One icon button: the icon alone is its content, so no surface, border or theme template shows a box. The button is a Button subclass because the
    // default look hook maps exact control types: it leaves this icon without the Core Button look's accent hover border and fill.
    private static Button Create(string geometry, bool filled, string hoverInk, string tip, string name, Action run)
    {
        var ink = new Path
        {
            Data = Geometry.Parse(geometry), Width = DialogMetrics.PresetIconSize, Height = DialogMetrics.PresetIconSize, Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false,
        };
        if (!filled)
        {
            ink.StrokeThickness = DialogMetrics.PresetIconStroke;
            ink.StrokeLineCap = PenLineCap.Round;
            ink.StrokeJoin = PenLineJoin.Round;
        }
        var button = new IconButton
        {
            Content = ink, Width = PresetButtonSize, Height = PresetButtonSize, Padding = DialogMetrics.PresetNoOutline, BorderThickness = DialogMetrics.PresetNoOutline,
            Background = AvaloniaPalette.TransparentBrush, Focusable = false,
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(button, tip);
        AutomationProperties.SetName(button, name);
        var over = false;
        var down = false;

        void Paint()
        {
            var colour = BandBrush.Of(ink, over || down ? hoverInk : RestInk);
            if (filled) ink.Fill = colour;
            else ink.Stroke = colour;
            ink.Opacity = down ? DialogMetrics.PresetPressedInk : 1;
        }

        button.PointerEntered += (_, _) => { over = true; Paint(); };
        button.PointerExited += (_, _) => { over = false; down = false; Paint(); };
        button.PointerPressed += (_, e) => { if (e.GetCurrentPoint(button).Properties.IsLeftButtonPressed) { down = true; Paint(); } };
        button.PointerReleased += (_, _) => { down = false; Paint(); };
        button.FollowTheme(Paint);
        button.AttachedToVisualTree += (_, _) => Paint();
        // The click ends the row's own press handling, as the text buttons did: the same commands, F2 / Delete still handled by the box.
        button.Click += (_, e) => { e.Handled = true; run(); };
        Paint();
        return button;
    }

    // A plain Button to automation (its peer, its content and its tooltip) that KEEPS the Fluent Button theme: the default look hook's type
    // table holds the exact Button type, so a subclass is not given the Core Button look class.
    private sealed class IconButton : Button
    {
        protected override Type StyleKeyOverride => typeof(Button);
    }
}
