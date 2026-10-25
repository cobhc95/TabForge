using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Media;
using Avalonia.Styling;
using TabForge.Platform.Theme;

namespace TabForge.Avalonia.Platform;

/// <summary>
/// The control templates of the border-template looks (Button, ToggleButton, ListBoxItem, ListViewItem, Thumb): nested borders named after the look's
/// template parts (as the WPF templates name them, for example "b", "Bd", "Chrome"), around a content presenter when the control has content.
/// The outer border shows the control's own background, border brush, thickness and padding.
/// Owns: which part names a look uses and the template built from them. Does not own: the part setters (AvaloniaLooks) or colours (AvaloniaPalette).
/// Tests: TabForge.Avalonia.Tests.ThemeTests.
/// </summary>
public static class AvaloniaButtonTemplates
{
    /// <summary>The Border part names the look's setters name, in first-use order; parts of other element types are not template parts here.</summary>
    public static IReadOnlyList<string> BorderParts(Look look) => look.Rules
        .SelectMany(rule => rule.Set)
        .Where(setter => setter.Part is not null && IsBorderProperty(setter.Property))
        .Select(setter => setter.Part!)
        .Distinct(StringComparer.Ordinal)
        .ToList();

    /// <summary>True for a part property the template's borders carry (Border.X, or UIElement.Opacity on a border part).</summary>
    public static bool IsBorderProperty(string property) => property.StartsWith("Border.", StringComparison.Ordinal) || property == "UIElement.Opacity";

    /// <summary>A style that gives controls of the look's class the template with its border parts, or null when the look names none.</summary>
    public static Style? TemplateStyle(Type kind, Look look)
    {
        var parts = BorderParts(look);
        if (parts.Count == 0) return null;
        var template = new FuncControlTemplate((parent, scope) => Build(parent, scope, parts));
        return new Style(x => Selectors.Class(Selectors.Is(x, kind), look.Name))
        {
            Setters = { new Setter(TemplatedControl.TemplateProperty, template) },
        };
    }

    /// <summary>
    /// A template of one border (named <paramref name="partName"/>) that shows the control's own background, border, corner radius and padding around
    /// its content, for a control whose look sets only control properties (a capsule) and so gets no template from <see cref="TemplateStyle"/>.
    /// </summary>
    public static IControlTemplate SingleBorder(string partName) => new FuncControlTemplate((parent, scope) => Build(parent, scope, new[] { partName }));

    private static Control Build(TemplatedControl parent, INameScope scope, IReadOnlyList<string> parts)
    {
        // A control with no content (a thumb) gets only the borders.
        Control? child = parent is ContentControl content ? BuildPresenter(content, scope) : null;
        for (var i = parts.Count - 1; i >= 0; i--)
        {
            var border = new Border { Name = parts[i], Child = child };
            if (i == 0)
            {
                Bind(border, Border.BackgroundProperty, parent, TemplatedControl.BackgroundProperty);
                Bind(border, Border.BorderBrushProperty, parent, TemplatedControl.BorderBrushProperty);
                Bind(border, Border.BorderThicknessProperty, parent, TemplatedControl.BorderThicknessProperty);
                Bind(border, Border.CornerRadiusProperty, parent, TemplatedControl.CornerRadiusProperty);
                Bind(border, Border.PaddingProperty, parent, TemplatedControl.PaddingProperty);
            }
            border.RegisterInNameScope(scope);
            child = border;
        }
        return child ?? throw new ArgumentException("A border template needs at least one part name.", nameof(parts));
    }

    // The content presenter that shows the control's content, bound to the control's content settings.
    private static ContentPresenter BuildPresenter(ContentControl parent, INameScope scope)
    {
        var presenter = new ContentPresenter { Name = "PART_ContentPresenter" };
        // The presenter paints no fill: the outer border carries the control fill, and a theme style on the presenter must not paint over it.
        presenter.SetValue(ContentPresenter.BackgroundProperty, Brushes.Transparent, BindingPriority.Template);
        Bind(presenter, ContentPresenter.ContentProperty, parent, ContentControl.ContentProperty);
        Bind(presenter, ContentPresenter.ContentTemplateProperty, parent, ContentControl.ContentTemplateProperty);
        Bind(presenter, ContentPresenter.HorizontalContentAlignmentProperty, parent, ContentControl.HorizontalContentAlignmentProperty);
        Bind(presenter, ContentPresenter.VerticalContentAlignmentProperty, parent, ContentControl.VerticalContentAlignmentProperty);
        presenter.RegisterInNameScope(scope);
        return presenter;
    }

    // Template priority, so a look's "/template/ Border#part" style setter wins over the control's own value, as a WPF template trigger does.
    internal static void Bind<T>(AvaloniaObject target, AvaloniaProperty<T> property, TemplatedControl parent, AvaloniaProperty<T> source) =>
        target.Bind(property, parent.GetObservable(source), BindingPriority.Template);
}
