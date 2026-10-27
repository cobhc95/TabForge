using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the timeline's scattered white cells lighting the score's shown track as display-only selected bars (no editing selection, no loop), and Esc clearing them.
// Does not own: the highlight rules (TimelineBarHighlightModelTests) or the loop rules (SelectionLoopLinkTests).
// Tests: SelectionLoopLink, BarHighlight.ScatteredBars, ScorePreviewEditor.SetMirrorBars.
public class ScoreCellMirrorTests
{
    private static (MainWindow Window, ScorePreviewView Score, BarHighlight Highlight, SelectionModel Model) Open()
    {
        var document = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        var window = new MainWindow(first: document);
        window.Show();
        var score = window.GetLogicalDescendants().OfType<ScorePreviewView>().Single();
        return (window, score, document.Highlight, document.Selection);
    }

    [AvaloniaFact]
    public void TimelineCellsDoNotLightTheScoreOrTouchItsSelection()
    {
        var (window, score, highlight, model) = Open();
        var track = score.Editor.SelectedTrackIndex;
        highlight.Set(track, 2);
        Assert.False(score.Editor.HasMirrorBars);   // disabled 24 Oct: timeline and score selections are separate
        highlight.Toggle(track, 5);
        Assert.False(score.Editor.HasMirrorBars);
        Assert.Equal(new[] { (2, 2), (5, 5) }, highlight.ScatteredBars(track));
        Assert.False(score.Editor.HasSelection);
        score.Editor.SelectAll();
        highlight.Toggle(track, 7);
        Assert.False(score.Editor.HasSelection);   // 25 Oct: a new timeline selection deselects the notes (the loop stays)
        Assert.False(model.HasLoop);
        window.Close();
    }

    [AvaloniaFact]
    public void ClearingTheWhiteCellsLeavesNoMirror()
    {
        var (window, score, highlight, _) = Open();
        var track = score.Editor.SelectedTrackIndex;
        highlight.Set(track, 1);
        highlight.Toggle(track, 4);
        Assert.False(score.Editor.HasMirrorBars);
        highlight.Clear();   // what the first Esc does to the cells
        Assert.False(score.Editor.HasMirrorBars);
        window.Close();
    }
}
