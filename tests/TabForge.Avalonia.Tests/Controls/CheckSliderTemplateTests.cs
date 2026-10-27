using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: that every CheckBox and Slider takes the WPF-style templates (not Fluent's accent fills) and that the look's checked rule shows the tick.
/// Tests: this class.
/// </summary>
public class CheckSliderTemplateTests
{
    [AvaloniaFact]
    public void CheckBoxHasTheWpfBoxAndShowsTheTickOnlyWhenChecked()
    {
        var box = new CheckBox { Content = "Bold" };
        var window = new Window { Content = box };
        window.Show();
        var tick = box.GetVisualDescendants().OfType<Path>().Single(p => p.Name == "check");
        Assert.Equal(15, box.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "box").Width);
        Assert.False(tick.IsVisible);
        box.IsChecked = true;
        Assert.True(tick.IsVisible);
    }

    [AvaloniaFact]
    public void SliderHasTheWpfThumbSize()
    {
        var slider = new Slider { Width = 200 };
        var window = new Window { Content = slider };
        window.Show();
        var thumb = slider.GetVisualDescendants().OfType<Thumb>().Single();
        Assert.Equal(11, thumb.Width);
        Assert.Equal(18, thumb.Height);
    }

    // The popup thumb drag writes back: the template Track is horizontal and bound two-way, so moving the Track moves the Slider.
    [AvaloniaFact]
    public void SliderTrackValueWritesBackToSlider()
    {
        var slider = new Slider { Width = 200, Minimum = 0, Maximum = 100, Value = 20 };
        var window = new Window { Content = slider };
        window.Show();
        var track = slider.GetVisualDescendants().OfType<Track>().Single(t => t.Name == "PART_Track");
        Assert.Equal(global::Avalonia.Layout.Orientation.Horizontal, track.Orientation);
        track.Value = 70;
        Assert.Equal(70, slider.Value);
    }
}
