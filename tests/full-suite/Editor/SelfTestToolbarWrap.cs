using System.Windows;
using System.Windows.Controls;
using TabForge.Controllers;
using TabForge.Models;
using TabForge.Views;

namespace TabForge;

/// <summary>The track list toolbar's second row at a narrow controls column (part of <see cref="SelfTest"/>).</summary>
public static partial class SelfTest
{
    /// <summary>One row at the default width; two whole rows at the minimum; a narrower request is raised; one row again when wide; compact mode restores either form.</summary>
    private static void TestToolbarWrap()
    {
        var song = new SongProject();
        var tracks = new TrackController();
        for (var i = 0; i < 2; i++) song.Tracks.Add(tracks.CreateTrack(song, TrackKind.Guitar));
        var panel = new ArrangementPanel();
        panel.Bind(song, Array.Empty<Playback.MidiOutputDeviceInfo>());
        var capsule = new StackPanel { Orientation = Orientation.Horizontal };
        for (var i = 0; i < 8; i++) capsule.Children.Add(new Button { Width = 32, Height = 30 });
        panel.SetPlaybar(new Border { Child = capsule });
        var window = new Window { Content = panel, Width = 1100, Height = 400 };
        using var alive = KeepAlive();
        try
        {
            ShowTestWindow(window);
            var toolbar = panel.Toolbar;
            var header = (FrameworkElement)((FrameworkElement)toolbar.Parent).Parent;
            var children = toolbar.Children.Cast<UIElement>().ToList();
            void Area(double width) { var s = panel.ColumnState; panel.ColumnState = (s.order, s.widths, width); window.UpdateLayout(); PumpUi(); }
            double Column() => panel.ColumnState.area;
            List<Rect> Bounds() => children.Cast<FrameworkElement>().Where(c => c.IsVisible).Select(c => c.TransformToAncestor(panel).TransformBounds(new Rect(0, 0, c.ActualWidth, c.ActualHeight))).ToList();
            int Rows()
            {
                var rows = new List<double>();
                foreach (var b in Bounds()) { var y = b.Top + b.Height / 2; if (!rows.Any(r => Math.Abs(r - y) < 12)) rows.Add(y); }
                return rows.Count;
            }
            bool Whole() => Bounds().All(b => b.Left >= -0.5 && b.Right <= Column() + 0.5 && b.Width > 0 && b.Top >= -2.5 && b.Bottom <= header.ActualHeight + 0.5);   // the zoom pair stands 2 px above its row, as in one row
            // The timeline's ruler and section strip end where the list's header ends.
            double Misaligned() => Math.Abs(panel.TimelineForTest.TranslatePoint(new Point(0, ArrangementPanel.RulerHeight + ArrangementPanel.SectionHeight), panel).Y
                - header.TranslatePoint(new Point(0, header.ActualHeight), panel).Y);

            window.UpdateLayout(); PumpUi();
            Check("toolbar wrap: one row at the default width", Column() == ArrangementPanel.ControlsWidth && !panel.ToolbarWrapped && Rows() == 1 && Whole() && Misaligned() < 0.5, $"{Column()} rows {Rows()} need {toolbar.OneRowWidth} {string.Join(" ", Bounds().Select(b => $"{b.Left:0},{b.Top:0},{b.Width:0},{b.Height:0}"))} header {header.ActualHeight} off {Misaligned()}");
            var min = panel.MinControlsWidth;
            Area(100);
            Check("toolbar wrap: a narrower request is raised to the minimum, below the one-row width", Column() == min && min < toolbar.OneRowWidth && min >= toolbar.TwoRowWidth, $"{Column()} min {min} one {toolbar.OneRowWidth} two {toolbar.TwoRowWidth}");
            Check("toolbar wrap: two rows at the minimum, every button whole, the ruler level with the header", panel.ToolbarWrapped && Rows() == 2 && Whole() && Misaligned() < 0.5, $"rows {Rows()} whole {Whole()} off {Misaligned()}");
            panel.CompactMode = true; window.UpdateLayout(); PumpUi();
            var inCompact = panel.ToolbarWrapped;
            panel.CompactMode = false; window.UpdateLayout(); PumpUi();
            Check("toolbar wrap: compact mode from two rows restores the two rows and the order", !inCompact && panel.ToolbarWrapped && Column() == min && toolbar.Children.Cast<UIElement>().SequenceEqual(children) && Rows() == 2 && Whole() && Misaligned() < 0.5, $"{inCompact} {panel.ToolbarWrapped} {Column()} rows {Rows()} whole {Whole()} off {Misaligned()}");
            Area(ArrangementPanel.ControlsWidth);
            Check("toolbar wrap: one row again at the default width", !panel.ToolbarWrapped && Rows() == 1 && Whole() && Misaligned() < 0.5, $"rows {Rows()} off {Misaligned()}");
        }
        finally { window.Close(); }
    }
}
