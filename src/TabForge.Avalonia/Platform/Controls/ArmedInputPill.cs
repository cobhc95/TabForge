using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using TabForge.Avalonia.Platform;
using TabForge.Platform.Theme;

namespace TabForge.Avalonia.Features.Recording.View;

/// <summary>
/// The armed strip joined pill: a rounded 1 DIP outline whose left half is the monitor toggle (a speaker) and whose right half is the input
/// selector (its label and a chevron that points up while its menu is open), the halves sharing one 1 DIP divider. Each half has its own hover
/// and pressed surface (styles on the pseudo-classes, so a pointer move rebuilds nothing); the monitor half fills with the accent while on and the
/// whole pill takes the accent outline while the menu is open. No pointer cursor (only resize cursors are used); the hover fill shows it is pressable. Colours are theme brushes only.
/// Owns: the pill styles, template, glyphs and the sizing of the two halves. Does not own: the edits and placement of the controls (ArmedRowLayer), the menu (TrackInputPicker).
/// Tests: TabForge.Avalonia.Tests.ArmedInputPillTests.
/// </summary>
internal static class ArmedInputPill
{
    internal const string HalfClass = "ArmedPillHalf", MonitorClass = "ArmedPillMonitor", InputClass = "ArmedPillInput", OpenClass = "ArmedPillOpen";
    private const string Face = "ArmedPillFace";
    private const double GlyphSize = 12, ChevronWidth = 8, ChevronHeight = 4.5, ChevronGap = 6, ChevronStroke = 1.4;

    /// <summary>The styles of both halves; added once to the layer that holds the pill.</summary>
    internal static Styles Build() => new()
    {
        new Style(x => x.Is<Button>().Class(HalfClass))
        {
            Setters =
            {
                new Setter(TemplatedControl.TemplateProperty, new FuncControlTemplate((parent, scope) => BuildFace(parent, scope))),
                new Setter(TemplatedControl.ForegroundProperty, Resource("TextBrush")),
            },
        },
        new Style(x => x.Is<Button>().Class(MonitorClass)) { Setters = { new Setter(TemplatedControl.ForegroundProperty, Resource("MutedBrush")) } },
        new Style(x => x.Is<Button>().Class(MonitorClass).Class(":pointerover")) { Setters = { new Setter(TemplatedControl.ForegroundProperty, Resource("TextBrush")) } },
        new Style(x => x.Is<Button>().Class(MonitorClass).Class(":checked")) { Setters = { new Setter(TemplatedControl.ForegroundProperty, Resource("OnAccentBrush")) } },
        FaceStyle(x => x.Is<Button>().Class(HalfClass), AvaloniaPalette.TransparentBrush, "BorderSoftBrush"),
        FaceStyle(x => x.Is<Button>().Class(HalfClass).Class(":pointerover"), Resource("HoverBrush"), "ButtonBorderBrush"),
        FaceStyle(x => x.Is<Button>().Class(HalfClass).Class(":pressed"), Resource("PressBrush"), "ButtonBorderBrush"),
        // Monitor on: the speaker half is the accent, its outline too.
        FaceStyle(x => x.Is<Button>().Class(HalfClass).Class(":checked"), Resource("AccentBrush"), "AccentBrush"),
        FaceStyle(x => x.Is<Button>().Class(HalfClass).Class(":checked").Class(":pointerover"), Resource("AccentHoverBrush"), "AccentBrush"),
        FaceStyle(x => x.Is<Button>().Class(HalfClass).Class(":checked").Class(":pressed"), Resource("AccentPressBrush"), "AccentBrush"),
        // The menu is open: the pill outline is the accent (both halves carry the class).
        new Style(x => x.Is<Button>().Class(HalfClass).Class(OpenClass).Template().OfType<Border>().Name(Face))
        {
            Setters = { new Setter(Border.BorderBrushProperty, Resource("AccentBrush")) },
        },
    };

    private static DynamicResourceExtension Resource(string key) => new(key);

    private static void Bind<T>(AvaloniaObject target, AvaloniaProperty<T> property, TemplatedControl parent, AvaloniaProperty<T> source) =>
        target.Bind(property, parent.GetObservable(source), global::Avalonia.Data.BindingPriority.Template);

    private static Style FaceStyle(Func<Selector?, Selector> select, object fill, string edgeKey) =>
        new(x => select(x).Template().OfType<Border>().Name(Face))
        {
            Setters = { new Setter(Border.BackgroundProperty, fill), new Setter(Border.BorderBrushProperty, Resource(edgeKey)) },
        };

    // One border (the face) that shows the corner radius of the half around its content; fill and outline come from the styles above.
    private static Control BuildFace(TemplatedControl parent, INameScope scope)
    {
        var presenter = new ContentPresenter { Name = "ArmedPillContent" };
        Bind(presenter, ContentPresenter.ContentProperty, parent, ContentControl.ContentProperty);
        Bind(presenter, ContentPresenter.HorizontalContentAlignmentProperty, parent, ContentControl.HorizontalContentAlignmentProperty);
        Bind(presenter, ContentPresenter.VerticalContentAlignmentProperty, parent, ContentControl.VerticalContentAlignmentProperty);
        Bind(presenter, ContentPresenter.ForegroundProperty, parent, TemplatedControl.ForegroundProperty);
        presenter.RegisterInNameScope(scope);
        var face = new Border { Name = Face, BorderThickness = UiMetrics.Sides(1, 1, 1, 1, 1), Child = presenter };
        Bind(face, Border.CornerRadiusProperty, parent, TemplatedControl.CornerRadiusProperty);
        face.RegisterInNameScope(scope);
        return face;
    }

    /// <summary>The speaker glyph of the monitor half, filled with the foreground of the button (muted off, on-accent while on).</summary>
    private static Control Speaker(Button owner)
    {
        var glyph = new Path { Data = Geometry.Parse(IconGeometry.Speaker), Stretch = Stretch.Uniform, Width = GlyphSize, Height = GlyphSize, IsHitTestVisible = false };
        glyph.Bind(Shape.FillProperty, owner.GetObservable(TemplatedControl.ForegroundProperty));
        return glyph;
    }

    /// <summary>The chevron of the input half (down; the half turns it up while its menu is open), drawn in the foreground of the button.</summary>
    private static Path Chevron(Button owner)
    {
        var chevron = new Path
        {
            Data = Geometry.Parse("M 0,0 L 4,4 L 8,0"), Width = ChevronWidth, Height = ChevronHeight, StrokeThickness = ChevronStroke, StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false,
        };
        chevron.WithMargin(ChevronGap, 0, 0, 0);
        chevron.Bind(Shape.StrokeProperty, owner.GetObservable(TemplatedControl.ForegroundProperty));
        return chevron;
    }

    /// <summary>Makes <paramref name="monitor"/> the left half of the pill and <paramref name="input"/> its right half, and follows the open state of the input menu.</summary>
    internal static void Join(ToggleButton monitor, TrackInputPicker input)
    {
        monitor.Classes.Add(HalfClass);
        monitor.Classes.Add(MonitorClass);
        monitor.Content = Speaker(monitor);
        monitor.HorizontalContentAlignment = HorizontalAlignment.Center;
        monitor.VerticalContentAlignment = VerticalAlignment.Center;
        input.Classes.Add(HalfClass);
        input.Classes.Add(InputClass);
        var chevron = Chevron(input);
        input.UsePill(chevron);
        if (input.Flyout is { } menu)
        {
            menu.Opened += (_, _) => SetOpen(monitor, input, chevron, true);
            menu.Closed += (_, _) => SetOpen(monitor, input, chevron, false);
        }
    }

    private static void SetOpen(Control monitor, Control input, Control chevron, bool open)
    {
        monitor.Classes.Set(OpenClass, open);
        input.Classes.Set(OpenClass, open);
        chevron.RenderTransform = open ? new RotateTransform(180) : null;
    }

    /// <summary>Sizes the two halves to a pill <paramref name="height"/> tall: the monitor half is the left cap, the input half the right cap (the radius of each cap is half the height).</summary>
    internal static void Fit(ToggleButton monitor, TrackInputPicker input, double height)
    {
        var radius = height / 2;
        if (monitor.Height != height) monitor.Height = height;
        var leftCap = new CornerRadius(radius, 0, 0, radius);
        if (monitor.CornerRadius != leftCap) monitor.CornerRadius = leftCap;
        if (input.Height != height) input.Height = height;
        var rightCap = new CornerRadius(0, radius, radius, 0);
        if (input.CornerRadius != rightCap) input.CornerRadius = rightCap;
    }
}
