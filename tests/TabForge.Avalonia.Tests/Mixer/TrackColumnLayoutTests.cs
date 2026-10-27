using System;
using TabForge.Avalonia.Platform;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the track row column layout (TrackRowCells.Layout) at the narrowest, the default and two wider list widths: no two columns overlap,
///     the column titles centre over their columns, the sliders map their edges to the value extremes, and the M box stays clear of the volume slider.
/// Does not own: the drawing of the cells (TrackRowCells.Draw*) or the list-width drag (ArrangementView).
/// </summary>
public class TrackColumnLayoutTests
{
    private static double[] ListWidths() => new[] { TrackListWidthRules.MinListWidth, 532, 700, 900 };

    [Fact]
    public void NoTwoColumnsOverlapAtAnyListWidth()
    {
        foreach (var listWidth in ListWidths())
        {
            var layout = TrackRowCells.Layout(TrackListWidthRules.NameWidthFor(listWidth));
            var spans = new[] { layout.Settings, layout.Kind, layout.Colour, layout.Number, layout.Name, layout.Fx, layout.Mute, layout.Solo, layout.Volume, layout.Pan };
            for (var i = 0; i < spans.Length; i++)
                for (var j = i + 1; j < spans.Length; j++)
                    Assert.True(spans[i].Right <= spans[j].Left + 1e-6 || spans[j].Right <= spans[i].Left + 1e-6,
                        $"columns {i} and {j} overlap at list width {listWidth}");
            Assert.Equal(listWidth, layout.Width, 6);
        }
    }

    [Fact]
    public void TitlesAreCentredOverTheirColumns()
    {
        const double titleWidth = 30;
        foreach (var listWidth in ListWidths())
        {
            var nameWidth = TrackListWidthRules.NameWidthFor(listWidth);
            foreach (var id in new[] { "name", "fx", "mute", "solo", "volume", "pan" })
            {
                var span = TrackRowCells.Column(id, nameWidth);
                var centre = TrackRowCells.TitleLeft(id, titleWidth, nameWidth) + titleWidth / 2;
                Assert.Equal(span.Left + span.Width / 2, centre, 6);
            }
        }
    }

    [Fact]
    public void SliderEdgesMapToTheValueExtremesAtEveryWidth()
    {
        foreach (var listWidth in ListWidths())
        {
            var nameWidth = TrackListWidthRules.NameWidthFor(listWidth);
            var volume = TrackRowCells.Box(TrackRowCells.Cell.Volume, 0, 34, nameWidth);
            var pan = TrackRowCells.Box(TrackRowCells.Cell.Pan, 0, 34, nameWidth);
            Assert.Equal(0, TrackRowCells.SliderValueAt(TrackRowCells.Cell.Volume, volume, volume.X));
            Assert.Equal(127, TrackRowCells.SliderValueAt(TrackRowCells.Cell.Volume, volume, volume.Right));
            Assert.Equal(-64, TrackRowCells.SliderValueAt(TrackRowCells.Cell.Pan, pan, pan.X));
            Assert.Equal(63, TrackRowCells.SliderValueAt(TrackRowCells.Cell.Pan, pan, pan.Right));
        }
    }

    [Fact]
    public void MuteBoxDoesNotIntersectTheVolumeSlider()
    {
        foreach (var listWidth in ListWidths())
        {
            var nameWidth = TrackListWidthRules.NameWidthFor(listWidth);
            var mute = TrackRowCells.Box(TrackRowCells.Cell.Mute, 0, 34, nameWidth);
            var volume = TrackRowCells.Box(TrackRowCells.Cell.Volume, 0, 34, nameWidth);
            Assert.True(mute.Right <= volume.X + 1e-6);
            Assert.True(volume.Right <= TrackRowCells.Column("volume", nameWidth).Right + 1e-6);
        }
    }
}
