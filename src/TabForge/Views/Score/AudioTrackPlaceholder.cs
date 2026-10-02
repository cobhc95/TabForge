using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TabForge.Services;

namespace TabForge.Views.Score;

// Owns: the centred "Audio track — no notation" message drawn in the score area while an audio track is selected.
// Does not own: the editor's drawing otherwise (TabEditorControl.Rendering) or the editing guard (EditorGuard).
// Tests: TestAddTrackLane (picture capture), TestAudioTrackEditorGuards.
internal static class AudioTrackPlaceholder
{
    /// <summary>Top of the message in page units: the middle of the visible score area (the scroll viewer around the editor), else a fixed spot near the top.</summary>
    public static double TopY(FrameworkElement editor, double zoom)
    {
        var viewport = 220.0; var offset = 0.0;
        for (DependencyObject? d = editor; d is not null; d = VisualTreeHelper.GetParent(d))
            if (d is ScrollViewer { ViewportHeight: > 0 } scroll) { viewport = scroll.ViewportHeight; offset = scroll.VerticalOffset; break; }
        return Math.Max(40, (offset + viewport / 2) / zoom - 14);
    }

    public static void Draw(DrawingContext dc, FrameworkElement editor, double zoom, double centreX, Brush brush) =>
        ScoreText.DrawCenteredIn(ScoreTextArea.Header, dc, EditorGuard.Message, centreX, TopY(editor, zoom), 22, brush, FontWeights.SemiBold, "Segoe UI");
}
