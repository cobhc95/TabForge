using System.Linq;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.LogicalTree;
using global::Avalonia.Threading;
using global::Avalonia.VisualTree;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the status bar's right end in the real preview window with the demo song: the audio interface button is shown and inside the bar,
//   also at the minimum window width, and the MIDI output name is not shown there.
// Does not own: the view's text rules (StatusBarViewTests), the presenters.
public sealed class StatusBarRightEndTests
{
    [AvaloniaFact]
    public void DemoSongShowsTheAudioInterfaceInsideTheBarAtStartup()
    {
        var main = new MainWindow(first: new SongDocument(() => null) { Project = DemoSongFactory.Create() });
        main.Show();
        Dispatcher.UIThread.RunJobs();
        AssertRightEndShowsAudioInterface(main);
        main.Close();
    }

    [AvaloniaFact]
    public void DemoSongShowsTheAudioInterfaceInsideTheBarAtMinimumWidth()
    {
        var main = new MainWindow(first: new SongDocument(() => null) { Project = DemoSongFactory.Create() })
        {
            Width = MainWindow.MinimumWidth,
            Height = MainWindow.MinimumHeight,
        };
        main.Show();
        Dispatcher.UIThread.RunJobs();
        AssertRightEndShowsAudioInterface(main);
        main.Close();
    }

    private static void AssertRightEndShowsAudioInterface(MainWindow main)
    {
        var bar = main.GetLogicalDescendants().OfType<StatusBarView>().Single();
        var output = bar.GetLogicalDescendants().OfType<TextBlock>().Single(t => t.Name == "StatusOutput");
        var audio = bar.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "AudioDevice");
        Assert.True(audio.IsVisible);
        Assert.False(output.IsVisible);
        var audioRight = audio.TranslatePoint(new Point(audio.Bounds.Width, 0), bar)?.X ?? -1;
        Assert.True(audioRight > 0 && audioRight <= bar.Bounds.Width, $"audio right edge {audioRight} outside bar width {bar.Bounds.Width}");
    }
}
