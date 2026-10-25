using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Styling;

namespace TabForge.Avalonia.Platform;

/// <summary>
/// The WPF scroll bar (App.xaml): a 12 DIP wide transparent track with a rounded thumb (radius 4, margin 2, at least 28 DIP long) in the ScrollThumbBrush,
/// always visible instead of Fluent's overlay hairline. The Core looks "ScrollBar", "ScrollBarThumb" and "ScrollBarThumbLength" add the horizontal size and hover colour.
/// Owns: the scroll bar template and the no-auto-hide rule. Does not own: the timeline scroll bar (its own control), the look rules (AvaloniaLooks).
/// Tests: TabForge.Avalonia.Tests.ThemeTests (ScrollBar width and thumb brush).
/// </summary>
public static class AvaloniaScrollBarStyles
{
    public const double ScrollBarThickness = 12;
    private const double ThumbCorner = 4, ThumbMargin = 2, ThumbMinLength = 28;

    public static Styles Build() => new()
    {
        // Fluent sets the template and minimum size per orientation, so these selectors are as specific as its own.
        new Style(x => x.OfType<ScrollBar>().Class(":vertical"))
        {
            Setters = { new Setter(TemplatedControl.TemplateProperty, new FuncControlTemplate<ScrollBar>(Content)), new Setter(Layoutable.WidthProperty, ScrollBarThickness), new Setter(Layoutable.MinWidthProperty, ScrollBarThickness) },
        },
        new Style(x => x.OfType<ScrollBar>().Class(":horizontal"))
        {
            Setters = { new Setter(TemplatedControl.TemplateProperty, new FuncControlTemplate<ScrollBar>(Content)), new Setter(Layoutable.HeightProperty, ScrollBarThickness), new Setter(Layoutable.MinHeightProperty, ScrollBarThickness) },
        },
        new Style(x => x.OfType<ScrollViewer>()) { Setters = { new Setter(ScrollViewer.AllowAutoHideProperty, false) } },
    };

    private static Control Content(ScrollBar bar, INameScope scope)
    {
        bar.Classes.Add("ScrollBar");
        var face = new Border { Name = "t", CornerRadius = new CornerRadius(ThumbCorner), Margin = new Thickness(ThumbMargin) };
        face.Bind(Border.BackgroundProperty, face.GetResourceObservable("ScrollThumbBrush"), BindingPriority.Template);
        var thumb = new Thumb
        {
            Name = "thumb", MinHeight = ThumbMinLength, MinWidth = 0,
            Template = new FuncControlTemplate<Thumb>((_, _) => face),
        };
        thumb.Classes.Add("ScrollBarThumb");
        thumb.Classes.Add("ScrollBarThumbLength");
        var less = new RepeatButton { Name = "PART_PageUpButton", Focusable = false, Opacity = 0 };
        var more = new RepeatButton { Name = "PART_PageDownButton", Focusable = false, Opacity = 0 };
        var track = new Track { Name = "PART_Track", Thumb = thumb, DecreaseButton = less, IncreaseButton = more, IsDirectionReversed = bar.Orientation == Orientation.Vertical };
        track.Bind(RangeBase.MinimumProperty, bar.GetObservable(RangeBase.MinimumProperty), BindingPriority.Template);
        track.Bind(RangeBase.MaximumProperty, bar.GetObservable(RangeBase.MaximumProperty), BindingPriority.Template);
        // Two-way: a thumb drag or track click moves the Track's value, which must write back to the ScrollBar that the ScrollViewer reads.
        track.Bind(RangeBase.ValueProperty, new Binding { Path = nameof(RangeBase.Value), Source = bar, Mode = BindingMode.TwoWay });
        track.Bind(Track.ViewportSizeProperty, bar.GetObservable(ScrollBar.ViewportSizeProperty), BindingPriority.Template);
        track.Bind(Track.OrientationProperty, bar.GetObservable(ScrollBar.OrientationProperty), BindingPriority.Template);
        track.RegisterInNameScope(scope); thumb.RegisterInNameScope(scope); less.RegisterInNameScope(scope); more.RegisterInNameScope(scope); face.RegisterInNameScope(scope);
        return new Grid { Background = global::Avalonia.Media.Brushes.Transparent, Children = { track } };
    }
}
