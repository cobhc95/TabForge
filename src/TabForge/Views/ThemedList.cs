using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;

namespace TabForge.Views;

/// <summary>
/// Theme styling for code-built list views (the plug-in browser...): rows with the theme's hover and selection
/// colours instead of WPF's bright default, and column headers that sort when clicked (again to reverse), with
/// an arrow on the sorted column and a soft hover. All colours come from the theme resources.
/// </summary>
internal static class ThemedList
{
    /// <summary>Row look: transparent, HoverBrush on hover, AccentSoftBrush + strong text when selected.</summary>
    public static void StyleRows(ListView list)
    {
        var border = new FrameworkElementFactory(typeof(Border), "Bd");
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
        border.SetValue(Border.PaddingProperty, new Thickness(0, 2, 0, 2));
        border.AppendChild(new FrameworkElementFactory(typeof(GridViewRowPresenter)));
        var template = new ControlTemplate(typeof(ListViewItem)) { VisualTree = border };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("HoverBrush"), "Bd"));
        var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("AccentSoftBrush"), "Bd"));
        selected.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("TextStrongBrush")));
        template.Triggers.Add(hover);
        template.Triggers.Add(selected);
        var style = new Style(typeof(ListViewItem));
        style.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("TextBrush")));
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        style.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, null));
        list.ItemContainerStyle = style;
    }

    /// <summary>Header look: theme panel colour, a soft hover, content on the left.</summary>
    public static Style HeaderStyle()
    {
        var border = new FrameworkElementFactory(typeof(Border), "Bd");
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        border.AppendChild(content);
        var template = new ControlTemplate(typeof(GridViewColumnHeader)) { VisualTree = border };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("HoverBrush"), "Bd"));
        hover.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("TextBrush")));
        template.Triggers.Add(hover);

        var header = new Style(typeof(GridViewColumnHeader));
        header.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("Panel2Brush")));
        header.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("MutedBrush")));
        header.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension("BorderSoftBrush")));
        header.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 1, 1)));
        header.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 4, 6, 4)));
        header.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        header.Setters.Add(new Setter(Control.TemplateProperty, template));
        return header;
    }

    /// <summary>
    /// Makes every column of <paramref name="view"/> sort on click: first click ascending, then it flips.
    /// <paramref name="changed"/> is told the column index and direction; the caller re-sorts its rows.
    /// </summary>
    public static void MakeSortable(GridView view, Action<int, bool> changed, int initialColumn = 0)
    {
        var style = HeaderStyle();
        view.ColumnHeaderContainerStyle = style;
        var arrows = new List<Path>();
        var column = initialColumn; var ascending = true;
        void Show()
        {
            for (var i = 0; i < arrows.Count; i++)
            {
                arrows[i].Visibility = i == column ? Visibility.Visible : Visibility.Hidden;
                arrows[i].Data = Geometry.Parse(ascending ? "M0,5 L4,0 L8,5 Z" : "M0,0 L8,0 L4,5 Z");
            }
        }
        for (var i = 0; i < view.Columns.Count; i++)
        {
            var index = i;
            var title = view.Columns[i].Header?.ToString() ?? "";
            var arrow = new Path { Width = 8, Height = 5, Stretch = Stretch.Fill, Margin = new Thickness(6, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center };
            arrow.SetResourceReference(Shape.FillProperty, "AccentBrush");
            arrows.Add(arrow);
            var panel = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(arrow, Dock.Right);
            panel.Children.Add(arrow);
            panel.Children.Add(new TextBlock { Text = title, TextTrimming = TextTrimming.CharacterEllipsis });
            var header = new GridViewColumnHeader { Style = style, Content = panel, ToolTip = $"Sort by {title.ToLowerInvariant()} (click again to reverse)" };
            header.Click += (_, _) =>
            {
                ascending = column == index ? !ascending : true;
                column = index;
                Show();
                changed(column, ascending);
            };
            view.Columns[i].Header = header;
        }
        Show();
    }
}
