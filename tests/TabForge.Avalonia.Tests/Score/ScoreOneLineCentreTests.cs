using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using TabForge.Avalonia.Platform;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the one-line score sits in the middle of the viewer's height when its line is shorter than the viewer (SC-12), as the WPF one-line view
//   centres its page; the page view keeps its top.
// Does not own: the layout of the line (Core ScoreLayoutEngine), the viewer (ScrollViewer).
// Tests: ScoreOneLineCentreTests (this file).
public class ScoreOneLineCentreTests
{
    [AvaloniaFact]
    public void TheOneLineScoreIsCentredVerticallyInTheViewer()
    {
        var score = new ScorePreviewView();
        var viewer = new ScrollViewer { Content = score, Width = 900, Height = 700, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        var window = new Window { Width = 900, Height = 700, Content = viewer };
        window.Show();
        score.Probe.HorizontalScroll = true;
        score.Refresh();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var content = score.Probe.ContentSize().Height * score.Zoom;
        Assert.True(content < viewer.Viewport.Height, "the one-line score is shorter than the viewer");
        Assert.InRange(score.Margin.Top, Math.Floor((viewer.Viewport.Height - content) / 2) - 1, Math.Floor((viewer.Viewport.Height - content) / 2) + 1);
        Assert.True(score.Margin.Top > 0);

        score.Probe.HorizontalScroll = false;
        score.Refresh();
        Assert.Equal(0, score.Margin.Top);
        window.Close();
    }
}
