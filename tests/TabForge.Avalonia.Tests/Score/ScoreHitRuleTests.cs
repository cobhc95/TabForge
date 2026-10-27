using System.Collections.Generic;
using System.Linq;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using TabForge.Views.Score;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the score click hit-rule gaps (inside a beat wins, trailing/left space falls back,
/// and Click follows the same rule as Resolve).
/// Tests: CursorPositions, ScoreHitRule, ScorePreviewEditor, ScoreGeometryProbe.
/// </summary>
public class ScoreHitRuleTests
{
    private static (ScoreGeometryProbe Probe, ScorePreviewEditor Editor, TrackModel Track) MakeDemo()
    {
        var song = DemoSongFactory.Create();
        var probe = new ScoreGeometryProbe(song, new AvaloniaDrawContext());
        return (probe, new ScorePreviewEditor(probe, song, Views.PreferencesCatalog.DefaultHotkeys()), song.Tracks[0]);
    }

    private static List<TabCell> QuarterBar(params int[] onsets)
    {
        var cells = Enumerable.Range(0, 16).Select(_ => new TabCell()).ToList();
        foreach (var i in onsets)
        {
            cells[i].DurationDenominator = 4; // MusicTime.cs:36
            cells[i].Notes.Add(new TabNote { StringIndex = 0, Fret = 5, MidiValue = 69 });
        }
        return cells;
    }

    [Fact]
    public void InsideBeatWidth_SelectsThatBeat()
    {
        var cells = QuarterBar(0, 4, 8, 12);
        Assert.Equal(0, CursorPositions.Resolve(cells, 0.5, 16)); // CursorPositions.cs:95
        Assert.Equal(4, CursorPositions.Resolve(cells, 4.5, 16));
        Assert.Equal(8, CursorPositions.Resolve(cells, 8.5, 16));
        Assert.Equal(12, CursorPositions.Resolve(cells, 12.5, 16));
    }

    [Fact]
    public void InsideBeatLength_NeverLandsInside()
    {
        var cells = QuarterBar(0, 4, 8, 12);
        Assert.Equal(0, CursorPositions.Resolve(cells, 2.0, 16)); // inside first quarter -> its start
        Assert.Equal(4, CursorPositions.Resolve(cells, 6.0, 16));
    }

    [Fact]
    public void TrailingEmptySpace_SelectsLastBeatToTheLeft()
    {
        var cells = QuarterBar(0);
        Assert.Equal(0, CursorPositions.Resolve(cells, 10.0, 16)); // only beat is to the left
    }

    [Fact]
    public void LeftOfFirstBeat_SelectsFirstBeat()
    {
        var cells = QuarterBar(0, 4, 8, 12);
        Assert.Equal(0, CursorPositions.Resolve(cells, -2.0, 16));
    }

    [Fact]
    public void GapJustPastBeatEnd_SelectsLeftBeat()
    {
        var cells = QuarterBar(0, 12);
        Assert.Equal(0, CursorPositions.Resolve(cells, 5.0, 16)); // nearer to beat at 0 than beat at 12
    }

    [AvaloniaFact]
    public void ClickPlacesCursorWhereHitRuleChooses()
    {
        var (probe, editor, track) = MakeDemo();
        var system = probe.Layout.GetLayout(track).Systems[0]; // ScoreGeometryProbe.cs:224
        var bar = system.Measures[1];
        var x = bar.X + bar.Width * 0.6;
        var y = probe.TabTop(0) + 2 * probe.StringGap; // ScoreGeometryProbe.cs:222
        editor.Click(x, y); // ScorePreviewEditor.cs:50 -> ScoreHitRule.cs:11
        var slot = probe.Layout.WarpFor(track, bar.MeasureIndex).SlotAt((x - bar.X) / bar.Width);
        var expected = CursorPositions.Resolve(track.Measures[bar.MeasureIndex].CellsForVoice(0), slot, probe.SlotsFor(bar.MeasureIndex)); // ScoreGeometryProbe.cs:223
        Assert.Equal((bar.MeasureIndex, expected, 2), editor.Cursor);
    }

    [AvaloniaFact]
    public void ClickAtBarLeftEdge_SelectsFirstBeat()
    {
        var (probe, editor, track) = MakeDemo();
        var system = probe.Layout.GetLayout(track).Systems[0];
        var bar = system.Measures[1];
        editor.Click(bar.X + 2, probe.TabTop(0)); // ScorePreviewEditor.cs:50
        Assert.Equal(bar.MeasureIndex, editor.Cursor!.Value.Measure);
        Assert.Equal(0, editor.Cursor!.Value.Cell); // demo bar 1 starts with a beat at cell 0
    }
}
