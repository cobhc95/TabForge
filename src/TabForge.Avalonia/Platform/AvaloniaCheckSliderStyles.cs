using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;

namespace TabForge.Avalonia.Platform;

/// <summary>
/// The templates of every CheckBox, RadioButton and Slider, replacing Fluent's accent-filled ones with the app's: the CheckBox is App.xaml's 15 DIP rounded
/// box with an accent tick (its checked and hover colours come from the Core look "CheckBox", whose class the template adds), the RadioButton is a
/// thin-outlined 13 DIP circle with a 5 DIP centred dot while checked, and the Slider is the default one (a 4 DIP track and a 11x18 DIP thumb
/// in the system control colours, which the app does not theme).
/// Owns: those three templates and the system slider colours. Does not own: the CheckBox state colours (Core look "CheckBox" via AvaloniaLooks).
/// Tests: TabForge.Avalonia.Tests.ParityShotsTests (A/B renders of the Score text and Global tuning dialogs).
/// </summary>
public static class AvaloniaCheckSliderStyles
{
    // App.xaml CheckBox template: box size, corner, tick path and stroke, and the gap to the label.
    private const double BoxSize = 15, BoxCorner = 3, TickStroke = 2, LabelGap = 6;
    // The tick's bounds start at the origin, so centring it in the box centres the visible mark.
    private const string TickPath = "M 0 4 L 4 8 L 10 0";
    // The default radio button: a 13 DIP circle with a 1 DIP outline and a 5 DIP dot when checked.
    private const double RadioSize = 13, RadioStrokeWidth = 1, RadioDotSize = 5;
    private const string RadioDotClass = "RadioDot";
    // The default slider (no app style) paints in fixed greys in both themes (palette keys SystemSliderTrack... and the derived thumb states).
    private const double TrackHeight = 4, ThumbWidth = 11, ThumbHeight = 18, SliderHeight = ThumbHeight;

    public static Styles Build() => new()
    {
        // The check box has no minimum size or padding and a 1 DIP margin (App.xaml); Fluent's are a 32 DIP minimum height and an 8 DIP left padding.
        new Style(x => x.OfType<CheckBox>())
        {
            Setters =
            {
                new Setter(TemplatedControl.TemplateProperty, new FuncControlTemplate<CheckBox>(CheckBoxContent)),
                new Setter(Layoutable.MinHeightProperty, 0.0),
                new Setter(Layoutable.MinWidthProperty, 0.0),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(0)),
                new Setter(Layoutable.MarginProperty, new Thickness(1)),
                new Setter(TemplatedControl.ForegroundProperty, new DynamicResourceExtension("TextBrush")),
            },
        },
        new Style(x => x.OfType<RadioButton>())
        {
            Setters =
            {
                new Setter(TemplatedControl.TemplateProperty, new FuncControlTemplate<RadioButton>(RadioContent)),
                // Option text uses the dialog body size (13) instead of the theme's 14.
                new Setter(TemplatedControl.FontSizeProperty, 13.0),
                new Setter(Layoutable.MinHeightProperty, 0.0),
                new Setter(Layoutable.MinWidthProperty, 0.0),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(0)),
                new Setter(Layoutable.MarginProperty, new Thickness(1)),
                new Setter(TemplatedControl.ForegroundProperty, new DynamicResourceExtension("TextBrush")),
            },
        },
        // The radio dot shows only while the button is checked; the template hides it at template priority.
        new Style(x => x.OfType<RadioButton>().Class(":checked").Template().OfType<Ellipse>().Class(RadioDotClass))
        {
            Setters = { new Setter(Visual.IsVisibleProperty, true) },
        },
        // The radio ring takes the accent on hover and the press shade when pressed, as the check box border (the look "CheckBox").
        new Style(x => x.OfType<RadioButton>().Class(":pointerover").Template().OfType<Ellipse>().Name("ring"))
        {
            Setters = { new Setter(Shape.StrokeProperty, new DynamicResourceExtension("AccentBrush")) },
        },
        new Style(x => x.OfType<RadioButton>().Class(":pressed").Template().OfType<Ellipse>().Name("ring"))
        {
            Setters = { new Setter(Shape.FillProperty, new DynamicResourceExtension("PressBrush")) },
        },
        // Fluent sets the slider template per orientation, so these selectors are as specific as its own.
        new Style(x => x.OfType<Slider>().Class(":horizontal"))
        {
            Setters =
            {
                new Setter(TemplatedControl.TemplateProperty, new FuncControlTemplate<Slider>(SliderContent)),
                new Setter(Layoutable.MinHeightProperty, 0.0),
            },
        },
        ThumbStyle(":pointerover", "SliderThumbHover", "SliderThumbHoverBorder"),
        ThumbStyle(":pressed", "SliderThumbPressed", "SliderThumbPressedBorder"),
        new Style(x => x.OfType<Slider>().Class(":disabled").Template().OfType<Thumb>())
        {
            Setters = { new Setter(TemplatedControl.BorderBrushProperty, new DynamicResourceExtension("SliderThumbDisabledBorder")) },
        },
    };

    private static Style ThumbStyle(string state, string fill, string border) =>
        new(x => x.OfType<Slider>().Template().OfType<Thumb>().Class(state))
        {
            Setters =
            {
                new Setter(TemplatedControl.BackgroundProperty, new DynamicResourceExtension(fill)),
                new Setter(TemplatedControl.BorderBrushProperty, new DynamicResourceExtension(border)),
            },
        };

    // Template priority, so the state styles above (and the looks) can recolour the part.
    private static void BindBrush(AvaloniaObject target, AvaloniaProperty<IBrush?> property, Control host, string key) =>
        target.Bind(property, host.GetResourceObservable(key), BindingPriority.Template);

    private static Control CheckBoxContent(CheckBox box, INameScope scope)
    {
        // The Core look class makes the "CheckBox" look rules (checked tick, hover border) apply to this control.
        box.Classes.Add("CheckBox");
        var tick = new Path
        {
            Name = "check", Data = Geometry.Parse(TickPath), StrokeThickness = TickStroke,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        BindBrush(tick, Shape.StrokeProperty, tick, "AccentBrush");
        // Template priority, so the look's checked rule (a style) can show the tick.
        tick.SetValue(Visual.IsVisibleProperty, false, BindingPriority.Template);
        var square = new Border
        {
            Name = "box", Width = BoxSize, Height = BoxSize, CornerRadius = new CornerRadius(BoxCorner), BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center, Child = tick,
        };
        // Template priority, so the look's hover rule (a style) can recolour the border.
        BindBrush(square, Border.BackgroundProperty, square, "WindowBrush");
        BindBrush(square, Border.BorderBrushProperty, square, "CheckBoxBorderBrush");
        DockPanel.SetDock(square, Dock.Left);
        square.RegisterInNameScope(scope); tick.RegisterInNameScope(scope);
        // A box with no content has no label, so its right edge is the box's own; an empty label would still reserve its gap.
        var row = new DockPanel { LastChildFill = true, Children = { square } };
        if (box.Content is not null) row.Children.Add(LabelPresenter(box, scope));
        return row;
    }

    private static Control RadioContent(RadioButton radio, INameScope scope)
    {
        var ring = new Ellipse
        {
            Name = "ring", Width = RadioSize, Height = RadioSize, StrokeThickness = RadioStrokeWidth,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        BindBrush(ring, Shape.StrokeProperty, ring, "RadioButtonOuterEllipseStroke");
        BindBrush(ring, Shape.FillProperty, ring, "RadioButtonOuterEllipseFill");
        var dot = new Ellipse
        {
            Name = "dot", Width = RadioDotSize, Height = RadioDotSize,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        dot.Classes.Add(RadioDotClass);
        BindBrush(dot, Shape.FillProperty, dot, "RadioButtonCheckGlyphFill");
        // Template priority, so the checked style above (a style) can show the dot.
        dot.SetValue(Visual.IsVisibleProperty, false, BindingPriority.Template);
        var circle = new Grid { Width = RadioSize, Height = RadioSize, VerticalAlignment = VerticalAlignment.Center, Children = { ring, dot } };
        DockPanel.SetDock(circle, Dock.Left);
        ring.RegisterInNameScope(scope); dot.RegisterInNameScope(scope);
        return new DockPanel { LastChildFill = true, Children = { circle, LabelPresenter(radio, scope) } };
    }

    // The label shows the host's content and foreground, LabelGap to the right of the box.
    private static ContentPresenter LabelPresenter(ContentControl host, INameScope scope)
    {
        var label = new ContentPresenter { Name = "label", Margin = new Thickness(LabelGap, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        label.Bind(ContentPresenter.ContentProperty, host.GetObservable(ContentControl.ContentProperty), BindingPriority.Template);
        label.Bind(ContentPresenter.ContentTemplateProperty, host.GetObservable(ContentControl.ContentTemplateProperty), BindingPriority.Template);
        label.Bind(ContentPresenter.ForegroundProperty, host.GetObservable(TemplatedControl.ForegroundProperty), BindingPriority.Template);
        label.RegisterInNameScope(scope);
        return label;
    }

    private static Control SliderContent(Slider slider, INameScope scope)
    {
        var track = new Border
        {
            Height = TrackHeight, BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center,
        };
        BindBrush(track, Border.BackgroundProperty, slider, "SystemSliderTrack");
        BindBrush(track, Border.BorderBrushProperty, slider, "SystemSliderTrackBorder");
        var thumb = new Thumb
        {
            Name = "thumb", Width = ThumbWidth, Height = ThumbHeight, BorderThickness = new Thickness(1),
            Template = new FuncControlTemplate<Thumb>((t, _) => ThumbFace(t)),
        };
        BindBrush(thumb, TemplatedControl.BackgroundProperty, slider, "SystemSliderThumb");
        BindBrush(thumb, TemplatedControl.BorderBrushProperty, slider, "SystemSliderThumbBorder");
        var less = new RepeatButton { Name = "PART_DecreaseButton", Focusable = false, Opacity = 0 };
        var more = new RepeatButton { Name = "PART_IncreaseButton", Focusable = false, Opacity = 0 };
        var bar = new Track { Name = "PART_Track", Orientation = Orientation.Horizontal, Thumb = thumb, DecreaseButton = less, IncreaseButton = more };
        bar.Bind(RangeBase.MinimumProperty, slider.GetObservable(RangeBase.MinimumProperty), BindingPriority.Template);
        bar.Bind(RangeBase.MaximumProperty, slider.GetObservable(RangeBase.MaximumProperty), BindingPriority.Template);
        // Two-way: a thumb drag or track click moves the Track's value, which must write back to the Slider (as the scroll bar does).
        bar.Bind(RangeBase.ValueProperty, new Binding { Path = nameof(RangeBase.Value), Source = slider, Mode = BindingMode.TwoWay });
        bar.RegisterInNameScope(scope); thumb.RegisterInNameScope(scope); less.RegisterInNameScope(scope); more.RegisterInNameScope(scope);
        return new Grid { Height = SliderHeight, VerticalAlignment = VerticalAlignment.Center, Children = { track, bar } };
    }

    // The thumb face shows the thumb's own background and border, so the state styles above repaint it.
    private static Control ThumbFace(Thumb thumb)
    {
        var face = new Border();
        face.Bind(Border.BackgroundProperty, thumb.GetObservable(TemplatedControl.BackgroundProperty), BindingPriority.Template);
        face.Bind(Border.BorderBrushProperty, thumb.GetObservable(TemplatedControl.BorderBrushProperty), BindingPriority.Template);
        face.Bind(Border.BorderThicknessProperty, thumb.GetObservable(TemplatedControl.BorderThicknessProperty), BindingPriority.Template);
        return face;
    }
}
