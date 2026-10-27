using System;
using System.Linq;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Transport;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the recording seek gate beyond the arrangement's ruler/lane click: rewind, section jumps and score cursor clicks do nothing while
/// recording and still seek when not recording. Stopping a recording is left to the existing TransportRecordingTests.
/// Does not own: the gates themselves (TransportBarView, SectionLinks, MainWindowDocumentPanes, BandView), the recording (MainWindowTransportRecord).
/// Tests: this class.
/// </summary>
public class RecordingSeekGateTests
{
    private const int TargetBar = 6;

    private sealed class Harness : IDisposable
    {
        public Harness()
        {
            Engine = new AvaloniaPlaybackSessionTests.FakeEngine();
            Ui = new AvaloniaPlaybackSessionTests.FakeUiThread();
            Sessions = new AvaloniaPlaybackSessions(Ui, () => Engine);
            Song = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
            Window = new MainWindow(first: Song, playback: Sessions);
            Window.Show();
            Window.UpdateLayout();
            Arrangement = Window.GetLogicalDescendants().OfType<ArrangementView>().First();
            Session = Sessions.For(Song)!;
        }

        public MainWindow Window { get; }
        public SongDocument Song { get; }
        public AvaloniaPlaybackSessions Sessions { get; }
        public AvaloniaPlaybackSession Session { get; }
        public AvaloniaPlaybackSessionTests.FakeEngine Engine { get; }
        public AvaloniaPlaybackSessionTests.FakeUiThread Ui { get; }
        public ArrangementView Arrangement { get; }

        /// <summary>The state the recording hooks set on the arrangement's Record button (the same check the gates read).</summary>
        public void StartRecording()
        {
            Arrangement.Transport.ShowRecording(true);
            Assert.True(Arrangement.Transport.Recording);
        }

        public void Dispose()
        {
            Song.MarkClean();
            Window.Close();
            Sessions.Dispose();
        }
    }

    private static TransportBarView.TransportButton Rewind(MainWindow main) =>
        main.GetVisualDescendants().OfType<TransportBarView.TransportButton>().First(b => b.Item.Name == "Rewind to beginning");

    private static ScorePreviewView ScoreOf(MainWindow main) =>
        main.GetLogicalDescendants().OfType<ScorePreviewView>().Single();

    // The middle of the chip: its edges are resize grab zones (TimelineInputController.SectionResize), not clicks.
    private static void ClickSection(MainWindow main, ArrangementView arrangement, int index)
    {
        var slot = arrangement.Timeline.SectionSlots()[index];
        var at = arrangement.Timeline.TranslatePoint(new Point(slot.Left + slot.Width / 2, slot.Top + 6), main)!.Value;
        main.MouseDown(at, MouseButton.Left);
        main.MouseUp(at, MouseButton.Left);
    }

    [AvaloniaFact]
    public void RewindDoesNotMoveThePlayheadWhileRecording()
    {
        using var h = new Harness();
        h.Session.Seek(4, 0);
        h.StartRecording();
        Assert.Equal(4, h.Session.Transport.PlayheadBar);

        Rewind(h.Window).Pressed!.Invoke();

        Assert.Equal(4, h.Session.Transport.PlayheadBar);
    }

    [AvaloniaFact]
    public void SectionJumpDoesNotMoveThePlayheadWhileRecording()
    {
        using var h = new Harness();
        h.Session.Seek(0, 0);
        h.StartRecording();

        ClickSection(h.Window, h.Arrangement, 1);

        Assert.Equal(0, h.Session.Transport.PlayheadBar);
    }

    [AvaloniaFact]
    public void ScoreCursorClickDoesNotMoveThePlayheadWhileRecording()
    {
        using var h = new Harness();
        h.Session.Seek(0, 0);
        h.StartRecording();

        ScoreOf(h.Window).RaiseCursorPlaced(TargetBar, 0);

        Assert.Equal(0, h.Session.Transport.PlayheadBar);
    }

    [AvaloniaFact]
    public void TheSameActionsSeekWhenNotRecording()
    {
        using var h = new Harness();
        h.Session.Seek(4, 0);
        Rewind(h.Window).Pressed!.Invoke();
        Assert.Equal(0, h.Session.Transport.PlayheadBar);

        var sectionBar = h.Song.Project.Markers.OrderBy(m => m.MeasureIndex).ElementAt(1).MeasureIndex;
        ClickSection(h.Window, h.Arrangement, 1);
        Assert.Equal(sectionBar, h.Session.Transport.PlayheadBar);

        ScoreOf(h.Window).RaiseCursorPlaced(TargetBar, 0);
        Assert.Equal(TargetBar, h.Session.Transport.PlayheadBar);
    }
}
