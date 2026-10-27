using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Services;
using TabForge.Views.Score;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: Close gap: finding the empty run before a section, closing it without a prompt when empty (later sections, bars, clips pulled back, one undo step), the three answers when it is not,
//   a repeat spanning the gap, and a section dragged left into the gap (full snap to the end of the previous section, and part way).
// Does not own: the menu rows (SectionGapMenuTests) or the section reorder drag (TimelineSectionDragTests).
// Tests: this class.
public sealed class SectionGapCloseTests
{
    private const int Bars = 12;

    // Verse bars 0-3 (length 4), gap bars 4-7, Chorus bars 8-11.
    private static SongProject NewSong()
    {
        var project = new SongProject();
        project.Tracks.Add(new TrackModel { Name = "Guitar", Measures = Enumerable.Range(0, Bars).Select(bar => new MeasureModel { Number = bar + 1 }).ToList() });
        project.Markers.Add(new MarkerModel { MeasureIndex = 0, Title = "Verse", LengthBars = 4 });
        project.Markers.Add(new MarkerModel { MeasureIndex = 8, Title = "Chorus" });
        return project;
    }

    private static void PutNote(SongProject project, int bar) => project.Tracks[0].Measures[bar].Cells[0].Notes.Add(new TabNote { StringIndex = 0, Fret = 3 });

    private static int ChorusBar(SongProject project) => project.Markers.Single(m => m.Title == "Chorus").MeasureIndex;

    [Fact]
    public void FindReturnsTheRunBetweenTheSectionsOnlyInTheGap()
    {
        var project = NewSong();
        Assert.Equal((4, 7), SectionGapEdits.Find(project, 6));
        Assert.Null(SectionGapEdits.Find(project, 2));
        Assert.Null(SectionGapEdits.Find(project, 9));
    }

    [Fact]
    public void AnEmptyGapClosesAtOnceMovesLaterThingsBackAndUndoesInOneStep()
    {
        var project = NewSong();
        var clipFrom = AudioClipTimelineRules.SecondsAtBar(project, 9, 0);
        project.Tracks[0].AudioClips.Add(new AudioClip { Name = "late", StartSec = clipFrom, SourceLengthSec = 1, FileLengthSec = 1 });
        var document = new SongDocument(() => null) { Project = project };
        Assert.True(SectionGapEdits.Contents(project, 4, 7).IsEmpty);

        var steps = document.Undo.UndoCount;
        Assert.True(DocumentEdits.Run(document, p => SectionGapEdits.Close(p, 4, 7, GapCloseMode.RemoveAll).Changed, label: "Close gap").Changed);
        Assert.Equal(steps + 1, document.Undo.UndoCount);
        Assert.Equal(Bars - 4, document.Project.Tracks[0].Measures.Count);
        Assert.Equal(4, ChorusBar(document.Project));
        Assert.Equal(AudioClipTimelineRules.SecondsAtBar(document.Project, 5, 0), document.Project.Tracks[0].AudioClips[0].StartSec, 3);

        DocumentEdits.Undo(document);
        Assert.Equal(Bars, document.Project.Tracks[0].Measures.Count);
        Assert.Equal(8, ChorusBar(document.Project));
    }

    [Fact]
    public void ANonEmptyGapListsItsNotesAndClips()
    {
        var project = NewSong();
        PutNote(project, 5);
        var from = AudioClipTimelineRules.SecondsAtBar(project, 6, 0);
        project.Tracks[0].AudioClips.Add(new AudioClip { Name = "pad", StartSec = from, SourceLengthSec = 1, FileLengthSec = 1 });
        var contents = SectionGapEdits.Contents(project, 4, 7);
        Assert.False(contents.IsEmpty);
        Assert.Equal(("Guitar", 1), contents.Notes.Single());
        Assert.Contains("pad", contents.Clips.Single());
        Assert.Contains("Guitar: 1 note", GapClosePromptWindow.BodyFor(contents));
    }

    [Fact]
    public void RemoveEverythingDropsTheBarsAndTheirNotes()
    {
        var project = NewSong();
        PutNote(project, 5);
        Assert.True(SectionGapEdits.Close(project, 4, 7, GapCloseMode.RemoveAll).Changed);
        Assert.Equal(Bars - 4, project.Tracks[0].Measures.Count);
        Assert.Equal(4, ChorusBar(project));
        Assert.DoesNotContain(project.Tracks[0].Measures, m => m.Cells.Any(c => c.Notes.Count > 0));
    }

    [Fact]
    public void MakeItASectionKeepsEverything()
    {
        var project = NewSong();
        PutNote(project, 5);
        Assert.True(SectionGapEdits.Close(project, 4, 7, GapCloseMode.MakeSection).Changed);
        Assert.Equal(Bars, project.Tracks[0].Measures.Count);
        Assert.Equal(8, ChorusBar(project));
        var made = Assert.Single(project.Markers, m => m.Title.StartsWith("Bars 5"));
        Assert.Equal((4, 4), (made.MeasureIndex, made.LengthBars));
        Assert.Null(SectionGapEdits.Find(project, 5));
    }

    [Fact]
    public void RemoveEmptyBarsOnlyKeepsTheBarsThatHoldSomething()
    {
        var project = NewSong();
        PutNote(project, 5);
        Assert.True(SectionGapEdits.Close(project, 4, 7, GapCloseMode.RemoveEmptyOnly).Changed);
        Assert.Equal(Bars - 3, project.Tracks[0].Measures.Count);
        Assert.Equal(5, ChorusBar(project));
        Assert.Single(project.Tracks[0].Measures, m => m.Cells.Any(c => c.Notes.Count > 0));
    }

    [Fact]
    public void ARepeatSpanningTheGapStaysConsistent()
    {
        var project = NewSong();
        project.Tracks[0].Measures[1].RepeatStart = true;
        project.Tracks[0].Measures[10].RepeatEnd = true;
        Assert.True(SectionGapEdits.Close(project, 4, 7, GapCloseMode.RemoveAll).Changed);
        Assert.True(project.Tracks[0].Measures[1].RepeatStart);
        Assert.True(project.Tracks[0].Measures[6].RepeatEnd);
    }

    [Fact]
    public async Task TheFlowSkipsThePromptWhenEmptyAndCancelChangesNothingWhenNot()
    {
        var document = new SongDocument(() => null) { Project = NewSong() };
        var asked = 0;
        Task<GapCloseMode?> Ask(int a, int b, GapContents c) { asked++; return Task.FromResult<GapCloseMode?>(null); }
        Assert.True(await MainWindowTimelineSections.CloseGapAsync(document, 4, 7, Ask, _ => { }));
        Assert.Equal(0, asked);

        var other = new SongDocument(() => null) { Project = NewSong() };
        PutNote(other.Project, 5);
        Assert.False(await MainWindowTimelineSections.CloseGapAsync(other, 4, 7, Ask, _ => { }));
        Assert.Equal(1, asked);
        Assert.Equal(Bars, other.Project.Tracks[0].Measures.Count);
    }

    private static (Window Window, TimelineView View, TimelineInputController Input, ScorePreviewEditor.PreviewDocument Doc) Open()
    {
        var song = NewSong();
        var doc = new ScorePreviewEditor.PreviewDocument(song);
        var view = new TimelineView(song) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        view.Reload(song);
        var input = new TimelineInputController(view, doc);
        input.SectionAnimator.Enabled = false;
        var window = new Window { Width = 1600, Height = 400, Content = view };
        window.Show();
        return (window, view, input, doc);
    }

    // Presses the middle of Chorus and drags it so its left edge lands at the bar line given.
    private static void DragChorusTo(Window window, TimelineView view, int bar)
    {
        var chorus = view.SectionSlots().Single(s => view.SectionMarkerAt(s.MarkerIndex)!.Title == "Chorus");
        var from = new Point(chorus.Left + chorus.Width / 2, chorus.Top + 4);
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(from + new Vector(-3, 0));
        var to = new Point(from.X - (chorus.Left - (view.BarX(bar) + 1)), from.Y);
        window.MouseMove(to);
        window.MouseUp(to, MouseButton.Left);
    }

    [AvaloniaFact]
    public void DraggingASectionLeftAgainstThePreviousOneSnapsAndClosesTheWholeGap()
    {
        var (window, view, _, doc) = Open();
        DragChorusTo(window, view, 4);
        Assert.Equal(4, ChorusBar(doc.Project));
        Assert.Equal(Bars - 4, doc.Project.Tracks[0].Measures.Count);
        window.Close();
    }

    [AvaloniaFact]
    public void DraggingPartWayIntoTheGapClosesOnlyTheDraggedOverBars()
    {
        var (window, view, _, doc) = Open();
        DragChorusTo(window, view, 6);
        Assert.Equal(6, ChorusBar(doc.Project));
        Assert.Equal(Bars - 2, doc.Project.Tracks[0].Measures.Count);
        window.Close();
    }

    [AvaloniaFact]
    public void ANonEmptyGapAsksAndCancelSnapsTheSectionBack()
    {
        var (window, view, input, doc) = Open();
        PutNote(doc.Project, 5);
        GapContents? seen = null;
        input.GapClosePrompt = (a, b, c) => { seen = c; return Task.FromResult<GapCloseMode?>(null); };
        DragChorusTo(window, view, 4);
        Assert.NotNull(seen);
        Assert.Equal(8, ChorusBar(doc.Project));
        Assert.Equal(Bars, doc.Project.Tracks[0].Measures.Count);
        window.Close();
    }
}
