using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Menus;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the section strip's right-click menu: an empty part (no section under the pointer) shows only Add section at bar N, Paste section and Timeline settings,
//   never acting on a neighbouring section; over a section the full menu stays; Add section at bar N and Paste section in a gap act at N.
// Does not own: the section menu's rows over a section (TimelineSectionMenuTests) or the other timeline menus (ContextMenuTests).
// Tests: this class.
public sealed class SectionGapMenuTests
{
    private const int Bars = 12;

    // Verse covers bars 0-3 (length 4), the gap is bars 4-7, Chorus starts at bar 8.
    private static TimelineView Open(out SongProject project)
    {
        project = new SongProject();
        project.Tracks.Add(new TrackModel { Name = "Guitar", Measures = Enumerable.Range(0, Bars).Select(bar => new MeasureModel { Number = bar + 1 }).ToList() });
        project.Markers.Add(new MarkerModel { MeasureIndex = 0, Title = "Verse", LengthBars = 4 });
        project.Markers.Add(new MarkerModel { MeasureIndex = 8, Title = "Chorus" });
        var view = new TimelineView(project) { HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top };
        view.Reload(project);
        var window = new Window { Width = 1600, Height = 600, Content = view };
        window.Show();
        window.UpdateLayout();
        view.UpdateLayout();
        return view;
    }

    private static Point OnStrip(TimelineView view, int bar) => new(view.BarX(bar) + 5, TimelineView.RulerHeight + TimelineView.SectionHeight / 2);

    [AvaloniaFact]
    public void AnEmptyPartOfTheSectionStripShowsOnlyAddPasteAndSettings()
    {
        var view = Open(out _);
        var rows = ContextMenuRows.TimelineAt(view, OnStrip(view, 5)).ToList();

        Assert.Equal(new[] { TimelineCommand.AddSectionHere, TimelineCommand.CloseSectionGap, TimelineCommand.PasteSectionAtBar, TimelineCommand.None, TimelineCommand.TimelineSettings },
            rows.Select(r => r.Command));
        Assert.True(rows[3].IsSeparator);
        Assert.Equal((4, 7), rows[1].HoverBars);
        Assert.StartsWith("Close gap (bars 5", rows[1].Header);
        Assert.Equal("Add section at bar 6", rows[0].Header);
        Assert.False(string.IsNullOrEmpty(rows[0].Shortcut));
        Assert.False(rows[2].Enabled);   // nothing on the clipboard
        Assert.Equal(-1, view.ContextMenus.ClickedSection);
        Assert.Equal(5, view.ContextMenus.ClickedBar);
        Assert.Contains(ContextMenuRows.CommandIdOf(rows[2]), MainWindowTimelineSections.Commands);
    }

    [AvaloniaFact]
    public void OverASectionTheFullSectionMenuStaysAndTargetsThatSection()
    {
        var view = Open(out _);
        var rows = ContextMenuRows.TimelineAt(view, OnStrip(view, 2)).ToList();
        var commands = rows.Select(r => r.Command).ToList();

        Assert.Contains(TimelineCommand.CopySectionMenu, commands);
        Assert.Contains(TimelineCommand.DeleteSection, commands);
        Assert.Contains(TimelineCommand.RenameSection, commands);
        Assert.DoesNotContain(TimelineCommand.PasteSectionAtBar, commands);
        Assert.Equal(0, view.ContextMenus.ClickedSection);
    }

    [AvaloniaFact]
    public async Task AddSectionAtBarNAddsAtNAndPasteInTheGapPastesAtN()
    {
        var view = Open(out var project);
        ContextMenuRows.TimelineAt(view, OnStrip(view, 5)).ToList();
        var document = new SongDocument(() => null) { Project = project };
        var prompts = new MainWindowTimelineSections.SectionMenuPrompts((_, _) => Task.FromResult(true),
            (_, _, _, _) => Task.FromResult<MarkerAnswer?>(new MarkerAnswer("Bridge", "#FF0000")), () => new AppSettings());

        await MainWindowTimelineSections.AddHereAsync(document, view.ContextMenus.ClickedBar, _ => { }, prompts);
        Assert.Single(project.Markers, m => m.MeasureIndex == 5 && m.Title == "Bridge");

        var clip = SectionMenuEdits.Copy(project, project.Markers.First(m => m.Title == "Verse"));
        Assert.NotNull(SectionMenuEdits.PasteAt(project, view.ContextMenus.ClickedBar, clip, 0, project.Markers.First(m => m.Title == "Verse")));
        Assert.Equal(Bars + 4, project.Tracks[0].Measures.Count);
        Assert.Single(project.Markers, m => m.MeasureIndex == 5 && m.Title == "Verse");
    }
}
