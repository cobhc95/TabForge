using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using TabForge.Platform.Theme;


namespace TabForge.Avalonia.Views;

// Owns: the layout toggle icons at the right end of the menu row (side panel | fretboard, keyboard mode | band view, compact mode | project
//   settings), each a 30 x 27 flat button with the app's 14 px stroked icon, running its command id; dimmed while no handler exists.
// Does not own: the commands (MainWindowCommands), the menu (MainWindowMenu), what the toggles show or hide.
// Tests: TabForge.Avalonia.Tests.MainWindowMenuTests.
/// <summary>The menu row's layout toggles, in the order and with the shared icon geometry (IconGeometry).</summary>
public static class MenuRowToggles
{
    /// <summary>The name of the toggles' panel, which other menus find the toggles by.</summary>
    public const string PanelName = "MenuRowToggles";

    /// <summary>The class of a toggle, which the window sets the check of and the menus find the toggle by (SetRowChecks, ToolbarMenuRows).</summary>
    public const string ToggleClass = "MenuRowToggle";

    /// <summary>The toolbar icon's size and stroke, in DIP.</summary>
    private const double IconStroke = 1.6;

    /// <summary>The toggle's one border template part, the outline the lit state paints.</summary>
    private const string OutlinePartName = "Bd";

    /// <summary>The lit state's palette keys and outline width: the toolbar's checked colours (AccentSoftBrush over AccentBrush).</summary>
    private const string LitFillKey = "SelectedTint", LitEdgeKey = "SelectedOutline", LitHoverKey = "AccentSoft";
    private const double LitOutlineWidth = 1;

    /// <summary>One toggle: its command id, tooltip and the icon path data (IconGeometry; 24 x 24 or 16 x 16 grid, drawn uniform at 14 px).</summary>
    public sealed record Toggle(string CommandId, string ToolTip, string IconData);

    /// <summary>The toggles in order; null is a separator. Icon data is the menu row's own path data.</summary>
    public static readonly IReadOnlyList<Toggle?> Items = new Toggle?[]
    {
        new("View.SidePanel", "Show / hide the side panel (tools, sections, practice)", IconGeometry.SidePanel),
        null,
        new("View.InstrumentPanel", "Show/hide fretboard / keyboard",
            IconGeometry.Fretboard),
        new("View.LearnMode", "Keyboard mode (experimental)", IconGeometry.LearnMode),
        null,
        new("View.BandView", "Band view: a row per track",
            IconGeometry.BandView),
        new("View.CompactMode", "Compact mode", IconGeometry.CompactMode),
        null,
        new("File.ProjectSettings", "Project settings: song info, credits, tempo, key",
            IconGeometry.Prefs),
    };

    public static StackPanel Build(MainWindowCommands commands)
    {
        var strip = new StackPanel
        {
            Name = PanelName, Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0),
        };
        foreach (var item in Items) strip.Children.Add(item is null ? Separator() : Button(item, commands));
        return strip;
    }

    /// <summary>An icon: the path data drawn uniform in a square of <paramref name="size"/> DIP and stroked in the text colour. The path sits in a
    /// Viewbox so it is scaled by its own bounds and centred; a bare Path stretches to its bounds and places them at its top-left, which is why the
    /// asymmetric fretboard icon sat off centre.</summary>
    public static Control Icon(string iconData, double size)
    {
        var data = Geometry.Parse(iconData);
        // The Viewbox shrinks the stroke with the icon, so a 24-unit icon drawn at 14 px came out thinner than a 16-unit one: the stroke is scaled
        // up by the shrink so every icon is drawn IconStroke DIP thick on screen. The Viewbox fits the Path's desired size, which runs from the
        // origin to the geometry's right and bottom edges (not its width and height), so the shrink is taken from those.
        var span = Math.Max(data.Bounds.Right, data.Bounds.Bottom);
        var stroke = span > 0 ? IconStroke * span / size : IconStroke;
        var path = new Path
        {
            Data = data, StrokeThickness = stroke,
            StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round,
        };
        path[!Shape.StrokeProperty] = new DynamicResourceExtension("TextPrimary");
        // The window's UI scale is a LayoutTransform on its root (ThemeSettingsBinding), which scales the stroke a second time: at 0.8 the 1.6 DIP
        // stroke came out 1.28 DIP. So the stroke is divided by the root's scale, and follows it when the scale changes. A popup copy sits outside
        // the scaled root, so it keeps the unscaled stroke.
        path.AttachedToVisualTree += (_, _) =>
        {
            var root = path.FindAncestorOfType<LayoutTransformControl>();
            if (root is null) return;
            void Follow() => path.StrokeThickness = stroke / (root.LayoutTransform is ScaleTransform scale ? scale.ScaleX : 1);
            Follow();
            // LayoutTransform is a plain property, so the scale is re-read after each layout of the root (a change of scale re-lays it out).
            root.LayoutUpdated += (_, _) => Follow();
        };
        return new Viewbox { Width = size, Height = size, Stretch = Stretch.Uniform, Child = path };
    }

    private static Control Button(Toggle toggle, MainWindowCommands commands)
    {
        var icon = Icon(toggle.IconData, IconSizing.Current);
        // A toggle button, so the mode's state shows on it: SetRowChecks sets IsChecked (the menu row and the toolbar follow the same command), and
        // Paint below lights it. A plain Button has no IsChecked.
        var button = new ToggleButton
        {
            Tag = toggle.CommandId, Content = icon, MinWidth = 30, Height = 27, Padding = new Thickness(4, 0),
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
            CornerRadius = Platform.AvaloniaFormControlStyles.ControlCornerRadius,
            // The one-border template, as the capsules carry: the Fluent template's presenter is cleared in the checked state by the app's form
            // styles, so a lit fill could never show on it.
            Template = Platform.AvaloniaButtonTemplates.SingleBorder(OutlinePartName),
            // Dimmed as the Button look's Disabled rule (opacity 0.45) while no handler exists; the theme's disabled fill would paint a grey block.
            Opacity = commands.Handles(toggle.CommandId) ? 1 : 0.45,
            Focusable = false,
        };
        button.Classes.Add(ToggleClass);
        // The glyph sits on the bar: no panel-coloured block, no focus rectangle. The three states are local values (idle, the soft grey hover shade
        // under the pointer, and the lit mode's accent-soft block with its accent line): the theme's own checked fill outranks a style's colours.
        var hovered = false;
        void Paint()
        {
            var lit = button.IsChecked == true;
            // A lit toggle under the pointer takes its accent colour, a step stronger than its soft lit fill, so the hover still shows.
            button.Background = lit ? PaletteBrush(button, hovered && button.IsEnabled ? LitHoverKey : LitFillKey)
                : hovered && button.IsEnabled ? Platform.HoverShade.IconBrush(button)
                : Platform.AvaloniaPalette.TransparentBrush;
            button.BorderBrush = lit ? PaletteBrush(button, LitEdgeKey) : Platform.AvaloniaPalette.TransparentBrush;
            button.BorderThickness = new Thickness(lit ? LitOutlineWidth : 0);   // reserved only while lit, so an idle toggle keeps its size
        }
        Platform.HoverShade.Attach(button, hover =>
        {
            hovered = hover;
            Paint();
        });
        button.IsCheckedChanged += (_, _) => Paint();
        Platform.AvaloniaPalette.FollowTheme(button, Paint);
        Paint();
        TabForge.Avalonia.Platform.Controls.HotkeyTip.Set(button, toggle.CommandId, toggle.ToolTip);
        global::Avalonia.Automation.AutomationProperties.SetName(button, toggle.ToolTip);
        button.Click += (_, _) => { if (commands.Handles(toggle.CommandId)) commands.Run(toggle.CommandId); };
        return button;
    }

    /// <summary>A palette colour as a brush, in the theme the toggle is shown in.</summary>
    private static IBrush PaletteBrush(Control control, string paletteKey) =>
        new SolidColorBrush(Platform.AvaloniaPalette.ColorOf(Platform.AvaloniaPalette.Get(paletteKey, control.ActualThemeVariant == ThemeVariant.Light)));

    /// <summary>The thin vertical line between icon clusters (the menu row also puts one before the Open / Save group).</summary>
    internal static Control Separator()
    {
        var line = new Border { Width = 1, Height = 15, Margin = new Thickness(5.5, 0), VerticalAlignment = VerticalAlignment.Center, Opacity = 0.4, IsHitTestVisible = false };
        line[!Border.BackgroundProperty] = new DynamicResourceExtension("TextMuted");
        return line;
    }
}
