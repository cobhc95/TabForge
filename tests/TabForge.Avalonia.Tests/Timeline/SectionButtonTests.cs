using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Transport;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the section buttons in both places, the timeline's strip blocks and the Sections rows: hover, a press that a release turns into a jump,
/// a drag that cancels the click, the confirm flash after a jump in both places, and the Sections row that follows the playhead's section.
/// Does not own: the jump rules (SectionLinks), the strip's drag and reorder (TimelineSectionDragTests), the feedback arithmetic (SectionButtonFeedbackTests).
/// Tests: TabForge.Avalonia.Tests.SectionButtonTests.
/// </summary>
public class SectionButtonTests
{
    // Window holds the arrangement (the strip); PanelWindow holds the Sections list. Each has its own window, so pointer hits land where they are aimed.
    private sealed record Rig(SongDocument Doc, ArrangementView Arrangement, SectionsPanelView Panel, ScorePreviewView Score, Window Window, Window PanelWindow, Clock Time)
    {
        public void Close()
        {
            Window.Close();
            PanelWindow.Close();
        }
    }

    /// <summary>The hand-driven time the section feedback is measured at, so a flash does not fade while a headless click is still being processed.</summary>
    private sealed class Clock { public System.TimeSpan Now; }

    private static Rig Open(AvaloniaPlaybackSessions? sessions = null)
    {
        var doc = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        doc.MarkClean();
        var tabs = new MainWindowTabs(doc, _ => Task.FromResult(TabForge.Presenters.PromptButton.Cancel), _ => Task.FromResult(false));
        var score = new ScorePreviewView();
        score.Show(doc);
        sessions ??= new AvaloniaPlaybackSessions(new AvaloniaPlaybackSessionTests.FakeUiThread(), () => new AvaloniaPlaybackSessionTests.FakeEngine());
        var arrangement = new ArrangementView(doc.Project, doc);
        var links = new SectionLinks(tabs, score, sessions, () => arrangement);
        links.Follow(arrangement);
        var panel = links.NewPanel();
        var time = new Clock();
        panel.UseFeedbackClock(() => time.Now);
        arrangement.Timeline.SectionButtons.UseClock(() => time.Now);
        var window = new Window { Width = 1300, Height = 400, Content = arrangement };
        var panelWindow = new Window { Width = 400, Height = 500, Content = panel };
        window.Show();
        panelWindow.Show();
        window.UpdateLayout();
        panelWindow.UpdateLayout();
        return new Rig(doc, arrangement, panel, score, window, panelWindow, time);
    }

    private static Point StripPoint(Rig rig, int section) =>
        rig.Arrangement.Timeline.TranslatePoint(new Point(rig.Arrangement.Timeline.SectionSlots()[section].Left + 24, rig.Arrangement.Timeline.SectionSlots()[section].Top + 6), rig.Window)!.Value;

    private static Border[] Rows(SectionsPanelView panel) =>
        panel.GetVisualDescendants().OfType<Border>().Where(b => b.Child is Grid && b.CornerRadius == new CornerRadius(2)).ToArray();

    [AvaloniaFact]
    public void HoveringAStripBlockNamesItAndLeavingClearsIt()
    {
        var rig = Open();
        var sections = rig.Panel.Sections;
        var timeline = rig.Arrangement.Timeline;

        rig.Window.MouseMove(StripPoint(rig, 1));
        Assert.Same(sections[1], timeline.SectionButtons.Hovered);
        Assert.Null(timeline.Cursor);   // hover is shading only: the pointer shape never changes (see NoHoverCursorTests)

        rig.Window.MouseMove(new Point(timeline.BarX(4) + 4, timeline.RowTop(2) + 10));
        Assert.Null(timeline.SectionButtons.Hovered);
        Assert.Null(timeline.Cursor);
        rig.Close();
    }

    [AvaloniaFact]
    public void PressAndReleaseOnAStripBlockJumpsAndFlashesItInBothPlaces()
    {
        var rig = Open();
        var sections = rig.Panel.Sections;
        var at = StripPoint(rig, 1);
        rig.Window.MouseDown(at, MouseButton.Left);
        Assert.Equal(1, rig.Arrangement.Timeline.SectionButtons.PressDepth(sections[1]), 6);
        rig.Window.MouseUp(at, MouseButton.Left);

        Assert.Equal(sections[1].MeasureIndex, rig.Score.Editor.SelectedMeasure);
        Assert.True(rig.Arrangement.Timeline.SectionButtons.FlashAlpha(sections[1]) > 0);
        Assert.True(rig.Panel.FlashStrength(sections[1]) > 0);
        Assert.Equal(0, rig.Arrangement.Timeline.SectionButtons.FlashAlpha(sections[0]));
        rig.Close();
    }

    [AvaloniaFact]
    public void ClickingASectionsRowFlashesItsRowAndItsStripBlock()
    {
        var rig = Open();
        var sections = rig.Panel.Sections;
        var row = Rows(rig.Panel)[2];
        var at = row.TranslatePoint(new Point(10, 5), rig.PanelWindow)!.Value;
        rig.PanelWindow.MouseDown(at, MouseButton.Left);
        rig.PanelWindow.MouseUp(at, MouseButton.Left);

        Assert.Equal(sections[2].MeasureIndex, rig.Score.Editor.SelectedMeasure);
        Assert.True(rig.Panel.FlashStrength(sections[2]) > 0);
        Assert.True(rig.Arrangement.Timeline.SectionButtons.FlashAlpha(sections[2]) > 0);
        rig.Time.Now = System.TimeSpan.FromMilliseconds(250);
        Assert.Equal(0, rig.Panel.FlashStrength(sections[2]));
        Assert.Equal(0, rig.Arrangement.Timeline.SectionButtons.FlashAlpha(sections[2]));
        rig.Close();
    }

    [AvaloniaFact]
    public void DraggingAStripBlockDoesNotJumpOrFlash()
    {
        var rig = Open();
        var jumps = 0;
        rig.Arrangement.Input.SectionClicked += (_, _) => jumps++;
        var sections = rig.Panel.Sections;
        var at = StripPoint(rig, 1);
        var moved = new Point(at.X + 40, at.Y);
        rig.Window.MouseDown(at, MouseButton.Left);
        rig.Window.MouseMove(moved, RawInputModifiers.LeftMouseButton);
        rig.Window.MouseUp(moved, MouseButton.Left);

        Assert.Equal(0, jumps);
        Assert.Equal(0, rig.Arrangement.Timeline.SectionButtons.FlashAlpha(sections[1]));
        // The cancelled press is easing back to rest: it is no longer held down.
        rig.Time.Now = System.TimeSpan.FromMilliseconds(40);
        Assert.True(rig.Arrangement.Timeline.SectionButtons.PressDepth(sections[1]) < 1);
        rig.Close();
    }

    [AvaloniaFact]
    public void ThePlayheadEnteringASectionSelectsItsRow()
    {
        var ui = new AvaloniaPlaybackSessionTests.FakeUiThread();
        AvaloniaPlaybackSessionTests.FakeEngine? engine = null;
        var sessions = new AvaloniaPlaybackSessions(ui, () => engine = new AvaloniaPlaybackSessionTests.FakeEngine());
        var rig = Open(sessions);
        var sections = rig.Panel.Sections;
        sessions.For(rig.Doc)!.PlayOrPause();

        engine!.Report(new PlaybackPosition { Bar = sections[2].MeasureIndex, Cell = 0, BarFraction = 0, ElapsedMs = 0 });
        ui.FrameTimer.Fire();

        Assert.Same(sections[2], rig.Panel.Highlighted);
        rig.Close();
    }
}
