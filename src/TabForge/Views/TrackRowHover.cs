using System.Windows;
using System.Windows.Controls;

namespace TabForge.Views;

/// <summary>
/// Owns the hover shade of one track-list row. Owns: wrapping the row content over a shade layer and
/// toggling it on pointer enter/leave. Does not own: the row's tint, stripe or selection (the timeline and row builder do).
/// </summary>
internal static class TrackRowHover
{
    /// <summary>Wraps the row content over the list's HoverBrush at half strength, so the tint and colour stripe show through.
    /// The selected track gets no shade, so its selection highlight stays the stronger one.</summary>
    internal static void Attach(TrackRowBorder row, int index, TrackTimeline timeline)
    {
        var shade = new Border { IsHitTestVisible = false, Opacity = 0 };
        shade.SetResourceReference(Border.BackgroundProperty, "HoverBrush");   // follows a theme switch
        var content = row.Child;
        row.Child = null;
        var host = new Grid();
        host.Children.Add(shade);
        host.Children.Add(content);
        row.Child = host;
        row.MouseEnter += (_, _) => shade.Opacity = timeline.SelectedTrack == index ? 0 : 0.5;
        row.MouseLeave += (_, _) => shade.Opacity = 0;
    }
}
