using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;

namespace TabForge.Avalonia.Platform.Controls;

// Owns: the caption-and-chevron content of the app's dropdown buttons (the COMPACT MODE / BAND VIEW header caption, the piano roll lane picker): an
//   upper-case semibold caption in the muted colour followed by the thin drawn chevron (never a text glyph), with the shared font size and gap.
// Does not own: the button around it (FlatToolButton, PianoRollToolLook), the menu it opens, what the caption says.
// Tests: TabForge.Avalonia.Tests.CompactTrackStripTests, PianoRollLaneButtonTests.
public static class DropdownCaption
{
    /// <summary>The name of the chevron path, so a test finds it.</summary>
    public const string ChevronName = "DropdownChevron";

    /// <summary>The theme brush key of the caption and the chevron stroke.</summary>
    public const string ForegroundKey = "MutedBrush";

    /// <summary>The shared caption size and gap (single source of truth lives in DialogMetrics).</summary>
    public const double FontSize = global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.DropdownCaptionFontSize;
    public const double Gap = global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.DropdownCaptionGap;
    private const string ChevronData = "M 1,1 L 5,5 L 9,1";

    /// <summary>A caption text block in the shared font, size and colour.</summary>
    public static TextBlock Caption(string text)
    {
        var caption = new TextBlock { Text = text, FontSize = global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.DropdownCaptionFontSize, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        caption[!TextBlock.ForegroundProperty] = new DynamicResourceExtension(ForegroundKey);
        return caption;
    }

    /// <summary>The thin chevron-down path.</summary>
    public static Path Chevron()
    {
        var chevron = new Path
        {
            Name = ChevronName, Data = Geometry.Parse(ChevronData), Width = global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.DropdownCaptionChevronWidth, Height = global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.DropdownCaptionChevronHeight, Stretch = Stretch.None, StrokeThickness = global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.DropdownCaptionChevronStroke,
            StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round, VerticalAlignment = VerticalAlignment.Center,
        };
        chevron[!Shape.StrokeProperty] = new DynamicResourceExtension(ForegroundKey);
        return chevron;
    }

    /// <summary>The caption then the chevron, in a row.</summary>
    public static StackPanel Content(Control caption) =>
        new() { Orientation = Orientation.Horizontal, Spacing = Gap, VerticalAlignment = VerticalAlignment.Center, Children = { caption, Chevron() } };
}
