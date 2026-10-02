using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace TabForge.Views;

/// <summary>Card, note and brush helpers shared by the Preferences page builders.</summary>
internal static class PreferencesCards
{
    internal static Button CreateButton(FrameworkElement scope, string text, string styleKey, string? tooltip = null)
    {
        var button = new Button { Content = text, Style = (Style)scope.FindResource(styleKey) };
        if (tooltip is not null) button.ToolTip = tooltip;
        AutomationProperties.SetName(button, text);
        return button;
    }

    internal static Border GroupCard(string title)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 2), TextWrapping = TextWrapping.Wrap });
        return new Border
        {
            Background = (Brush)Application.Current.FindResource("Panel2Brush"),
            BorderBrush = (Brush)Application.Current.FindResource("BorderSoftBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 14, 16, 14),
            Child = stack,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
    }

    internal static void AddCardContent(Border card, FrameworkElement row)
    {
        if (card.Child is not StackPanel stack) return;
        if (stack.Children.Count > 1) row.Margin = new Thickness(0, 12, 0, 0);
        stack.Children.Add(row);
    }

    internal static Border InformationCard(string title, string text)
    {
        var card = GroupCard(title);
        AddCardContent(card, Note(text, 12, new Thickness(0, 2, 0, 0)));
        return card;
    }

    internal static FrameworkElement Note(string text, double fontSize = 12, Thickness? margin = null)
    {
        var label = new TextBlock { Text = text, FontSize = fontSize, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.FindResource("MutedBrush") };
        return new Border { Child = label, Margin = margin ?? new Thickness(0) };
    }

    internal static Brush Brush(string hex) => TabForge.Visualization.ColourText.BrushOr(hex);

    internal static Brush SafeBrush(string? text) => TryColour(text, out var color) ? new SolidColorBrush(color) : Brushes.Transparent;

    internal static bool TryColour(string? text, out Color color) => TabForge.Visualization.ColourText.TryParseSetting(text, out color);
}
