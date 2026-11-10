using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using TabForge.Platform.Theme;

namespace TabForge.Avalonia.Features.LeftStrip;

// Owns: the placeholder Composer panel beside the left strip: its header, a few placeholder riff rows and a small Preview box. No riff library yet.
// Does not own: when it shows (LeftStripFeature), the saved open state (LeftStripSettings).
// Tests: TabForge.Avalonia.Tests.LeftStripSettingsTests (the open-state rules only).
public static class ComposerPanel
{
    /// <summary>The panel's element name.</summary>
    public const string PanelName = "ComposerPanel";

    /// <summary>The placeholder rows.</summary>
    private static readonly string[] Rows = { "Half-time chug", "Blast 16ths", "Gallop fill" };

    public static Border Build()
    {
        var stack = new StackPanel { Spacing = ChromeMetrics.ComposerRowGap, Margin = new Thickness(10) };
        stack.Children.Add(Text("Composer", ChromeMetrics.TextHeading, "TextPrimary"));
        foreach (var row in Rows) stack.Children.Add(Box(Text(row, ChromeMetrics.TextBody, "TextPrimary"), 0));
        stack.Children.Add(Box(Text("Preview", ChromeMetrics.TextSmall, "TextMuted"), 60));
        var panel = new Border
        {
            Name = PanelName, Width = ChromeMetrics.ComposerPanelWidth, BorderThickness = new Thickness(0, 0, 1, 0), Child = stack,
        };
        panel[!Border.BackgroundProperty] = new DynamicResourceExtension("PanelBackground");
        panel[!Border.BorderBrushProperty] = new DynamicResourceExtension("SoftBorder");
        return panel;
    }

    private static TextBlock Text(string text, double size, string colourKey)
    {
        var block = new TextBlock { Text = text, FontSize = size, VerticalAlignment = VerticalAlignment.Center };
        block[!TextBlock.ForegroundProperty] = new DynamicResourceExtension(colourKey);
        return block;
    }

    private static Border Box(Control child, double minHeight)
    {
        var box = new Border
        {
            Child = child, MinHeight = minHeight, Padding = new Thickness(8, 5), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(ChromeMetrics.RadiusMedium),
        };
        box[!Border.BorderBrushProperty] = new DynamicResourceExtension("SoftBorder");
        return box;
    }
}
