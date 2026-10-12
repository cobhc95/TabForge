using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using TabForge.Models;

namespace TabForge.Views.Band;

// Owns: the parts of a header strip of track pills, shared by the Band view and compact mode: the muted caption, a track's pill
//   (coloured dot + name) and the × close button.
// Does not own: what a pill click or the close button does, and which pills are lit (the strip that uses the parts).
// Tests: TestBandPillsAndRows, TestCompactMode.
internal static class TrackPillStrip
{
    /// <summary>The strip's caption: capitals, muted, no box.</summary>
    public static TextBlock Caption(string text)
    {
        var caption = new TextBlock { Text = text, FontSize = 12.65, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 2, 6, 0) };
        caption.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        return caption;
    }

    /// <summary>The × at the right end of the strip; its tooltip carries the live key of <paramref name="commandId"/>.</summary>
    public static Button CloseButton(string text, string commandId)
    {
        // A drawn cross, not the "×" character: a glyph sits off the middle of its box.
        var cross = new System.Windows.Shapes.Path { Data = System.Windows.Media.Geometry.Parse("M 0,0 L 10,10 M 10,0 L 0,10"), Width = 10, Height = 10, Stretch = System.Windows.Media.Stretch.Fill, StrokeThickness = 1.5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        cross.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "TextBrush");
        var close = new Button { Content = cross, Width = 26, Height = 24, Padding = new Thickness(0), Focusable = false, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 1, 8, 0) };
        close.SetResourceReference(FrameworkElement.StyleProperty, "ToolButton");
        System.Windows.Automation.AutomationProperties.SetName(close, text);
        TooltipShortcuts.Bind(close, text, commandId);
        return close;
    }

    /// <summary>The scroll area that keeps pills that do not fit on one line, cut off at the edge.</summary>
    public static ScrollViewer PillScroll(UIElement pills) =>
        new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false, Content = pills };

    /// <summary>One track's pill: its colour as a dot, then its name.</summary>
    public static ToggleButton Pill(TrackModel track, string toolTip, string automationName)
    {
        var label = new TextBlock { Text = track.Name, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 160, VerticalAlignment = VerticalAlignment.Center };
        // The app-wide TextBlock style paints TextBrush; the pill's state colour (TrackPill style) must win.
        label.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding(nameof(Control.Foreground))
            { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(ToggleButton), 1) });
        var chip = new System.Windows.Shapes.Ellipse
        {
            Width = 8, Height = 8, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center,
            Fill = new SolidColorBrush(ColourHex.TryParse(track.ColorHex, out var c) ? Color.FromRgb(c.R, c.G, c.B) : Colors.Gray)
        };
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(chip);
        content.Children.Add(label);
        var pill = new ToggleButton { Margin = new Thickness(0, 0, 4, 4), Content = content, ToolTip = toolTip };
        pill.SetResourceReference(FrameworkElement.StyleProperty, "TrackPill");
        System.Windows.Automation.AutomationProperties.SetName(pill, automationName);
        return pill;
    }
}
