using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the pointer shape never changes on hover or press: the section strip's hover, a text link and the status bar's links set no cursor
/// (the owner's rule; hover feedback is shading only). Resize cursors on splitters and grips stay.
/// Does not own: the hover shade (HoverShade) or the section flash (SectionButtonFeedback).
/// Tests: TabForge.Avalonia.Tests.NoHoverCursorTests.
/// </summary>
public class NoHoverCursorTests
{
    [AvaloniaFact]
    public void HoveringASectionOnTheStripSetsNoCursor()
    {
        var timeline = new TimelineView(DemoSongFactory.Create());
        var section = timeline.SectionMarkerAt(0);
        Assert.NotNull(section);

        timeline.SetHoveredSection(section);

        Assert.Equal(section, timeline.SectionButtons.Hovered);
        Assert.Null(timeline.Cursor);
    }

    [AvaloniaFact]
    public void ATextLinkKeepsThePointerShape()
    {
        var link = new Button();

        TextLinkButton.Apply(link);

        Assert.Null(link.Cursor);
    }
}
