using System.Linq;
using System.Windows;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

/// <summary>Timeline hover shade (cell under the pointer) and the playback position marker setting (Line / Bar marker / Both).</summary>
public static partial class SelfTest
{
    private static SongProject HoverSong(int tracks = 3, int bars = 12)
    {
        var song = new SongProject();
        for (var t = 0; t < tracks; t++)
            song.Tracks.Add(new TrackModel
            {
                Name = "T" + t, Kind = TrackKind.Guitar,
                Measures = Enumerable.Range(0, bars).Select(_ => new MeasureModel()).ToList()
            });
        return song;
    }

    /// <summary>The setting: default Line, validator fallback, catalog row and the bindable cycle command.</summary>
    private static void TestPlayheadStyleSetting()
    {
        var settings = new AppSettings();
        Check("playback position marker defaults to Line", settings.Timeline.PlayheadStyle == PlayheadStyles.Line);
        var bad = new AppSettings(); bad.Timeline.PlayheadStyle = "sparkles";
        Check("an unknown playback position marker falls back to Line", SettingsValidator.Normalize(bad).Timeline.PlayheadStyle == PlayheadStyles.Line);
        var cased = new AppSettings(); cased.Timeline.PlayheadStyle = "bar MARKER";
        Check("the playback position marker is matched without regard to case", SettingsValidator.Normalize(cased).Timeline.PlayheadStyle == PlayheadStyles.BarMarker);
        var nulled = new AppSettings(); nulled.Timeline.PlayheadStyle = null!;
        Check("a missing playback position marker falls back to Line", SettingsValidator.Normalize(nulled).Timeline.PlayheadStyle == PlayheadStyles.Line);
        Check("the style cycles Line, Bar marker, Both and back",
            PlayheadStyles.Next(PlayheadStyles.Line) == PlayheadStyles.BarMarker && PlayheadStyles.Next(PlayheadStyles.BarMarker) == PlayheadStyles.Both
            && PlayheadStyles.Next(PlayheadStyles.Both) == PlayheadStyles.Line);
        Check("Line shows only the line, Bar marker only the marker, Both shows both",
            PlayheadStyles.ShowsLine("Line") && !PlayheadStyles.ShowsBarMarker("Line") && !PlayheadStyles.ShowsLine("Bar marker")
            && PlayheadStyles.ShowsBarMarker("Bar marker") && PlayheadStyles.ShowsLine("Both") && PlayheadStyles.ShowsBarMarker("Both"));
        var row = SettingsCatalog.Build(new AppSettings()).FirstOrDefault(d => d.Key == "timeline.playheadstyle");
        Check("the setting is a searchable Choice row in the Timeline group with Line as its value",
            row is not null && row.Category == SettingsCatalog.Timeline && row.Kind == SettingKind.Choice && Equals(row.Get(), "Line")
            && SettingsCatalog.Matches(row, "playhead") && SettingsCatalog.Matches(row, "playback position"));
        var action = HotkeyCatalog.ById("View.CyclePlayheadStyle");
        Check("View.CyclePlayheadStyle is a bindable command, unbound by default", action is not null && action.DefaultGesture == "");
    }

    /// <summary>Hover index from a point (also after a vertical scroll) and the marker's cell for a bar and track.</summary>
    private static void TestTimelineHoverAndBarMarker()
    {
        var song = HoverSong();
        var timeline = new TrackTimeline { Project = song, MeasureWidth = 30 };
        var gridTop = ArrangementPanel.RulerHeight + ArrangementPanel.SectionHeight;
        var row = ArrangementPanel.DefaultTrackRowHeight;
        Check("hover: a point inside bar 4 of track 2 is that cell",
            timeline.HoverCellAt(new Point(timeline.XOfBar(3) + 5, gridTop + row + 10)) == (3, 1));
        Check("hover: the ruler and section strip have no hover cell", timeline.HoverCellAt(new Point(40, gridTop - 2)) == (-1, -1));
        Check("hover: beyond the last bar or below the last track has none",
            timeline.HoverCellAt(new Point(timeline.XOfBar(12) + 4, gridTop + 10)) == (-1, -1) && timeline.HoverCellAt(new Point(40, gridTop + row * 3 + 4)) == (-1, -1));
        timeline.VerticalScrollOffset = row;
        Check("hover: after scrolling one row, the same point is the next track",
            timeline.HoverCellAt(new Point(timeline.XOfBar(3) + 5, gridTop + 10)) == (3, 1) && timeline.HoverCellAt(new Point(timeline.XOfBar(3) + 5, gridTop + row + 10)) == (3, 2));
        var cell = timeline.CellBounds(3, 1)!.Value;
        Check("hover: the cell rectangle follows the scroll offset", Math.Abs(cell.Y - (gridTop + 1)) < 0.01 && Math.Abs(cell.X - timeline.XOfBar(3)) < 0.01);

        var marker = ArrangementPanel.BarMarkerRect(new Rect(100, 50, 29, 28));
        Check("bar marker: a square of about 45% of the cell height, centred in the cell",
            Math.Abs(marker.Height - Math.Round(28 * 0.45)) < 0.01 && marker.Width == marker.Height
            && Math.Abs(marker.X + marker.Width / 2 - (100 + 29.0 / 2)) < 0.01 && Math.Abs(marker.Y + marker.Height / 2 - (50 + 14.0)) < 0.01);

        var panel = new ArrangementPanel();
        panel.Bind(song, Array.Empty<Playback.MidiOutputDeviceInfo>());
        var window = new Window { Content = panel, Width = 900, Height = 400 };
        using var alive = KeepAlive();
        try
        {
            ShowTestWindow(window);
            panel.SetSelectedTrack(1);
            panel.SetPlayhead(4, 0.3);
            Check("playback position marker Line: the line shows, the bar marker does not", panel.PlayheadLineVisible && !panel.BarMarkerVisible);
            panel.PlayheadStyle = PlayheadStyles.BarMarker;
            var expectedCell = panel.BarMarkerCell()!.Value;
            var expected = ArrangementPanel.BarMarkerRect(expectedCell);
            Check("playback position marker Bar marker: the line is hidden and the marker sits in the playhead bar of the selected track",
                !panel.PlayheadLineVisible && panel.BarMarkerVisible && panel.BarMarkerBounds == expected && expectedCell.Contains(expected.TopLeft) && expectedCell.Contains(expected.BottomRight));
            panel.SetPlayhead(4, 0.9);
            Check("the bar marker stays put while the playhead moves inside the bar", panel.BarMarkerBounds == expected);
            panel.SetPlayhead(5, 0.1);
            Check("the bar marker moves one bar right when the playhead enters the next bar",
                Math.Abs(panel.BarMarkerBounds.Y - expected.Y) < 0.01 && panel.BarMarkerBounds.X > expected.X + 20);
            panel.SetSelectedTrack(2);
            Check("the bar marker follows the selected track", panel.BarMarkerBounds.Y > expected.Y + ArrangementPanel.DefaultTrackRowHeight - 1);
            panel.PlayheadStyle = PlayheadStyles.Both;
            Check("playback position marker Both: the line and the marker show", panel.PlayheadLineVisible && panel.BarMarkerVisible);
            panel.PlayheadStyle = PlayheadStyles.Line;
            Check("switching back to Line hides the marker", panel.PlayheadLineVisible && !panel.BarMarkerVisible);

            var centre = panel.CellCentre(6, 0)!.Value;
            panel.SimulateHover(centre);
            var shade = panel.HoverCellBounds;
            Check("hover shade: sits exactly on the hovered cell", shade is { } s && Math.Abs(s.X - panel.CellCentre(6, 0)!.Value.X + s.Width / 2) < 0.01 && s.Height == ArrangementPanel.DefaultTrackRowHeight - 2);
            panel.SimulateHover(new Point(centre.X + 40, centre.Y));
            Check("hover shade: moves to the next cell with the pointer", panel.HoverCellBounds is { } n && n.X > shade!.Value.X + 20);
            panel.SimulateHover(new Point(5, 5));
            Check("hover shade: cleared when the pointer is off every cell", panel.HoverCellBounds is null);
        }
        finally { window.Close(); }
    }

    /// <summary>The section hover tip: plain drag moves the section with its bars, Ctrl+drag moves only the marker.</summary>
    private static void TestSectionTipWording()
    {
        var song = HoverSong(3, 12);
        song.Markers.Add(new MarkerModel { MeasureIndex = 2, Title = "Verse 1" });
        var tip = new SectionTipController(new TipHost { Project = song }, new System.Windows.Controls.Border());
        var addHint = TooltipShortcuts.Append("Add section", "Section.Add");
        Eq("section tip with free bars: plain drag, Ctrl+drag marker move, then edge/menu/add",
            "Drag: move Verse 1 with its bars (other sections make room)\n" +
            "Ctrl+drag: move only the Verse 1 marker left into the free bars (its bars stay, a gap is left behind)\n" +
            "Drag an edge: resize · Right-click: section options · " + addHint,
            tip.TextFor(0));

        var packed = HoverSong(3, 12);
        packed.Markers.Add(new MarkerModel { MeasureIndex = 0, Title = "Verse 1" });
        var packedTip = new SectionTipController(new TipHost { Project = packed }, new System.Windows.Controls.Border());
        Eq("section tip with no free bars: plain drag still works, Ctrl+drag says why it cannot move alone",
            "Drag: move Verse 1 with its bars (other sections make room)\n" +
            "Ctrl+drag: can't move the marker alone (no empty bars beside Verse 1)\n" +
            "Drag an edge: resize · Right-click: section options · " + addHint,
            packedTip.TextFor(0));
    }

    private sealed class TipHost : ISectionTipHost
    {
        public SongProject? Project { get; init; }
        public bool IsMouseOver => false;
        public bool SectionGestureActive => false;
        public int HoverSectionIndex => -1;
    }
}
