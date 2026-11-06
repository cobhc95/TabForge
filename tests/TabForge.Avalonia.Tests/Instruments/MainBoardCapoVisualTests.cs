using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Platform;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the main-board capo visuals: a full capo and a partial capo draw their clamp bar on the
// instrument-panel fretboard (the bar region differs from a capo-less render), behind-capo dim unchanged.
// Does not own: the card placement flow (CapoPlacementTests) or the capo rules (FretboardCapoRules).
public sealed class MainBoardCapoVisualTests
{
    private static (InstrumentPanelView View, FretboardGeometryRules.Layout Layout, InstrumentFretNumberTests.RecordingDrawContext Draw) Render(TrackModel track)
    {
        var view = new InstrumentPanelView();
        view.Show(track, System.Array.Empty<InstrumentLitNotes.Lit>());
        view.Measure(new Size(1100, 170));
        view.Arrange(new Rect(0, 0, 1100, 170));
        var draw = new InstrumentFretNumberTests.RecordingDrawContext();
        view.Draw(draw, light: false);
        return (view, view.FretboardLayout(), draw);
    }

    private static TrackModel Guitar()
    {
        var track = DemoSongFactory.Create().Tracks[0];
        track.Capo = 0;
        track.PartialCapos = null;
        return track;
    }

    private static bool CoversCentre(InstrumentFretNumberTests.RecordingDrawContext draw, double centreX)
    {
        foreach (var image in draw.Images)
            if (image.X <= centreX && centreX <= image.X + image.Width && image.Width / image.Height < 1.5)
                return true;
        return false;
    }

    [AvaloniaFact]
    public void FullCapoBarRegionDiffersFromCapolessRender()
    {
        var track = Guitar();
        track.Capo = 4;
        var (_, layout, draw) = Render(track);
        var centre = FretboardGeometryRules.FretX(layout, 4);
        Assert.True(CoversCentre(draw, centre), "full capo 4 draws no clamp bar over its fret centre");

        var plain = Guitar();
        var (_, _, offDraw) = Render(plain);
        Assert.False(CoversCentre(offDraw, centre), "capo-less render must not draw a bar over fret 4");
    }

    [AvaloniaFact]
    public void PartialCapoBarRegionDiffersFromCapolessRender()
    {
        var track = Guitar();
        track.PartialCapos = new System.Collections.Generic.List<PartialCapo> { new() { Fret = 7, StringMask = 0b000110 } };
        var (_, layout, draw) = Render(track);
        var centre = FretboardGeometryRules.FretX(layout, 7);
        Assert.True(CoversCentre(draw, centre), "partial capo 7 draws no clamp bar over its fret centre");
        var wood = FretboardArtist.Wood(layout, bass: false);
        var clipped = false;
        foreach (var clip in draw.Clips)
            if (clip.Y >= wood.Y - 0.001 && clip.Y + clip.Height <= wood.Bottom + 0.001 && clip.Height < wood.Height)
                clipped = true;
        Assert.True(clipped, "partial capo 7 is not clipped to its two strings");

        var plain = Guitar();
        var (_, _, offDraw) = Render(plain);
        Assert.False(CoversCentre(offDraw, centre), "capo-less render must not draw a bar over fret 7");
    }

    [AvaloniaFact]
    public void CapoHoverOverTheBarKeepsThePointerShape()
    {
        var track = Guitar();
        track.Capo = 4;
        var view = new InstrumentPanelView();
        view.Show(track, System.Array.Empty<InstrumentLitNotes.Lit>());
        view.Measure(new Size(1100, 170));
        view.Arrange(new Rect(0, 0, 1100, 170));
        var at = view.FretCentre(2, 4);
        view.UpdateCapoHover(at.X, at.Y);
        Assert.False(view.CapoHovered); // written fret 4 sits at physical fret 8 under capo 4: not on the bar
        var layout = view.FretboardLayout();
        var scale = view.FretboardScale();
        view.UpdateCapoHover(FretboardGeometryRules.FretX(layout, 4) * scale, FretboardGeometryRules.StringY(layout, 2) * scale);
        Assert.True(view.CapoHovered);
        Assert.Null(view.Cursor);   // the hover shade shows it; the pointer shape never changes
        view.UpdateCapoHover(2, 2);
        Assert.False(view.CapoHovered);
        Assert.Null(view.Cursor);
    }

    [AvaloniaTheory]
    [InlineData("Black")]
    [InlineData("Bronze")]
    [InlineData("Steel")]
    [InlineData("Garbage")]
    [InlineData("")]
    public void FullCapoBarDrawsForEveryCapoFinish(string finish)
    {
        var settings = new AppSettings();
        settings.Editing.FretboardCapoLook = finish;
        var track = Guitar();
        track.Capo = 4;
        var view = new InstrumentPanelView { Look = FretboardSettingsBinding.Look.From(settings) };
        view.Show(track, System.Array.Empty<InstrumentLitNotes.Lit>());
        view.Measure(new Size(1100, 170));
        view.Arrange(new Rect(0, 0, 1100, 170));
        var draw = new InstrumentFretNumberTests.RecordingDrawContext();
        view.Draw(draw, light: false);
        var centre = FretboardGeometryRules.FretX(view.FretboardLayout(), 4);
        Assert.True(CoversCentre(draw, centre), $"capo finish '{finish}' draws no clamp bar for full capo 4");
    }
}
