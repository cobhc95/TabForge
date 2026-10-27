using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using TabForge.Avalonia.Platform;
using TabForge.Documents;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the score's opening position: a shown song is scrolled so its first system (staff and tab) is in view, not the title block.
/// Does not own: the zoom on open (ScorePreviewZoomOnOpenTests) or the cursor on open (ScorePreviewEditor.ShowDocument).
/// Tests: ScorePreviewView.Show, ScorePreviewView.RevealFirstSystem.
/// </summary>
public class ScorePreviewFirstSystemTests
{
    [AvaloniaFact]
    public void ShownSongOpensScrolledPastItsTitleBlock()
    {
        var song = new SongDocument(() => null) { Project = SongDocument.BlankProject(), ZoomFactor = 1 };
        var score = new ScorePreviewView();
        var viewer = new ScrollViewer { Content = score, Width = 600, Height = 300 };
        var window = new Window { Content = viewer, Width = 600, Height = 300 };
        window.Show();
        score.Show(song);
        Dispatcher.UIThread.RunJobs();
        Assert.True(viewer.Offset.Y > 0);
        window.Close();
    }

    [AvaloniaFact]
    public void ShownSongShowsItsFirstStaffAfterOneRenderedFrame()
    {
        var song = new SongDocument(() => null) { Project = SongDocument.BlankProject(), ZoomFactor = 1 };
        var score = new ScorePreviewView();
        var viewer = new ScrollViewer { Content = score, Width = 600, Height = 300 };
        var window = new Window { Content = viewer, Width = 600, Height = 300 };
        window.Show();
        score.Show(song);
        using var frame = window.CaptureRenderedFrame();
        Assert.True(viewer.Offset.Y > 0);
        Assert.InRange(score.FirstStaffTop - viewer.Offset.Y, 0, viewer.Viewport.Height);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void FirstStaffAndTabStaffAreInViewInTheFirstFrameWithNoTitleBlockAbove(bool light)
    {
        var app = global::Avalonia.Application.Current!;
        app.RequestedThemeVariant = light ? global::Avalonia.Styling.ThemeVariant.Light : global::Avalonia.Styling.ThemeVariant.Dark;
        var song = new SongDocument(() => null) { Project = SongDocument.BlankProject(), ZoomFactor = 1 };
        var score = new ScorePreviewView();
        var viewer = new ScrollViewer { Content = score, Width = 600, Height = 300 };
        var window = new Window { Content = viewer, Width = 600, Height = 300 };
        try
        {
            window.Show();
            score.Show(song);
            using var frame = window.CaptureRenderedFrame() ?? throw new System.InvalidOperationException("The window rendered no frame.");
            frame.Save(DialogTestKit.RenderPath(nameof(ScorePreviewFirstSystemTests), $"first-system-{(light ? "light" : "dark")}.png"));
            var top = score.FirstStaffTop - viewer.Offset.Y;
            Assert.InRange(top, 0, viewer.Viewport.Height);
            Assert.InRange(score.FirstTabBottom - viewer.Offset.Y, top, viewer.Viewport.Height);
            Assert.True(viewer.Offset.Y > 0, "The title block is above the view.");
        }
        finally
        {
            window.Close();
            app.RequestedThemeVariant = global::Avalonia.Styling.ThemeVariant.Dark;
        }
    }
}
