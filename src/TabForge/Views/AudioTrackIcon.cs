using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace TabForge.Views;

// Owns: the waveform icon of an audio track (track-list row, properties picture). Does not touch the instrument icons.
// Tests: TestAddTrackLane (an audio row shows it).
/// <summary>A small drawn waveform: vertical bars of different heights, filled with a theme brush (follows light and dark).</summary>
internal static class AudioTrackIcon
{
    private static readonly double[] Heights = { 0.30, 0.62, 0.95, 0.55, 0.80, 0.40, 0.66, 0.26 };
    private static readonly Geometry Bars = Build();

    private static Geometry Build()
    {
        var group = new GeometryGroup();
        const double barWidth = 2.0, gap = 1.2;
        for (var i = 0; i < Heights.Length; i++)
        {
            var h = Heights[i] * 16;
            group.Children.Add(new RectangleGeometry(new Rect(i * (barWidth + gap), (16 - h) / 2, barWidth, h), 1, 1));
        }
        group.Freeze();
        return group;
    }

    /// <summary>The icon at <paramref name="size"/> device-independent pixels, filled with the theme brush <paramref name="brushKey"/>.</summary>
    public static FrameworkElement Element(double size, string brushKey = "LegibleBrush")
    {
        var path = new Path { Data = Bars, Stretch = Stretch.Uniform, Width = size, Height = size * 0.8, IsHitTestVisible = false };
        path.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, brushKey);
        System.Windows.Automation.AutomationProperties.SetName(path, "Audio track");
        return path;
    }
}
