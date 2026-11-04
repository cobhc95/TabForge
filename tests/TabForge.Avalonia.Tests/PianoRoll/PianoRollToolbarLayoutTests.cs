using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the piano roll toolbar's compact shared flow and consistent control heights.
// Does not own: the edits, the commit rules or the other gestures (PianoRollViewTests).
// Tests: PianoRollView.BuildToolbar, PianoRollView.BuildInfoLine, PianoRollToolbarLayout.
public sealed class PianoRollToolbarLayoutTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void AllGroupsShareAvailableWidthAndReflowWithoutOverlap(bool tab)
    {
        var window = Show(tab ? PianoRollSongs.Tab() : PianoRollSongs.Midi());
        try
        {
            var toolbar = window.GetVisualDescendants().OfType<DockPanel>().Single(p => p.Name == PianoRollToolbarLayout.ToolbarName);
            var flow = Assert.IsType<WrapPanel>(Assert.Single(toolbar.Children));
            var step = flow.GetVisualDescendants().OfType<ToggleButton>().Single(b => b.Name == "PianoRollStepInput");
            foreach (var stepOn in new[] { false, true })
            {
                step.IsChecked = stepOn;
                foreach (var width in new[] { 2000, 1066, 900, 520, 2000 })
                {
                    window.Width = width;
                    window.UpdateLayout();
                    var items = flow.Children.Where(c => c.IsVisible).ToList();
                    Assert.True(items.Count > 12, "Fields and view controls must join the captioned groups in the same flow.");
                    for (var i = 0; i < items.Count; i++)
                    {
                        var item = items[i];
                        Assert.True(item.Bounds.Right <= flow.Bounds.Width + 1 || item.Bounds.Width > flow.Bounds.Width);
                        for (var j = 0; j < i; j++)
                            Assert.False(item.Bounds.Deflate(0.5).Intersects(items[j].Bounds.Deflate(0.5)), "Toolbar groups overlap.");
                        if (i == 0) continue;
                        var previous = items[i - 1];
                        var sameLine = item.Bounds.Top < previous.Bounds.Bottom && previous.Bounds.Top < item.Bounds.Bottom;
                        if (sameLine)
                            Assert.True(item.Bounds.Left >= previous.Bounds.Right + flow.ItemSpacing - 1);
                        else
                            Assert.True(previous.Bounds.Right + flow.ItemSpacing + item.Bounds.Width > flow.Bounds.Width - 1,
                                $"A group wrapped despite fitting in the preceding line (width {width}, item {i}, previous {previous.Bounds}, item {item.Bounds}, flow {flow.Bounds}).");
                    }
                }
            }
        }
        finally { window.Close(); }
    }

    private static Window Show(SongDocument document) => Show(document, new FollowSettings());

    private static Window Show(SongDocument document, FollowSettings follow)
    {
        var view = new PianoRollView(new PianoRollHost { Document = () => document, Follow = () => follow });
        var window = new Window { Width = 2000, Height = 700, Content = view };
        window.Show();
        view.Show(document, 0, newVisit: true);
        window.UpdateLayout();
        return window;
    }

    private static void ToolbarControlsShareOneHeight(Window window, string rowName)
    {
        var row = window.GetVisualDescendants().OfType<DockPanel>().Single(p => p.Name == rowName);
        var controls = row.GetVisualDescendants()
            .OfType<Control>()
            .Where(c => c is Button or ComboBox or TextBox or CheckBox)
            .Where(c => c.IsVisible && c.IsEffectivelyVisible && c.Bounds.Width > 0.5)
            .ToList();
        Assert.True(controls.Count >= 5, rowName + " should hold at least five toolbar controls, found " + controls.Count + ".");
        var heights = controls.Select(c => c.Bounds.Height).ToList();
        Assert.True(heights.Max() - heights.Min() < 0.5, rowName + " heights differ: " + string.Join(", ", heights.Select(h => h.ToString("0.0"))));
    }

    [AvaloniaFact]
    public void ToolbarControlsShareOneHeightAcrossWrappedLines()
    {
        var window = Show(PianoRollSongs.Midi());
        try
        {
            ToolbarControlsShareOneHeight(window, PianoRollToolbarLayout.ToolbarName);
        }
        finally
        {
            window.Close();
        }
    }

    private static ToggleButton FollowButton(Window window) => window.GetVisualDescendants().OfType<ToggleButton>()
        .Single(b => (ToolTip.GetTip(b) as string)?.StartsWith("Follow playback", System.StringComparison.Ordinal) == true);

    [AvaloniaFact]
    public void FollowPlaybackIsOnByDefaultAndSitsWithTheZoomButtons()
    {
        var window = Show(PianoRollSongs.Midi(), new FollowSettings());
        try
        {
            var follow = FollowButton(window);
            Assert.True(follow.IsChecked == true, "Follow playback should be on with no saved value.");
            var row = window.GetVisualDescendants().OfType<DockPanel>().Single(p => p.Name == PianoRollToolbarLayout.ToolbarName);
            Assert.Contains(row.GetVisualDescendants(), c => ReferenceEquals(c, follow));
            Assert.Equal(3, follow.GetLogicalAncestors().OfType<Border>().First().GetVisualDescendants().OfType<Button>().Count());
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void SavedFollowPlaybackOffStaysOff()
    {
        var window = Show(PianoRollSongs.Midi(), new FollowSettings { FollowPlayback = false });
        try
        {
            Assert.False(FollowButton(window).IsChecked == true, "A saved off state should keep Follow playback off.");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ScaleButtonAndLockShareRowGeometry()
    {
        var window = Show(PianoRollSongs.Midi());
        try
        {
            var row = window.GetVisualDescendants().OfType<DockPanel>().Single(p => p.Name == PianoRollToolbarLayout.ToolbarName);
            foreach (var name in new[] { "PianoRollLockScale", "PianoRollScaleButton" })
            {
                var control = row.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name);
                Assert.True(System.Math.Abs(control.Bounds.Height - PianoRollToolbarLayout.ControlHeight) < 0.5, name + " height " + control.Bounds.Height);
            }
        }
        finally
        {
            window.Close();
        }
    }
}
