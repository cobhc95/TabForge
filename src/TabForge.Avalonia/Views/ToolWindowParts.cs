using TabForge.Platform.Theme;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;

namespace TabForge.Avalonia.Views;

/// <summary>
/// The layout pieces the tool windows (Render, Video export, Tutorial) share: a label, a small muted note, a row of controls, a titled group
/// and the status line that reports a button the preview cannot run. Colours are palette keys (DynamicResource), never literal brushes.
/// Owns: the spacing and the palette keys of these pieces, and the wording of the "needs the full app" status line.
/// Does not own: what each window shows (its own class and presenter) or the palette values (AvaloniaPalette).
/// Tests: TabForge.Avalonia.Tests.RenderVideoExportTutorialTests.
/// </summary>
public static class ToolWindowParts
{
    /// <summary>The status line text for a button whose work only the full app can do.</summary>
    public static string NeedsFullApp(string work) => $"{work} isn't available yet.";

    /// <summary>The tooltip of a button or menu row that is shown but cannot work yet.</summary>
    public const string NotAvailableTip = "Not available yet";

    /// <summary>Turns <paramref name="control"/> off with the "Not available yet" tooltip (shown even while disabled); true turns it off.</summary>
    public static void NotAvailable(Control control, bool unavailable = true)
    {
        if (!unavailable) return;
        control.IsEnabled = false;
        ToolTip.SetTip(control, NotAvailableTip);
        ToolTip.SetShowOnDisabled(control, true);
    }

    /// <summary>The text a text box shows; a box with no text yet reads as empty.</summary>
    public static string TextOf(TextBox box) => box.Text ?? "";

    public static TextBlock Label(string text) => new()
    {
        Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0),
        [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextPrimary"),
    };

    public static TextBlock Small(string text) => new()
    {
        Text = text, FontSize = ChromeMetrics.ToolHintFontSize, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4),
        [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted"),
    };

    /// <summary>A horizontal row; each item gets a 6 px gap after it.</summary>
    public static StackPanel Row(params Control[] items)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
        foreach (var item in items)
        {
            item.VerticalAlignment = VerticalAlignment.Center;
            item.Margin = new Thickness(item.Margin.Left, item.Margin.Top, item.Margin.Right + 6, item.Margin.Bottom);
            row.Children.Add(item);
        }
        return row;
    }

    /// <summary>A titled group as the WPF GroupBox (App.xaml): a raised header band with a semibold title over a panel body, both in the general border with 4 DIP corners. The header's name is the header text, so tests find each group.</summary>
    public static Border Group(string header, Control content)
    {
        var title = new TextBlock
        {
            Name = "GroupHeader", Text = header, FontWeight = FontWeight.SemiBold, Margin = new Thickness(8, 4),
            [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("AccentBrush"),
        };
        var band =new Border { CornerRadius = new CornerRadius(4, 4, 0, 0), Child = title, [!Border.BackgroundProperty] = new DynamicResourceExtension("Panel3Brush") };
        var body = new Border
        {
            Padding = new Thickness(6), BorderThickness = new Thickness(0, 1, 0, 0), CornerRadius = new CornerRadius(0, 0, 4, 4), Child = content,
            [!Border.BackgroundProperty] = new DynamicResourceExtension("PanelBrush"), [!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderBrush"),
        };
        Grid.SetRow(body, 1);
        return new Border
        {
            Name = header, Margin = new Thickness(3), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), ClipToBounds = true,
            // A Grid, not a StackPanel: the body gets the height left under the heading, so a scroll viewer inside it scrolls when the content overflows.
            Child = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Children = { band, body } },
            [!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderBrush"), [!Border.BackgroundProperty] = new DynamicResourceExtension("Panel2Brush"),
        };
    }

    /// <summary>The status text block the window's footer reports into.</summary>
    public static TextBlock Status(string name) => new()
    {
        Name = name, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4),
        [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextPrimary"),
    };
}
