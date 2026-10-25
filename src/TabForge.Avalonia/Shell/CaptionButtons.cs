using System;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using TabForge.Platform;
using TabForge.Platform.Theme;

namespace TabForge.Avalonia.Shell;

/// <summary>
/// The title row's buttons on the right: Record video, Monitor FX and Settings (40 px wide), then Minimise, Maximise or Restore, and Close (46 px wide),
/// all 44 px tall, in the WPF order. A button takes the theme look of its class (ChromeCaptionButton, ChromeCloseButton), so hover and press
/// colours come from the look table; glyph colours are palette keys.
/// Owns: building the buttons and their glyphs. Does not own: what a button does (the caller's actions and IWindowChrome) or the looks (Core).
/// Tests: TabForge.Avalonia.Tests.TitleBarTests.
/// </summary>
public static class CaptionButtons
{
    ///<summary>The width the six buttons take: the tab strip fits in the window width less this.</summary>
    public const double ReservedWidth = 3 * ChromeMetrics.CaptionIconButtonWidth + 3 * ChromeMetrics.CaptionSystemButtonWidth;

    // The shared icons (IconGeometry, 16 x 16 design box) are stretched uniformly to the glyph size. The minimise bar is Avalonia's own flat bar, drawn unstretched in the 10 px box.
    private const string MinimiseGeometry = "M 0,5 L 10,5";

    // The Record video glyph: the camera outline, and the red recording dot (9 px with a 1.5 px ring) over the box's top right corner, as WPF draws it.
    private const double CameraGlyphWidth = 17, CameraGlyphHeight = 14, RecordGlyphBox = 20, RecordDotSize = 9, RecordDotRingWidth = 1.5;

    // The stroke of a caption glyph, as the WPF window draws the camera and the system glyphs.
    private const double GlyphStroke = 1.4;
    // The camera outline's stroke on screen, DIP: the toggles' stroke (MenuRowToggles.IconStroke), so the camera matches them.
    private const double CameraStroke = 1.6;

    /// <summary>The six buttons in a row, named Record, MonitorFx, Settings, Minimise, Maximise and Close.</summary>
    public static StackPanel Build(IWindowChrome chrome, Action recordVideo, Action openSettings, Func<string, bool>? nativeOwnsClick = null, MonitorFxBinding? monitor = null)
    {
        var maxGlyph = Stroked(IconGeometry.Maximise, "ChromeGlyph");
        var row = new StackPanel
        {
            Name = "CaptionButtons", Orientation = Orientation.Horizontal, Height = ChromeMetrics.CaptionRowHeight,
            Children =
            {
                CaptionButton("Record", "Record video", "ChromeCaptionButton", ChromeMetrics.CaptionIconButtonWidth, Camera(), recordVideo, nativeOwnsClick),
                MonitorFxCaptionButton.Build(monitor, nativeOwnsClick),
                CaptionButton("Settings", "Settings", "ChromeCaptionButton", ChromeMetrics.CaptionIconButtonWidth, Gear(), openSettings, nativeOwnsClick),
                CaptionButton("Minimise", "Minimise", "ChromeCaptionButton", ChromeMetrics.CaptionSystemButtonWidth, Stroked(MinimiseGeometry, "ChromeGlyph", Stretch.None), chrome.Minimize, nativeOwnsClick),
                CaptionButton("Maximise", "Maximise", "ChromeCaptionButton", ChromeMetrics.CaptionSystemButtonWidth, maxGlyph, chrome.ToggleMaximise, nativeOwnsClick),
                CaptionButton("Close", "Close", "ChromeCloseButton", ChromeMetrics.CaptionSystemButtonWidth, Stroked(IconGeometry.Close, "ChromeGlyph"), chrome.Close, nativeOwnsClick),
            },
        };
        // The close cross turns white on the red hover and press fills, in both themes.
        foreach (var state in new[] { ":pointerover", ":pressed" })
        {
            var hot = new Style(x => x.OfType<Button>().Class("ChromeCloseButton").Class(state).Descendant().OfType<Path>());
            hot.Setters.Add(new Setter(Shape.StrokeProperty, new DynamicResourceExtension("MainCloseGlyph")));
            row.Styles.Add(hot);
        }
        // The maximise button shows the restore glyph while the window is maximised.
        chrome.StateChanged += (_, _) => maxGlyph.Data = Geometry.Parse(chrome.State == WindowChromeState.Maximised ? IconGeometry.Restore : IconGeometry.Maximise);
        return row;
    }

    /// <summary>Shows or hides the red recording dot on the Record video button (the dot is the button's only Ellipse).</summary>
    public static void ShowRecordingDot(Button record, bool recording) =>
        record.GetLogicalDescendants().OfType<Ellipse>().Single().IsVisible = recording;

    /// <summary>Shows a native hover or press on a caption button with the pointer's visuals (the look table styles :pointerover and :pressed).</summary>
    public static void ShowState(Button button, bool hot, bool pressed)
    {
        var classes = (IPseudoClasses)button.Classes;
        classes.Set(":pointerover", hot);
        classes.Set(":pressed", pressed);
    }

    internal static Button CaptionButton(string name, string label, string look, double width, Control glyph, Action click, Func<string, bool>? nativeOwnsClick)
    {
        var button = new Button
        {
            Name = name, Width = width, Height = ChromeMetrics.CaptionRowHeight, Padding = new Thickness(0), BorderThickness = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
            Background = AvaloniaPalette.TransparentBrush, Content = glyph, Focusable = false,
        };
        button.Classes.Add(look);
        AutomationProperties.SetName(button, label);
        ToolTip.SetTip(button, label);
        button.Click += (_, _) => { if (nativeOwnsClick?.Invoke(name) != true) click(); };
        return button;
    }

    private static Path Stroked(string geometry, string brushKey, Stretch stretch = Stretch.Uniform)
    {
        var path = new Path
        {
            Data = Geometry.Parse(geometry), Width = 10, Height = 10, Stretch = stretch, StrokeThickness = GlyphStroke,
            StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round, IsHitTestVisible = false,
        };
        path[!Shape.StrokeProperty] = new DynamicResourceExtension(brushKey);
        FollowRootScale(path, GlyphStroke);
        return path;
    }

    // The window's UI scale is a LayoutTransform on its root, which scales the stroke a second time. The stroke is divided by that scale so it
    // stays <paramref name="stroke"/> DIP on screen, and follows the scale when it changes (as MenuRowToggles.Icon does). A popup copy sits
    // outside the scaled root and keeps the unscaled stroke.
    private static void FollowRootScale(Path path, double stroke)
    {
        path.AttachedToVisualTree += (_, _) =>
        {
            var root = path.FindAncestorOfType<LayoutTransformControl>();
            if (root is null) return;
            void Follow() => path.StrokeThickness = stroke / (root.LayoutTransform is ScaleTransform scale ? scale.ScaleX : 1);
            Follow();
            root.LayoutUpdated += (_, _) => Follow();
        };
    }

    // The camera outline with the recording dot over its top right corner (hidden until a recording runs).
    private static Grid Camera()
    {
        // The outline sits in a Viewbox, as the menu row's icons do (MenuRowToggles.Icon): the Path's Uniform stretch first normalises the
        // geometry into its own bounds (the camera's units start at 2,6), then the Viewbox fits those bounds into the WPF 17 x 14 box and
        // centres them in it. Left / centre is the WPF window's placement, so the dot overhangs the camera's top right corner as it does there.
        var data = Geometry.Parse(IconGeometry.Camera);
        // The Viewbox's own scale for this outline (its width and height limits, as MenuRowToggles.Icon takes them): the stroke is set in the
        // outline's units so it is CameraStroke DIP on screen, the toggles' stroke. The window's UI-scale LayoutTransform scales it again, so
        // it is divided by that scale too, and follows it when the scale changes (as the toggles do).
        var viewScale = Math.Min(CameraGlyphWidth / data.Bounds.Width, CameraGlyphHeight / data.Bounds.Height);
        var stroke = viewScale > 0 ? CameraStroke / viewScale : CameraStroke;
        var outline = new Path
        {
            Data = data, Width = CameraGlyphWidth, Height = CameraGlyphHeight, Stretch = Stretch.Uniform, StrokeThickness = stroke,
            StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round, IsHitTestVisible = false,
        };
        outline[!Shape.StrokeProperty] = new DynamicResourceExtension("ChromeGlyph");
        FollowRootScale(outline, stroke);
        var glyph = new Viewbox
        {
            Width = CameraGlyphWidth, Height = CameraGlyphHeight, Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false, Child = outline,
        };
        var dot = new Ellipse
        {
            Name = "VideoRecDot", Width = RecordDotSize, Height = RecordDotSize, StrokeThickness = RecordDotRingWidth, IsVisible = false,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false,
        };
        dot[!Shape.FillProperty] = new DynamicResourceExtension("Danger");
        dot[!Shape.StrokeProperty] = new DynamicResourceExtension("ChromeStrip");
        return new Grid { Width = RecordGlyphBox, Height = RecordGlyphBox, IsHitTestVisible = false, Children = { glyph, dot } };
    }

    private static Path Gear()
    {
        var path = new Path { Data = Geometry.Parse(IconGeometry.Cog), Width = 16, Height = 16, Stretch = Stretch.Uniform, IsHitTestVisible = false };
        path[!Shape.FillProperty] = new DynamicResourceExtension("ChromeGlyph");
        return path;
    }
}
