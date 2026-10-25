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
using TabForge.Presets;
using TabForge.Presenters;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: sections as links: a Sections row click jumps the cursor, the score and playback to the section's first bar and highlights it; Ctrl selects its
/// bars; Up/Down/Enter in the list; a section strip click jumps while a drag does not.
/// Does not own: the jump rules (SectionLinks), the strip gestures (TimelineInputController).
/// Tests: SectionLinks, SectionsPanelView, TimelineInputController.
/// </summary>
public class SectionLinkTests
{
    private static (SongDocument Doc, SectionLinks Links, SectionsPanelView Panel, ScorePreviewView Score, AvaloniaPlaybackSessions Sessions, Window Window) Setup()
    {
        var doc = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        doc.MarkClean();
        var tabs = new MainWindowTabs(doc, _ => Task.FromResult(TabForge.Presenters.PromptButton.Cancel), _ => Task.FromResult(false));
        var score = new ScorePreviewView();
        score.Show(doc);
        var sessions = new AvaloniaPlaybackSessions(new AvaloniaPlaybackSessionTests.FakeUiThread(), () => new AvaloniaPlaybackSessionTests.FakeEngine());
        var links = new SectionLinks(tabs, score, sessions, () => null);
        var panel = links.NewPanel();
        var window = new Window { Width = 400, Height = 500, Content = panel };
        window.Show();
        return (doc, links, panel, score, sessions, window);
    }

    private static Border[] Rows(SectionsPanelView panel) =>
        panel.GetVisualDescendants().OfType<Border>().Where(b => b.Child is Grid && b.CornerRadius == new CornerRadius(2)).ToArray();

    private static void Click(Window window, Control target, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        var at = target.TranslatePoint(new Point(10, 5), window)!.Value;
        window.MouseDown(at, MouseButton.Left, modifiers);
        window.MouseUp(at, MouseButton.Left, modifiers);
    }

    [AvaloniaFact]
    public void ClickingARowJumpsCursorPlaybackAndHighlightToTheSection()
    {
        var (doc, _, panel, score, sessions, window) = Setup();
        var sections = panel.Sections;
        Assert.True(sections.Count >= 2);
        var rows = Rows(panel);
        Click(window, rows[1]);
        var bar = sections[1].MeasureIndex;
        Assert.Equal(bar, score.Editor.SelectedMeasure);
        Assert.Equal(0, score.Editor.SelectedCell);
        Assert.Equal(bar, sessions.For(doc)!.Transport.PlayheadBar);
        Assert.Same(sections[1], panel.Highlighted);
        Click(window, rows[0]);
        Assert.Equal(sections[0].MeasureIndex, score.Editor.SelectedMeasure);
        Assert.Same(sections[0], panel.Highlighted);
        window.Close();
    }

    [AvaloniaFact]
    public void CtrlClickSelectsTheWholeSectionRange()
    {
        var (_, _, panel, score, _, window) = Setup();
        var sections = panel.Sections;
        Click(window, Rows(panel)[0], RawInputModifiers.Control);
        var range = score.Editor.SelectedBarRange();
        Assert.Equal(sections[0].MeasureIndex, range!.Value.First);
        Assert.Equal(sections[1].MeasureIndex - 1, range.Value.Last);
        window.Close();
    }

    [AvaloniaFact]
    public void ArrowKeysMoveTheHighlightAndEnterJumps()
    {
        var (_, _, panel, score, _, window) = Setup();
        var sections = panel.Sections;
        panel.Focus();
        Assert.Same(sections[0], panel.Highlighted);   // the cursor starts in the first section
        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        Assert.Same(sections[1], panel.Highlighted);
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Assert.Equal(sections[1].MeasureIndex, score.Editor.SelectedMeasure);
        window.Close();
    }

    [AvaloniaFact]
    public void MovingTheCursorMovesTheHighlight()
    {
        var (_, links, panel, score, _, window) = Setup();
        var sections = panel.Sections;
        score.Editor.SetPosition(sections[1].MeasureIndex, 0, 0);
        links.Refresh();
        Assert.Same(sections[1], panel.Highlighted);
        window.Close();
    }

    [AvaloniaFact]
    public void SectionStripClickJumpsButADragDoesNot()
    {
        var doc = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        var arrangement = new ArrangementView(doc.Project, doc);
        var clicks = 0;
        arrangement.Input.SectionClicked += (_, _) => clicks++;
        var window = new Window { Width = 1300, Height = 400, Content = arrangement };
        window.Show();
        window.UpdateLayout();   // the strip's point is read from the arranged timeline, below the transport bar
        var slot = arrangement.Timeline.SectionSlots()[1];
        var at = arrangement.Timeline.TranslatePoint(new Point(slot.Left + 8, slot.Top + 6), window)!.Value;
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Assert.Equal(1, clicks);
        window.MouseDown(at, MouseButton.Left);
        window.MouseMove(new Point(at.X + 40, at.Y), RawInputModifiers.LeftMouseButton);
        window.MouseUp(new Point(at.X + 40, at.Y), MouseButton.Left);
        Assert.Equal(1, clicks);
        window.Close();
    }
}
