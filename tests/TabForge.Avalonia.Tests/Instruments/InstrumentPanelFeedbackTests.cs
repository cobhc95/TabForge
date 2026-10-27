using System.Linq;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Presenters;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the hover and pressed feedback of drum pads: the HoverSurface and PressSurface colours of the theme are painted under the lit fill,
/// and a lit pad keeps its colour on top of the shade.
/// Tests: InstrumentPanelView.Draw with the hovered and pressed key set.
/// </summary>
public class InstrumentPanelFeedbackTests
{
    private static InstrumentPanelView Drums(out TrackModel track)
    {
        track = new TrackModel { Kind = TrackKind.Drums, MidiChannel = 9, Measures = TemplateFactory.Measures(1) };
        var view = new InstrumentPanelView { Kind = InstrumentPanelKind.Drums };
        view.Show(track, System.Array.Empty<InstrumentLitNotes.Lit>());
        return view;
    }

    private static InstrumentFretNumberTests.RecordingDrawContext Paint(InstrumentPanelView view, bool light)
    {
        var d = new InstrumentFretNumberTests.RecordingDrawContext();
        view.Draw(d, light);
        return d;
    }

    private static bool HasFill(InstrumentFretNumberTests.RecordingDrawContext d, UiColor colour) => d.Rects.Any(r => r.Fill == colour);

    [AvaloniaFact]
    public void HoveredPadPaintsHoverSurfaceAndPressedPadPaintsPressSurface()
    {
        foreach (var light in new[] { false, true })
        {
            var view = Drums(out _);
            var hover = AvaloniaPalette.Get("HoverSurface", light);
            var press = AvaloniaPalette.Get("PressSurface", light);
            Assert.False(HasFill(Paint(view, light), hover));

            view.SetFeedbackForTest(hover: 38, pressed: null);
            Assert.True(HasFill(Paint(view, light), hover));

            view.SetFeedbackForTest(hover: 38, pressed: 38);
            var pressed = Paint(view, light);
            Assert.True(HasFill(pressed, press));
            Assert.False(HasFill(pressed, hover));
        }
    }

    [AvaloniaFact]
    public void LitPadStaysVisibleOverTheHoverShade()
    {
        var view = Drums(out var track);
        view.Show(track, new[] { new InstrumentLitNotes.Lit(0, 0, 38) });
        view.SetFeedbackForTest(hover: 38, pressed: null);
        var d = Paint(view, false);
        var hover = AvaloniaPalette.Get("HoverSurface", false);
        var current = AvaloniaPalette.Get("InstrumentCurrent", false);
        var shadeIndex = d.Rects.FindIndex(r => r.Fill == hover);
        var litIndex = d.Rects.FindIndex(r => r.Fill is { } f && f.R == current.R && f.G == current.G && f.B == current.B);
        Assert.True(shadeIndex >= 0 && litIndex > shadeIndex, "the lit fill is drawn on top of the hover shade");
    }
}
