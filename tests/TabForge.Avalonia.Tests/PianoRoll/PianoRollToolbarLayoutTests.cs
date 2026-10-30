using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the piano roll toolbar's two-row layout: every button, dropdown, value box and check in a row shares one height and one vertical centre.
// Does not own: the edits, the commit rules or the other gestures (PianoRollViewTests).
// Tests: PianoRollView.BuildToolbar, PianoRollView.BuildInfoLine, PianoRollToolbarLayout.
public sealed class PianoRollToolbarLayoutTests
{
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

    private static void RowSharesOneHeightAndCentre(Window window, string rowName)
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
        var centres = controls
            .Select(c => c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), window))
            .ToList();
        Assert.All(centres, p => Assert.NotNull(p));
        var ys = centres.Select(p => p!.Value.Y).ToList();
        Assert.True(ys.Max() - ys.Min() < 1.0, rowName + " vertical centres differ: " + string.Join(", ", ys.Select(y => y.ToString("0.0"))));
    }

    [AvaloniaFact]
    public void EachToolbarRowSharesOneHeightAndOneVerticalCentre()
    {
        var window = Show(PianoRollSongs.Midi());
        try
        {
            RowSharesOneHeightAndCentre(window, PianoRollToolbarLayout.Row1Name);
            RowSharesOneHeightAndCentre(window, PianoRollToolbarLayout.Row2Name);
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
            var row1 = window.GetVisualDescendants().OfType<DockPanel>().Single(p => p.Name == PianoRollToolbarLayout.Row1Name);
            var row2 = window.GetVisualDescendants().OfType<DockPanel>().Single(p => p.Name == PianoRollToolbarLayout.Row2Name);
            Assert.Contains(row2.GetVisualDescendants(), c => ReferenceEquals(c, follow));
            Assert.DoesNotContain(row1.GetVisualDescendants(), c => ReferenceEquals(c, follow));
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
            var row = window.GetVisualDescendants().OfType<DockPanel>().Single(p => p.Name == PianoRollToolbarLayout.Row1Name);
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
