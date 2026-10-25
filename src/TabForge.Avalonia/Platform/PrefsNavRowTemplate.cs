using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Media;

namespace TabForge.Avalonia.Platform;

/// <summary>
/// The control template of the Preferences rail rows: one border, PART_NavBorder, that paints the row's own background, edge, radius and padding,
/// around a content presenter that paints no fill. The row's fill and accent bar are therefore the item's own properties, and no Fluent template part paints over them.
/// Owns: the rail row template. Does not own: the row colours and states (AvaloniaFormControlStyles), the rail itself (PreferencesWindow).
/// Tests: TabForge.Avalonia.Tests.PrefsNavRowTemplateTests.
/// </summary>
internal static class PrefsNavRowTemplate
{
    /// <summary>The template for one rail row. A new instance per call, so the template holds no shared state.</summary>
    public static FuncControlTemplate<ListBoxItem> Create() => new((item, scope) => Build(item, scope));

    private static Control Build(ListBoxItem item, INameScope scope)
    {
        // The presenter's own fill is set on the element, so no style on the item can paint over it.
        var presenter = new ContentPresenter { Name = "PART_ContentPresenter", Background = Brushes.Transparent };
        AvaloniaButtonTemplates.Bind(presenter, ContentPresenter.ContentProperty, item, ContentControl.ContentProperty);
        AvaloniaButtonTemplates.Bind(presenter, ContentPresenter.ContentTemplateProperty, item, ContentControl.ContentTemplateProperty);
        AvaloniaButtonTemplates.Bind(presenter, ContentPresenter.HorizontalContentAlignmentProperty, item, ContentControl.HorizontalContentAlignmentProperty);
        AvaloniaButtonTemplates.Bind(presenter, ContentPresenter.VerticalContentAlignmentProperty, item, ContentControl.VerticalContentAlignmentProperty);
        presenter.RegisterInNameScope(scope);

        var border = new Border { Name = "PART_NavBorder", Child = presenter };
        AvaloniaButtonTemplates.Bind(border, Border.BackgroundProperty, item, TemplatedControl.BackgroundProperty);
        AvaloniaButtonTemplates.Bind(border, Border.BorderBrushProperty, item, TemplatedControl.BorderBrushProperty);
        AvaloniaButtonTemplates.Bind(border, Border.BorderThicknessProperty, item, TemplatedControl.BorderThicknessProperty);
        AvaloniaButtonTemplates.Bind(border, Border.CornerRadiusProperty, item, TemplatedControl.CornerRadiusProperty);
        AvaloniaButtonTemplates.Bind(border, Border.PaddingProperty, item, TemplatedControl.PaddingProperty);
        border.RegisterInNameScope(scope);
        return border;
    }
}
