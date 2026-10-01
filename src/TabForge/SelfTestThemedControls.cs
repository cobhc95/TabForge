using System.Windows;
using System.Windows.Controls;

namespace TabForge;

/// <summary>Themed CheckBox wraps long labels (Track properties' tint row); the themed progress bar has its parts.</summary>
public static partial class SelfTest
{
    private static void TestThemedCheckBoxAndProgressBar()
    {
        TextBlock Label(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap };
        var shortBox = new CheckBox { Content = Label("Tint") };
        var longBox = new CheckBox { Content = Label("Tint the track's row and lane with its colour") };
        shortBox.Measure(new Size(250, double.PositiveInfinity));
        longBox.Measure(new Size(250, double.PositiveInfinity));
        Check("check box: a long label wraps inside a 250 px column instead of being cut off",
            longBox.DesiredSize.Width <= 250.5 && longBox.DesiredSize.Height > shortBox.DesiredSize.Height + 4,
            $"{longBox.DesiredSize.Width:0}x{longBox.DesiredSize.Height:0} vs {shortBox.DesiredSize.Height:0}");

        var bar = new ProgressBar { Maximum = 1, Value = 0.5 };
        bar.SetResourceReference(FrameworkElement.StyleProperty, "ThemedProgressBar");
        bar.Measure(new Size(200, 40));
        bar.Arrange(new Rect(0, 0, 200, 10));
        bar.ApplyTemplate();
        Check("progress bar: the themed style has a track and an indicator part",
            bar.Template?.FindName("PART_Track", bar) is not null && bar.Template.FindName("PART_Indicator", bar) is not null);
    }
}
