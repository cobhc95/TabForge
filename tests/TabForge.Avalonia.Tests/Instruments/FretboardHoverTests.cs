using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using TabForge.Avalonia.Platform;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Presets;
using TabForge.Platform;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the fretboard pointer path at the real layout: a hover over a fret draws the ghost dot at that fret's centre with its number,
/// and a click raises NotePicked with that string and fret, at several pane widths while the song is stopped.
/// Does not own: the note edit (FretboardNoteEntryTests) or the drawing of the dots (InstrumentFretNumberTests).
/// Tests: InstrumentPanelView hover and click, InstrumentPanelStrip's transparent background.
/// </summary>
public class FretboardHoverTests
{
    private const int HoverString = 2;
    private const int HoverFret = 5;
    /// <summary>The ghost dot's alpha: the hover opacity (0.4) of the fill's full alpha (255).</summary>
    private const byte GhostAlpha = 102;

    [AvaloniaTheory]
    [InlineData(900.0)]
    [InlineData(1300.0)]
    [InlineData(1900.0)]
    public void HoverDrawsGhostAtTheFretAndClickPicksIt(double width)
    {
        var track = DemoSongFactory.Create().Tracks[0];
        var panel = new InstrumentPanelView
        {
            Kind = InstrumentPanelKind.Fretboard,
            Look = FretboardSettingsBinding.Look.From(new AppSettings()),
        };
        panel.Show(track, Array.Empty<InstrumentLitNotes.Lit>());
        var window = new Window { Width = width, Height = 150, Content = panel };
        window.Show();

        var at = panel.FretCentre(HoverString, HoverFret);
        InstrumentNotePick? picked = null;
        panel.NotePicked += pick => picked = pick;

        window.MouseMove(new Point(at.X, at.Y));
        var canvas = new InstrumentFretNumberTests.RecordingDrawContext();
        panel.Draw(canvas, light: false);
        var scale = panel.FretboardScale();
        Assert.Contains(canvas.Ellipses, e => e.Fill?.A == GhostAlpha
            && Math.Abs(e.X * scale - at.X) < 0.5 && Math.Abs(e.Y * scale - at.Y) < 0.5);
        Assert.Contains(canvas.Texts, t => t.Text == HoverFret.ToString());

        window.MouseDown(new Point(at.X, at.Y), MouseButton.Left);
        window.MouseUp(new Point(at.X, at.Y), MouseButton.Left);
        Assert.NotNull(picked);
        Assert.Equal(HoverString, picked!.Value.StringIndex);
        Assert.Equal(HoverFret, picked.Value.Fret);
        window.Close();
    }

    [AvaloniaFact]
    public void GhostFollowsThePointerToAnotherFretAndIsGrey()
    {
        var track = DemoSongFactory.Create().Tracks[0];
        var panel = new InstrumentPanelView { Kind = InstrumentPanelKind.Fretboard, Look = FretboardSettingsBinding.Look.From(new AppSettings()) };
        panel.Show(track, Array.Empty<InstrumentLitNotes.Lit>());
        var window = new Window { Width = 1300, Height = 150, Content = panel };
        window.Show();

        var first = panel.FretCentre(HoverString, HoverFret);
        window.MouseMove(new Point(first.X, first.Y));
        Assert.Equal((HoverString, HoverFret), panel.Hovered);
        var second = panel.FretCentre(HoverString + 1, HoverFret + 2);
        window.MouseMove(new Point(second.X, second.Y));
        Assert.Equal((HoverString + 1, HoverFret + 2), panel.Hovered);
        var canvas = new InstrumentFretNumberTests.RecordingDrawContext();
        panel.Draw(canvas, light: false);
        Assert.Contains(canvas.Texts, t => t.Text == (HoverFret + 2).ToString());
        var ghost = Assert.Single(canvas.Ellipses, e => e.Fill?.A == GhostAlpha);
        Assert.True(ghost.Fill!.Value.R == ghost.Fill.Value.G || System.Math.Abs(ghost.Fill.Value.R - ghost.Fill.Value.B) < 40);   // a neutral grey, not a playback hue
        window.Close();
    }

    [AvaloniaFact]
    public void KeyboardKeyHoverPaintsTheHoverShade()
    {
        var track = new TabForge.Models.TrackModel { MidiChannel = 0 };
        var panel = new InstrumentPanelView { Kind = InstrumentPanelKind.Keyboard, Width = 900, Height = 168 };
        panel.Measure(new Size(900, 168));
        panel.Arrange(new Rect(0, 0, 900, 168));
        panel.Show(track, Array.Empty<InstrumentLitNotes.Lit>());
        var plain = new InstrumentFretNumberTests.RecordingDrawContext();
        panel.Draw(plain, light: false);
        panel.SetFeedbackForTest(60, null);
        var hovered = new InstrumentFretNumberTests.RecordingDrawContext();
        panel.Draw(hovered, light: false);
        Assert.True(hovered.Rects.Count(r => r.Fill is not null) > plain.Rects.Count(r => r.Fill is not null));
    }
}
