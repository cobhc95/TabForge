using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the legend's clickable rows: one full-width row per listed role (now always, next and upcoming with look-ahead), inside the legend card
///   and over its drawn row; no fill at rest, a shade and hairline edge on hover, back to rest on leave; the pointer shape never changes.
/// Does not own: the picker (ColourPickerPopup) or the colour storage (FretNoteColoursTests).
/// Tests: FretboardLegendRows, InstrumentPanelView.LegendRowBounds.
/// </summary>
public class FretboardLegendRowsTests
{
    private static (InstrumentPanelStrip Strip, Window Window) Open(bool lookAhead)
    {
        var settings = new AppSettings();
        settings.Editing.PreviewNotesEnabled = lookAhead;
        var score = new ScorePreviewView();
        score.Show(new TabForge.Documents.SongDocument(() => null) { Project = DemoSongFactory.Create() });
        var strip = new InstrumentPanelStrip(score) { NoteColourStore = (() => settings, () => { }) };
        strip.Panel.Look = FretboardSettingsBinding.Look.From(settings);
        var window = new Window { Width = 1300, Height = 260, Content = strip };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        return (strip, window);
    }

    [AvaloniaFact]
    public void EachListedRoleHasATileInOneRowInsideTheLegend()
    {
        var (strip, window) = Open(lookAhead: true);
        var rows = FretNoteColours.Roles.Select(r => strip.LegendRows.RowOf(r)).ToArray();
        Assert.All(rows, row => Assert.True(row.IsVisible));
        var bounds = FretNoteColours.Roles.Select(r => strip.Panel.LegendRowBounds(r)!.Value).ToArray();
        Assert.All(bounds, bound => Assert.Equal(bounds[0].Width, bound.Width, 6));                      // three tiles of one width
        Assert.All(bounds, bound => Assert.Equal(bounds[0].Y, bound.Y, 6));                              // in one row
        Assert.True(bounds[1].X >= bounds[0].Right - 0.01 && bounds[2].X >= bounds[1].Right - 0.01);     // side by side, never overlapping
        var scales = strip.ScalesAnchor;
        Assert.True(Canvas.GetTop(scales) >= bounds[2].Bottom - 0.01, "Scales stays under the tiles");
        window.Close();
    }

    [AvaloniaFact]
    public void WithoutLookAheadOnlyTheNowRowShows()
    {
        var (strip, window) = Open(lookAhead: false);
        Assert.True(strip.LegendRows.RowOf(FretNoteRole.Now).IsVisible);
        Assert.False(strip.LegendRows.RowOf(FretNoteRole.Next).IsVisible);
        Assert.False(strip.LegendRows.RowOf(FretNoteRole.Upcoming).IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void HoverShadesTheRowAndLeavingRestsItWithoutAPointerChange()
    {
        var (strip, window) = Open(lookAhead: true);
        var row = strip.LegendRows.RowOf(FretNoteRole.Next);
        Assert.Same(AvaloniaPalette.TransparentBrush, row.Background);
        var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
        row.RaiseEvent(new PointerEventArgs(InputElement.PointerEnteredEvent, row, pointer, row, new Point(2, 2), 0, new PointerPointProperties(), KeyModifiers.None));
        Assert.NotSame(AvaloniaPalette.TransparentBrush, row.Background);
        Assert.NotSame(AvaloniaPalette.TransparentBrush, row.BorderBrush);   // the hairline edge
        Assert.True(((ISolidColorBrush)row.Background!).Color.A < 255, "the shade lets the drawn dot and label show");
        Assert.Null(row.Cursor);
        row.RaiseEvent(new PointerEventArgs(InputElement.PointerExitedEvent, row, pointer, row, new Point(2, 2), 0, new PointerPointProperties(), KeyModifiers.None));
        Assert.Same(AvaloniaPalette.TransparentBrush, row.Background);
        window.Close();
    }
}
