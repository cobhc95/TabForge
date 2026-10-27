using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Transport;
using TabForge.Avalonia.Views;
using TabForge.Presets;
using TabForge.Views.Score;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: a press on the arrangement timeline moving the playhead and seeking playback to the pressed bar.
/// Does not own: the press gestures (TimelineInputController), the playback seek (AvaloniaPlaybackSession).
/// Tests: ArrangementView, TimelineInputController.
/// </summary>
public class ArrangementSeekTests
{
    [AvaloniaFact]
    public void ClickingABarOnALaneSeeksPlaybackToThatBar()
    {
        var song = DemoSongFactory.Create();
        var doc = new ScorePreviewEditor.PreviewDocument(song);
        using var session = new AvaloniaPlaybackSession(new AvaloniaPlaybackSessionTests.FakeEngine(), new AvaloniaPlaybackSessionTests.FakeUiThread(), () => song);
        var arrangement = new ArrangementView(song, doc);
        arrangement.Show(doc);
        arrangement.BindPlayback(session);
        var window = new Window { Width = 1300, Height = 400, Content = arrangement };
        window.Show();
        window.UpdateLayout();   // the lane point is read from the arranged timeline, below the transport bar
        var timeline = arrangement.Timeline;
        var at = timeline.TranslatePoint(new Point(timeline.BarX(5) + 5, timeline.RowTop(1) + 10), window)!.Value;
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Assert.Equal(5, session.Transport.PlayheadBar);
        window.Close();
    }
}
